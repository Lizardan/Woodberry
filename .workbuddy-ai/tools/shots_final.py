"""Финальные кадры для ручного QA: комната, окно, лес снаружи."""

import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402


def run(snippet):
    return call("execute_code", {"action": "execute", "code": snippet})


def shot(name):
    return call("manage_scene", {
        "action": "screenshot", "fileName": name, "superSize": 1, "include_image": False,
    }).get("status")


def place(x, y):
    return run(
        'var p = GameObject.Find("Player");'
        f'p.transform.position = new Vector3({x}f, {y}f, 0f);'
        f'var b = p.GetComponent<Rigidbody2D>(); if (b != null) b.position = new Vector2({x}f, {y}f);'
        'return "ok";')


def main():
    call("manage_editor", {"action": "play"})
    time.sleep(6)

    for name, x, y in (
        ("qa_room_center", 0.0, 0.5),
        ("qa_room_table", -1.6, 1.0),
        ("qa_window_north", -2.5, 1.9),
        ("qa_window_east", 3.6, 0.0),
    ):
        place(x, y)
        time.sleep(2.0)
        print(name, shot(name + ".png"))
        time.sleep(2.5)

    call("manage_editor", {"action": "stop"})


if __name__ == "__main__":
    main()
