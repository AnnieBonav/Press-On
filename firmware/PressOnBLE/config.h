// Press-On v2 firmware configuration.
// Change values here, not in PressOnBLE.ino.
#pragma once

// ---------------------------------------------------------------------------
// Board pins (Seeed XIAO ESP32-C3 or ESP32-S3, same footprint and labels)
// ---------------------------------------------------------------------------
// Flex sensor voltage divider output. Same wiring as v1, but powered from 3V3:
//   3V3 ── fixed resistor ── A0 ── flex sensor ── GND
#define PIN_FLEX            A0

// Optional battery sense: BAT+ ── 220k ── A1 ── 220k ── GND
// Set to -1 if you didn't add the divider.
#define PIN_BATTERY_SENSE   A1
#define BATTERY_DIVIDER     2.0f      // (R1 + R2) / R2

// Optional status LED (XIAO has no user LED on C3; use an external one or -1)
#define PIN_STATUS_LED      -1

// ---------------------------------------------------------------------------
// Haptics. Pick ONE:
//   HAPTICS_NONE     no motor yet
//   HAPTICS_PWM      coin/ERM motor through a transistor (or a DRV8833 channel) on PIN_HAPTIC
//   HAPTICS_DRV2605  Adafruit/TI DRV2605L driver on I2C (SDA = D4, SCL = D5)
// ---------------------------------------------------------------------------
#define HAPTICS_NONE        0
#define HAPTICS_PWM         1
#define HAPTICS_DRV2605     2
#define HAPTICS_MODE        HAPTICS_NONE

#define PIN_HAPTIC          D3
#define HAPTIC_USE_LRA      1         // DRV2605 only: 1 = LRA coin motor, 0 = ERM motor

// ---------------------------------------------------------------------------
// Sensing
// ---------------------------------------------------------------------------
#define SAMPLE_HZ           100       // ADC reads per second
#define NOTIFY_HZ           30        // BLE packets per second
#define SERIAL_HZ           10        // USB serial lines per second (v1-app compatible)
#define SMOOTHING_ALPHA     0.25f     // 0..1, higher = less smoothing
#define PRESSURE_CURVE      2.0f      // matches v1: 1 - (1 - x)^2 feel

// ---------------------------------------------------------------------------
// Power
// ---------------------------------------------------------------------------
// After this long with no BLE connection and no squeeze, the ball naps.
// While napping it wakes briefly every SLEEP_CHECK_MS and fully wakes on a squeeze.
#define IDLE_NAP_MS         (10UL * 60UL * 1000UL)
#define SLEEP_CHECK_MS      1200
#define WAKE_PRESSURE       0.35f     // squeeze this hard (0..1) to wake it up

// ---------------------------------------------------------------------------
// BLE
// ---------------------------------------------------------------------------
#define DEVICE_NAME         "Press-On"

// Custom service. Full protocol in docs/BLE_PROTOCOL.md
#define UUID_SERVICE        "7a1e0001-5d2c-4b8e-9f3a-50e55b0a1100"
#define UUID_PRESSURE       "7a1e0002-5d2c-4b8e-9f3a-50e55b0a1100"   // notify, read
#define UUID_CALIBRATION    "7a1e0003-5d2c-4b8e-9f3a-50e55b0a1100"   // read, write, notify
#define UUID_COMMAND        "7a1e0004-5d2c-4b8e-9f3a-50e55b0a1100"   // write
