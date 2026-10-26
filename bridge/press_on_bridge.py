#!/usr/bin/env python3
"""
Press-On bridge: connects to the ball and streams it to Unity (or anything else) over UDP.

    python press_on_bridge.py            # find the ball over Bluetooth
    python press_on_bridge.py --sim      # no hardware: fake squeezes, for building the Unity side
    python press_on_bridge.py --serial /dev/cu.usbmodem101   # v1 ball, or v2 over USB-C

Out (to Unity):  UDP 127.0.0.1:9050, one JSON object per message:
    {"t": 12.34, "raw": 3011, "smooth": 3005, "pressure": 0.42, "battery": 87,
     "connected": true, "source": "ble", "seq": 1203}

In (from Unity): UDP 127.0.0.1:9051, JSON commands:
    {"cmd": "haptic", "pattern": 2, "intensity": 200}
    {"cmd": "haptic_level", "strength": 120}
    {"cmd": "haptic_stop"}
    {"cmd": "calibrate", "which": "relaxed" | "squeezed" | "reset"}

Protocol details: docs/BLE_PROTOCOL.md
"""
from __future__ import annotations

import argparse
import asyncio
import json
import math
import random
import socket
import struct
import sys
import time

SERVICE_UUID = "7a1e0001-5d2c-4b8e-9f3a-50e55b0a1100"
PRESSURE_UUID = "7a1e0002-5d2c-4b8e-9f3a-50e55b0a1100"
CALIBRATION_UUID = "7a1e0003-5d2c-4b8e-9f3a-50e55b0a1100"
COMMAND_UUID = "7a1e0004-5d2c-4b8e-9f3a-50e55b0a1100"
BATTERY_LEVEL_UUID = "00002a19-0000-1000-8000-00805f9b34fb"

PRESSURE_STRUCT = struct.Struct("<HHHH")  # raw, smooth, pressure (0..1000), seq

CMD_CAL_RELAXED, CMD_CAL_SQUEEZED, CMD_CAL_RESET = 0x01, 0x02, 0x03
CMD_HAPTIC_PLAY, CMD_HAPTIC_STOP, CMD_HAPTIC_LEVEL = 0x10, 0x11, 0x12


def decode_pressure(data: bytes) -> dict:
    raw, smooth, pressure, seq = PRESSURE_STRUCT.unpack(bytes(data[:8]))
    return {"raw": raw, "smooth": smooth, "pressure": round(pressure / 1000.0, 4), "seq": seq}


def command_to_bytes(msg: dict) -> bytes | None:
    """Turn a Unity JSON command into the bytes the ball expects."""
    cmd = msg.get("cmd")
    if cmd == "haptic":
        return bytes([CMD_HAPTIC_PLAY, int(msg.get("pattern", 1)) & 0xFF, int(msg.get("intensity", 255)) & 0xFF])
    if cmd == "haptic_level":
        return bytes([CMD_HAPTIC_LEVEL, int(msg.get("strength", 0)) & 0xFF])
    if cmd == "haptic_stop":
        return bytes([CMD_HAPTIC_STOP])
    if cmd == "calibrate":
        which = msg.get("which", "reset")
        return bytes([{"relaxed": CMD_CAL_RELAXED, "squeezed": CMD_CAL_SQUEEZED}.get(which, CMD_CAL_RESET)])
    return None


