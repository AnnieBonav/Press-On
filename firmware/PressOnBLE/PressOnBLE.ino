/*
  Press-On v2 firmware
  Seeed XIAO ESP32-C3 / ESP32-S3

  - Reads the flex sensor inside the ball and smooths it
  - Turns it into a 0..1 pressure value using a stored calibration
  - Streams it over Bluetooth LE (and over USB serial, compatible with the v1 Python app)
  - Plays haptic patterns on command (optional motor)
  - Naps when nobody is using it, and wakes up when you squeeze

  Protocol: docs/BLE_PROTOCOL.md      Settings: config.h
*/

#include <Arduino.h>
#include <Preferences.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>
#include "esp_sleep.h"
#include "config.h"

#if HAPTICS_MODE == HAPTICS_DRV2605
  #include <Wire.h>
  #include <Adafruit_DRV2605.h>
  Adafruit_DRV2605 drv;
#endif

// ---------------------------------------------------------------------------
// Packets (little-endian, packed). Keep in sync with docs/BLE_PROTOCOL.md
// ---------------------------------------------------------------------------
struct __attribute__((packed)) PressurePacket {
  uint16_t raw;        // latest ADC reading (0..4095)
  uint16_t smooth;     // smoothed ADC reading
  uint16_t pressure;   // 0..1000, after calibration and curve
  uint16_t seq;        // increments every packet, wraps
};

struct __attribute__((packed)) CalibrationPacket {
  uint16_t relaxed;    // ADC value with the ball untouched
  uint16_t squeezed;   // ADC value squeezed as hard as is comfortable
  uint8_t  source;     // 0 = automatic, 1 = set by the user
};

enum Command : uint8_t {
  CMD_CAL_RELAXED_NOW  = 0x01,
  CMD_CAL_SQUEEZED_NOW = 0x02,
  CMD_CAL_RESET        = 0x03,
  CMD_HAPTIC_PLAY      = 0x10,  // [0x10, pattern, intensity 0..255]
  CMD_HAPTIC_STOP      = 0x11,
  CMD_HAPTIC_LEVEL     = 0x12,  // [0x12, strength 0..255] continuous, until stop
  CMD_NAP_NOW          = 0x20,
};

// ---------------------------------------------------------------------------
// State that survives deep sleep (naps)
// ---------------------------------------------------------------------------
RTC_DATA_ATTR bool     napping = false;
RTC_DATA_ATTR uint16_t rtcRelaxed = 0;
RTC_DATA_ATTR uint16_t rtcSqueezed = 0;

// ---------------------------------------------------------------------------
// Runtime state
// ---------------------------------------------------------------------------
Preferences prefs;
CalibrationPacket cal = {0, 0, 0};

float    smoothValue = 0;
uint16_t rawValue = 0;
float    pressure = 0;          // 0..1
uint16_t seq = 0;

uint32_t lastSampleMs = 0, lastNotifyMs = 0, lastSerialMs = 0, lastBatteryMs = 0;
uint32_t lastActivityMs = 0;

BLEServer*         server = nullptr;
BLECharacteristic* chrPressure = nullptr;
BLECharacteristic* chrCalibration = nullptr;
BLECharacteristic* chrCommand = nullptr;
BLECharacteristic* chrBattery = nullptr;
volatile bool      clientConnected = false;
volatile bool      restartAdvertising = false;

// Commands arrive on the BLE task; we handle them in loop()
portMUX_TYPE   commandMux = portMUX_INITIALIZER_UNLOCKED;
volatile bool  pendingCommand = false;
uint8_t        pendingBytes[4] = {0};
size_t         pendingLen = 0;

// ---------------------------------------------------------------------------
// Haptics: tiny non-blocking pattern player
// ---------------------------------------------------------------------------
struct HapticStep { uint8_t strength; uint16_t ms; };

