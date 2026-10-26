# Press-On Unity visualizer (v2)

A starting point for the new Press-On interface: a patient view (an orb that squashes and breathes with the ball) and a therapist view (a live pressure timeline), plus haptic coaching and session recording.

```
Ball ──Bluetooth──> bridge/press_on_bridge.py ──UDP 9050──> Unity
     <────────────                            <──UDP 9051── (haptics, calibration)
```

Unity talks to the ball through the small Python bridge, so it works the same on Mac and Windows without a Bluetooth plugin, and you can build the whole interface with `--sim` before the hardware is ready.

## Open it

1. Unity Hub → **Add → Add project from disk** → pick `unity/PressOnVisualizer`. Any Unity 6 or 2022.3 version works (Built-in or URP).
2. In Unity: menu **Press-On → Build Demo Scene**. It creates and saves `Assets/PressOn/Scenes/PressOnDemo.unity`.
3. Start the bridge:
   ```bash
   cd bridge
   pip install -r requirements.txt
   python press_on_bridge.py --sim          # fake ball
   python press_on_bridge.py                # real ball over Bluetooth
   python press_on_bridge.py --serial /dev/cu.usbmodem101 --baud 9600   # v1 ball (Arduino Nano)
   ```
4. Press **Play**.

Without the bridge running, use the "Manual pressure" slider on the debug panel.

## Scripts (`Assets/PressOn/Scripts`)

| Script | What it does |
| --- | --- |
| `PressOnClient` | Receives readings, smooths them, exposes `Pressure` (0 to 1), sends haptic and calibration commands. One per scene; use `PressOnClient.Instance`. |
| `PressureOrb` | Patient view. Squashes like the real ball, breathes slowly when relaxed, changes color. Shape and motion change too, so it doesn't rely on color alone. |
| `PressureTimeline` | Therapist view. Scrolling line of the last 20 seconds with a "hard squeeze" guide. |
| `HapticCoach` | Guard mode: if someone squeezes hard for a few seconds, the ball buzzes "ease off". Breathing mode: the ball pulses a slow breath to follow. |
| `SessionRecorder` | Saves a session to CSV in `Application.persistentDataPath/PressOnSessions`. |
| `PressOnDebugPanel` | On-screen buttons for calibration, haptics, coach mode and recording. Swap for your real UI. |

## Ideas for the real interface

- Patient and therapist views on different screens (the bridge can send to two machines: run it with `--host`).
- Session summary: time spent relaxed vs. tense, number of hard squeezes, calmest moment.
- A breathing exercise where the orb and the ball's haptics move together.
