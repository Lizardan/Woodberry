"""Ждёт, пока редактор переживёт перекомпиляцию и снова начнёт отвечать."""

import json
import sys
import time

sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
from unity_mcp import call  # noqa: E402


def wait(attempts=60, delay=3.0):
    for i in range(attempts):
        try:
            result = call("read_console", {
                "action": "get", "types": ["error"], "count": 50, "format": "plain",
            }, timeout=15)
            return result
        except Exception:
            time.sleep(delay)
    return None


if __name__ == "__main__":
    result = wait()
    print(json.dumps(result, ensure_ascii=False, indent=2) if result else "мост не поднялся")
