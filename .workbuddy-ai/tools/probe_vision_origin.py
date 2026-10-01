"""Где на самом деле стоит полигон обзора относительно игрока.

Смотрим габариты полигона, а не центр: центр смещается обрезкой стенами
и конусом окна, а габариты однозначно показывают, вокруг чего он построен.
"""

import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_mcp import call  # noqa: E402

SAMPLE = """
var overlay = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.VisibilityOverlay>();
var player = GameObject.Find("Player");
var source = player.GetComponent<Woodberry.Gameplay.Perception.PlayerVisionSource>();
var cam = Camera.main;

var pts = overlay.LastPolygonPoints;
float minX = 9999f, maxX = -9999f, minY = 9999f, maxY = -9999f;
for (int i = 0; i < pts.Count; i++)
{
    if (pts[i].x < minX) minX = pts[i].x;
    if (pts[i].x > maxX) maxX = pts[i].x;
    if (pts[i].y < minY) minY = pts[i].y;
    if (pts[i].y > maxY) maxY = pts[i].y;
}

return "player=" + player.transform.position.ToString("F2")
    + " sourceOrigin=" + source.Origin.ToString("F2")
    + " cam=" + cam.transform.position.ToString("F2")
    + " box=[" + minX.ToString("F1") + ".." + maxX.ToString("F1")
    + " ; " + minY.ToString("F1") + ".." + maxY.ToString("F1") + "]"
    + " points=" + pts.Count;
"""


def run(snippet):
    result = call("execute_code", {"action": "execute", "code": snippet})
    data = result.get("result", {}).get("data", {})
    return data.get("result") if isinstance(data, dict) else result


def main():
    call("manage_editor", {"action": "play"})
    time.sleep(6)

    print("старт:", run(SAMPLE))

    for x in (1.5, 3.0):
        run('var p = GameObject.Find("Player");'
            f'p.transform.position = new Vector3({x}f, 0f, 0f);'
            f'var b = p.GetComponent<Rigidbody2D>(); if (b != null) b.position = new Vector2({x}f, 0f);'
            'return "ok";')
        time.sleep(1.5)
        print("после телепорта в x=%.2f:" % x, run(SAMPLE))

    call("manage_editor", {"action": "stop"})


if __name__ == "__main__":
    main()
