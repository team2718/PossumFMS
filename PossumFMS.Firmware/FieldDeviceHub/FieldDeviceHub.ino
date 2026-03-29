#include "Adafruit_VL53L0X.h"
#include <Adafruit_NeoPixel.h>
#include <esp_system.h>
#include <math.h>
#include <WiFi.h>
#include "bson_helper.h"
#include "wifi_secrets.h"

const char* FMS_HOST      = "192.168.1.167";
const uint16_t FMS_PORT   = 1678;

const char* DEVICE_NAME   = "red_hub";   // unique label shown in FMS
const char* ALLIANCE_NAME = "red";

const uint32_t HEARTBEAT_INTERVAL_MS = 50;
const uint32_t REPLY_TIMEOUT_MS = 100;
const uint32_t INITIAL_REPLY_TIMEOUT_MS = 500;
const uint32_t REPLY_BODY_TIMEOUT_MS = 200;
const uint32_t FLASH_INTERVAL_MS = 250;
const int BALL_DETECT_DROP_MM = 20;
const int BALL_DETECT_RISE_MM = 10;
const int SENSOR_VALID_MIN_MM = 30;
const int SENSOR_VALID_MAX_MM = 400;
const int SENSOR_MAX_STEP_MM = 700;
const uint32_t SENSOR_STALE_MS = 500;
const uint32_t SENSOR_STALE_LOG_INTERVAL_MS = 1000;

const size_t RX_BUF_SIZE = 256;

#if CONFIG_FREERTOS_UNICORE
const BaseType_t NETWORK_LED_CORE = 0;
const BaseType_t BALL_COUNT_CORE = 0;
#else
const BaseType_t NETWORK_LED_CORE = 1;
const BaseType_t BALL_COUNT_CORE = 0;
#endif

WiFiClient client;

#define LED_PIN   13
#define LED_COUNT 175

Adafruit_NeoPixel strip(LED_COUNT, LED_PIN, NEO_RGBW + NEO_KHZ800);

const int XSHUT_PINS[]          = { 2, 4, 18, 19 };
const int SENSOR_COUNT          = sizeof(XSHUT_PINS) / sizeof(XSHUT_PINS[0]);
const uint8_t SENSOR_BASE_ADDR  = 0x30;  // sensor i gets address 0x30+i

Adafruit_VL53L0X sensors[SENSOR_COUNT];

enum FlashingStatus : uint8_t {
  FlashingStatusOff = 0,
  FlashingStatusWhite = 1,
  FlashingStatusDark = 2,
};

portMUX_TYPE stateLock = portMUX_INITIALIZER_UNLOCKED;

volatile uint32_t fuelCount = 0;
volatile uint32_t lastReplyTimeMs = 0;
volatile bool shouldCountFuel = true;

volatile uint8_t ledR = 0;
volatile uint8_t ledG = 0;
volatile uint8_t ledB = 0;
volatile FlashingStatus flashingStatus = FlashingStatusOff;
volatile bool sensorStateResetRequested = false;

bool hasReceivedReplySinceConnect = false;


