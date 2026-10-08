"""Диагностика: где на кадре оказалось тело после сценической сборки.

Проблема, которую решает скрипт: в тёмных сценах тело рисуется, но не
читается. Глазами по готовому PNG не понять, это «тело не там, где нужно»
или «тело там, но его погасил свет». Скрипт считает маску фигуры и печатает
её габариты и среднюю яркость по зонам кадра.

Запуск:
    python probe_body.py --seed 15
"""

from __future__ import annotations

import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import art_common as ac  # noqa: E402
import art_portrait as ap  # noqa: E402
import art_stage as st  # noqa: E402


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--seed", type=int, default=15)
    p.add_argument("--size", type=int, default=512)
    args = p.parse_args()

    # пол и телосложение разыгрываются тем же порядком, что в main()
    rng_probe = np.random.default_rng(args.seed)
    ap._sample(rng_probe, ap.ARCHETYPES)
    sex = ap._sample(rng_probe, ap.SEXES)
    build = ap._sample(rng_probe, ap.BUILDS)
    shape = ap.BUILD_SHAPE[build]
    ap.build_portrait(args.seed, size=args.size, sex=sex, build=build)

    rng = np.random.default_rng(args.seed + 77)
    pose_name = st.POSES[int(rng.integers(0, len(st.POSES)))]
    scene_name = st.SCENES[int(rng.integers(0, len(st.SCENES)))]
    pose = st.POSE_DATA[pose_name]
    u = float(args.size)

    print(f"seed={args.seed} sex={sex} build={build} pose={pose_name} scene={scene_name}")

    body = ac.Layer(args.size, ss=1)
    hcx, hcy = 0.0, u * 0.055
    st.paint_body(body, rng, hcx, hcy, u * 0.150, u * 0.180,
                  pose["body"], shape, u)
    body_rgba = body.downscale()
    body_warped = st.warp_body(body_rgba, pose, u)

    alpha = body_warped[..., 3]
    mask = alpha > 0.1
    print("тело: alpha>0.1 пикселей", int(mask.sum()), "из", args.size ** 2)

    if mask.any():
        rows = np.where(mask.any(axis=1))[0]
        cols = np.where(mask.any(axis=0))[0]
        print("  габарит y:", int(rows[0]), "..", int(rows[-1]),
              f"({rows[0] / args.size:.0%}..{rows[-1] / args.size:.0%} кадра)")
        print("  габарит x:", int(cols[0]), "..", int(cols[-1]),
              f"({cols[0] / args.size:.0%}..{cols[-1] / args.size:.0%} кадра)")
        print("  альфа: max", round(float(alpha.max()), 3),
              "mean", round(float(alpha[mask].mean()), 3))

    # зоны: где голова, где грудь, где низ
    for name, (y0, y1) in (("голова", (0.20, 0.40)),
                           ("грудь", (0.40, 0.62)),
                           ("низ", (0.62, 0.90))):
        sl = body_warped[int(y0 * args.size):int(y1 * args.size)]
        a = sl[..., 3]
        m = a > 0.1
        if m.any():
            rgb = sl[..., :3][m]
            print(f"  {name}: доля {m.mean():.1%}  яркость {rgb.mean():.3f}")
        else:
            print(f"  {name}: пусто")

    # как сцена гасит тело
    lum, tint = st.scene_light(args.size, u, rng, scene_name, pose)
    for name, (y0, y1) in (("грудь", (0.40, 0.62)), ("низ", (0.62, 0.90))):
        band = lum[int(y0 * args.size):int(y1 * args.size)]
        print(f"  свет {name}: mean {band.mean():.3f} max {band.max():.3f}")


if __name__ == "__main__":
    main()