// Pattern ids are part of the protocol. Add new ones at the end.
const HapticStep PATTERN_TICK[]      = {{255, 40}, {0, 10}};
const HapticStep PATTERN_EASE_OFF[]  = {{200, 90}, {0, 90}, {200, 90}, {0, 10}};          // "too hard, let go a bit"
const HapticStep PATTERN_PRESS_MORE[]= {{90, 220}, {0, 10}};                              // gentle nudge
const HapticStep PATTERN_BREATHE[]   = {{40, 400}, {80, 400}, {120, 400}, {160, 400},      // inhale ramp up
                                        {120, 400}, {80, 400}, {40, 400}, {0, 1200}};     // exhale ramp down
const HapticStep PATTERN_HEARTBEAT[] = {{220, 70}, {0, 110}, {150, 70}, {0, 700}};

struct Pattern { const HapticStep* steps; uint8_t count; };
const Pattern PATTERNS[] = {
  {nullptr, 0},                                                         // 0 = off
  {PATTERN_TICK,       sizeof(PATTERN_TICK) / sizeof(HapticStep)},       // 1
  {PATTERN_EASE_OFF,   sizeof(PATTERN_EASE_OFF) / sizeof(HapticStep)},   // 2
  {PATTERN_PRESS_MORE, sizeof(PATTERN_PRESS_MORE) / sizeof(HapticStep)}, // 3
  {PATTERN_BREATHE,    sizeof(PATTERN_BREATHE) / sizeof(HapticStep)},    // 4
  {PATTERN_HEARTBEAT,  sizeof(PATTERN_HEARTBEAT) / sizeof(HapticStep)},  // 5
};
const uint8_t PATTERN_COUNT = sizeof(PATTERNS) / sizeof(Pattern);

const Pattern* hapticPattern = nullptr;
uint8_t  hapticStep = 0;
uint32_t hapticStepStart = 0;
float    hapticIntensity = 1.0f;
int16_t  hapticHold = -1;           // >= 0 while CMD_HAPTIC_LEVEL is active

void hapticWrite(uint8_t strength) {
#if HAPTICS_MODE == HAPTICS_PWM
  analogWrite(PIN_HAPTIC, strength);
#elif HAPTICS_MODE == HAPTICS_DRV2605
  drv.setRealtimeValue(strength >> 1);   // RTP takes 0..127
#else
  (void)strength;
#endif
}

void hapticStop() {
  hapticPattern = nullptr;
  hapticHold = -1;
  hapticWrite(0);
}

void hapticPlay(uint8_t id, uint8_t intensity) {
  if (id == 0 || id >= PATTERN_COUNT) { hapticStop(); return; }
  hapticHold = -1;
  hapticPattern = &PATTERNS[id];
  hapticStep = 0;
  hapticStepStart = millis();
  hapticIntensity = intensity / 255.0f;
  hapticWrite((uint8_t)(hapticPattern->steps[0].strength * hapticIntensity));
}

void hapticUpdate() {
  if (hapticHold >= 0 || hapticPattern == nullptr) return;
  const HapticStep& step = hapticPattern->steps[hapticStep];
  if (millis() - hapticStepStart < step.ms) return;
  hapticStep++;
  if (hapticStep >= hapticPattern->count) { hapticStop(); return; }
  hapticStepStart = millis();
  hapticWrite((uint8_t)(hapticPattern->steps[hapticStep].strength * hapticIntensity));
}

void hapticBegin() {
#if HAPTICS_MODE == HAPTICS_PWM
  pinMode(PIN_HAPTIC, OUTPUT);
  analogWrite(PIN_HAPTIC, 0);
#elif HAPTICS_MODE == HAPTICS_DRV2605
  Wire.begin();
  if (drv.begin()) {
  #if HAPTIC_USE_LRA
    drv.useLRA();
  #else
    drv.useERM();
  #endif
    drv.setMode(DRV2605_MODE_REALTIME);
    drv.setRealtimeValue(0);
  } else {
    Serial.println();  // keep serial output numeric for the v1 app
  }
#endif
}

