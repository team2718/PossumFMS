#include "Adafruit_VL53L0X.h"
#include <Adafruit_NeoPixel.h>
#include <esp_system.h>
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

Adafruit_VL53L0X lox = Adafruit_VL53L0X();

enum FlashingStatus : uint8_t {
  FlashingStatusOff = 0,
  FlashingStatusWhite = 1,
  FlashingStatusDark = 2,
};

portMUX_TYPE stateLock = portMUX_INITIALIZER_UNLOCKED;

volatile uint32_t pendingFuelDelta = 0;
volatile uint32_t inFlightFuelDelta = 0;
volatile uint32_t lastReplyTimeMs = 0;

volatile uint8_t ledR = 0;
volatile uint8_t ledG = 0;
volatile uint8_t ledB = 0;
volatile FlashingStatus flashingStatus = FlashingStatusOff;

bool hasInFlightHeartbeat = false;
bool hasReceivedReplySinceConnect = false;
uint16_t heartbeatSequence = 1;
uint16_t heartbeatPrefix = 0;
uint32_t currentHeartbeatId = 1;


void setup() {
  Serial.begin(115200);

  strip.begin();           // INITIALIZE NeoPixel strip object (REQUIRED)
  strip.show();            // Turn OFF all pixels ASAP
  strip.setBrightness(50); // Set BRIGHTNESS to about 1/5 (max = 255)

  Serial.println("sigma sigma sigma");
  if (!lox.begin()) {
    Serial.println(F("Failed to boot VL53L0X"));
    while(1);
  }

  heartbeatPrefix = (uint16_t)(esp_random() & 0x7FFFu);
  if (heartbeatPrefix == 0) {
    heartbeatPrefix = 1;
  }
  currentHeartbeatId = (((uint32_t)heartbeatPrefix) << 16) | heartbeatSequence;

  lox.startRangeContinuous();

  xTaskCreatePinnedToCore(networkLedTask, "network_led", 6144, nullptr, 1, nullptr, NETWORK_LED_CORE);
  xTaskCreatePinnedToCore(ballCountTask, "ball_count", 4096, nullptr, 1, nullptr, BALL_COUNT_CORE);
}

VL53L0X_RangingMeasurementData_t measure;

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

  bool seeBall = false;

  while (true) {
    if (lox.isRangeComplete()) {
      int rangemm = lox.readRange();

      if (!seeBall && rangemm < 70) {
        seeBall = true;
        portENTER_CRITICAL(&stateLock);
        pendingFuelDelta++;
        portEXIT_CRITICAL(&stateLock);
      }

      if (seeBall && rangemm > 120) {
        seeBall = false;
      }
    }

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

      uint32_t sentFuelDelta = 0;
      uint32_t sentHeartbeatId = 0;
      uint32_t sentAtMs = sendHeartbeat(&sentFuelDelta, &sentHeartbeatId);
      receiveReply(sentAtMs, sentFuelDelta, sentHeartbeatId);
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

  uint32_t baseColor = strip.Color(currentG, currentR, currentB);
  bool flashOn = ((millis() / FLASH_INTERVAL_MS) % 2) == 0;

  if (currentFlashingStatus == FlashingStatusWhite && flashOn) {
    colorWipe(strip.Color(255, 255, 255));
    return;
  }

  if (currentFlashingStatus == FlashingStatusDark && flashOn) {
    colorWipe(strip.Color(0, 0, 0));
    return;
  }

  colorWipe(baseColor);
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
    while (!client.connect(FMS_HOST, FMS_PORT)) {
    Serial.println("[HUB] FMS connection failed; retrying in 1 s.");
        delay(1000);
    }
  client.setNoDelay(true);

  portENTER_CRITICAL(&stateLock);
  lastReplyTimeMs = 0;
  portEXIT_CRITICAL(&stateLock);

  hasReceivedReplySinceConnect = false;
  Serial.println("[HUB] FMS connected.");
}