class Output:
    """Sends readings to Unity and prints a small live bar in the terminal."""

    def __init__(self, host: str, port: int, quiet: bool):
        self.addr = (host, port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.quiet = quiet
        self.start = time.monotonic()
        self.battery: int | None = None
        self.last_print = 0.0

    def send(self, reading: dict, source: str, connected: bool = True):
        msg = {"t": round(time.monotonic() - self.start, 3), **reading,
               "battery": self.battery, "connected": connected, "source": source}
        self.sock.sendto(json.dumps(msg).encode(), self.addr)
        if not self.quiet and time.monotonic() - self.last_print > 0.1:
            self.last_print = time.monotonic()
            p = reading.get("pressure", 0.0)
            bar = "█" * int(p * 30)
            bat = f" bat {self.battery}%" if self.battery is not None else ""
            sys.stdout.write(f"\r[{source}] {p:5.2f} |{bar:<30}|{bat}   ")
            sys.stdout.flush()


class CommandInbox(asyncio.DatagramProtocol):
    """Receives JSON commands from Unity."""

    def __init__(self, queue: asyncio.Queue):
        self.queue = queue

    def datagram_received(self, data, addr):
        try:
            self.queue.put_nowait(json.loads(data.decode()))
        except (ValueError, UnicodeDecodeError):
            print(f"\nIgnoring bad command: {data!r}")


# ---------------------------------------------------------------------------
# Sources
# ---------------------------------------------------------------------------
async def run_ble(out: Output, commands: asyncio.Queue, name: str):
    from bleak import BleakClient, BleakScanner  # imported here so --sim works without bleak

    while True:
        print(f"Looking for '{name}' over Bluetooth...")
        device = await BleakScanner.find_device_by_filter(
            lambda d, adv: (d.name == name) or (SERVICE_UUID in [u.lower() for u in adv.service_uuids]),
            timeout=10.0,
        )
        if device is None:
            print("Not found yet. Is it on and charged? Retrying.")
            out.send({"raw": 0, "smooth": 0, "pressure": 0.0, "seq": 0}, "ble", connected=False)
            continue

        disconnected = asyncio.Event()
        try:
            async with BleakClient(device, disconnected_callback=lambda _: disconnected.set()) as client:
                print(f"Connected to {device.name} ({device.address})")

                def on_pressure(_, data: bytearray):
                    out.send(decode_pressure(data), "ble")

                def on_battery(_, data: bytearray):
                    out.battery = int(data[0])

                await client.start_notify(PRESSURE_UUID, on_pressure)
                try:
                    out.battery = int((await client.read_gatt_char(BATTERY_LEVEL_UUID))[0])
                    await client.start_notify(BATTERY_LEVEL_UUID, on_battery)
                except Exception:
                    pass  # battery service is optional

                while not disconnected.is_set():
                    try:
                        msg = await asyncio.wait_for(commands.get(), timeout=0.5)
                    except asyncio.TimeoutError:
                        continue
                    payload = command_to_bytes(msg)
                    if payload:
                        await client.write_gatt_char(COMMAND_UUID, payload, response=False)
        except Exception as exc:  # connection dropped, out of range, etc.
            print(f"\nConnection lost: {exc}")
        out.send({"raw": 0, "smooth": 0, "pressure": 0.0, "seq": 0}, "ble", connected=False)
        await asyncio.sleep(1.0)


async def run_serial(out: Output, commands: asyncio.Queue, port: str, baud: int, relaxed: int | None, squeezed: int | None):
    """USB serial: v1 ball (Arduino Nano, 9600 baud) or v2 ball over USB-C (115200 baud)."""
    import serial  # pyserial

    ser = serial.Serial(port, baud, timeout=0.2)
    print(f"Reading {port} at {baud} baud")
    smooth, seq = None, 0
    lo, hi = squeezed, relaxed  # calibration in the bridge, since the v1 ball has none

    loop = asyncio.get_running_loop()
    while True:
        line = await loop.run_in_executor(None, ser.readline)
        while not commands.empty():
            msg = commands.get_nowait()
            if msg.get("cmd") == "calibrate" and smooth is not None:
                which = msg.get("which")
                if which == "relaxed": hi = int(smooth)
                elif which == "squeezed": lo = int(smooth)
                else: hi = lo = None
            if msg.get("cmd") == "haptic":  # v2 ball understands single digits
                ser.write(str(int(msg.get("pattern", 1)) % 10).encode())
        try:
            raw = int(line.decode(errors="ignore").strip())
        except ValueError:
            continue
        smooth = raw if smooth is None else smooth + 0.3 * (raw - smooth)
        if hi is None: hi = int(smooth)                      # first reading = relaxed
        if lo is None: lo = int(hi * 0.05)                   # v1 went from ~1023 to ~25
        if (hi - smooth) / max(1, hi - lo) > 1: lo = int(smooth)
        x = min(1.0, max(0.0, (hi - smooth) / max(1, hi - lo)))
        out.send({"raw": raw, "smooth": int(smooth), "pressure": round(1 - (1 - x) ** 2, 4), "seq": seq}, "serial")
        seq = (seq + 1) % 65536


async def run_sim(out: Output, commands: asyncio.Queue, hz: int = 30):
    """Fake squeezes: slow breathing-like squeezes with the odd hard grip."""
    print("Simulating a ball (no hardware). Ctrl+C to stop.")
    out.battery = 92
    seq, t0 = 0, time.monotonic()
    grip_until = 0.0
    while True:
        t = time.monotonic() - t0
        base = 0.35 + 0.3 * math.sin(t * 2 * math.pi / 6.0)          # one squeeze every 6 s
        if random.random() < 0.003:
            grip_until = t + random.uniform(1.0, 2.5)                   # sometimes squeeze hard
        p = 0.92 if t < grip_until else max(0.0, base + random.uniform(-0.03, 0.03))
        x = 1 - math.sqrt(1 - min(1.0, p))                               # undo the curve to fake a raw value
        raw = int(3900 - x * 3700)
        out.send({"raw": raw, "smooth": raw, "pressure": round(p, 4), "seq": seq}, "sim")
        seq = (seq + 1) % 65536
        while not commands.empty():
            print(f"\n(sim) got command: {commands.get_nowait()}")
        await asyncio.sleep(1 / hz)


async def main():
    ap = argparse.ArgumentParser(description="Stream the Press-On ball to Unity over UDP.")
    src = ap.add_mutually_exclusive_group()
    src.add_argument("--sim", action="store_true", help="simulate a ball, no hardware needed")
    src.add_argument("--serial", metavar="PORT", help="read over USB serial instead of Bluetooth")
    ap.add_argument("--baud", type=int, default=115200, help="serial baud (v1 Arduino Nano ball: 9600)")
    ap.add_argument("--relaxed", type=int, help="serial only: reading with the ball untouched")
    ap.add_argument("--squeezed", type=int, help="serial only: reading squeezed hard")
    ap.add_argument("--name", default="Press-On", help="Bluetooth name to look for")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--out-port", type=int, default=9050, help="UDP port Unity listens on")
    ap.add_argument("--in-port", type=int, default=9051, help="UDP port for commands from Unity")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    out = Output(args.host, args.out_port, args.quiet)
    commands: asyncio.Queue = asyncio.Queue()
    loop = asyncio.get_running_loop()
    await loop.create_datagram_endpoint(lambda: CommandInbox(commands), local_addr=(args.host, args.in_port))
    print(f"Sending to udp://{args.host}:{args.out_port}, listening for commands on {args.in_port}")

    if args.sim:
        await run_sim(out, commands)
    elif args.serial:
        await run_serial(out, commands, args.serial, args.baud, args.relaxed, args.squeezed)
    else:
        await run_ble(out, commands, args.name)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nBye!")
