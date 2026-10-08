"""Диагностика: найти источник вертикальной светлой полосы по краю кадра.

Полоса видна в готовом портрете у левой/правой границы. Причина может быть
в теле, в фоне или в зерне, поэтому слои сохраняются ПООТДЕЛЬНОСТИ до
финального композита, а не после него.

Запуск:
    python probe_stripe.py --seed 32000 --out ../portraits_probe
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
    p.add_argument("--seed", type=int, default=32000)
    p.add_argument("--size", type=int, default=512)
    p.add_argument("--out", default=".")
    args = p.parse_args()
    os.makedirs(args.out, exist_ok=True)

    rng_probe = np.random.default_rng(args.seed)
    ap._sample(rng_probe, ap.ARCHETYPES)
    sex = ap._sample(rng_probe, ap.SEXES)
    build = ap._sample(rng_probe, ap.BUILDS)
    shape = ap.BUILD_SHAPE[build]
    portrait, head = ap.build_portrait(args.seed, size=args.size, sex=sex, build=build)

    u = float(args.size)
    rng = np.random.default_rng(args.seed + 77)
    pose_name = st.POSES[int(rng.integers(0, len(st.POSES)))]
    scene_name = st.SCENES[int(rng.integers(0, len(st.SCENES)))]
    pose = st.POSE_DATA[pose_name]
    print(f"seed={args.seed} pose={pose_name} scene={scene_name} turn={pose['turn']}")

    lum, tint = st.scene_light(args.size, u, rng, scene_name, pose)
    yy, xx = np.mgrid[0:args.size, 0:args.size].astype(np.float32)
    below = np.clip((yy / args.size - 0.34) / 0.66, 0.0, 1.0) ** 1.4
    lum = lum + below * 0.52
    bg = st.build_scene_bg(args.size, u, rng, args.seed, scene_name, lum, tint)

    body = ac.Layer(args.size, ss=1)
    hcy = u * 0.055
    body_cx = -pose["turn"] * u * 0.34
    st.paint_body(body, rng, body_cx, hcy, head["hw"], head["hh"],
                  pose["body"], shape, u)
    body_raw = body.downscale()

    body_w = st.warp_body(body_raw, pose, u)
    head_w = st.warp_body(portrait, pose, u)

    # сдвиг головы
    head_cx = -pose["turn"] * u * 0.15
    if abs(head_cx) > 1e-6:
        s = int(round(head_cx))
        head_w = np.roll(head_w, s, axis=1)
        if s > 0:
            head_w[:, :s] = 0.0
        else:
            head_w[:, s:] = 0.0

    def report(name, arr):
        a = arr[..., 3]
        cols = a.mean(axis=0)
        rows = a.mean(axis=1)
        print(f"{name}: alpha cols[0]={cols[0]:.3f} cols[1]={cols[1]:.3f} "
              f"cols[-1]={cols[-1]:.3f} cols[-2]={cols[-2]:.3f}")
        print(f"    alpha rows[0]={rows[0]:.3f} rows[-1]={rows[-1]:.3f}")

    report("body_raw", body_raw)
    report("body_warped", body_w)
    report("head_warped", head_w)
    report("bg", bg)

    def dump(name, arr):
        path = os.path.join(args.out, f"stage_{name}.png")
        data = (np.clip(arr, 0, 1) * 255).astype(np.uint8)
        Image.fromarray(data, "RGBA").save(path)

    dump("bg", bg)
    dump("body", body_w)
    dump("head", head_w)

    # средняя яркость крайних столбцов после всего конвейера
    final, _, _ = st.stage_portrait(portrait, args.seed + 77, size=args.size,
                                    pose_name=pose_name, scene_name=scene_name,
                                    build_shape=shape,
                                    head_hw=head["hw"], head_hh=head["hh"])
    for label, col in (("col 0", 0), ("col 1", 1), ("col 2", 2),
                       ("col -1", -1), ("col -2", -2), ("col -3", -3),
                       ("col mid", args.size // 2)):
        print(f"  final {label}: {final[:, col, :3].mean():.4f}")


if __name__ == "__main__":
    main()