void setup() {
  Serial.begin(115200);

  delay(100);

  strip.begin();           // INITIALIZE NeoPixel strip object (REQUIRED)
  strip.show();            // Turn OFF all pixels ASAP
  strip.setBrightness(50); // Set BRIGHTNESS to about 1/5 (max = 255)

  ledR = 0;
  ledG = 255;
  ledB = 255;
  flashingStatus = FlashingStatusOff;
  colorWipe(strip.Color(255, 0, 255));

  Wire.begin();
  Wire.setClock(50000);

  // Disable all sensors first so we can address them one by one.
  for (int i = 0; i < SENSOR_COUNT; i++) {
    pinMode(XSHUT_PINS[i], OUTPUT);
    digitalWrite(XSHUT_PINS[i], LOW);
  }
  delay(10);

  for (int i = 0; i < SENSOR_COUNT; i++) {
    digitalWrite(XSHUT_PINS[i], HIGH);
    delay(10);
    if (!sensors[i].begin(SENSOR_BASE_ADDR + i)) {
      Serial.printf("Failed to boot VL53L0X sensor %d (XSHUT pin %d)\n", i, XSHUT_PINS[i]);
      while (1);
    }
    sensors[i].startRangeContinuous();
    Serial.printf("[HUB] Sensor %d ready at I2C 0x%02X\n", i, SENSOR_BASE_ADDR + i);
  }

  xTaskCreatePinnedToCore(networkLedTask, "network_led", 6144, nullptr, 1, nullptr, NETWORK_LED_CORE);
  xTaskCreatePinnedToCore(ballCountTask, "ball_count", 4096, nullptr, 1, nullptr, BALL_COUNT_CORE);
}

void loop() {
  delay(1000);
}

void colorWipe(uint32_t color) {
  for(int i=0; i<strip.numPixels(); i++) { // For each pixel in strip...
    strip.setPixelColor(i, color);         //  Set pixel's color (in RAM)
  }
  strip.show();
}

