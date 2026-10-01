"""Запуск C#-сниппета в редакторе Unity через мост (execute_code).

Код читается из файла, чтобы не бороться с экранированием кавычек в shell.
Обёртка моста уже добавляет `using System; using System.Collections.Generic;
using System.Linq; using System.Reflection; using UnityEngine; using UnityEditor;`
и помещает код внутрь `public static object Execute()`.

Использование:
    python unity_code.py <файл.cs>
    python unity_code.py --inline '<одна строка кода>'
"""

import json
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    if argv[1] == "--inline":
        code = argv[2]
    else:
        with open(argv[1], "r", encoding="utf-8") as handle:
            code = handle.read()

    response = call("execute_code", {"action": "execute", "code": code})
    print(json.dumps(response, ensure_ascii=False, indent=2))

    status = response.get("status")
    result = response.get("result") or {}
    if status == "success" and isinstance(result, dict) and result.get("success") is False:
        return 1
    return 0 if status == "success" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
