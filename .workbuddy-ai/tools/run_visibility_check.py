"""Прогон проверки обзора в Play Mode: три позиции игрока и три скриншота.

Игрок не может дойти сам: сцена запускается без Bootstrap, поэтому ввода нет.
Ставим его позицией напрямую и смотрим, что видно с каждой точки.
"""

import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def code(snippet):
    return call("execute_code", {"action": "execute", "code": snippet})


def shot(name):
    result = call("manage_scene", {
        "action": "screenshot",
        "fileName": name,
        "superSize": 1,
        "include_image": False,
    })
    return result.get("status")


def move(x, y):
    snippet = f"""
var player = GameObject.Find("Player");
player.transform.position = new Vector3({x}f, {y}f, 0f);
var body = player.GetComponent<Rigidbody2D>();
if (body != null) body.position = new Vector2({x}f, {y}f);
return player.transform.position.ToString("F2");
"""
    return code(snippet)


def main():
    print("play:", call("manage_editor", {"action": "play"}).get("status"))
    time.sleep(6)

    positions = [
        ("vis_center", 0.0, 0.5),
        ("vis_north_window", -2.5, 1.9),
        ("vis_east_window", 3.6, 0.0),
    ]

    for name, x, y in positions:
        print(name, "->", json.dumps(move(x, y), ensure_ascii=False)[:160])
        time.sleep(1.5)
        print("   shot:", shot(name + ".png"))
        time.sleep(2.5)

    print("probe:", json.dumps(code(open(
        os.path.join(ROOT, ".workbuddy-ai/tools/probe_window.cs"),
        encoding="utf-8").read()), ensure_ascii=False)[:1200])

    print("stop:", call("manage_editor", {"action": "stop"}).get("status"))


if __name__ == "__main__":
    main()
