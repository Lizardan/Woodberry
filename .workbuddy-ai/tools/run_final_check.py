"""Финальная проверка в Play Mode: обзор, окно, анимация шага.

В сцене Game нет composition root, поэтому ввода у игрока нет и сам он не
пойдёт. Ставим позицию напрямую и включаем параметр аниматора руками —
иначе проверить цикл шага в этой сцене нечем.
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
    return call("execute_code", {"action": "execute", "code": snippet})


def shot(name):
    return call("manage_scene", {
        "action": "screenshot", "fileName": name, "superSize": 1, "include_image": False,
    }).get("status")


def brief(result):
    return json.dumps(result, ensure_ascii=False)[:400]


def main():
    print("play:", call("manage_editor", {"action": "play"}).get("status"))
    time.sleep(6)

    # 1. Обзор в центре комнаты.
    run('GameObject.Find("Player").transform.position = new Vector3(0f, 0.5f, 0f);'
        'var b = GameObject.Find("Player").GetComponent<Rigidbody2D>();'
        'if (b != null) b.position = new Vector2(0f, 0.5f);'
        'return "ok";')
    time.sleep(1.5)
    print("shot center:", shot("final_center.png"))
    time.sleep(2.5)

    # 2. Шаг: включаем параметр аниматора и смотрим, меняются ли кадры.
    run(open(os.path.join(TOOLS, "check_anim.cs"), encoding="utf-8").read())
    frames = []
    for _ in range(10):
        time.sleep(0.12)
        result = run('var r = GameObject.Find("Player").transform.Find("Visual")'
                     '.GetComponent<SpriteRenderer>();'
                     'var a = GameObject.Find("Player").GetComponent<Animator>();'
                     'return r.sprite.name + "|" + a.GetCurrentAnimatorStateInfo(0).normalizedTime.ToString("F2");')
        frames.append(result.get("result", {}).get("data", {}).get("result", "?"))
    print("кадры шага:", json.dumps(frames, ensure_ascii=False))

    print("shot walk:", shot("final_walk.png"))
    time.sleep(2.5)

    # 3. Игрок у восточного окна — виден конус наружу.
    run('GameObject.Find("Player").transform.position = new Vector3(3.6f, 0f, 0f);'
        'var b = GameObject.Find("Player").GetComponent<Rigidbody2D>();'
        'if (b != null) b.position = new Vector2(3.6f, 0f);'
        'return "ok";')
    time.sleep(1.5)
    print("shot window:", shot("final_window.png"))
    time.sleep(2.5)

    print("probe:", brief(run(open(os.path.join(TOOLS, "probe_window.cs"),
                                   encoding="utf-8").read())))
    print("stop:", call("manage_editor", {"action": "stop"}).get("status"))


if __name__ == "__main__":
    main()
