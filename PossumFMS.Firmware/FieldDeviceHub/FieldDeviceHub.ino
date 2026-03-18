#include "Adafruit_VL53L0X.h"
#include <Adafruit_NeoPixel.h>
#include <WiFi.h>
#include "bson_helper.h"
#include "wifi_secrets.h"

const char* FMS_HOST      = "192.168.1.167";
const uint16_t FMS_PORT   = 1678;

const char* DEVICE_NAME   = "red_hub";   // unique label shown in FMS

const uint32_t HEARTBEAT_INTERVAL_MS = 50;
const uint32_t DEBOUNCE_MS           = 50;

const size_t RX_BUF_SIZE = 256;

WiFiClient client;

#define LED_PIN   13
#define LED_COUNT 60

Adafruit_NeoPixel strip(LED_COUNT, LED_PIN, NEO_RGBW + NEO_KHZ800);

Adafruit_VL53L0X lox = Adafruit_VL53L0X();


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

  connectWifi();
  connectFms();

  lox.startRangeContinuous();
}

uint numBalls = 0;
bool seeBall = false;

uint8_t r = 0;
uint8_t g = 0;
uint8_t b = 0;

bool flash = false;
bool flashWhite = false;

VL53L0X_RangingMeasurementData_t measure;

void loop() {
  static uint32_t lastHeartbeatMs = 0;

  // ── Reconnect if needed ──────────────────────────────────────────────────
  if (WiFi.status() != WL_CONNECTED) {
      Serial.println("[ESTOP] WiFi lost – reconnecting…");
      connectWifi();
  }
  if (!client.connected()) {
      Serial.println("[ESTOP] TCP lost – reconnecting…");
      connectFms();
  }

  if (lox.isRangeComplete()) {
    int rangemm = lox.readRange();

    if (!seeBall && rangemm < 70) {
      seeBall = true;
      numBalls++;
    }

    if (seeBall && rangemm > 120) {
      seeBall = false;
    }
  }

  colorWipe(strip.Color(g, r, b));

  // ── Heartbeat ────────────────────────────────────────────────────────────
  uint32_t now = millis();
  
  if (now - lastHeartbeatMs >= HEARTBEAT_INTERVAL_MS) {
      lastHeartbeatMs = now;
      sendHeartbeat();
      receiveReply();
  }
}

void colorWipe(uint32_t color) {
  for(int i=0; i<strip.numPixels(); i++) { // For each pixel in strip...
    strip.setPixelColor(i, color);         //  Set pixel's color (in RAM)
  }
  strip.show();
}

// =============================================================================
//  WiFi helpers
// =============================================================================
void connectWifi() {
    WiFi.mode(WIFI_STA);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
    Serial.print("[ESTOP] Connecting to WiFi");
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print(".");
    }
    Serial.printf("\n[ESTOP] WiFi connected – IP: %s\n",
                  WiFi.localIP().toString().c_str());
}

// =============================================================================
//  FMS TCP helpers
// =============================================================================
void connectFms() {
    Serial.printf("[ESTOP] Connecting to FMS %s:%d…\n", FMS_HOST, FMS_PORT);
    while (!client.connect(FMS_HOST, FMS_PORT)) {
        Serial.println("[ESTOP] FMS connection failed – retrying in 1 s…");
        delay(1000);
    }
    Serial.println("[ESTOP] FMS connected.");
}

// ── Build and send an e-stop heartbeat ───────────────────────────────────────
void sendHeartbeat() {
    BsonEncoder enc;
    enc.begin();
    enc.addString("name",             DEVICE_NAME);
    enc.addString("type",             "hub");
    enc.addString  ("alliance",  "red");
    enc.addInt32  ("fuel_delta",  numBalls);
    enc.end();

    numBalls = 0;

    client.write(enc.buf, enc.length());
}

// ── Read and parse the server reply ──────────────────────────────────────────
// E-stop reply only has: accepted (bool) — and error (string) on failure.
void receiveReply() {
    // Wait up to 100 ms for at least the 4-byte length header.
    uint32_t start = millis();
    while (client.available() < 4) {
        if (millis() - start > 100) {
            Serial.println("[ESTOP] Reply timeout.");
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
        Serial.printf("[ESTOP] Bad reply length: %d\n", docLen);
        return;
    }

    uint8_t rxBuf[RX_BUF_SIZE];
    memcpy(rxBuf, lenBuf, 4);
    int remaining = docLen - 4;
    start = millis();
    while (client.available() < remaining) {
        if (millis() - start > 200) {
            Serial.println("[ESTOP] Reply body timeout.");
            return;
        }
        delay(1);
    }
    client.readBytes(rxBuf + 4, remaining);

    BsonDecoder dec(rxBuf, docLen);
    bool accepted = dec.getBool("accepted", false);
    if (!accepted) {
        String err = dec.getString("error", "(no error field)");
        Serial.printf("[ESTOP] Server rejected message: %s\n", err.c_str());
    }

    r = dec.getInt32("led_r", 0);
    g = dec.getInt32("led_g", 0);
    b = dec.getInt32("led_b", 0);
}