// ---------------------------------------------------------------------------
// Sensing and calibration
// ---------------------------------------------------------------------------
uint16_t readFlexAveraged(uint8_t n) {
  uint32_t sum = 0;
  for (uint8_t i = 0; i < n; i++) sum += analogRead(PIN_FLEX);
  return (uint16_t)(sum / n);
}

float computePressure(float value) {
  float span = (float)cal.relaxed - (float)cal.squeezed;
  if (fabsf(span) < 8.0f) return 0.0f;                 // not calibrated enough yet
  float x = ((float)cal.relaxed - value) / span;        // works whichever way the divider is wired
  if (x < 0) x = 0;
  if (x > 1) x = 1;
  return 1.0f - powf(1.0f - x, PRESSURE_CURVE);
}

void saveCalibration() {
  prefs.putUShort("relaxed", cal.relaxed);
  prefs.putUShort("squeezed", cal.squeezed);
  prefs.putUChar("source", cal.source);
  rtcRelaxed = cal.relaxed;
  rtcSqueezed = cal.squeezed;
}

void publishCalibration() {
  if (!chrCalibration) return;
  chrCalibration->setValue((uint8_t*)&cal, sizeof(cal));
  if (clientConnected) chrCalibration->notify();
}

void autoCalibrateFromRest() {
  // Assumes nobody is squeezing during the first moment after power-on.
  uint16_t rest = readFlexAveraged(64);
  cal.relaxed = rest;
  // v1 readings went from ~1023 at rest to ~25 squeezed, so start with a wide guess.
  cal.squeezed = (rest > 2048) ? (uint16_t)(rest * 0.05f) : (uint16_t)min(4095.0f, rest + (4095 - rest) * 0.95f);
  cal.source = 0;
  saveCalibration();
}

void adaptCalibration(float value) {
  // Automatic calibration slowly learns the real range as the ball is used.
  if (cal.source != 0) return;
  float span = (float)cal.relaxed - (float)cal.squeezed;
  float x = ((float)cal.relaxed - value) / span;
  if (x < -0.02f) cal.relaxed = (uint16_t)(cal.relaxed * 0.995f + value * 0.005f);  // rest drifted
  if (x > 1.0f)   cal.squeezed = (uint16_t)value;                                     // harder squeeze seen
}

// ---------------------------------------------------------------------------
// Battery (optional divider on PIN_BATTERY_SENSE)
// ---------------------------------------------------------------------------
uint8_t readBatteryPercent() {
#if PIN_BATTERY_SENSE >= 0
  uint32_t mv = 0;
  for (int i = 0; i < 8; i++) mv += analogReadMilliVolts(PIN_BATTERY_SENSE);
  float volts = (mv / 8.0f) * BATTERY_DIVIDER / 1000.0f;
  // Rough single-cell LiPo curve
  if (volts >= 4.15f) return 100;
  if (volts <= 3.30f) return 0;
  if (volts >= 3.85f) return (uint8_t)(70 + (volts - 3.85f) / 0.30f * 30);
  if (volts >= 3.65f) return (uint8_t)(30 + (volts - 3.65f) / 0.20f * 40);
  return (uint8_t)((volts - 3.30f) / 0.35f * 30);
#else
  return 100;
#endif
}

// ---------------------------------------------------------------------------
// BLE
// ---------------------------------------------------------------------------
class ServerCallbacks : public BLEServerCallbacks {
  void onConnect(BLEServer*) override { clientConnected = true; }
  void onDisconnect(BLEServer*) override { clientConnected = false; restartAdvertising = true; }
};

class CommandCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* c) override {
    size_t len = c->getLength();
    if (len == 0) return;
    portENTER_CRITICAL(&commandMux);
    pendingLen = min(len, sizeof(pendingBytes));
    memcpy(pendingBytes, c->getData(), pendingLen);
    pendingCommand = true;
    portEXIT_CRITICAL(&commandMux);
  }
};

class CalibrationCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* c) override {
    if (c->getLength() < 4) return;
    // The new values are read straight from the characteristic in handleCommand()
    portENTER_CRITICAL(&commandMux);
    pendingBytes[0] = 0xC0;           // internal: "set calibration"
    pendingLen = 1;
    pendingCommand = true;
    portEXIT_CRITICAL(&commandMux);
  }
};