void ballCountTask(void* parameter) {
  (void)parameter;

  int peakRange[SENSOR_COUNT] = {};
  int troughRange[SENSOR_COUNT] = {};
  bool hasSample[SENSOR_COUNT] = {};
  bool waitingForRise[SENSOR_COUNT] = {};
  uint32_t lastRangeSeenMs[SENSOR_COUNT] = {};
  uint32_t lastStaleLogMs[SENSOR_COUNT] = {};
  uint32_t lastDiagnosticLogMs = 0;
  uint32_t lastRecoveryAttemptMs[SENSOR_COUNT] = {};
  int lastValidRangeMm[SENSOR_COUNT] = {};
  uint32_t invalidRangeCount[SENSOR_COUNT] = {};

  while (true) {
    uint32_t now = millis();

    bool resetSensorStateNow = false;
    portENTER_CRITICAL(&stateLock);
    if (sensorStateResetRequested) {
      sensorStateResetRequested = false;
      resetSensorStateNow = true;
    }
    portEXIT_CRITICAL(&stateLock);

    if (resetSensorStateNow) {
      for (int i = 0; i < SENSOR_COUNT; i++) {
        peakRange[i] = 0;
        troughRange[i] = 0;
        hasSample[i] = false;
        waitingForRise[i] = false;
        lastRangeSeenMs[i] = 0;
        lastStaleLogMs[i] = 0;
        lastRecoveryAttemptMs[i] = now;
        lastValidRangeMm[i] = 0;
        invalidRangeCount[i] = 0;
        sensors[i].startRangeContinuous();
      }
      Serial.println("[HUB] Sensor state reset requested by clear_fuel_count.");
    }

    // Serial.printf("fuelCount: %d\n", fuelCount);

    for (int i = 0; i < SENSOR_COUNT; i++) {
      if (sensors[i].isRangeComplete()) {
        int rangemm = sensors[i].readRange();
        lastRangeSeenMs[i] = now;
        lastRecoveryAttemptMs[i] = now; // Reset recovery timer on successful read

        bool inValidBounds = rangemm >= SENSOR_VALID_MIN_MM && rangemm <= SENSOR_VALID_MAX_MM;
        bool plausibleStep = !hasSample[i] || abs(rangemm - lastValidRangeMm[i]) <= SENSOR_MAX_STEP_MM;
        if (!inValidBounds || !plausibleStep) {
          invalidRangeCount[i]++;

          if ((invalidRangeCount[i] % 10) == 1) {
            Serial.printf(
              "[HUB] Sensor %d invalid range %d mm (bounds %d-%d, plausible=%s, invalid_count=%lu); resetting state\n",
              i,
              rangemm,
              SENSOR_VALID_MIN_MM,
              SENSOR_VALID_MAX_MM,
              plausibleStep ? "yes" : "no",
              (unsigned long)invalidRangeCount[i]);
          }

          hasSample[i] = false;
          waitingForRise[i] = false;
          sensors[i].startRangeContinuous();
          continue;
        }

        lastValidRangeMm[i] = rangemm;

        // if (i == 0) {
        //   Serial.printf("range[%d]: %04d mm\n", i, rangemm);
        // }

        if (!hasSample[i]) {
          hasSample[i] = true;
          peakRange[i] = rangemm;
          troughRange[i] = rangemm;
          continue;
        }

        if (!waitingForRise[i]) {
          if (rangemm > peakRange[i]) {
            peakRange[i] = rangemm;
          }

          if ((peakRange[i] - rangemm) >= BALL_DETECT_DROP_MM) {
            waitingForRise[i] = true;
            troughRange[i] = rangemm;
          }
        } else {
          if (rangemm < troughRange[i]) {
            troughRange[i] = rangemm;
          }

          if ((rangemm - troughRange[i]) >= BALL_DETECT_RISE_MM) {
            portENTER_CRITICAL(&stateLock);
            if (shouldCountFuel) {
              fuelCount++;
            }
            portEXIT_CRITICAL(&stateLock);

            waitingForRise[i] = false;
            peakRange[i] = rangemm;
            troughRange[i] = rangemm;
          }
        }
      } else if (hasSample[i]) {
        uint32_t staleMs = now - lastRangeSeenMs[i];
        
        // Try recovery at 500ms, 2000ms, and 10000ms intervals
        if (staleMs >= SENSOR_STALE_MS) {
          bool shouldRecover = false;
          
          if (staleMs >= 10000 && now - lastRecoveryAttemptMs[i] >= 2000) {
            shouldRecover = true;
          } else if (staleMs >= 2000 && now - lastRecoveryAttemptMs[i] >= 1000) {
            shouldRecover = true;
          } else if (staleMs >= SENSOR_STALE_MS && now - lastRecoveryAttemptMs[i] >= 500) {
            shouldRecover = true;
          }
          
          if (shouldRecover) {
            Serial.printf("[HUB] Sensor %d stale for %lu ms, attempting recovery\n", i, (unsigned long)staleMs);
            lastRecoveryAttemptMs[i] = now;
            sensors[i].startRangeContinuous();
          }
        }
      }
    }

    // Periodic diagnostic logging to track sensor health
    if (now - lastDiagnosticLogMs >= 5000) {
      lastDiagnosticLogMs = now;
      Serial.println("[HUB] === Sensor Diagnostic Report ===");
      uint32_t currentFuelCount;
      portENTER_CRITICAL(&stateLock);
      currentFuelCount = fuelCount;
      portEXIT_CRITICAL(&stateLock);
      
      Serial.printf("[HUB] Current fuel count: %lu\n", (unsigned long)currentFuelCount);
      for (int i = 0; i < SENSOR_COUNT; i++) {
        if (!hasSample[i]) {
          Serial.printf("[HUB]   Sensor %d: No samples yet\n", i);
        } else {
          uint32_t staleMs = now - lastRangeSeenMs[i];
          Serial.printf("[HUB]   Sensor %d: %s (stale %lu ms, %s)\n", 
            i,
            waitingForRise[i] ? "WaitingForRise" : "AcquiringPeak",
            (unsigned long)staleMs,
            staleMs >= SENSOR_STALE_MS ? "STALE" : "OK");
        }
        if (invalidRangeCount[i] > 0) {
          Serial.printf("[HUB]   Sensor %d invalid samples: %lu\n", i, (unsigned long)invalidRangeCount[i]);
        }
      }
      Serial.println("[HUB] ================================");
    }

    // Serial.printf("fuelCount: %d, shouldCountFuel: %d\n", fuelCount, shouldCountFuel);

    vTaskDelay(pdMS_TO_TICKS(1));
  }
}

