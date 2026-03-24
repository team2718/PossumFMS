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

const int XSHUT_PINS[]          = { 25, 26 };
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

  Serial.println("sigma sigma sigma");

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

  bool seeBall[SENSOR_COUNT] = {};

  while (true) {
    for (int i = 0; i < SENSOR_COUNT; i++) {
      if (sensors[i].isRangeComplete()) {
        int rangemm = sensors[i].readRange();

        Serial.printf("range[%d]: %04d mm\n", i, rangemm);

        if (!seeBall[i] && rangemm < 70) {
          seeBall[i] = true;
          portENTER_CRITICAL(&stateLock);
          if (shouldCountFuel) {
            fuelCount++;
          }
          portEXIT_CRITICAL(&stateLock);
        }

        if (seeBall[i] && rangemm > 120) {
          seeBall[i] = false;
        }
      }
    }

    // Serial.printf("seeBall[0]: %d, fuelCount: %d, shouldCountFuel: %d\n", seeBall[0], fuelCount, shouldCountFuel);

    vTaskDelay(pdMS_TO_TICKS(1));
  }
}

void networkLedTask(void* parameter) {
  (void)parameter;

  uint32_t lastHeartbeatMs = 0;

  while (true) {
    if (WiFi.status() != WL_CONNECTED) {
      Serial.println("[HUB] WiFi lost; reconnecting.");
      connectWifi();
    }

    if (!client.connected()) {
      Serial.println("[HUB] TCP lost; reconnecting.");
      connectFms();
    }

    renderLed();

    uint32_t now = millis();
    if (now - lastHeartbeatMs >= HEARTBEAT_INTERVAL_MS) {
      lastHeartbeatMs = now;

      uint32_t sentAtMs = sendHeartbeat();
      receiveReply(sentAtMs);
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
    }
    ledR = (uint8_t)dec.getInt32("led_r", 0);
    ledG = (uint8_t)dec.getInt32("led_g", 0);
    ledB = (uint8_t)dec.getInt32("led_b", 0);
    flashingStatus = nextFlashingStatus;
    portEXIT_CRITICAL(&stateLock);

    hasReceivedReplySinceConnect = true;
}