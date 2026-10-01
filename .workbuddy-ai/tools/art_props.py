"""Предметы обстановки дома: стол, ящик, шкаф, ковёр.

Пустая комната не читается как дом: пол, стены и окна сами по себе дают
коробку. Предметы дают масштаб (рядом с ними видно, насколько мал персонаж)
и заодно работают препятствиями — за столом обзор обрезается, что и
показывает «притупление» угла зрения на предметах, а не только на стенах.

Всё рисуется сверху: у стола видна столешница и углы ножек, у ящика —
крышка с набитыми планками, у шкафа — крышка и ручки.
"""

import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from art_common import Layer, fbm, jitter, mix, rgb, save, shade  # noqa: E402


def _wood_texture(size, seed, strength=0.16):
    return fbm(size, base_res=8, octaves=4, seed=seed)


def table(size=128, seed=61):
    """Стол сверху: столешница, доски, тёмная кромка, углы ножек."""
    rng = np.random.default_rng(seed)
    layer = Layer(size, ss=4)

    half_w, half_h = 60.0, 42.0
    wood = rgb("wood_mid")
    wood_lit = rgb("wood_lit")
    wood_dark = rgb("wood_dark")

    # Ножки — тёмные пятна чуть наружу от столешницы.
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            layer.ellipse(sx * (half_w - 6.0), sy * (half_h - 5.0), 7.0, 7.0,
                          jitter(wood_dark, rng, 0.12))

    # Столешница.
    layer.rect(0.0, 0.0, half_w, half_h, jitter(wood, rng, 0.08), radius=5.0)

    # Доски: тёмные линии вдоль длинной оси.
    for i in range(-2, 3):
        y = i * (half_h / 2.4)
        layer.rect(0.0, y, half_w - 3.0, 1.1, wood_dark, alpha=0.55)

    # Кромка: узкая полоса по периметру темнее центра.
    for inset, alpha in ((2.0, 0.35), (4.0, 0.2)):
        layer.rect(0.0, half_h - inset, half_w - inset, 1.4, wood_dark, alpha=alpha)
        layer.rect(0.0, -(half_h - inset), half_w - inset, 1.4, wood_dark, alpha=alpha)
        layer.rect(half_w - inset, 0.0, 1.4, half_h - inset, wood_dark, alpha=alpha)
        layer.rect(-(half_w - inset), 0.0, 1.4, half_h - inset, wood_dark, alpha=alpha)

    rgba = layer.downscale()
    tex = _wood_texture(size, seed + 7)
    return shade(rgba, rim_strength=0.30,
                 rim_color=np.array(rgb("rim_cool"), dtype=np.float32),
                 ambient=0.74, direct=0.42,
                 texture=tex, texture_strength=0.20, rim_width=2)


def crate(size=64, seed=71):
    """Ящик сверху: крышка, набитые планки, тёмные углы."""
    rng = np.random.default_rng(seed)
    layer = Layer(size, ss=4)

    half = size * 0.40
    wood = rgb("wood_mid")
    wood_dark = rgb("wood_dark")

    layer.rect(0.0, 0.0, half, half, jitter(wood, rng, 0.10), radius=3.0)

    # Планки крест-накрест.
    layer.rect(0.0, 0.0, half, 3.0, wood_dark, alpha=0.75)
    layer.rect(0.0, 0.0, 3.0, half, wood_dark, alpha=0.75)
    # Обвязка по периметру.
    for inset in (2.0, 4.0):
        layer.rect(0.0, half - inset, half - inset, 1.6, wood_dark, alpha=0.6)
        layer.rect(0.0, -(half - inset), half - inset, 1.6, wood_dark, alpha=0.6)
        layer.rect(half - inset, 0.0, 1.6, half - inset, wood_dark, alpha=0.6)
        layer.rect(-(half - inset), 0.0, 1.6, half - inset, wood_dark, alpha=0.6)

    rgba = layer.downscale()
    tex = _wood_texture(size, seed + 7, 0.2)
    return shade(rgba, rim_strength=0.30,
                 rim_color=np.array(rgb("rim_cool"), dtype=np.float32),
                 ambient=0.72, direct=0.46,
                 texture=tex, texture_strength=0.24, rim_width=2)


def shelf(size=128, seed=83):
    """Шкаф сверху: узкая длинная крышка с дверцами и ручками."""
    rng = np.random.default_rng(seed)
    layer = Layer(size, ss=4)

    half_w, half_h = size * 0.46, size * 0.20
    wood = rgb("wood_mid")
    wood_dark = rgb("wood_dark")
    metal = rgb("metal")

    layer.rect(0.0, 0.0, half_w, half_h, jitter(wood, rng, 0.09), radius=3.0)
    # Дверцы: две створки.
    layer.rect(0.0, 0.0, 1.4, half_h - 2.0, wood_dark, alpha=0.8)
    for inset in (2.0, 4.0):
        layer.rect(0.0, half_h - inset, half_w - inset, 1.4, wood_dark, alpha=0.55)
        layer.rect(0.0, -(half_h - inset), half_w - inset, 1.4, wood_dark, alpha=0.55)
    # Ручки.
    for sx in (-1.0, 1.0):
        layer.ellipse(sx * 10.0, 0.0, 2.4, 2.4, metal)
        layer.ellipse(sx * -10.0, 0.0, 2.4, 2.4, metal)

    rgba = layer.downscale()
    tex = _wood_texture(size, seed + 7)
    return shade(rgba, rim_strength=0.32,
                 rim_color=np.array(rgb("rim_cool"), dtype=np.float32),
                 ambient=0.70, direct=0.48,
                 texture=tex, texture_strength=0.20, rim_width=2)


def rug(size=192, seed=97):
    """Ковёр сверху: тёмное пятно с потёртым краем. Не препятствие."""
    rng = np.random.default_rng(seed)
    layer = Layer(size, ss=3)

    half_w, half_h = size * 0.42, size * 0.30
    base = mix(rgb("coat_dark"), rgb("leaf"), 0.45)
    layer.rect(0.0, 0.0, half_w, half_h, base, radius=10.0)
    layer.rect(0.0, 0.0, half_w - 8.0, half_h - 8.0, mix(base, rgb("dirt"), 0.4),
               radius=8.0, alpha=0.7)

    rgba = layer.downscale()

    # Рваная кромка: ковёр не должен читаться ровным прямоугольником.
    edge = fbm(size, base_res=8, octaves=4, seed=seed + 300)
    ys, xs = np.mgrid[0:size, 0:size]
    nx = np.abs(xs - size * 0.5) / (half_w + 2.0)
    ny = np.abs(ys - size * 0.5) / (half_h + 2.0)
    falloff = np.clip(1.0 - np.maximum(nx, ny), 0.0, 1.0)
    rgba[..., 3] = np.clip(rgba[..., 3] * (0.45 + 1.1 * falloff) * (0.6 + 0.8 * edge), 0.0, 1.0)

    return shade(rgba, rim_strength=0.16,
                 rim_color=np.array(rgb("rim_cool"), dtype=np.float32),
                 ambient=0.80, direct=0.30,
                 texture=edge, texture_strength=0.18, rim_width=2)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)

    for name, rgba in (
        ("Prop_Table.png", table(128, seed=61)),
        ("Prop_Crate.png", crate(64, seed=71)),
        ("Prop_Shelf.png", shelf(128, seed=83)),
        ("Prop_Rug.png", rug(192, seed=97)),
    ):
        size = save(rgba, os.path.join(out_dir, name))
        print("  ", name, size)


if __name__ == "__main__":
    main()