void bleBegin() {
  BLEDevice::init(DEVICE_NAME);
  server = BLEDevice::createServer();
  server->setCallbacks(new ServerCallbacks());

  BLEService* svc = server->createService(UUID_SERVICE);

  chrPressure = svc->createCharacteristic(UUID_PRESSURE,
    BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY);
  chrPressure->addDescriptor(new BLE2902());

  chrCalibration = svc->createCharacteristic(UUID_CALIBRATION,
    BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_NOTIFY);
  chrCalibration->addDescriptor(new BLE2902());
  chrCalibration->setCallbacks(new CalibrationCallbacks());

  chrCommand = svc->createCharacteristic(UUID_COMMAND,
    BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_WRITE_NR);
  chrCommand->setCallbacks(new CommandCallbacks());
  svc->start();

  // Standard Battery Service, so phones and OS settings can show the level too
  BLEService* bat = server->createService(BLEUUID((uint16_t)0x180F));
  chrBattery = bat->createCharacteristic(BLEUUID((uint16_t)0x2A19),
    BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY);
  chrBattery->addDescriptor(new BLE2902());
  uint8_t level = readBatteryPercent();
  chrBattery->setValue(&level, 1);
  bat->start();

  publishCalibration();

  BLEAdvertising* adv = BLEDevice::getAdvertising();
  adv->addServiceUUID(UUID_SERVICE);
  adv->setScanResponse(true);
  BLEDevice::startAdvertising();
}

void handleCommand() {
  uint8_t b[4];
  size_t len;
  portENTER_CRITICAL(&commandMux);
  memcpy(b, pendingBytes, sizeof(b));
  len = pendingLen;
  pendingCommand = false;
  portEXIT_CRITICAL(&commandMux);
  if (len == 0) return;

  switch (b[0]) {
    case CMD_CAL_RELAXED_NOW:
      cal.relaxed = readFlexAveraged(64); cal.source = 1; saveCalibration(); publishCalibration(); break;
    case CMD_CAL_SQUEEZED_NOW:
      cal.squeezed = readFlexAveraged(64); cal.source = 1; saveCalibration(); publishCalibration(); break;
    case CMD_CAL_RESET:
      autoCalibrateFromRest(); publishCalibration(); break;
    case CMD_HAPTIC_PLAY:
      hapticPlay(len > 1 ? b[1] : 1, len > 2 ? b[2] : 255); break;
    case CMD_HAPTIC_STOP:
      hapticStop(); break;
    case CMD_HAPTIC_LEVEL:
      hapticPattern = nullptr; hapticHold = len > 1 ? b[1] : 0; hapticWrite((uint8_t)hapticHold); break;
    case CMD_NAP_NOW:
      lastActivityMs = millis() - IDLE_NAP_MS - 1; break;
    case 0xC0: {                                    // calibration written directly
      const uint8_t* d = chrCalibration->getData();
      cal.relaxed = d[0] | (d[1] << 8);
      cal.squeezed = d[2] | (d[3] << 8);
      cal.source = 1;
      saveCalibration(); publishCalibration();
      break;
    }
  }
}

// ---------------------------------------------------------------------------
// USB serial: numbers only (one reading per line), so the v1 Python app still works.
// Single-character commands: r = relaxed now, s = squeezed now, x = reset, 1..5 = haptic pattern
// ---------------------------------------------------------------------------
void queueCommand(uint8_t a, uint8_t b1, uint8_t b2, size_t len) {
  portENTER_CRITICAL(&commandMux);
  pendingBytes[0] = a; pendingBytes[1] = b1; pendingBytes[2] = b2;
  pendingLen = len;
  pendingCommand = true;
  portEXIT_CRITICAL(&commandMux);
}

