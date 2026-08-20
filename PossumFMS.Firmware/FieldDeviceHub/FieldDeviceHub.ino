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
const uint32_t BALL_DEBOUNCE_MS = 100;
const uint32_t BALL_MIN_BLOCKED_MS = 50;
const uint32_t TCP_RECONNECT_INTERVAL_MS = 1000;
const uint32_t WIFI_RECONNECT_INTERVAL_MS = 10000;

const size_t RX_BUF_SIZE = 256;

#if CONFIG_FREERTOS_UNICORE
const BaseType_t NETWORK_LED_CORE = 0;
const BaseType_t BALL_COUNT_CORE = 0;
#else
const BaseType_t NETWORK_LED_CORE = 1;
const BaseType_t BALL_COUNT_CORE = 0;
#endif

enum ConnectionState {
  CONN_STATE_WIFI_DISCONNECTED,
  CONN_STATE_WIFI_CONNECTING,
  CONN_STATE_TCP_CONNECTING,
  CONN_STATE_CONNECTED
};

WiFiClient client;

#define LED_PIN   13
#define LED_COUNT 175

Adafruit_NeoPixel strip(LED_COUNT, LED_PIN, NEO_RGBW + NEO_KHZ800);

// E18-D80NK infrared photoelectric switches (NPN, active-low: LOW = ball detected)
const int SENSOR_PINS[]  = { 2, 4, 18, 19 };
const int SENSOR_COUNT   = sizeof(SENSOR_PINS) / sizeof(SENSOR_PINS[0]);

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
volatile uint8_t ledG = 255;
volatile uint8_t ledB = 255;
volatile FlashingStatus flashingStatus = FlashingStatusOff;
volatile bool sensorStateResetRequested = false;

bool hasReceivedReplySinceConnect = false;

// ── Forward Declarations ──────────────────────────────────────────────────────
void ballCountTask(void* parameter);
void networkLedTask(void* parameter);
void renderLed();
void colorWipe(uint32_t color);
uint32_t sendHeartbeat();
void receiveReply(uint32_t sentAtMs);

void setup() {
  Serial.begin(115200);
  delay(100);

  strip.begin();
  strip.show();
  strip.setBrightness(50);

  ledR = 0;
  ledG = 255;
  ledB = 255;
  flashingStatus = FlashingStatusOff;
  colorWipe(strip.Color(255, 0, 255));

  for (int i = 0; i < SENSOR_COUNT; i++) {
    pinMode(SENSOR_PINS[i], INPUT_PULLUP);
    Serial.printf("[HUB] Photoelectric sensor %d on pin %d\n", i, SENSOR_PINS[i]);
  }

  xTaskCreatePinnedToCore(networkLedTask, "network_led", 6144, nullptr, 1, nullptr, NETWORK_LED_CORE);
  xTaskCreatePinnedToCore(ballCountTask, "ball_count", 4096, nullptr, 1, nullptr, BALL_COUNT_CORE);
}

void loop() {
  delay(1000);
}

void colorWipe(uint32_t color) {
  for(int i = 0; i < strip.numPixels(); i++) {
    strip.setPixelColor(i, color);
  }
  strip.show();
}

void ballCountTask(void* parameter) {
  (void)parameter;

  bool lastState[SENSOR_COUNT];
  uint32_t lastCountMs[SENSOR_COUNT];
  uint32_t sensorCount[SENSOR_COUNT];
  uint32_t blockedSinceMs[SENSOR_COUNT];
  uint32_t lastDiagnosticLogMs = 0;

  for (int i = 0; i < SENSOR_COUNT; i++) {
    lastState[i] = (digitalRead(SENSOR_PINS[i]) == HIGH);
    lastCountMs[i] = 0;
    sensorCount[i] = 0;
    blockedSinceMs[i] = 0;
  }

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
        lastState[i] = (digitalRead(SENSOR_PINS[i]) == HIGH);
        lastCountMs[i] = 0;
        sensorCount[i] = 0;
        blockedSinceMs[i] = 0;
      }
      Serial.println("[HUB] Sensor state reset requested by clear_fuel_count.");
    }

    for (int i = 0; i < SENSOR_COUNT; i++) {
      bool currentState = (digitalRead(SENSOR_PINS[i]) == HIGH);

      if (!currentState && lastState[i]) {
        blockedSinceMs[i] = now;
      } else if (currentState && !lastState[i]) {
        uint32_t blockedDurationMs = now - blockedSinceMs[i];
        if (blockedDurationMs >= BALL_MIN_BLOCKED_MS && now - lastCountMs[i] >= BALL_DEBOUNCE_MS) {
          portENTER_CRITICAL(&stateLock);
          if (shouldCountFuel) {
            fuelCount++;
          }
          portEXIT_CRITICAL(&stateLock);
          sensorCount[i]++;
          Serial.printf("[HUB] Ball detected on sensor %d (blocked %lu ms, sensor total: %lu)\n",
            i, (unsigned long)blockedDurationMs, (unsigned long)sensorCount[i]);
          lastCountMs[i] = now;
        } else if (blockedDurationMs < BALL_MIN_BLOCKED_MS) {
          Serial.printf("[HUB] Sensor %d: pulse ignored (blocked only %lu ms, below %lu ms minimum)\n",
            i, (unsigned long)blockedDurationMs, (unsigned long)BALL_MIN_BLOCKED_MS);
        }
      }

      lastState[i] = currentState;
    }

    if (now - lastDiagnosticLogMs >= 5000) {
      lastDiagnosticLogMs = now;
      uint32_t currentFuelCount;
      portENTER_CRITICAL(&stateLock);
      currentFuelCount = fuelCount;
      portEXIT_CRITICAL(&stateLock);
      Serial.printf("[HUB] Fuel count: %lu\n", (unsigned long)currentFuelCount);
      for (int i = 0; i < SENSOR_COUNT; i++) {
        Serial.printf("[HUB]   Sensor %d (pin %d): %lu counts, currently %s\n",
          i, SENSOR_PINS[i], (unsigned long)sensorCount[i], lastState[i] ? "clear" : "blocked");
      }
    }

    vTaskDelay(pdMS_TO_TICKS(1));
  }
}

