#!/usr/bin/env python3
"""
Press-On v2 enclosure generator.

Makes printable STLs for a ball that holds a Seeed XIAO ESP32-C3/S3 and a small LiPo,
with a recessed USB-C outlet on the side for charging and flashing:

  shell_top.stl     flexible shell, top half (TPU / NinjaTek, like v1). Has the joining lip.
  shell_bottom.stl  flexible shell, bottom half.
  pod_tray.stl      rigid core (PLA/PETG) that holds the LiPo and the XIAO, with the USB-C sleeve.
  pod_lid.stl       rigid lid for the core, with wire holes and an optional haptic motor seat.
  preview.glb       colored, exploded view of everything (open in any 3D viewer)

    pip install trimesh manifold3d numpy
    python enclosure.py                   # 75 mm ball (v1 size)
    python enclosure.py --diameter 85     # bigger hand, bigger battery room

All sizes in millimeters. MEASURE YOUR PARTS with calipers and adjust the numbers below.
"""
import argparse
import os

import numpy as np
import trimesh
from trimesh.creation import box, cylinder, icosphere
from trimesh.transformations import rotation_matrix, translation_matrix

# ---------------------------------------------------------------------------
# Parts you'll put inside (measure yours!)
# ---------------------------------------------------------------------------
XIAO_L, XIAO_W = 21.0, 17.8      # board outline
XIAO_PCB = 1.2                   # PCB thickness
XIAO_TOP = 3.4                   # tallest part on top (USB-C / shield)
USB_OVERHANG = 1.0               # how far the USB-C receptacle sticks past the PCB edge
USB_CENTER_ABOVE_PCB = 1.6       # receptacle center height above the PCB top

LIPO_L, LIPO_W, LIPO_T = 30.0, 20.0, 4.0   # e.g. "402030" 200 mAh. 502030 = 5 mm thick
FOAM_UNDER_XIAO = 1.0            # foam tape between LiPo and XIAO

# USB-C cable plug opening (the plastic overmold, not the metal tip). Slim cables fit best.
PLUG_W, PLUG_H = 13.0, 7.6

# ---------------------------------------------------------------------------
# Printing
# ---------------------------------------------------------------------------
SHELL_T = 3.0          # flexible shell thickness
POD_WALL = 1.6         # rigid pod walls
FIT = 0.3              # clearance for parts that slide together
LIP_H, LIP_T = 3.0, 1.2


def rounded_box(size, radius, center=(0, 0, 0)):
    """Box with rounded vertical edges (around Z)."""
    sx, sy, sz = size
    r = min(radius, sx / 2 - 0.01, sy / 2 - 0.01)
    core = [box([sx - 2 * r, sy, sz]), box([sx, sy - 2 * r, sz])]
    for dx in (-1, 1):
        for dy in (-1, 1):
            c = cylinder(radius=r, height=sz, sections=32)
            c.apply_translation([dx * (sx / 2 - r), dy * (sy / 2 - r), 0])
            core.append(c)
    m = trimesh.boolean.union(core, engine="manifold")
    m.apply_translation(center)
    return m


def x_tube(width, height, length, radius, x0):
    """Rounded rectangle extruded along +X, starting at x0, centered on Y/Z."""
    t = rounded_box([height, width, length], radius)          # build along Z...
    t.apply_transform(rotation_matrix(np.pi / 2, [0, 1, 0]))   # ...then lay it along X
    t.apply_translation([x0 + length / 2, 0, 0])
    return t


