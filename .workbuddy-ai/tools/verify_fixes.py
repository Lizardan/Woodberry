"""Финальная проверка трёх правок в живом редакторе.

1. Окно: наружу выходит ровно один конус, круг игрока через проём не течёт.
2. Разворот: угол берётся той же функцией, что и в игре.
3. Маска: камера маски не расходится с основной при движении.

Ввода у игрока в этой сцене нет (нет composition root), поэтому поворот
ставится вызовом самой PlayerFacing — проверяется реальный путь вычисления,
а не подставленное число.
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

FACINGS = [("down", 0.0, -1.0), ("right", 1.0, 0.0), ("up", 0.0, 1.0), ("left", -1.0, 0.0)]


def run(snippet):
    result = call("execute_code", {"action": "execute", "code": snippet})
    data = result.get("result", {}).get("data", {})
    return data.get("result") if isinstance(data, dict) else result


def shot(name):
    call("manage_scene", {
        "action": "screenshot", "fileName": name + ".png",
        "superSize": 1, "include_image": False,
    })
    time.sleep(2.5)
    src = os.path.join(SHOTS, name + ".png")
    if not os.path.exists(src):
        print("  нет файла", name)
        return None
    image = Image.open(src).convert("RGB")
    array = np.asarray(image, dtype=np.float32)
    Image.fromarray(np.clip(array * 3.0, 0, 255).astype(np.uint8)).save(
        os.path.join(OUT, name + "_lit.png"))
    call("manage_asset", {"action": "delete", "path": "Assets/Screenshots/" + name + ".png"})
    return array


def main():
    os.makedirs(OUT, exist_ok=True)
    call("manage_editor", {"action": "play"})
    time.sleep(6)

    # 1. Окно: подходим вплотную.
    run('var p = GameObject.Find("Player");'
        'p.transform.position = new Vector3(3.55f, 0f, 0f);'
        'var b = p.GetComponent<Rigidbody2D>(); if (b != null) b.position = new Vector2(3.55f, 0f);'
        'return "ok";')
    time.sleep(2.0)
    print("окно:", "снято" if shot("fix_window") is not None else "ошибка")

    # 2. Разворот: ставим угол той же функцией, что использует контроллер.
    for name, x, y in FACINGS:
        angle = run(
            'var v = GameObject.Find("Player").transform.Find("Visual");'
            f'var dir = new UnityEngine.Vector2({x}f, {y}f);'
            'var deg = Woodberry.Gameplay.Player.PlayerFacing.ComputeZRotation(dir);'
            'v.localRotation = UnityEngine.Quaternion.Euler(0f, 0f, deg);'
            'return deg.ToString("F1");')
        print("  разворот", name, "-> угол", angle)
        time.sleep(0.6)
        shot("fix_face_" + name)

    # 3. Маска: расхождение камер при движении.
    worst = 0.0
    for i in range(8):
        x = -2.0 + i * 0.5
        run('var p = GameObject.Find("Player");'
            f'p.transform.position = new Vector3({x}f, 0.5f, 0f);'
            f'var b = p.GetComponent<Rigidbody2D>(); if (b != null) b.position = new Vector2({x}f, 0.5f);'
            'return "ok";')
        time.sleep(0.25)
        value = run('var m = Camera.main;'
                    'var k = m.transform.Find("VisionMaskCamera");'
                    'return UnityEngine.Vector3.Distance(m.transform.position, k.position).ToString("F6");')
        try:
            worst = max(worst, float(str(value).replace(",", ".")))
        except ValueError:
            print("  не разобрал:", value)

    print("максимальное расхождение камер маски и основной:", worst)

    call("manage_editor", {"action": "stop"})


if __name__ == "__main__":
    main()
