"""Финальные кадры для QA: снять в Assets, вынести наружу, из Assets убрать.

Скриншоты в `Assets/` попадают в импорт и в сборку, поэтому по
`docs/specs/asset-standards.md` после проверки их там быть не должно.
"""

import os
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SHOTS = os.path.join(ROOT, "Assets", "Screenshots")
OUT = os.path.join(ROOT, ".workbuddy-ai", "qa")

PLACES = (
    ("qa_room_center", 0.0, 0.5),
    ("qa_room_table", -1.6, 1.0),
    ("qa_window_north", -2.5, 1.9),
    ("qa_window_east", 3.6, 0.0),
)


def run(snippet):
    return call("execute_code", {"action": "execute", "code": snippet})


def main():
    os.makedirs(OUT, exist_ok=True)

    call("manage_editor", {"action": "play"})
    time.sleep(6)

    for name, x, y in PLACES:
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
        print("снят", name)

    call("manage_editor", {"action": "stop"})
    time.sleep(2)

    for name, _, _ in PLACES:
        src = os.path.join(SHOTS, name + ".png")
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(OUT, name + ".png"))
            call("manage_asset", {"action": "delete", "path": "Assets/Screenshots/" + name + ".png"})

    leftovers = [f for f in os.listdir(SHOTS) if not f.endswith(".meta")] if os.path.isdir(SHOTS) else []
    print("осталось в Assets/Screenshots:", leftovers)
    print("вынесено в:", OUT)


if __name__ == "__main__":
    main()
