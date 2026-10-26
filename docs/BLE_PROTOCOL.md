# Press-On v2 Bluetooth protocol

The ball advertises as **Press-On** and exposes one custom service plus the standard Battery Service.
All multi-byte numbers are **little-endian**.

## Service `7a1e0001-5d2c-4b8e-9f3a-50e55b0a1100`

| Characteristic | UUID | Properties | Payload |
| --- | --- | --- | --- |
| Pressure | `7a1e0002-5d2c-4b8e-9f3a-50e55b0a1100` | read, notify (30 Hz) | 8 bytes, see below |
| Calibration | `7a1e0003-5d2c-4b8e-9f3a-50e55b0a1100` | read, write, notify | 5 bytes read / 4 bytes write |
| Command | `7a1e0004-5d2c-4b8e-9f3a-50e55b0a1100` | write, write without response | 1 to 3 bytes |

### Pressure (8 bytes)

| Offset | Type | Field | Meaning |
| --- | --- | --- | --- |
| 0 | uint16 | raw | Latest ADC reading, 0 to 4095 |
| 2 | uint16 | smooth | Smoothed ADC reading |
| 4 | uint16 | pressure | 0 to 1000. 0 = untouched, 1000 = squeezed as hard as calibrated. Same curve as v1: `1 - (1 - x)^2` |
| 6 | uint16 | seq | Packet counter, wraps at 65535. Gaps mean dropped packets |

### Calibration

Read (5 bytes): `uint16 relaxed`, `uint16 squeezed`, `uint8 source` (0 = automatic, 1 = set by the user).
Write (4 bytes): `uint16 relaxed`, `uint16 squeezed`. Saved on the ball, survives power-off.

Without user calibration the ball takes its resting value at power-on and keeps learning the squeezed end as it's used. Works whichever way the voltage divider is wired.

### Commands

| Bytes | Command |
| --- | --- |
| `01` | Take the current reading as "relaxed" |
| `02` | Take the current reading as "squeezed" |
| `03` | Reset to automatic calibration |
| `10 pp ii` | Play haptic pattern `pp` at intensity `ii` (0 to 255) |
| `11` | Stop haptics |
| `12 ss` | Hold the motor at strength `ss` (0 to 255) until stopped |
| `20` | Nap now (deep sleep; squeeze to wake) |

Haptic patterns:

| Id | Pattern | Meant for |
| --- | --- | --- |
| 1 | Tick | Acknowledge, "hello" |
| 2 | Ease off | Double buzz: "that's too hard, let go a bit" |
| 3 | Press more | Gentle long nudge |
| 4 | Breathe | 4.4 s swell and fade to pace a breath |
| 5 | Heartbeat | Calm double pulse |

## Battery Service `0x180F`

Battery Level `0x2A19`, uint8 percent, notifies every 10 s. Needs the optional divider on A1 (see `hardware/v2/README.md`); without it the ball always reports 100.

## USB serial (115200 baud)

One integer per line, 10 times a second: the smoothed reading. This is the same format as v1, so the old Python app still works over USB (set its Max to around 4095).

Single characters sent to the ball: `r` relaxed now, `s` squeezed now, `x` reset calibration, `1` to `5` play a haptic pattern.
