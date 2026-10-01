"""Удаление мёртвых ассетов прежнего персонажа через Unity (не в обход)."""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402

DEAD = [
    "Assets/Woodberry/Art/Characters/PlayerArm.png",
    "Assets/Woodberry/Art/Characters/PlayerHead.png",
    "Assets/Woodberry/Art/Characters/PlayerLeg.png",
    "Assets/Woodberry/Art/Characters/PlayerShadow.png",
    "Assets/Woodberry/Art/Characters/PlayerTorso.png",
    "Assets/Woodberry/Art/Characters/Animations/Idle.anim",
    "Assets/Woodberry/Art/Characters/Animations/Walk.anim",
    "Assets/Woodberry/Art/Characters/Animations/Player.controller",
]

# Сначала контроллер и клипы: префаб на них уже не ссылается, но порядок
# от «корня» к «листьям» избавляет от временных битых ссылок в импорте.
for path in reversed(DEAD) if False else DEAD:
    result = call("manage_asset", {"action": "delete", "path": path})
    print(path, "->", result.get("status"),
          str(result.get("result"))[:80] if result.get("status") != "success" else "ok")

# Скриншоты проверки в Assets не остаются: они попадают в импорт и в сборку.
shots = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))), "Assets", "Screenshots")
if os.path.isdir(shots):
    for name in sorted(os.listdir(shots)):
        if name.endswith(".meta"):
            continue
        result = call("manage_asset", {
            "action": "delete", "path": "Assets/Screenshots/" + name,
        })
        print("Screenshots/" + name, "->", result.get("status"))
