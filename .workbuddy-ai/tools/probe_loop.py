"""Тикает ли игровой цикл в редакторе во время замеров.

Если frameCount между вызовами не растёт, все наблюдения «камера не
двинулась» и «полигон замер» — артефакт окружения, а не дефект продукта.
"""

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
return "frame=" + Time.frameCount
    + " dt=" + Time.deltaTime.ToString("F5")
    + " time=" + Time.time.ToString("F2")
    + " scale=" + Time.timeScale
    + " paused=" + UnityEditor.EditorApplication.isPaused
    + " focused=" + Application.isFocused;
"""


def main():
    call("manage_editor", {"action": "play"})
    time.sleep(6)

    for i in range(5):
        print(run(SAMPLE))
        time.sleep(1.0)

    call("manage_editor", {"action": "stop"})


if __name__ == "__main__":
    main()
