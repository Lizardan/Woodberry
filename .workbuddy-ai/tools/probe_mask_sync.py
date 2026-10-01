"""Проверка, что камера маски не расходится с основной при движении.

Дрожание контура было следствием рассинхрона: маска снималась с прошлой
позиции камеры. Теперь камера маски — дочерняя, поэтому расхождение
обязано быть нулевым на каждом шаге.
"""

import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402


def run(snippet):
    result = call("execute_code", {"action": "execute", "code": snippet})
    data = result.get("result", {}).get("data", {})
    return data.get("result") if isinstance(data, dict) else result


SAMPLE = """
var main = Camera.main;
var mask = main.transform.Find("VisionMaskCamera");
if (mask == null) return "НЕТ камеры маски в детях основной";
var mc = mask.GetComponent<Camera>();
var rig = main.GetComponent<Woodberry.CameraRig.TopDownCameraRig>();
var player = GameObject.Find("Player");
return "d=" + Vector3.Distance(main.transform.position, mask.position).ToString("F6")
    + " child=" + (mask.parent == main.transform)
    + " main=" + main.transform.position.ToString("F3")
    + " mask=" + mask.position.ToString("F3")
    + " player=" + player.transform.position.ToString("F3")
    + " target=" + (rig.Target != null ? rig.Target.position.ToString("F3") : "NULL")
    + " rigOn=" + rig.enabled
    + " rtsize=" + (mc.targetTexture != null ? mc.targetTexture.width + "x" + mc.targetTexture.height : "нет");
"""


def main():
    call("manage_editor", {"action": "play"})
    time.sleep(6)

    print("старт:", run(SAMPLE))

    # Имитируем движение: камера идёт за игроком рывками по 0.15 юнита.
    for i in range(10):
        x = -3.0 + i * 0.6
        y = 0.2 * i
        run('var p = GameObject.Find("Player");'
            f'p.transform.position = new Vector3({x}f, {y}f, 0f);'
            f'var b = p.GetComponent<Rigidbody2D>(); if (b != null) b.position = new Vector2({x}f, {y}f);'
            'return "ok";')
        time.sleep(0.25)
        print("  шаг", i, run(SAMPLE))

    call("manage_editor", {"action": "stop"})


if __name__ == "__main__":
    main()