void networkLedTask(void* parameter) {
  (void)parameter;

  uint32_t lastHeartbeatMs = 0;
  uint32_t lastTaskLogMs = 0;

  while (true) {
    uint32_t now = millis();
    
    if (WiFi.status() != WL_CONNECTED) {
      Serial.println("[HUB] WiFi lost; reconnecting.");
      connectWifi();
    }

    if (!client.connected()) {
      Serial.println("[HUB] TCP lost; reconnecting.");
      connectFms();
    }

    renderLed();

    if (now - lastHeartbeatMs >= HEARTBEAT_INTERVAL_MS) {
      lastHeartbeatMs = now;

      uint32_t sentAtMs = sendHeartbeat();
      receiveReply(sentAtMs);
    }
    
    // Log network task status periodically
    if (now - lastTaskLogMs >= 10000) {
      lastTaskLogMs = now;
      Serial.printf("[HUB] Network task alive - WiFi: %s, TCP: %s\n", 
        WiFi.status() == WL_CONNECTED ? "OK" : "FAILED",
        client.connected() ? "OK" : "DISCONNECTED");
    }

    vTaskDelay(pdMS_TO_TICKS(5));
  }
}

void renderLed() {
  uint8_t currentR;
  uint8_t currentG;
  uint8_t currentB;
  FlashingStatus currentFlashingStatus;

  portENTER_CRITICAL(&stateLock);
  currentR = ledR;
  currentG = ledG;
  currentB = ledB;
  currentFlashingStatus = flashingStatus;
  portEXIT_CRITICAL(&stateLock);

  uint8_t renderedR = currentR;
  uint8_t renderedG = currentG;
  uint8_t renderedB = currentB;

  if (currentFlashingStatus != FlashingStatusOff) {
    float phase = (float)(millis() % (FLASH_INTERVAL_MS * 2)) / (float)(FLASH_INTERVAL_MS * 2);
    float blend = 0.5f - 0.5f * cosf(2.0f * PI * phase);

    uint8_t targetR = currentFlashingStatus == FlashingStatusWhite ? 255 : 0;
    uint8_t targetG = currentFlashingStatus == FlashingStatusWhite ? 255 : 0;
    uint8_t targetB = currentFlashingStatus == FlashingStatusWhite ? 255 : 0;

    renderedR = (uint8_t)roundf(currentR + ((targetR - currentR) * blend));
    renderedG = (uint8_t)roundf(currentG + ((targetG - currentG) * blend));
    renderedB = (uint8_t)roundf(currentB + ((targetB - currentB) * blend));
  }

  colorWipe(strip.Color(renderedG, renderedR, renderedB));
}

// =============================================================================
//  WiFi helpers
// =============================================================================
void connectWifi() {
    WiFi.mode(WIFI_STA);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
    Serial.print("[HUB] Connecting to WiFi");
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print(".");
    }
    Serial.printf("\n[HUB] WiFi connected - IP: %s\n",
                  WiFi.localIP().toString().c_str());
}

// =============================================================================
//  FMS TCP helpers
// =============================================================================
void connectFms() {
  client.stop();
  Serial.printf("[HUB] Connecting to FMS %s:%d\n", FMS_HOST, FMS_PORT);

  portENTER_CRITICAL(&stateLock);
  ledR = 0;
  ledG = 255;
  ledB = 255;
  flashingStatus = FlashingStatusOff;
  lastReplyTimeMs = 0;
  shouldCountFuel = true;
  portEXIT_CRITICAL(&stateLock);

  colorWipe(strip.Color(255, 0, 255));

    while (!client.connect(FMS_HOST, FMS_PORT)) {
    Serial.println("[HUB] FMS connection failed; retrying in 1 s.");
        delay(1000);
    }
  client.setNoDelay(true);

  hasReceivedReplySinceConnect = false;
  Serial.println("[HUB] FMS connected.");
}

