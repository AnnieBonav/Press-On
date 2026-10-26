# Press-On

## v2: wireless (in progress)

v2 moves the electronics inside the ball: a Seeed XIAO ESP32-C3 or S3 on a small LiPo, Bluetooth LE instead of the aux cable and Arduino box, haptic feedback, and a Unity interface.

| Folder | What's in it |
| --- | --- |
| [`firmware/`](firmware) | XIAO firmware: flex sensor, calibration, Bluetooth, haptics, USB serial (still works with the v1 app), squeeze-to-wake |
| [`bridge/`](bridge) | Python bridge from the ball (Bluetooth, USB or simulated) to Unity over UDP |
| [`unity/`](unity) | Unity starter project: patient view, therapist timeline, haptic coach, session recording |
| [`hardware/v2/`](hardware/v2) | Printable ball and electronics pod with a USB-C outlet, wiring and parts list |
| [`docs/BLE_PROTOCOL.md`](docs/BLE_PROTOCOL.md) | The Bluetooth protocol, if you want to talk to the ball from anything else |

### Quick start

```bash
# 1. Flash the XIAO (PlatformIO), or open firmware/PressOnBLE/PressOnBLE.ino in the Arduino IDE
cd firmware && pio run -e xiao_esp32c3 -t upload        # or -e xiao_esp32s3

# 2. Run the bridge
cd ../bridge && pip install -r requirements.txt
python press_on_bridge.py            # finds "Press-On" over Bluetooth
python press_on_bridge.py --sim      # no hardware needed

# 3. Open unity/PressOnVisualizer, run Press-On > Build Demo Scene, press Play
```

Arduino IDE: install the "esp32" boards package by Espressif, pick "XIAO_ESP32C3" or "XIAO_ESP32S3". Only install the "Adafruit DRV2605" library if you use that haptic driver.

---

## v1

## What is it?
Press-On is an akin stress-ball like prototype that... The project´s idea was pitched by the department of Social Sciences of the University of Applied Sciences of St. Pölten. The prototype first started by using 6cm toy balls, a protboard, an 8cm 500g flex sensor and an Arduiono UNO. It quickly evolved to the afterward described parts.

## How it works

## Technical stuff
### Electronics
#### Flex sensor
The used flex sensor is now 12 cm long, 10kg of pressure and 20 oms of resistance. It goes inside of the ball 5 mm away from the oustide, perfectly centered.

#### Arduino Nano
The Microcontroller that is currently used is an Arduino Nano. The ground, V5 output, and A0 and A1 analogue pins for input. One of the sensor´s pins goes though resistance and ends up in the V5 ouput, while the other one goes directly as input. Either of the sensor pins can be connected to ouput/input as there is no "positive" or "negative" side (it is only a resistance).

The analogue output is sent to the computer by connecting the arduino Nano to a USB input through a Micro-USB to UBS-A cable.

The input is gotten from the ball by connecting the two pins from the sensor to a female-jack, and the information is sent through a male-male jack that ends-up in the electronics.

#### C for arduino code
The Arduino Nano is coded using c in the Arduino IDE. Libraries like time were used for testing purposes.

#### Electronics case
The electronics case has a rubber-like texture at the end so it helps-put with positioning and does not slide off the table.

### Stress-ball
The current prototype incldues a stress-bal-like 3D printed sphere that is both flexible and steady so the user is able to press on it, with yellow Ninja-Tek Flex filament. It was modeled with Solidworks and the current prototupe measures 7.5cm/6.5mm in diameter.

### Software
#### Python for connection with serial port (USB)
Python serial is used for getting the data stream that the arduino Nano throws through the micro-usb to usb-a cable connected from the prototype´s box to the computer.

#### Python for Graphical User Interface (GUI)
The Graphical User Interface is created using various python 3 Libraries. Mainly tkinter, serial and thread.

## How to run

## How to create code executable
### For windows
pyinstaller --onefile --clean TkinterGUI.py

### For mac
sudo pyinstaller --onefile --clean TkinterGUI.py

## Considerations
## Electronics handling
- Thanks to the jack connecting the ball to the electonics, it is easy to plug-in/plug out.
- As long as the Micro-USB to USB-A cable is long enough, having the ball in one hand while the elecytronics case rests on the table should be no problem.