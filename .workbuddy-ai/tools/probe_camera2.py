"""Замер: доезжает ли камера после телепорта игрока. Считает остаток пути."""

import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402


def run(snippet):
    result = call("execute_code", {"action": "execute", "code": snippet})
    return result.get("result", {}).get("data", {}).get("result")


SAMPLE = """
var cam = Camera.main;
var rig = cam.GetComponent<Woodberry.CameraRig.TopDownCameraRig>();
var t = rig.Target;
var desired = new Vector2(t.position.x, t.position.y) + new Vector2(0f, 0.4f);
var current = new Vector2(cam.transform.position.x, cam.transform.position.y);
return "target=" + t.position.ToString("F2")
    + " cam=" + cam.transform.position.ToString("F2")
    + " desired=" + desired.ToString("F2")
    + " remain=" + Vector2.Distance(current, desired).ToString("F3");
"""


def main():
    call("manage_editor", {"action": "play"})
    time.sleep(6)
    print("старт:", run(SAMPLE))

    run('var p = GameObject.Find("Player");'
        'p.transform.position = new Vector3(3.6f, 0f, 0f);'
        'var b = p.GetComponent<Rigidbody2D>(); if (b != null) b.position = new Vector2(3.6f, 0f);'
        'return "moved";')

    for i in range(8):
        time.sleep(0.5)
        print("  t=%.1f" % ((i + 1) * 0.5), run(SAMPLE))

    call("manage_editor", {"action": "stop"})


if __name__ == "__main__":
    main()