// ── Build and send a hub heartbeat ───────────────────────────────────────────
uint32_t sendHeartbeat() {
  uint32_t fuelCountSnapshot;
  uint32_t lastReplyTimeSnapshot;

  portENTER_CRITICAL(&stateLock);
  fuelCountSnapshot = fuelCount;
  lastReplyTimeSnapshot = lastReplyTimeMs;
  portEXIT_CRITICAL(&stateLock);

    BsonEncoder enc;
    enc.begin();
    enc.addString("name",             DEVICE_NAME);
    enc.addString("type",             "hub");
  enc.addInt32 ("last_reply_time_ms", (int32_t)lastReplyTimeSnapshot);
  enc.addString("alliance",         ALLIANCE_NAME);
  enc.addInt32 ("fuel_count",       (int32_t)fuelCountSnapshot);
    enc.end();

  uint32_t sentAtMs = millis();
  client.write(enc.buf, enc.length());
  return sentAtMs;
}

// ── Read and parse the server reply ──────────────────────────────────────────
void receiveReply(uint32_t sentAtMs) {
  uint32_t replyTimeoutMs = hasReceivedReplySinceConnect
    ? REPLY_TIMEOUT_MS
    : INITIAL_REPLY_TIMEOUT_MS;

    uint32_t start = millis();
    while (client.available() < 4) {
    if (millis() - start > replyTimeoutMs) {
      Serial.println("[HUB] Reply timeout.");
            return;
        }
        delay(1);
    }

    uint8_t lenBuf[4];
    client.readBytes(lenBuf, 4);
    int32_t docLen = (int32_t)(lenBuf[0]
                             | ((uint32_t)lenBuf[1] <<  8)
                             | ((uint32_t)lenBuf[2] << 16)
                             | ((uint32_t)lenBuf[3] << 24));

    if (docLen < 5 || docLen > (int32_t)RX_BUF_SIZE) {
                  Serial.printf("[HUB] Bad reply length: %d\n", docLen);
        return;
    }

    uint8_t rxBuf[RX_BUF_SIZE];
    memcpy(rxBuf, lenBuf, 4);
    int remaining = docLen - 4;
    start = millis();
    while (client.available() < remaining) {
      if (millis() - start > REPLY_BODY_TIMEOUT_MS) {
        Serial.println("[HUB] Reply body timeout.");
            return;
        }
        delay(1);
    }
    client.readBytes(rxBuf + 4, remaining);

    BsonDecoder dec(rxBuf, docLen);
    bool accepted = dec.getBool("accepted", false);
    if (!accepted) {
        String err = dec.getString("error", "(no error field)");
        Serial.printf("[HUB] Server rejected message: %s\n", err.c_str());
        return;
    }

    FlashingStatus nextFlashingStatus = FlashingStatusOff;
    String flashingStatusValue = dec.getString("flashing_status", "off");
    if (flashingStatusValue == "flash_white") {
      nextFlashingStatus = FlashingStatusWhite;
    } else if (flashingStatusValue == "flash_off") {
      nextFlashingStatus = FlashingStatusDark;
    }

    bool nextShouldCountFuel = dec.getBool("should_count_fuel", true);
    bool clearFuelCount = dec.getBool("clear_fuel_count", false);

    portENTER_CRITICAL(&stateLock);
    lastReplyTimeMs = millis() - sentAtMs;
    shouldCountFuel = nextShouldCountFuel;
    if (clearFuelCount) {
      fuelCount = 0;
      sensorStateResetRequested = true;
    }
    ledR = (uint8_t)dec.getInt32("led_r", 0);
    ledG = (uint8_t)dec.getInt32("led_g", 0);
    ledB = (uint8_t)dec.getInt32("led_b", 0);
    flashingStatus = nextFlashingStatus;
    portEXIT_CRITICAL(&stateLock);

    hasReceivedReplySinceConnect = true;
}