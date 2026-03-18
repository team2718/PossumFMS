#pragma once
#include <Arduino.h>

// =============================================================================
//  bson_helper.h  –  Minimal BSON encoder / decoder for the FMS ESP32 client
//
//  Supported BSON types (all that the FMS protocol requires):
//    0x02  UTF-8 string
//    0x08  Boolean
//    0x10  Int32
//
//  Wire format recap (little-endian):
//    [4 bytes] total document length  (includes these 4 bytes AND the terminal 0x00)
//    [ N bytes] elements …
//    [1 byte ] 0x00  (document terminator)
//
//  Each element:
//    [1 byte ] type tag
//    [C bytes] key  (null-terminated C-string)
//    [? bytes] value (type-dependent)
//
//  String value:
//    [4 bytes] string length INCLUDING the trailing null
//    [N bytes] UTF-8 bytes + 0x00
//
//  Boolean value:
//    [1 byte ] 0x00 = false, 0x01 = true
//
//  Int32 value:
//    [4 bytes] signed 32-bit little-endian integer
// =============================================================================

// ── BSON types ────────────────────────────────────────────────────────────────
#define BSON_TYPE_STRING  0x02
#define BSON_TYPE_BOOL    0x08
#define BSON_TYPE_INT32   0x10

// ── Buffer size for outgoing documents ───────────────────────────────────────
// 256 bytes is plenty for our small heartbeat messages.
#define BSON_BUF_SIZE 256

// =============================================================================
//  BsonEncoder
//  Usage:
//    BsonEncoder enc;
//    enc.begin();
//    enc.addString("name", "red_hub");
//    enc.addString("type", "hub");
//    enc.addInt32 ("fuel_delta", 3);
//    enc.addBool  ("some_flag",  true);
//    enc.end();
//    client.write(enc.buf, enc.length());
// =============================================================================
class BsonEncoder {
public:
    uint8_t buf[BSON_BUF_SIZE];

    // Call before adding any fields.
    void begin() {
        _pos = 0;
        // Reserve 4 bytes for the document length (filled in by end()).
        _writeInt32(0);
    }

    // Add a UTF-8 string field.
    void addString(const char* key, const char* value) {
        _writeByte(BSON_TYPE_STRING);
        _writeCStr(key);
        uint32_t strLen = strlen(value) + 1;   // includes trailing null
        _writeInt32(strLen);
        for (uint32_t i = 0; i < strLen; i++) _writeByte(value[i]);
    }

    // Add a boolean field.
    void addBool(const char* key, bool value) {
        _writeByte(BSON_TYPE_BOOL);
        _writeCStr(key);
        _writeByte(value ? 0x01 : 0x00);
    }

    // Add a signed 32-bit integer field.
    void addInt32(const char* key, int32_t value) {
        _writeByte(BSON_TYPE_INT32);
        _writeCStr(key);
        _writeInt32(value);
    }

    // Finalise the document: writes the terminal 0x00 and back-fills the length.
    void end() {
        _writeByte(0x00);                       // document terminator
        uint32_t docLen = _pos;
        // Back-fill length at offset 0 (little-endian).
        buf[0] = (docLen)       & 0xFF;
        buf[1] = (docLen >>  8) & 0xFF;
        buf[2] = (docLen >> 16) & 0xFF;
        buf[3] = (docLen >> 24) & 0xFF;
    }

    // Returns the total byte length of the finished document.
    size_t length() const { return _pos; }

private:
    size_t _pos = 0;

    void _writeByte(uint8_t b) {
        if (_pos < BSON_BUF_SIZE) buf[_pos++] = b;
    }

    void _writeCStr(const char* s) {
        while (*s) _writeByte((uint8_t)*s++);
        _writeByte(0x00);
    }

    void _writeInt32(int32_t v) {
        _writeByte((v)       & 0xFF);
        _writeByte((v >>  8) & 0xFF);
        _writeByte((v >> 16) & 0xFF);
        _writeByte((v >> 24) & 0xFF);
    }
};