void networkLedTask(void* parameter) {
  (void)parameter;

  ConnectionState connState = CONN_STATE_WIFI_DISCONNECTED;
  uint32_t lastStateActionMs = 0;
  uint32_t lastHeartbeatMs = 0;
  uint32_t lastTaskLogMs = 0;

  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  connState = CONN_STATE_WIFI_CONNECTING;
  lastStateActionMs = millis();

  while (true) {
    uint32_t now = millis();

    // ── Non-blocking connection state machine ──────────────────────────────
    switch (connState) {
      case CONN_STATE_WIFI_DISCONNECTED:
        Serial.println("[HUB] Reconnecting WiFi…");
        WiFi.mode(WIFI_STA);
        WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
        connState = CONN_STATE_WIFI_CONNECTING;
        lastStateActionMs = now;
        break;

      case CONN_STATE_WIFI_CONNECTING:
        if (WiFi.status() == WL_CONNECTED) {
          Serial.printf("\n[HUB] WiFi connected – IP: %s\n", WiFi.localIP().toString().c_str());
          connState = CONN_STATE_TCP_CONNECTING;
          lastStateActionMs = 0;
        } else if (now - lastStateActionMs >= WIFI_RECONNECT_INTERVAL_MS) {
          Serial.println("\n[HUB] WiFi connection timeout; retrying.");
          WiFi.disconnect();
          connState = CONN_STATE_WIFI_DISCONNECTED;
        }
        break;

      case CONN_STATE_TCP_CONNECTING:
        if (WiFi.status() != WL_CONNECTED) {
          Serial.println("[HUB] WiFi lost while connecting TCP.");
          client.stop();
          connState = CONN_STATE_WIFI_DISCONNECTED;
          break;
        }

        if (now - lastStateActionMs >= TCP_RECONNECT_INTERVAL_MS) {
          lastStateActionMs = now;
          Serial.printf("[HUB] Connecting to FMS %s:%d…\n", FMS_HOST, FMS_PORT);
          client.stop();
          if (client.connect(FMS_HOST, FMS_PORT)) {
            client.setNoDelay(true);
            portENTER_CRITICAL(&stateLock);
            ledR = 0;
            ledG = 255;
            ledB = 255;
            flashingStatus = FlashingStatusOff;
            lastReplyTimeMs = 0;
            shouldCountFuel = true;
            portEXIT_CRITICAL(&stateLock);

            hasReceivedReplySinceConnect = false;
            connState = CONN_STATE_CONNECTED;
            Serial.println("[HUB] FMS connected.");
          } else {
            Serial.println("[HUB] FMS connection failed; will retry.");
          }
        }
        break;

      case CONN_STATE_CONNECTED:
        if (WiFi.status() != WL_CONNECTED) {
          Serial.println("[HUB] WiFi lost – resetting socket.");
          client.stop();
          connState = CONN_STATE_WIFI_DISCONNECTED;
        } else if (!client.connected()) {
          Serial.println("[HUB] TCP lost; reconnecting.");
          client.stop();
          connState = CONN_STATE_TCP_CONNECTING;
          lastStateActionMs = now;
        }
        break;
    }

    // Always render LEDs smoothly each tick
    renderLed();

    if (connState == CONN_STATE_CONNECTED) {
      if (now - lastHeartbeatMs >= HEARTBEAT_INTERVAL_MS) {
        lastHeartbeatMs = now;
        uint32_t sentAtMs = sendHeartbeat();
        receiveReply(sentAtMs);
      }
    }

    if (now - lastTaskLogMs >= 10000) {
      lastTaskLogMs = now;
      Serial.printf("[HUB] Network task alive – State: %d, WiFi: %s, TCP: %s\n",
        connState,
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
  enc.addString("name",               DEVICE_NAME);
  enc.addString("type",               "hub");
  enc.addInt32 ("last_reply_time_ms", (int32_t)lastReplyTimeSnapshot);
  enc.addString("alliance",           ALLIANCE_NAME);
  enc.addInt32 ("fuel_count",         (int32_t)fuelCountSnapshot);
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