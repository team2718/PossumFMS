// =============================================================================
//  fms_estop.ino  –  FMS E-Stop / A-Stop Device Firmware for ESP32
//
//  Behaviour:
//    • Connects to WiFi, then opens a TCP socket to the FMS server.
//    • Every 50 ms sends a BSON heartbeat:
//        { name, type:"estop", estop_activated, astop_activated }
//    • Both buttons are active-low (pulled high internally; press = GND).
//    • Hardware debounce is handled in software.
//    • Non-blocking state machine for WiFi and TCP auto-reconnection.
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

const char* DEVICE_NAME   = "estop_field";   // unique label shown in FMS
const char* ALLIANCE_NAME = "field";
const int STATION_NUMBER  = 0;

const uint32_t HEARTBEAT_INTERVAL_MS = 50;
const uint32_t REPLY_TIMEOUT_MS = 50;
const uint32_t INITIAL_REPLY_TIMEOUT_MS = 500;
const uint32_t REPLY_BODY_TIMEOUT_MS = 200;
const uint32_t TCP_RECONNECT_INTERVAL_MS = 1000;
const uint32_t WIFI_RECONNECT_INTERVAL_MS = 10000;

// GPIO pins (active-low with internal pull-up / pull-down)
const int ESTOP_PIN = 15;
const int ASTOP_PIN = 18;

// Receive buffer
const size_t RX_BUF_SIZE = 256;
// ─────────────────────────────────────────────────────────────────────────────

enum ConnectionState {
    CONN_STATE_WIFI_DISCONNECTED,
    CONN_STATE_WIFI_CONNECTING,
    CONN_STATE_TCP_CONNECTING,
    CONN_STATE_CONNECTED
};

WiFiClient client;

volatile boolean estopped = false;
volatile boolean astopped = false;

uint32_t lastReplyTimeMs = 0;
bool hasReceivedReplySinceConnect = false;
ConnectionState connState = CONN_STATE_WIFI_DISCONNECTED;
uint32_t lastStateActionMs = 0;

void ARDUINO_ISR_ATTR estopBtnCallback() {
    estopped = true;
}

void ARDUINO_ISR_ATTR astopBtnCallback() {
    astopped = true;
}

// ── Forward Declarations ──────────────────────────────────────────────────────
uint32_t sendHeartbeat(bool estopActivated, bool astopActivated);
void receiveReply(uint32_t sentAtMs);
void updateConnectionState();

// ── Setup ─────────────────────────────────────────────────────────────────────
void setup() {
    Serial.begin(115200);
    Serial.println("[ESTOP] Booting…");

    pinMode(ESTOP_PIN, INPUT_PULLDOWN);
    pinMode(ASTOP_PIN, INPUT_PULLDOWN);

    attachInterrupt(digitalPinToInterrupt(ESTOP_PIN), estopBtnCallback, FALLING);
    attachInterrupt(digitalPinToInterrupt(ASTOP_PIN), astopBtnCallback, FALLING);

    WiFi.mode(WIFI_STA);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
    connState = CONN_STATE_WIFI_CONNECTING;
    lastStateActionMs = millis();
    Serial.print("[ESTOP] Initiated WiFi connection…");
}

// ── Main loop ─────────────────────────────────────────────────────────────────
void loop() {
    static uint32_t lastHeartbeatMs = 0;
    uint32_t now = millis();

    updateConnectionState();

    if (connState == CONN_STATE_CONNECTED) {
        if (now - lastHeartbeatMs >= HEARTBEAT_INTERVAL_MS) {
            lastHeartbeatMs = now;
            uint32_t sentAtMs = sendHeartbeat(estopped, astopped);
            if (estopped) {
                Serial.println("[ESTOP] Attempting to send E-Stop!");
            }
            if (astopped) {
                Serial.println("[ESTOP] Attempting to send A-Stop!");
            }
            receiveReply(sentAtMs);
        }
    }

    delay(1);
}

// =============================================================================
//  Non-blocking Connection State Machine
// =============================================================================
void updateConnectionState() {
    uint32_t now = millis();

    switch (connState) {
        case CONN_STATE_WIFI_DISCONNECTED:
            Serial.println("[ESTOP] Reconnecting to WiFi…");
            WiFi.mode(WIFI_STA);
            WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
            connState = CONN_STATE_WIFI_CONNECTING;
            lastStateActionMs = now;
            break;

        case CONN_STATE_WIFI_CONNECTING:
            if (WiFi.status() == WL_CONNECTED) {
                Serial.printf("\n[ESTOP] WiFi connected – IP: %s\n", WiFi.localIP().toString().c_str());
                connState = CONN_STATE_TCP_CONNECTING;
                lastStateActionMs = 0; // trigger immediate TCP connect
            } else if (now - lastStateActionMs >= WIFI_RECONNECT_INTERVAL_MS) {
                Serial.println("\n[ESTOP] WiFi connection timeout – retrying…");
                WiFi.disconnect();
                connState = CONN_STATE_WIFI_DISCONNECTED;
            }
            break;

        case CONN_STATE_TCP_CONNECTING:
            if (WiFi.status() != WL_CONNECTED) {
                Serial.println("[ESTOP] WiFi lost while connecting TCP.");
                client.stop();
                connState = CONN_STATE_WIFI_DISCONNECTED;
                break;
            }

            if (now - lastStateActionMs >= TCP_RECONNECT_INTERVAL_MS) {
                lastStateActionMs = now;
                Serial.printf("[ESTOP] Connecting to FMS %s:%d…\n", FMS_HOST, FMS_PORT);
                client.stop();
                if (client.connect(FMS_HOST, FMS_PORT)) {
                    client.setNoDelay(true);
                    lastReplyTimeMs = 0;
                    hasReceivedReplySinceConnect = false;
                    connState = CONN_STATE_CONNECTED;
                    Serial.println("[ESTOP] FMS connected.");
                } else {
                    Serial.println("[ESTOP] FMS connection failed; will retry.");
                }
            }
            break;

        case CONN_STATE_CONNECTED:
            if (WiFi.status() != WL_CONNECTED) {
                Serial.println("[ESTOP] WiFi lost – resetting socket.");
                client.stop();
                connState = CONN_STATE_WIFI_DISCONNECTED;
            } else if (!client.connected()) {
                Serial.println("[ESTOP] TCP lost – reconnecting.");
                client.stop();
                connState = CONN_STATE_TCP_CONNECTING;
                lastStateActionMs = now;
            }
            break;
    }
}

// ── Build and send an e-stop heartbeat ───────────────────────────────────────
uint32_t sendHeartbeat(bool estopActivated, bool astopActivated) {
    BsonEncoder enc;
    enc.begin();
    enc.addString("name",             DEVICE_NAME);
    enc.addString("type",             "estop");
    enc.addInt32 ("last_reply_time_ms", (int32_t)lastReplyTimeMs);
    enc.addString("alliance",         ALLIANCE_NAME);
    enc.addInt32 ("station",          STATION_NUMBER);
    enc.addBool  ("estop_activated",  estopActivated);
    enc.addBool  ("astop_activated",  astopActivated);
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
        if (millis() - start > REPLY_BODY_TIMEOUT_MS) {
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
    lastReplyTimeMs = millis() - sentAtMs;
    hasReceivedReplySinceConnect = true;

    if (estopped) {
        Serial.println("[ESTOP] E-Stop accepted by server.");
    }
    if (astopped) {
        Serial.println("[ESTOP] A-Stop accepted by server.");
    }
    estopped = false;
    astopped = false;
}
