"""Воспроизведение: поворот персонажа по четырём направлениям.

Персонаж должен идти сам, поэтому подставляем заглушку IInputReader —
это штатная точка внедрения PlayerController. Смотрим фактический
localRotation узла Visual и снимаем кадр на каждое направление.
"""

import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TOOLS = os.path.join(ROOT, ".workbuddy-ai", "tools")


def run(snippet):
    result = call("execute_code", {"action": "execute", "code": snippet})
    data = result.get("result", {}).get("data", {})
    return data.get("result") if isinstance(data, dict) else result


def main():
    call("manage_editor", {"action": "play"})
    time.sleep(6)

    print("stub:", run(open(os.path.join(TOOLS, "stub_input.cs"), encoding="utf-8").read()))

    directions = [
        ("down", 0.0, -1.0),
        ("up", 0.0, 1.0),
        ("right", 1.0, 0.0),
        ("left", -1.0, 0.0),
    ]

    for name, x, y in directions:
        snippet = (
            'var holder = ProbeInputHolder.Instance;'
            f'holder.Direction = new UnityEngine.Vector2({x}f, {y}f);'
            'return "set";'
        )
        run(snippet)
        time.sleep(1.2)

        sample = (
            'var p = GameObject.Find("Player");'
            'var v = p.transform.Find("Visual");'
            'var c = p.GetComponent<Woodberry.Gameplay.Player.PlayerController>();'
            'return "facing=" + c.Facing.ToString("F2")'
            ' + " rotZ=" + v.localEulerAngles.z.ToString("F1")'
            ' + " pos=" + p.transform.position.ToString("F2");'
        )
        print("  ", name, "->", run(sample))
        call("manage_scene", {
            "action": "screenshot", "fileName": "face_" + name + ".png",
            "superSize": 1, "include_image": False,
        })
        time.sleep(2.0)

    call("manage_editor", {"action": "stop"})


if __name__ == "__main__":
    main()
