"""Воспроизведение: что видно у окна на разном расстоянии.

Кадры тёмные, поэтому рядом сохраняем осветлённую версию — иначе «два
конуса» и «один конус» на скриншоте не различить.
"""

import os
import sys
import time

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SHOTS = os.path.join(ROOT, "Assets", "Screenshots")
OUT = os.path.join(ROOT, ".workbuddy-ai", "qa")

# Окно на востоке: центр (4.75, 0), активация с 2.4 единиц.
POSITIONS = [
    ("win_d3_0", 1.75, 0.0),
    ("win_d2_0", 2.75, 0.0),
    ("win_d1_2", 3.55, 0.0),
    ("win_d0_6", 4.15, 0.0),
]


def run(snippet):
    return call("execute_code", {"action": "execute", "code": snippet})


def main():
    os.makedirs(OUT, exist_ok=True)
    call("manage_editor", {"action": "play"})
    time.sleep(6)

    for name, x, y in POSITIONS:
        run('var p = GameObject.Find("Player");'
            f'p.transform.position = new Vector3({x}f, {y}f, 0f);'
            f'var b = p.GetComponent<Rigidbody2D>(); if (b != null) b.position = new Vector2({x}f, {y}f);'
            'return "ok";')
        time.sleep(2.0)
        call("manage_scene", {
            "action": "screenshot", "fileName": name + ".png",
            "superSize": 1, "include_image": False,
        })
        time.sleep(2.5)

        src = os.path.join(SHOTS, name + ".png")
        if os.path.exists(src):
            im = Image.open(src).convert("RGB")
            a = np.asarray(im, dtype=np.float32)
            Image.fromarray(np.clip(a * 3.0, 0, 255).astype(np.uint8)).save(
                os.path.join(OUT, name + "_lit.png"))
            call("manage_asset", {"action": "delete", "path": "Assets/Screenshots/" + name + ".png"})
            print("снят", name)

    call("manage_editor", {"action": "stop"})


if __name__ == "__main__":
    main()