// ── Build and send an e-stop heartbeat ───────────────────────────────────────
void advanceHeartbeatId() {
  heartbeatSequence++;
  if (heartbeatSequence == 0) {
    heartbeatPrefix = (uint16_t)(esp_random() & 0x7FFFu);
    if (heartbeatPrefix == 0) {
      heartbeatPrefix = 1;
    }
    heartbeatSequence = 1;
  }

  currentHeartbeatId = (((uint32_t)heartbeatPrefix) << 16) | heartbeatSequence;
}

uint32_t sendHeartbeat(uint32_t* sentFuelDelta, uint32_t* sentHeartbeatId) {
  uint32_t pendingFuelDeltaSnapshot;
  uint32_t inFlightFuelDeltaSnapshot;
  uint32_t lastReplyTimeSnapshot;

  portENTER_CRITICAL(&stateLock);
  if (!hasInFlightHeartbeat) {
    inFlightFuelDelta = pendingFuelDelta;
    pendingFuelDelta = 0;
    hasInFlightHeartbeat = true;
  }

  pendingFuelDeltaSnapshot = pendingFuelDelta;
  inFlightFuelDeltaSnapshot = inFlightFuelDelta;
  lastReplyTimeSnapshot = lastReplyTimeMs;
  portEXIT_CRITICAL(&stateLock);

    BsonEncoder enc;
    enc.begin();
    enc.addString("name",             DEVICE_NAME);
    enc.addString("type",             "hub");
  enc.addInt32 ("last_reply_time_ms", (int32_t)lastReplyTimeSnapshot);
  enc.addInt32 ("heartbeat_id",      (int32_t)currentHeartbeatId);
  enc.addString("alliance",         ALLIANCE_NAME);
  enc.addInt32 ("fuel_delta",       (int32_t)inFlightFuelDeltaSnapshot);
    enc.end();

  (void)pendingFuelDeltaSnapshot;
  *sentFuelDelta = inFlightFuelDeltaSnapshot;
  *sentHeartbeatId = currentHeartbeatId;

  uint32_t sentAtMs = millis();
  client.write(enc.buf, enc.length());
  return sentAtMs;
}

void acknowledgeFuelDelta(uint32_t sentHeartbeatId, uint32_t sentFuelDelta) {
  portENTER_CRITICAL(&stateLock);
  if (hasInFlightHeartbeat && currentHeartbeatId == sentHeartbeatId) {
    inFlightFuelDelta = 0;
    hasInFlightHeartbeat = false;
  }
  portEXIT_CRITICAL(&stateLock);

  (void)sentFuelDelta;
  if (currentHeartbeatId == sentHeartbeatId) {
    advanceHeartbeatId();
  }
}

// ── Read and parse the server reply ──────────────────────────────────────────
void receiveReply(uint32_t sentAtMs, uint32_t sentFuelDelta, uint32_t sentHeartbeatId) {
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

    uint32_t acceptedHeartbeatId = (uint32_t)dec.getInt32("accepted_heartbeat_id", -1);
    if (acceptedHeartbeatId != sentHeartbeatId) {
      Serial.printf("[HUB] Reply heartbeat mismatch: expected %lu got %lu\n",
                    (unsigned long)sentHeartbeatId,
                    (unsigned long)acceptedHeartbeatId);
      return;
    }

    FlashingStatus nextFlashingStatus = FlashingStatusOff;
    String flashingStatusValue = dec.getString("flashing_status", "off");
    if (flashingStatusValue == "flash_white") {
      nextFlashingStatus = FlashingStatusWhite;
    } else if (flashingStatusValue == "flash_off") {
      nextFlashingStatus = FlashingStatusDark;
    }

    portENTER_CRITICAL(&stateLock);
    lastReplyTimeMs = millis() - sentAtMs;
    ledR = (uint8_t)dec.getInt32("led_r", 0);
    ledG = (uint8_t)dec.getInt32("led_g", 0);
    ledB = (uint8_t)dec.getInt32("led_b", 0);
    flashingStatus = nextFlashingStatus;
    portEXIT_CRITICAL(&stateLock);

    hasReceivedReplySinceConnect = true;
    acknowledgeFuelDelta(sentHeartbeatId, sentFuelDelta);
}