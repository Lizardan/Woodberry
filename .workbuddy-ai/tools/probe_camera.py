"""Замер слежения камеры: телепорт игрока и выборка позиций по времени.

По скриншотам это не читается — сцена тёмная, и «камера не доехала» легко
спутать с «камера доехала, но персонаж не виден». Поэтому меряем числами.
"""

import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402


def run(snippet):
    result = call("execute_code", {"action": "execute", "code": snippet})
    return result.get("result", {}).get("data", {}).get("result")


SAMPLE = """
var player = GameObject.Find("Player");
var cam = Camera.main;
var rig = cam.GetComponent<Woodberry.CameraRig.TopDownCameraRig>();
var target = rig != null ? rig.Target : null;
return "player=" + player.transform.position.ToString("F2")
    + " cam=" + cam.transform.position.ToString("F2")
    + " target=" + (target == null ? "NULL" : target.name + target.position.ToString("F2"));
"""


def main():
    print("play:", call("manage_editor", {"action": "play"}).get("status"))
    time.sleep(6)

    print("старт:", run(SAMPLE))

    run('var p = GameObject.Find("Player");'
        'p.transform.position = new Vector3(3.6f, 0f, 0f);'
        'var b = p.GetComponent<Rigidbody2D>(); if (b != null) b.position = new Vector2(3.6f, 0f);'
        'return "moved";')

    for i in range(6):
        time.sleep(0.7)
        print("  t=%.1fs" % ((i + 1) * 0.7), run(SAMPLE))

    print("stop:", call("manage_editor", {"action": "stop"}).get("status"))


if __name__ == "__main__":
    main()