def build(diameter: float):
    R = diameter / 2
    R_in = R - SHELL_T

    # --- Pod interior, in a frame where the USB-C receptacle face sits at x = 0 and its center at z = 0
    in_len = max(XIAO_L + USB_OVERHANG, LIPO_L) + 2 * FIT
    in_w = max(XIAO_W, LIPO_W) + 2 * FIT
    pcb_top = -USB_CENTER_ABOVE_PCB
    pcb_bot = pcb_top - XIAO_PCB
    lipo_top = pcb_bot - FOAM_UNDER_XIAO
    floor_in = lipo_top - LIPO_T - FIT
    lid_in = pcb_top + XIAO_TOP + FIT
    z_bot, z_lid_top = floor_in - POD_WALL, lid_in + POD_WALL
    in_x0 = -in_len
    out_len = in_len + 2 * POD_WALL
    out_w = in_w + 2 * POD_WALL

    def pod_points(shift):
        xs = (in_x0 - POD_WALL + shift, POD_WALL + shift)
        ys = (-out_w / 2, out_w / 2)
        zs = (z_bot, z_lid_top)
        return np.array([[x, y, z] for x in xs for y in ys for z in zs])

    # Push the pod as far toward the outlet as the inner sphere allows (keeps the plug recess short)
    fits = lambda s: np.all(np.linalg.norm(pod_points(s), axis=1) <= R_in - 0.5)
    candidates = [s for s in np.arange(R, -R, -0.1) if fits(s)]
    if not candidates:
        raise SystemExit(f"The electronics don't fit in a {diameter} mm ball. Try --diameter 85 or a thinner LiPo.")
    shift = float(candidates[0])
    recess = R - shift

    # --- Pod tray (open top), with the USB-C sleeve out to the surface
    tray_h = lid_in - z_bot
    outer = rounded_box([out_len, out_w, tray_h], 3.0, center=(shift + (in_x0 - POD_WALL + POD_WALL) / 2, 0, z_bot + tray_h / 2))
    cavity = rounded_box([in_len, in_w, tray_h], 1.5, center=(shift + in_x0 / 2, 0, floor_in + tray_h / 2))
    sleeve_out = x_tube(PLUG_W + 2 * POD_WALL, PLUG_H + 2 * POD_WALL, R + 2 - shift, 2.5, shift - 0.01)
    sleeve_in = x_tube(PLUG_W, PLUG_H, R + 10 - shift, 1.8, shift - POD_WALL - 1)
    outer_sphere = icosphere(subdivisions=5, radius=R)
    sleeve = trimesh.boolean.intersection([sleeve_out, outer_sphere], engine="manifold")
    tray = trimesh.boolean.union([outer, sleeve], engine="manifold")
    tray = trimesh.boolean.difference([tray, cavity, sleeve_in], engine="manifold")

    # --- Pod lid: plate plus a plug that fits inside the tray walls
    plate = rounded_box([out_len, out_w, POD_WALL], 3.0, center=(shift + (in_x0 - POD_WALL + POD_WALL) / 2, 0, lid_in + POD_WALL / 2))
    plug = rounded_box([in_len - 2 * FIT, in_w - 2 * FIT, 2.0], 1.2, center=(shift + in_x0 / 2, 0, lid_in - 1.0))
    plug_hollow = rounded_box([in_len - 2 * FIT - 2.4, in_w - 2 * FIT - 2.4, 2.2], 0.6, center=(shift + in_x0 / 2, 0, lid_in - 1.0))
    lid = trimesh.boolean.difference([trimesh.boolean.union([plate, plug], engine="manifold"), plug_hollow], engine="manifold")
    holes = []
    for y in (-3.0, 3.0):                                  # flex sensor wires
        h = cylinder(radius=1.3, height=10, sections=24)
        h.apply_translation([shift + in_x0 + 4, y, lid_in])
        holes.append(h)
    seat = cylinder(radius=5.2, height=2.0, sections=48)   # 10 mm coin haptic motor, glued on top
    seat.apply_translation([shift + in_x0 / 2 - 3, 0, lid_in + POD_WALL])
    holes.append(seat)
    mh = cylinder(radius=0.9, height=10, sections=16)      # motor wires
    mh.apply_translation([shift + in_x0 / 2 - 3, 6.5, lid_in])
    holes.append(mh)
    # keep the lid clear of the USB sleeve
    holes.append(box([20, PLUG_W + 2 * POD_WALL + 0.6, 30], transform=translation_matrix([shift + 10 - 0.3, 0, 0])))
    lid = trimesh.boolean.difference([lid] + holes, engine="manifold")

    # --- Flexible shell, split at the equator, with the sleeve opening
    shell = trimesh.boolean.difference([outer_sphere, icosphere(subdivisions=5, radius=R_in)], engine="manifold")
    opening = x_tube(PLUG_W + 2 * POD_WALL + FIT, PLUG_H + 2 * POD_WALL + FIT, R + 5, 2.6, 0)
    shell = trimesh.boolean.difference([shell, opening], engine="manifold")
    half = box([4 * R, 4 * R, 2 * R], transform=translation_matrix([0, 0, R]))
    shell_top = trimesh.boolean.intersection([shell, half], engine="manifold")
    shell_bottom = trimesh.boolean.difference([shell, half], engine="manifold")

    # Lip on the top half that slides inside the bottom half
    lip = trimesh.boolean.difference([
        cylinder(radius=R_in + 0.01, height=LIP_H, sections=128),
        cylinder(radius=R_in - LIP_T, height=LIP_H + 1, sections=128),
    ], engine="manifold")
    lip.apply_translation([0, 0, -LIP_H / 2 + 0.01])
    lip = trimesh.boolean.difference([lip, opening], engine="manifold")
    shell_top = trimesh.boolean.union([shell_top, lip], engine="manifold")
    # matching clearance in the bottom half
    groove = cylinder(radius=R_in - LIP_T - FIT, height=LIP_H + FIT, sections=128)
    shell_bottom = trimesh.boolean.difference([shell_bottom, trimesh.boolean.difference([
        cylinder(radius=R_in + FIT, height=LIP_H + FIT, sections=128), groove], engine="manifold").apply_translation([0, 0, -(LIP_H + FIT) / 2])], engine="manifold")

    info = {
        "diameter": diameter,
        "plug_recess_mm": round(recess, 1),
        "pod_outer_mm": [round(out_len, 1), round(out_w, 1), round(z_lid_top - z_bot, 1)],
        "cavity_mm": [round(in_len, 1), round(in_w, 1), round(lid_in - floor_in, 1)],
    }
    return {"shell_top": shell_top, "shell_bottom": shell_bottom, "pod_tray": tray, "pod_lid": lid}, info