void handleSerialInput() {
  while (Serial.available()) {
    char c = Serial.read();
    if (c == 'r') queueCommand(CMD_CAL_RELAXED_NOW, 0, 0, 1);
    else if (c == 's') queueCommand(CMD_CAL_SQUEEZED_NOW, 0, 0, 1);
    else if (c == 'x') queueCommand(CMD_CAL_RESET, 0, 0, 1);
    else if (c >= '1' && c <= '9') queueCommand(CMD_HAPTIC_PLAY, c - '0', 255, 3);
  }
}

// ---------------------------------------------------------------------------
// Naps: deep sleep with a timer, wake fully when squeezed
// ---------------------------------------------------------------------------
void goNap() {
  hapticStop();
  napping = true;
  esp_sleep_enable_timer_wakeup((uint64_t)SLEEP_CHECK_MS * 1000ULL);
  esp_deep_sleep_start();
}

bool squeezedSinceNap() {
  // Quick check right after a timer wake, before starting Bluetooth (saves battery)
  cal.relaxed = rtcRelaxed;
  cal.squeezed = rtcSqueezed;
  return computePressure(readFlexAveraged(16)) >= WAKE_PRESSURE;
}

// ---------------------------------------------------------------------------
void setup() {
  analogReadResolution(12);
  pinMode(PIN_FLEX, INPUT);

  if (napping && esp_sleep_get_wakeup_cause() == ESP_SLEEP_WAKEUP_TIMER) {
    if (!squeezedSinceNap()) goNap();   // still idle: straight back to sleep
  }
  napping = false;

  Serial.begin(115200);
#if PIN_STATUS_LED >= 0
  pinMode(PIN_STATUS_LED, OUTPUT);
#endif

  prefs.begin("presson", false);
  if (prefs.isKey("relaxed")) {
    cal.relaxed = prefs.getUShort("relaxed", 0);
    cal.squeezed = prefs.getUShort("squeezed", 0);
    cal.source = prefs.getUChar("source", 0);
    rtcRelaxed = cal.relaxed; rtcSqueezed = cal.squeezed;
  } else {
    autoCalibrateFromRest();
  }

  smoothValue = readFlexAveraged(16);
  hapticBegin();
  bleBegin();

  lastActivityMs = millis();
  hapticPlay(1, 160);                   // little "hello" tick
}

void loop() {
  uint32_t now = millis();

  if (now - lastSampleMs >= 1000 / SAMPLE_HZ) {
    lastSampleMs = now;
    rawValue = analogRead(PIN_FLEX);
    smoothValue += SMOOTHING_ALPHA * ((float)rawValue - smoothValue);
    adaptCalibration(smoothValue);
    pressure = computePressure(smoothValue);
    if (pressure > 0.08f) lastActivityMs = now;
  }

  if (clientConnected && now - lastNotifyMs >= 1000 / NOTIFY_HZ) {
    lastNotifyMs = now;
    PressurePacket p = { rawValue, (uint16_t)smoothValue, (uint16_t)(pressure * 1000.0f), seq++ };
    chrPressure->setValue((uint8_t*)&p, sizeof(p));
    chrPressure->notify();
    lastActivityMs = now;               // someone is listening, stay awake
  }

  if (now - lastSerialMs >= 1000 / SERIAL_HZ) {
    lastSerialMs = now;
    Serial.println((int)smoothValue);  // same format as v1: one integer per line
  }

  if (now - lastBatteryMs >= 10000) {
    lastBatteryMs = now;
    uint8_t level = readBatteryPercent();
    chrBattery->setValue(&level, 1);
    if (clientConnected) chrBattery->notify();
  }

  if (restartAdvertising) {
    restartAdvertising = false;
    delay(200);
    BLEDevice::startAdvertising();
  }

  handleSerialInput();
  if (pendingCommand) handleCommand();
  hapticUpdate();

#if PIN_STATUS_LED >= 0
  digitalWrite(PIN_STATUS_LED, clientConnected ? HIGH : ((now / 500) % 2));
#endif

  if (!clientConnected && now - lastActivityMs > IDLE_NAP_MS) goNap();
}