// =============================================================================
//  BsonDecoder
//  Parses a raw BSON document received from the server.
//
//  Usage:
//    BsonDecoder dec(buf, numBytes);
//    bool accepted  = dec.getBool  ("accepted",        false);
//    int  r         = dec.getInt32 ("led_r",           0);
//    String status  = dec.getString("flashing_status", "off");
// =============================================================================
class BsonDecoder {
public:
    BsonDecoder(const uint8_t* data, size_t len)
        : _data(data), _len(len) {}

    // Returns the string value for 'key', or 'defaultVal' if not found.
    String getString(const char* key, const char* defaultVal = "") {
        size_t pos = 4;   // skip document length prefix
        while (pos < _len - 1) {
            uint8_t type = _data[pos++];
            if (type == 0x00) break;                  // end of document

            // Read the key.
            String k = _readCStr(pos);
            if (pos >= _len) break;

            if (type == BSON_TYPE_STRING) {
                int32_t strLen = _readInt32(pos);
                pos += 4;
                if (strLen <= 0 || pos + strLen > _len) break;
                String val = "";
                for (int32_t i = 0; i < strLen - 1; i++) val += (char)_data[pos + i];
                pos += strLen;
                if (k == key) return val;
            } else if (type == BSON_TYPE_BOOL) {
                bool val = (_data[pos++] == 0x01);
                if (k == key) return val ? "true" : "false";
            } else if (type == BSON_TYPE_INT32) {
                int32_t val = _readInt32(pos); pos += 4;
                if (k == key) return String(val);
            } else {
                break;  // unknown type — stop parsing
            }
        }
        return String(defaultVal);
    }

    // Returns the bool value for 'key', or 'defaultVal' if not found.
    bool getBool(const char* key, bool defaultVal = false) {
        size_t pos = 4;
        while (pos < _len - 1) {
            uint8_t type = _data[pos++];
            if (type == 0x00) break;
            String k = _readCStr(pos);
            if (pos >= _len) break;
            if (type == BSON_TYPE_STRING) {
                int32_t strLen = _readInt32(pos); pos += 4;
                if (strLen <= 0 || pos + strLen > _len) break;
                pos += strLen;
            } else if (type == BSON_TYPE_BOOL) {
                bool val = (_data[pos++] == 0x01);
                if (k == key) return val;
            } else if (type == BSON_TYPE_INT32) {
                pos += 4;
            } else {
                break;
            }
        }
        return defaultVal;
    }

    // Returns the int32 value for 'key', or 'defaultVal' if not found.
    int32_t getInt32(const char* key, int32_t defaultVal = 0) {
        size_t pos = 4;
        while (pos < _len - 1) {
            uint8_t type = _data[pos++];
            if (type == 0x00) break;
            String k = _readCStr(pos);
            if (pos >= _len) break;
            if (type == BSON_TYPE_STRING) {
                int32_t strLen = _readInt32(pos); pos += 4;
                if (strLen <= 0 || pos + strLen > _len) break;
                pos += strLen;
            } else if (type == BSON_TYPE_BOOL) {
                bool val = (_data[pos++] == 0x01);
                (void)val;
            } else if (type == BSON_TYPE_INT32) {
                int32_t val = _readInt32(pos); pos += 4;
                if (k == key) return val;
            } else {
                break;
            }
        }
        return defaultVal;
    }

private:
    const uint8_t* _data;
    size_t _len;

    // Reads a null-terminated key string starting at pos, advances pos past it.
    String _readCStr(size_t& pos) {
        String s = "";
        while (pos < _len && _data[pos] != 0x00) s += (char)_data[pos++];
        pos++;  // skip null terminator
        return s;
    }

    int32_t _readInt32(size_t pos) {
        if (pos + 4 > _len) return 0;
        return (int32_t)(_data[pos]
                       | ((uint32_t)_data[pos+1] <<  8)
                       | ((uint32_t)_data[pos+2] << 16)
                       | ((uint32_t)_data[pos+3] << 24));
    }
};