def export(parts, info, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for name, mesh in parts.items():
        mesh.export(os.path.join(out_dir, f"{name}.stl"))

    # Colored, exploded preview
    colors = {"shell_top": [255, 186, 20, 140], "shell_bottom": [255, 186, 20, 255],
              "pod_tray": [204, 55, 101, 255], "pod_lid": [118, 60, 152, 255]}
    offsets = {"shell_top": [0, 0, 45], "shell_bottom": [0, 0, -45], "pod_tray": [0, 0, 0], "pod_lid": [0, 0, 18]}
    scene = trimesh.Scene()
    for name, mesh in parts.items():
        m = mesh.copy()
        m.apply_translation(offsets[name])
        m.visual.face_colors = colors[name]
        m.apply_transform(rotation_matrix(-np.pi / 2, [1, 0, 0]))  # glTF is Y-up
        m.apply_scale(0.001)  # glTF is in meters
        scene.add_geometry(m, node_name=name)
    scene.export(os.path.join(out_dir, "preview.glb"))

    print(f"Ball {info['diameter']} mm")
    print(f"  Pod outside: {info['pod_outer_mm']} mm, cavity: {info['cavity_mm']} mm")
    print(f"  USB-C sits {info['plug_recess_mm']} mm below the surface (the cable plug goes into the sleeve)")
    for name, mesh in parts.items():
        print(f"  {name}.stl  watertight={mesh.is_watertight}  volume={mesh.volume / 1000:.1f} cm3")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--diameter", type=float, default=75.0)
    ap.add_argument("--out", default=None, help="output folder (default: stl_<diameter>mm)")
    a = ap.parse_args()
    parts, info = build(a.diameter)
    export(parts, info, a.out or os.path.join(os.path.dirname(os.path.abspath(__file__)), f"stl_{int(a.diameter)}mm"))
