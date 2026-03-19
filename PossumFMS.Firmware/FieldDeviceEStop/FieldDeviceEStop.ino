// =============================================================================
//  fms_estop.ino  –  FMS E-Stop / A-Stop Device Firmware for ESP32
//
//  Behaviour:
//    • Connects to WiFi, then opens a TCP socket to the FMS server.
//    • Every 20 ms sends a BSON heartbeat:
//        { name, type:"estop", estop_activated, astop_activated }
//    • Both buttons are active-low (pulled high internally; press = GND).
//    • Hardware debounce is handled in software (50 ms).
//    • Auto-reconnects WiFi and TCP if either drops.
//
//  Wiring (change pin numbers to match your board):
//    ESTOP_PIN   GPIO 15  – E-Stop button (NO contact, other leg to GND)
//    ASTOP_PIN   GPIO 18  – A-Stop button (NO contact, other leg to GND)
//
//  Safety note:  For a real competition you should ALSO latch the e-stop in
//  hardware so a dropped TCP connection never accidentally clears it.
// =============================================================================

#include <WiFi.h>
#include "wifi_secrets.h"
#include "bson_helper.h"

// ── User configuration ────────────────────────────────────────────────────────
const char* FMS_HOST      = "192.168.1.167";
const uint16_t FMS_PORT   = 1678;

const char* DEVICE_NAME   = "estop_1";   // unique label shown in FMS

const uint32_t HEARTBEAT_INTERVAL_MS = 50;
const uint32_t DEBOUNCE_MS           = 50;

// GPIO pins (active-low with internal pull-up)
const int ESTOP_PIN = 15;
const int ASTOP_PIN = 18;

// Receive buffer
const size_t RX_BUF_SIZE = 256;
// ─────────────────────────────────────────────────────────────────────────────

WiFiClient client;

volatile boolean estopped = false;
volatile boolean astopped = false;

void ARDUINO_ISR_ATTR estopBtnCallback() {
    estopped = true;
}

void ARDUINO_ISR_ATTR astopBtnCallback() {
    astopped = true;
}

// ── Setup ─────────────────────────────────────────────────────────────────────
void setup() {
    Serial.begin(115200);
    Serial.println("[ESTOP] Booting…");

    pinMode(ESTOP_PIN, INPUT_PULLDOWN);
    pinMode(ASTOP_PIN, INPUT_PULLDOWN);

    attachInterrupt(digitalPinToInterrupt(ESTOP_PIN), estopBtnCallback, FALLING);
    attachInterrupt(digitalPinToInterrupt(ASTOP_PIN), astopBtnCallback, FALLING);


    connectWifi();
    connectFms();
}

// ── Main loop ─────────────────────────────────────────────────────────────────
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

    // ── Poll buttons (debounced) ─────────────────────────────────────────────
    // readButton(ESTOP_PIN, estopBtn);
    // readButton(ASTOP_PIN, astopBtn);

    // ── Heartbeat ────────────────────────────────────────────────────────────
    uint32_t now = millis();
    
    if (now - lastHeartbeatMs >= HEARTBEAT_INTERVAL_MS) {
        lastHeartbeatMs = now;
        sendHeartbeat(estopped, astopped);
        if (estopped) {
            Serial.println("Attempting to send E-Stop!");
        }
        if (astopped) {
            Serial.println("Attempting to send A-Stop!");
        }
        receiveReply();
    }
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
void sendHeartbeat(bool estopActivated, bool astopActivated) {
    BsonEncoder enc;
    enc.begin();
    enc.addString("name",             DEVICE_NAME);
    enc.addString("type",             "estop");
    enc.addBool  ("estop_activated",  estopActivated);
    enc.addBool  ("astop_activated",  astopActivated);
    enc.end();

    client.write(enc.buf, enc.length());
}

// ── Read and parse the server reply ──────────────────────────────────────────
// E-stop reply only has: accepted (bool) — and error (string) on failure.
void receiveReply() {
    // Wait up to 50 ms for at least the 4-byte length header.
    uint32_t start = millis();
    while (client.available() < 4) {
        if (millis() - start > 50) {
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
        return;
    }
    
    // Heartbeat was accepted and read successfully. Clear the latched button states.
    if (estopped) {
        Serial.println("E-Stop accepted by server.");
    }
    if (astopped) {
        Serial.println("A-Stop accepted by server.");
    }
    estopped = false;
    astopped = false;
}
