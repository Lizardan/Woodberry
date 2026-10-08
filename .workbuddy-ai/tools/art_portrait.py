"""Процедурные портреты жителей деревни для Woodberry.

Диалог в игре подаётся как в визуальной новелле: крупный портрет + текст.
Этот генератор собирает гротескные лица тёмного фэнтези так, что одна и та
же деревня (seed) даёт одни и те же лица.

Запуск:
    python art_portrait.py --out PR_DIR [--count 4] [--seed 1] [--size 512]

─────────────────────────── Как это нарисовано ───────────────────────────

Первая версия рисовала «овал + два глаза + рот» плоскими заливками, и лица
выходили игрушечными: ровные светлые пятна, симметрия, никакого возраста.
Гротеск живёт не в форме, а в ТОНАЛЬНОМ РЕЛЬЕФЕ, поэтому сборка тут такая:

1. `_skin_base` — не заливка, а тональная карта кожи: пятна, прожилки,
   холодный подтон в тенях, землистый по краям. Считается по numpy.
2. `_sculpt` — рельеф черепа. Каждая «косточка» (лоб, скулы, челюсть,
   надбровные дуги) добавляет к карте света гауссову выпуклость или впадину.
   Именно этот шаг даёт объём и асимметрию; без него лицо мертво.
3. Черты (нос, глаза, губы, уши, морщины) рисуются поверх, но тоже как
   объём: у каждого своя тень и свой блик.
4. Волосы, борода, убор — отдельный слой поверх кожи, со своей текстурой.
5. Финальный проход `_grade` — общий свет сбоку, кромка, виньетка, зерно.

Поэтому весь код ниже — не «набор примитивов», а конвейер слоёв: каждый
следующий шаг знает о предыдущем только через результирующий RGBA, а общая
форма держится маской силуэта.
"""

from __future__ import annotations

import argparse
import math
import os

import numpy as np
from PIL import Image, ImageFilter

import art_common as ac
from art_common import Layer

# ─────────────────────────── Палитра портрета ───────────────────────────
# Отдельная от сцены: портрет смотрят вблизи, поэтому кожа и волосы не
# могут быть такими же глухими, как силуэт на полу. Тон остаётся больным,
# но появляются полутона — иначе лицо перестаёт читаться.

P = {
    # фон
    "void":        (0.022, 0.026, 0.032),
    "void_glow":   (0.098, 0.088, 0.074),

    # кожа — четыре тона: холодная, серая, желтушная, мертвецкая
    "skin_cold":   (0.560, 0.505, 0.470),
    "skin_grey":   (0.452, 0.430, 0.412),
    "skin_sallow": (0.545, 0.462, 0.352),
    "skin_dead":   (0.408, 0.412, 0.406),

    # тона кожи, задающие рельеф (к ним же подмешивается цвет в тенях)
    "skin_hi":     (0.665, 0.585, 0.510),
    "skin_mid":    (0.500, 0.440, 0.395),
    "skin_lo":     (0.310, 0.262, 0.232),
    "skin_deep":   (0.150, 0.118, 0.104),
    "skin_blue":   (0.322, 0.310, 0.322),   # холодный подтон в тенях
    "skin_rot":    (0.398, 0.278, 0.246),   # землистая болезненность

    # волосы
    "hair_dark":   (0.098, 0.090, 0.082),
    "hair_ash":    (0.243, 0.230, 0.208),
    "hair_grey":   (0.420, 0.412, 0.392),
    "hair_rust":   (0.286, 0.148, 0.088),

    # глаза
    "sclera":      (0.648, 0.605, 0.548),
    "sclera_ill":  (0.470, 0.500, 0.470),   # желтушный белок
    "sclera_blood": (0.520, 0.520, 0.520),
    "iris_a":      (0.098, 0.078, 0.062),
    "iris_b":      (0.210, 0.118, 0.060),
    "iris_c":      (0.140, 0.170, 0.155),
    "pupil":       (0.030, 0.028, 0.026),

    # губы
    "lip":         (0.360, 0.248, 0.226),
    "lip_dry":     (0.300, 0.212, 0.196),
    "lip_dark":    (0.180, 0.118, 0.110),

    # одежда и металл
    "cloth_dark":  (0.085, 0.090, 0.088),
    "cloth_mid":   (0.148, 0.152, 0.144),
    "cloth_lit":   (0.230, 0.230, 0.212),
    "leather":     (0.128, 0.100, 0.078),
    "iron":        (0.210, 0.212, 0.222),
    "iron_lit":    (0.400, 0.400, 0.412),
    "bone":        (0.545, 0.528, 0.470),

    # эффекты
    "rim":         (0.480, 0.560, 0.690),
    "outline":     (0.026, 0.029, 0.034),
    "blood":       (0.285, 0.072, 0.062),
}

ARCHETYPES = [
    "gaunt",     # худой, вытянутый череп, впалые щёки
    "heavy",     # тяжёлый, широкий, массивная челюсть
    "hollow",    # провалы глаз, черепной гость
    "bloated",   # отёчный, болезненный
    "sharp",     # острые угловатые черты
    "broken",    # следы побоев, перекос
    "saintly",   # сухой аскетичный лик
    "witchy",    # крупные глаза, острый подбородок
    "ancient",   # глубокая старость, пергаментная кожа
]

# Пол — отдельная ось, а не архетип. Он меняет не «характер», а анатомию:
# пропорции черепа, линию бровей, массивность челюсти, наличие бороды,
# набор доступных причёсок и форму плеч. Без этой оси все жители деревни
# выходили бородатыми мужиками.
SEXES = ["male", "female"]

# Телосложение — третья ось. Влияет на ширину плеч, высоту фигуры и посадку.
BUILDS = ["slight", "average", "broad", "stout"]
BUILD_SHAPE = {
    # (ширина плеч, высота/посадка, толщина воротника)
    "slight":  dict(shoulder=0.86, drop=0.02, collar=0.78),
    "average": dict(shoulder=1.00, drop=0.00, collar=1.00),
    "broad":   dict(shoulder=1.24, drop=-0.01, collar=1.22),
    "stout":   dict(shoulder=1.16, drop=0.04, collar=1.34),
}

HAIR_STYLES = ["short", "long", "wild", "matted", "receding", "bald", "pulled", "braided"]
# Женские причёски: те же формы, но без залысин и с убранными волосами
FEMALE_HAIR = ["long", "long", "pulled", "braided", "wild", "matted", "headscarf_wrap"]
BEARD_KINDS = ["none", "none", "stubble", "full", "goatee", "mutton"]
EYE_KINDS = ["normal", "heavy", "sunken", "wide", "narrow"]
NOSE_KINDS = ["straight", "hooked", "bulbous", "broken", "pinched", "long"]
HEAD_WEAR = ["none", "none", "none", "hood", "hat", "headscarf", "coif"]


def _c(name):
    return P[name]


def _u(rng, lo, hi):
    return float(rng.uniform(lo, hi))


def _sample(rng, options):
    return options[int(rng.integers(0, len(options)))]


def _blur(mask, radius):
    if radius <= 0:
        return mask
    img = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8), "L")
    img = img.filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img, dtype=np.float32) / 255.0


# ─────────────────────────── Силуэт ───────────────────────────

def _face_outline(hw, hh, taper, shear, asymmetry):
    """Контур черепа: округлый верх, сужение к подбородку, перекос.

    `asymmetry` ломает зеркальность: реальный череп не симметричен, и левая
    половина лица всегда чуть массивнее правой. Без этого лица выглядят
    как отражение, а отражение — признак мультяшности.
    """
    pts = []
    n = 72
    for i in range(n):
        a = math.pi * 2.0 * i / n
        s, c = math.sin(a), -math.cos(a)
        side = 1.0 if s >= 0 else -1.0
        x = s * hw * (1.0 + asymmetry * side * 0.5)
        y = c * hh
        if y > 0:  # нижняя половина — сужается к подбородку
            k = y / max(hh, 1e-6)
            x *= 1.0 - (1.0 - taper) * (k ** 1.25)
        # перекос: смещение растёт к подбородку
        x += shear * hw * (y / max(hh, 1e-6)) ** 2
        # лёгкая бугристость черепа в верхней половине
        if y < 0:
            x += math.sin(a * 3.0 + asymmetry * 4.0) * hw * 0.018
        pts.append((x, y))
    return pts


def _mask_from_polygon(size, pts, cx, cy):
    """Растровая маска силуэта — ею потом режется каждый слой."""
    img = Image.new("L", (size, size), 0)
    from PIL import ImageDraw
    ImageDraw.Draw(img).polygon(
        [(size * 0.5 + x, size * 0.5 + y) for x, y in pts], fill=255)
    return np.asarray(img, dtype=np.float32) / 255.0


# ─────────────────────────── Кожа ───────────────────────────

def _skin_base(size, rng, seed, skin_tone, archetype, hw, hh, cx, cy):
    """Тональная карта кожи: пятна, прожилки, неравномерность.

    Не заливка, а база под рельеф: чем разнороднее этот слой, тем живее
    выглядит итог. Молодая кожа чище, старая — в пигментных пятнах и
    красноте у носа.
    """
    age = _u(rng, 0.0, 1.0)
    if archetype == "ancient":
        age = max(age, 0.85)
    if archetype in ("saintly", "witchy"):
        age = min(age, 0.45)

    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    nx = (xx - (size * 0.5 + cx)) / max(hw, 1e-6)
    ny = (yy - (size * 0.5 + cy)) / max(hh, 1e-6)

    # крупные пятна тона
    blotch = ac.fbm(size, base_res=3, octaves=4, seed=seed + 21)
    # мелкая фактура пор
    pore = ac.fbm(size, base_res=64, octaves=2, seed=seed + 22)
    # прожилки
    veins = ac.ridge(size, seed=seed + 23, base_res=10, octaves=3)

    tone = np.array(skin_tone, dtype=np.float32)[None, None, :] * np.ones(
        (size, size, 1), dtype=np.float32)

    # разнотон: пятна усиливаются к краям лица и с возрастом
    edge = np.clip(np.sqrt(nx * nx + ny * ny) - 0.55, 0.0, 1.0)
    spot = (blotch - 0.5) * (0.085 + 0.075 * age) * (0.6 + edge * 1.4)
    tone *= (1.0 + spot)[..., None]

    # землистый подтон по краям — кожа уходит в холодную грязь
    tone = tone * (1.0 - edge[..., None] * 0.30) + \
        np.array(_c("skin_rot"), dtype=np.float32)[None, None, :] * \
        (edge * 0.30 * (0.4 + 0.6 * age))[..., None]

    # краснота у носа и щёк
    nose_zone = np.exp(-(((nx / 0.42) ** 2 + ((ny - 0.05) / 0.34) ** 2)))
    tone = tone * (1.0 - nose_zone[..., None] * 0.22) + \
        np.array(_c("skin_rot"), dtype=np.float32)[None, None, :] * \
        (nose_zone * 0.22 * (0.5 + 0.5 * age))[..., None]

    # прожилки под глазами и на висках
    vein_mask = np.clip(veins - 0.72, 0.0, 1.0) / 0.28
    cup = np.exp(-(((nx / 0.62) ** 2 + ((ny - 0.02) / 0.20) ** 2)))
    v = np.clip(vein_mask * cup * (0.30 + 0.30 * age), 0.0, 0.55)
    tone = tone * (1.0 - v[..., None]) + \
        np.array(_c("skin_blue"), dtype=np.float32)[None, None, :] * v[..., None]

    # поры — самая мелкая дрожь
    tone *= (0.965 + pore * 0.07)[..., None]

    return np.clip(tone, 0.0, 1.0)


def _bump(size, cx, cy, rx, ry, strength, angle=0.0, falloff=2.0):
    """Гауссова выпуклость/впадина в карте света — «косточка» черепа.

    Это главный инструмент рельефа: выпуклость лба, скула, надбровная дуга,
    впадина виска. Сумма таких бугров и есть лицо как форма, а не как рисунок.
    """
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    dx = xx - (size * 0.5 + cx)
    dy = yy - (size * 0.5 + cy)
    if abs(angle) > 1e-6:
        ca, sa = math.cos(angle), math.sin(angle)
        dx, dy = dx * ca + dy * sa, -dx * sa + dy * ca
    r = np.sqrt((dx / max(rx, 1e-6)) ** 2 + (dy / max(ry, 1e-6)) ** 2)
    return strength * np.exp(-(r ** falloff) * 1.6)


def _sculpt(size, rng, archetype, hw, hh, cx, cy, asym, sex="male"):
    """Карта рельефа: положительное — выпукло, отрицательное — впадина.

    `sex` меняет именно анатомию костей, а не «украшает» лицо: женские
    надбровные дуги слабее, челюсть и подбородок мягче, лоб ровнее.
    Это принципиально: без правки рельефа «женское» лицо остаётся мужским
    с длинными волосами.
    """
    h = np.zeros((size, size), dtype=np.float32)
    female = sex == "female"

    # лоб: у аскетов высокий и гладкий, у тяжёлых — нависающий
    brow_y = cy - hh * 0.42
    h += _bump(size, cx, brow_y, hw * 0.78, hh * 0.30, 0.13 if female else 0.16)
    if archetype in ("hollow", "gaunt"):
        h += _bump(size, cx, cy - hh * 0.22, hw * 0.80, hh * 0.16, -0.10)

    # надбровные дуги — по одной на глаз, слегка разной высоты
    ridge = 0.14 if female else 0.22
    for side in (-1, 1):
        h += _bump(size, cx + side * hw * 0.40 * (1 + asym * side * 0.4),
                   cy - hh * (0.10 + 0.03 * side * asym),
                   hw * 0.36, hh * 0.085, ridge)

    # виски — впадины, они держат объём верхней части
    for side in (-1, 1):
        h += _bump(size, cx + side * hw * 0.90, cy - hh * 0.30,
                   hw * 0.24, hh * 0.22, -0.20)

    # скулы: у женщин выше и мягче округлены
    cheek = 0.30 if archetype in ("gaunt", "hollow", "saintly") else 0.20
    cheek_y = cy + (hh * 0.14 if female else hh * 0.20)
    for side in (-1, 1):
        h += _bump(size, cx + side * hw * 0.66 * (1 + asym * side * 0.3),
                   cheek_y, hw * 0.34, hh * 0.20, cheek)

    # впадины щёк под скулами — самое важное для худобы
    if archetype in ("gaunt", "hollow", "saintly", "ancient"):
        for side in (-1, 1):
            h += _bump(size, cx + side * hw * 0.62, cy + hh * 0.48,
                       hw * 0.30, hh * 0.24, -0.24 if female else -0.30)
    elif archetype == "bloated":
        for side in (-1, 1):
            h += _bump(size, cx + side * hw * 0.58, cy + hh * 0.44,
                       hw * 0.34, hh * 0.26, 0.12)

    # челюсть и подбородок: женские мягче и уже — угловатость убираем
    jaw = 0.07 if female else (0.22 if archetype == "heavy" else 0.12)
    jaw_w = 0.42 if female else 0.52
    h += _bump(size, cx, cy + hh * 0.74, hw * jaw_w, hh * 0.18, jaw)
    h += _bump(size, cx + asym * hw * 0.16, cy + hh * 0.92,
               hw * (0.20 if female else 0.26), hh * 0.14, 0.11 if female else 0.16)

    # переносица — выпукло, но узко
    h += _bump(size, cx, cy - hh * 0.02, hw * (0.085 if female else 0.10),
               hh * 0.20, 0.20)

    # надпереносье — впадина между бровями
    h += _bump(size, cx, cy - hh * 0.14, hw * 0.20, hh * 0.07, -0.16)

    return h


def _apply_relief(tone, relief, light=(0.35, 0.30)):
    """Накладывает рельеф на тональную карту кожи.

    Свет идёт слева-сверху: выпуклость, обращённая к свету, светлеет,
    отвёрнутая — темнеет. Направление берётся из градиента карты, поэтому
    это честное освещение формы, а не «нарисованные тени».
    """
    gy, gx = np.gradient(relief)
    # производная по нормали к свету
    lam = -(gx * light[0] + gy * light[1]) * 6.0
    lam = np.tanh(lam * 2.2)

    hi = np.array(_c("skin_hi"), dtype=np.float32)
    lo = np.array(_c("skin_lo"), dtype=np.float32)
    deep = np.array(_c("skin_deep"), dtype=np.float32)

    amp = np.clip(lam, 0.0, 1.0)[..., None]
    out = tone * (1.0 - amp * 0.72) + hi[None, None, :] * (amp * 0.72)

    amp2 = np.clip(-lam, 0.0, 1.0)[..., None]
    shade = lo[None, None, :] * 0.72 + deep[None, None, :] * 0.28
    out = out * (1.0 - amp2 * 0.62) + shade * (amp2 * 0.62)

    return np.clip(out, 0.0, 1.0)


def _wrinkles(size, rng, age, hw, hh, cx, cy, asym):
    """Морщины как карта модуляции кожи — второй по важности признак возраста."""
    w = np.zeros((size, size), dtype=np.float32)

    def line(x0, y0, x1, y1, thick, strength):
        nonlocal w
        n = max(2, int(math.hypot(x1 - x0, y1 - y0)))
        for i in range(n + 1):
            t = i / n
            px = x0 + (x1 - x0) * t
            py = y0 + (y1 - y0) * t
            w += _bump(size, px, py, thick, thick * 0.55, strength, falloff=1.4)

    # лобные борозды — тем заметнее, чем старше
    if age > 0.35:
        k = (age - 0.35) / 0.65
        for i in range(3):
            y = cy - hh * (0.62 - i * 0.075)
            line(cx - hw * 0.55, y + hh * 0.012 * i,
                 cx + hw * 0.55, y - hh * 0.012 * i,
                 hh * 0.020, -0.55 * k)

    # носогубные складки — есть почти у всех, кроме юных
    k2 = max(0.0, min(1.0, (age - 0.15) / 0.85))
    for side in (-1, 1):
        line(cx + side * hw * 0.22 * (1 + asym * 0.5), cy + hh * 0.34,
             cx + side * hw * 0.52, cy + hh * 0.74,
             hh * 0.024, -0.70 * k2)

    # мешки и морщинки под глазами
    if age > 0.25:
        k3 = (age - 0.25) / 0.75
        for side in (-1, 1):
            line(cx + side * hw * 0.52, cy + hh * 0.16,
                 cx + side * hw * 0.16, cy + hh * 0.24,
                 hh * 0.017, -0.50 * k3)

    # «гусиные лапки» у внешних углов глаз
    if age > 0.4:
        k4 = (age - 0.4) / 0.6
        for side in (-1, 1):
            for i in range(3):
                a = -0.5 + i * 0.5
                line(cx + side * hw * 0.62, cy - hh * 0.02 + a * hh * 0.09,
                     cx + side * hw * 0.90, cy - hh * 0.02 + a * hh * 0.16,
                     hh * 0.013, -0.42 * k4)

    # вертикальные морщины над переносицей — у злых и хмурых
    if age > 0.3:
        k5 = (age - 0.3) / 0.7
        for side in (-1, 1):
            line(cx + side * hw * 0.10, cy - hh * 0.30,
                 cx + side * hw * 0.13, cy - hh * 0.12,
                 hh * 0.013, -0.5 * k5)

    return w


# ─────────────────────────── Черты ───────────────────────────

def _paint_nose(layer, rng, kind, cx, cy, hw, hh, asym):
    """Нос: шесть форм. Рисуется впадиной ноздрей и выпуклостью кончика."""
    length = _u(rng, 0.28, 0.38) * hh
    width = _u(rng, 0.10, 0.15) * hw
    tip_y = cy + length

    if kind == "hooked":
        length *= 1.16
        bridge = [(-width * 0.30, cy), (-width * 0.42, cy + length * 0.42),
                  (width * 0.06, tip_y - length * 0.28), (width * 0.38, tip_y)]
    elif kind == "bulbous":
        length *= 0.92
        width *= 1.55
        bridge = [(-width * 0.34, cy), (-width * 0.40, cy + length * 0.5),
                  (0.0, tip_y - length * 0.2), (width * 0.40, cy + length * 0.5),
                  (width * 0.34, cy)]
    elif kind == "broken":
        length *= 1.05
        bridge = [(-width * 0.30, cy), (-width * 0.46, cy + length * 0.34),
                  (width * 0.10, cy + length * 0.48), (width * 0.34, tip_y)]
    elif kind == "pinched":
        width *= 0.72
        length *= 0.96
        bridge = [(-width * 0.26, cy), (-width * 0.30, cy + length * 0.5),
                  (0.0, tip_y), (width * 0.30, cy + length * 0.5),
                  (width * 0.26, cy)]
    elif kind == "long":
        length *= 1.34
        width *= 0.86
        bridge = [(-width * 0.32, cy), (-width * 0.36, cy + length * 0.5),
                  (width * 0.04, tip_y - length * 0.16), (width * 0.36, tip_y)]
    else:  # straight
        bridge = [(-width * 0.32, cy), (-width * 0.36, cy + length * 0.45),
                  (0.0, tip_y - length * 0.12), (width * 0.36, tip_y)]

    # спинка носа — светлая полоса с тенью по краю
    bridge = [(x + asym * width * 0.08, y) for x, y in bridge]
    layer.polygon(bridge, _c("skin_hi"), alpha=0.55)
    layer.capsule(bridge[0][0], bridge[0][1], bridge[-1][0], bridge[-1][1],
                  width * 0.42, _c("skin_hi"), alpha=0.30)

    # ноздри — тёмные косые щели
    for side in (-1, 1):
        layer.capsule(cx + side * width * 0.40 + asym * width * 0.06,
                      tip_y - width * 0.02,
                      cx + side * width * 0.60,
                      tip_y + width * 0.16,
                      width * 0.085, _c("skin_deep"), alpha=0.88)

    # кончик — блик
    layer.ellipse(cx - width * 0.14, tip_y - width * 0.10,
                  width * 0.30, width * 0.24, _c("skin_hi"), alpha=0.42)
    # тень под носом
    layer.ellipse(cx, tip_y + width * 0.30, width * 0.62, width * 0.20,
                  _c("skin_deep"), alpha=0.35)


def _paint_eye(layer, rng, kind, cx, cy, rx, ry, side, redness, asym):
    """Глаз: белок, радужка, зрачок, веки, мешок, ресницы."""
    if kind == "narrow":
        ry *= 0.72
    elif kind == "wide":
        ry *= 1.30
    elif kind == "heavy":
        ry *= 0.92

    # глазница — прицельно вокруг глаза, не шире, иначе два глаза сольются
    socket = rx * (1.34 if kind != "sunken" else 1.55)
    socket_y = ry * (1.62 if kind != "sunken" else 1.95)
    layer.ellipse(cx, cy, socket, socket_y, _c("skin_lo"), alpha=0.72)
    layer.ellipse(cx, cy + (cy * 0) , socket * 1.16, socket_y * 1.20,
                  _c("skin_deep"), alpha=0.30)

    # белок
    base = _c("sclera")
    if redness > 0.6:
        base = _c("sclera_blood")
    elif redness > 0.3:
        base = ac.mix(_c("sclera"), _c("sclera_ill"), (redness - 0.3) / 0.3)
    layer.ellipse(cx, cy, rx, ry, base)
    # верхняя часть белка темнее — веко нависает
    layer.polygon([
        (cx - rx, cy - ry * 0.15),
        (cx + rx, cy - ry * 0.15),
        (cx + rx, cy - ry * 1.05),
        (cx - rx, cy - ry * 1.05),
    ], _c("skin_lo"), alpha=0.45)

    # радужка — цвет варьируется, с венчиком
    iris_r = rx * (0.56 if kind != "heavy" else 0.62)
    iris_col = _sample(rng, ["iris_a", "iris_b", "iris_c"])
    ic = _c(iris_col)
    layer.ellipse(cx, cy + ry * 0.04, iris_r, iris_r * 0.94, ic)
    layer.ellipse(cx, cy + ry * 0.04, iris_r * 0.86, iris_r * 0.80,
                  ac.mix(ic, _c("pupil"), 0.45))
    layer.ellipse(cx, cy + ry * 0.04, iris_r * 0.44, iris_r * 0.42, _c("pupil"))
    # блик
    layer.ellipse(cx - iris_r * 0.36, cy - iris_r * 0.34,
                  iris_r * 0.24, iris_r * 0.20, (0.88, 0.86, 0.80), alpha=0.85)

    # верхнее веко — тяжёлое, с ресничной кромкой
    droop = _u(rng, 0.10, 0.42)
    if kind == "heavy":
        droop = max(droop, 0.45)
    lid_y = cy - ry * (0.70 - droop * 0.4)
    layer.polygon([
        (cx - rx * 1.06, cy - ry * 1.20),
        (cx + rx * 1.06, cy - ry * 1.20),
        (cx + rx * 0.96, lid_y),
        (cx - rx * 0.96, lid_y),
    ], _c("skin_lo"), alpha=0.80)
    layer.capsule(cx - rx * 0.96, lid_y, cx + rx * 0.96, lid_y,
                  ry * 0.16, _c("skin_deep"), alpha=0.75)

    # нижнее веко — узкая тёмная линия
    layer.capsule(cx - rx * 0.92, cy + ry * 0.86, cx + rx * 0.92, cy + ry * 0.80,
                  ry * 0.10, _c("skin_lo"), alpha=0.55)

    # мешок под глазом
    layer.ellipse(cx, cy + ry * (1.55 if kind == "sunken" else 1.38),
                  rx * 0.92, ry * 0.40, _c("skin_lo"), alpha=0.42)
    if kind == "sunken":
        layer.ellipse(cx, cy, rx * 0.98, ry * 1.10, _c("skin_deep"), alpha=0.22)


def _paint_brow(layer, rng, cx, cy, span, thick, color, anger, side, asym):
    """Бровь: клин, толще у переносицы, с наклоном."""
    inner_x = cx + side * span * 0.26
    outer_x = cx + side * span * 1.06
    inner_y = cy - anger * span * 0.10 + asym * side * span * 0.03
    outer_y = cy + anger * span * 0.10 - span * 0.06

    layer.capsule(inner_x, inner_y, outer_x, outer_y, thick, color, alpha=0.94)
    # уплотнение у переносицы
    layer.ellipse(inner_x, inner_y, thick * 1.15, thick * 1.05, color, alpha=0.92)
    # редкие волоски сверху — бровь не обрубок
    for i in range(4):
        t = 0.15 + 0.25 * i
        px = inner_x + (outer_x - inner_x) * t
        py = inner_y + (outer_y - inner_y) * t
        layer.capsule(px, py, px + side * span * 0.05,
                      py - thick * _u(rng, 1.4, 2.6), thick * 0.38,
                      color, alpha=0.62)


def _paint_mouth(layer, rng, cx, cy, hw, hh, mood):
    """Рот: тонкие губы, опущенные или поджатые углы."""
    span = hw * _u(rng, 0.34, 0.44)
    thickness = hh * _u(rng, 0.030, 0.045)
    curl = (mood - 0.5) * 2.0  # -1 поджат/вниз, +1 в улыбку

    # тень под нижней губой
    layer.ellipse(cx, cy + thickness * 2.4, span * 0.80, thickness * 0.90,
                  _c("skin_lo"), alpha=0.45)
    # носогубная тень
    layer.ellipse(cx, cy - thickness * 1.6, span * 0.86, thickness * 0.85,
                  _c("skin_lo"), alpha=0.35)

    # линия губ
    layer.polygon([
        (cx - span, cy - curl * span * 0.10),
        (cx - span * 0.5, cy - curl * span * 0.02 - thickness * 0.30),
        (cx + span * 0.5, cy - curl * span * 0.02 + thickness * 0.24),
        (cx + span, cy - curl * span * 0.13),
        (cx + span * 0.5, cy + curl * span * 0.02 + thickness * 1.5),
        (cx - span * 0.5, cy + curl * span * 0.02 + thickness * 1.55),
    ], _c("lip_dry"))

    # верхняя губа тёмнее
    layer.polygon([
        (cx - span * 0.92, cy - curl * span * 0.09),
        (cx, cy - curl * span * 0.03 - thickness * 0.55),
        (cx + span * 0.92, cy - curl * span * 0.11),
        (cx + span * 0.5, cy - curl * span * 0.02 + thickness * 0.18),
        (cx - span * 0.5, cy - curl * span * 0.02 - thickness * 0.20),
    ], _c("lip_dark"), alpha=0.72)

    # щель между губами
    layer.capsule(cx - span * 0.86, cy, cx + span * 0.86, cy - curl * span * 0.06,
                  thickness * 0.16, (0.045, 0.035, 0.032), alpha=0.85)

    # уголки — тёмные ямки
    for side in (-1, 1):
        layer.ellipse(cx + side * span * 0.96, cy - curl * side * side * span * 0.10,
                      thickness * 0.42, thickness * 0.55, _c("skin_deep"), alpha=0.55)


def _paint_scars(layer, rng, cx, cy, hw, hh, count):
    for _ in range(count):
        sx = cx + _u(rng, -0.62, 0.62) * hw
        sy = cy + _u(rng, -0.45, 0.70) * hh
        length = _u(rng, 0.24, 0.48) * hh
        ang = _u(rng, -0.9, 0.9)
        ex = sx + math.sin(ang) * length
        ey = sy + math.cos(ang) * length
        # рубец: тёмная борозда + светлая кромка
        layer.capsule(sx, sy, ex, ey, hh * 0.012, _c("blood"), alpha=0.62)
        layer.capsule(sx, sy - hh * 0.013, ex, ey - hh * 0.013,
                      hh * 0.008, _c("skin_hi"), alpha=0.30)


def _paint_ears(layer, rng, cx, cy, hw, hh, hair_color, covered):
    """Уши по бокам головы — деталь силуэта, её часто забывают.

    Рисуются ДО волос и убора, прижатыми к черепу. Если волосы или убор
    закрывают бок, наружу остаётся только мочка. Наружу торчит лишь
    треть уха — иначе уши превращаются в круглые «наушники» по бокам
    лица, что и было в предыдущей версии.
    """
    for side in (-1, 1):
        ex = cx + side * hw * (0.88 + _u(rng, 0.0, 0.03))
        ey = cy + hh * _u(rng, 0.08, 0.18)
        rx = hw * _u(rng, 0.070, 0.095)
        ry = hh * _u(rng, 0.11, 0.15)
        # Ухо темнее кожи: светлое ухо читается как круглая «шайба» или
        # наушник сбоку лица — грубый артефакт, который замечают сразу.
        layer.ellipse(ex, ey, rx, ry, _c("skin_lo"), alpha=0.92)
        layer.ellipse(ex + side * rx * 0.34, ey, rx * 0.54, ry * 0.54,
                      _c("skin_deep"), alpha=0.55)
        if covered:
            # срезаем внешнюю часть уха волосами — остаётся только мочка
            layer.ellipse(ex - side * rx * 0.30, ey - ry * 0.30,
                          rx * 0.86, ry * 0.66, hair_color, alpha=0.97)


def _paint_hair(layer, rng, style, color, cx, cy, hw, hh, asym, hairline):
    """Волосы занимают ТОЛЬКО зону выше линии бровей.

    Грабли первой версии: `top` стоял почти в центре черепа, а радиус шапки
    был `hh*0.62`, поэтому эллипс волос накрывал всё лицо до глаз — портрет
    получался в маске. Линия глаз лежит на `cy - hh*0.02`, брови на
    `cy - hh*0.16`, значит подол волос обязан быть ВЫШЕ этого, иначе волосы
    съедают лицо. Ориентир — `hairline` как доля от макушки: 0.50 = по
    бровям-макушке, больше = выше (лысее).
    """
    cap_rx = hw * 1.08
    # Макушка черепа — на `cy - hh`, висок на `cy - hh*0.30`, брови на
    # `cy - hh*0.16`. Волосы обязаны жить СТРОГО между макушкой и бровями:
    # если подол уходит ниже бровей, лицо превращается в маску.
    crown = cy - hh * 0.96
    brow_line = cy - hh * 0.20
    # `hairline` 0 = волосы до бровей, 1 = сильно выше (лысеет)
    hem = brow_line - (brow_line - crown) * hairline * 0.75

    if style == "bald":
        # венчик по бокам + блики на черепе
        for side in (-1, 1):
            layer.capsule(cx + side * hw * 0.82, cy - hh * 0.24,
                          cx + side * hw * 0.90, cy + hh * 0.52,
                          hw * 0.11, color, alpha=0.78)
        layer.ellipse(cx - hw * 0.24, cy - hh * 0.58, hw * 0.40, hh * 0.12,
                      _c("skin_hi"), alpha=0.16)
        return

    if style == "receding":
        # залысины: шапка есть, но углы выедены кверху, подол поднят
        layer.polygon([
            (cx - cap_rx * 0.98, hem + hh * 0.10),
            (cx - cap_rx * 0.56, crown + hh * 0.30),
            (cx - cap_rx * 0.20, crown + hh * 0.14),
            (cx + cap_rx * 0.20, crown + hh * 0.14),
            (cx + cap_rx * 0.56, crown + hh * 0.30),
            (cx + cap_rx * 0.98, hem + hh * 0.10),
            (cx + cap_rx * 0.86, hem + hh * 0.30),
            (cx - cap_rx * 0.86, hem + hh * 0.30),
        ], color)
        for side in (-1, 1):
            layer.capsule(cx + side * hw * 0.84, cy - hh * 0.36,
                          cx + side * hw * 0.92, cy + hh * 0.36,
                          hw * 0.10, color, alpha=0.85)
        return

    # Основная шапка: эллипс от макушки до подола. Верх выходит за череп
    # на пару процентов — это «объём причёски», иначе волосы выглядят
    # нарисованными прямо по коже.
    cap_cy = (crown + hem) * 0.5
    cap_ry = (hem - crown) * 0.5
    layer.ellipse(cx + asym * hw * 0.07, cap_cy, cap_rx, cap_ry * 1.02, color)
    # Расширение у висков делаем ТОЛЬКО когда волос действительно много и
    # подол опущен к бровям. Иначе у высокого `hairline` эти пятна повисают
    # светлыми «рожками» над ушами — шапка уже кончилась, а виски остались.
    if hem > cy - hh * 0.34:
        for side in (-1, 1):
            layer.ellipse(cx + side * cap_rx * 0.78, cy - hh * 0.52,
                          cap_rx * 0.34, hh * 0.30, color, alpha=0.85)

    if style == "long":
        for side in (-1, 1):
            layer.polygon([
                (cx + side * cap_rx * 0.86, cy - hh * 0.30),
                (cx + side * cap_rx * 1.16, cy + hh * 0.28),
                (cx + side * cap_rx * 1.12, cy + hh * 1.30),
                (cx + side * cap_rx * 0.62, cy + hh * 1.18),
            ], color)
    elif style == "matted":
        # сбитая шапка: рваная кромка по подолу, но выше бровей
        for i in range(9):
            t = -1.0 + 0.25 * i
            layer.capsule(cx + t * cap_rx * 0.92, cap_cy,
                          cx + t * cap_rx * 0.98, hem + abs(t) * hh * 0.06,
                          hw * 0.10, color)
    elif style == "wild":
        for i in range(11):
            a = math.pi * (0.05 + 0.90 * i / 10)
            tx = cx - math.cos(a) * cap_rx * _u(rng, 1.05, 1.28)
            ty = cap_cy - math.sin(a) * cap_ry * _u(rng, 0.9, 1.5)
            layer.capsule(cx, cap_cy + cap_ry * 0.3, tx, ty, hw * 0.085, color)
    elif style == "pulled":
        layer.ellipse(cx, cap_cy - hh * 0.04, cap_rx * 0.94, cap_ry * 1.02, color)
        layer.capsule(cx + hw * 0.96, cy - hh * 0.28,
                      cx + hw * 1.24, cy + hh * 0.78, hw * 0.20, color)
    elif style == "braided":
        # косы по бокам — характерная женская причёска в средневековье
        layer.ellipse(cx, cap_cy - hh * 0.02, cap_rx * 0.98, cap_ry * 1.00, color)
        for side in (-1, 1):
            layer.capsule(cx + side * hw * 0.94, cy - hh * 0.24,
                          cx + side * hw * 1.06, cy + hh * 0.55, hw * 0.14, color)
            # звенья косы
            for k in range(4):
                t = 0.12 + 0.24 * k
                layer.ellipse(cx + side * hw * (0.96 + 0.10 * t),
                              cy - hh * 0.24 + hh * 0.79 * t,
                              hw * 0.075, hh * 0.055,
                              ac.mix(color, (1, 1, 1), 0.16), alpha=0.55)
    elif style == "headscarf_wrap":
        # платок закрывает волосы целиком — только кромка у лба
        layer.ellipse(cx, cap_cy, cap_rx * 1.04, cap_ry * 1.10, color)
        layer.ellipse(cx, hem - hh * 0.06, cap_rx * 0.94, hh * 0.09,
                      color, alpha=0.9)

    # свет на шапке волос
    layer.ellipse(cx - hw * 0.30, cap_cy - cap_ry * 0.42, hw * 0.48, hh * 0.15,
                  ac.mix(color, (1, 1, 1), 0.35), alpha=0.20)


def _paint_beard(layer, rng, kind, color, cx, cy, hw, hh, asym):
    """Борода держится нижней трети лица и не лезет на щёки."""
    if kind == "none":
        return

    if kind == "stubble":
        layer.polygon([
            (cx - hw * 0.70, cy + hh * 0.68),
            (cx - hw * 0.34, cy + hh * 1.16),
            (cx + hw * 0.34 + asym * hw * 0.1, cy + hh * 1.16),
            (cx + hw * 0.70, cy + hh * 0.68),
            (cx + hw * 0.42, cy + hh * 0.60),
            (cx - hw * 0.42, cy + hh * 0.60),
        ], color, alpha=0.30)
        return

    if kind == "goatee":
        layer.polygon([
            (cx - hw * 0.22, cy + hh * 0.82),
            (cx + hw * 0.22, cy + hh * 0.82),
            (cx + hw * 0.16 + asym * hw * 0.06, cy + hh * 1.26),
            (cx - hw * 0.16, cy + hh * 1.26),
        ], color)
        layer.capsule(cx - hw * 0.34, cy + hh * 0.56, cx + hw * 0.34, cy + hh * 0.56,
                      hh * 0.030, color, alpha=0.85)   # усы
        return

    if kind == "mutton":
        # баки, соединяющиеся с подбородком по краю челюсти
        for side in (-1, 1):
            layer.polygon([
                (cx + side * hw * 0.78, cy + hh * 0.10),
                (cx + side * hw * 0.94, cy + hh * 0.40),
                (cx + side * hw * 0.70, cy + hh * 0.98),
                (cx + side * hw * 0.48, cy + hh * 0.86),
                (cx + side * hw * 0.60, cy + hh * 0.24),
            ], color, alpha=0.95)
        layer.ellipse(cx, cy + hh * 0.98, hw * 0.52, hh * 0.28, color)
        layer.capsule(cx - hw * 0.30, cy + hh * 0.50, cx + hw * 0.30, cy + hh * 0.50,
                      hh * 0.028, color, alpha=0.85)
        return

    # full: борода держится нижней третью лица и срастается с челюстью
    jaw_y = cy + hh * 0.72
    layer.polygon([
        (cx - hw * 0.72, jaw_y),
        (cx - hw * 0.52, cy + hh * 1.02),
        (cx - hw * 0.26 + asym * hw * 0.08, cy + hh * 1.20),
        (cx + hw * 0.26, cy + hh * 1.20),
        (cx + hw * 0.52, cy + hh * 1.02),
        (cx + hw * 0.72, jaw_y),
        (cx + hw * 0.44, jaw_y - hh * 0.04),
        (cx - hw * 0.44, jaw_y - hh * 0.04),
    ], color)
    # усы
    layer.capsule(cx - hw * 0.40, cy + hh * 0.50, cx + hw * 0.40, cy + hh * 0.50,
                  hh * 0.032, color)
    # пряди — борода не сплошное пятно
    for i in range(6):
        t = -0.42 + 0.17 * i
        layer.capsule(cx + t * hw * 1.05, cy + hh * 0.78,
                      cx + t * hw * 0.88, cy + hh * 1.16,
                      hw * 0.045, ac.mix(color, (1, 1, 1), 0.24), alpha=0.30)


def _paint_headwear(layer, rng, kind, cx, cy, hw, hh, hairline):
    """Убор садится выше линии роста волос и не закрывает лицо.

    Грабли первой версии: `hood` рисовался от `cy - hh*1.08` и внутрь до
    `cy - hh*0.92`, а `hat` ставил тулью на `cy - hh*0.72` — обе формы
    опускались до линии глаз и превращали лицо в чёрную маску. Здесь низ
    убора жёстко привязан к `brim`, который лежит выше бровей.
    """
    crown = cy - hh * 0.98
    brim = cy - hh * 0.70          # низ убора — заметно выше бровей
    eye_safe = cy - hh * 0.42      # КРАСНАЯ ЛИНИЯ: ниже неё убор не опускается

    if kind == "hood":
        layer.polygon([
            (cx - hw * 1.44, cy + hh * 0.55),
            (cx - hw * 1.16, cy - hh * 1.06),
            (cx, cy - hh * 1.38),
            (cx + hw * 1.16, cy - hh * 1.06),
            (cx + hw * 1.44, cy + hh * 0.55),
            (cx + hw * 1.18, cy + hh * 0.56),
            (cx + hw * 1.02, eye_safe),
            (cx, cy - hh * 1.14),
            (cx - hw * 1.02, eye_safe),
            (cx - hw * 1.18, cy + hh * 0.56),
        ], _c("cloth_dark"), alpha=0.97)
        for i in range(4):
            t = -0.8 + 0.53 * i
            layer.capsule(cx + t * hw * 1.06, cy - hh * 0.92,
                          cx + t * hw * 1.32, cy + hh * 0.50,
                          hw * 0.035, _c("cloth_mid"), alpha=0.30)

    elif kind == "hat":
        # поля на уровне подола волос, тулья — над макушкой
        layer.ellipse(cx, brim, hw * 1.80, hh * 0.16, _c("cloth_mid"))
        layer.ellipse(cx, brim - hh * 0.05, hw * 1.68, hh * 0.10,
                      _c("cloth_lit"), alpha=0.30)
        layer.ellipse(cx, crown + hh * 0.02, hw * 0.92, hh * 0.46, _c("cloth_dark"))
        layer.ellipse(cx, crown + hh * 0.10, hw * 0.64, hh * 0.20,
                      _c("cloth_lit"), alpha=0.38)

    elif kind == "headscarf":
        layer.ellipse(cx, crown + hh * 0.36, hw * 1.04, hh * 0.52, _c("cloth_mid"))
        layer.capsule(cx + hw * 0.92, cy - hh * 0.20,
                      cx + hw * 1.26, cy + hh * 0.90, hw * 0.19, _c("cloth_mid"))
        # узел у виска — узнаваемая деталь
        layer.ellipse(cx + hw * 1.02, cy - hh * 0.42, hw * 0.20, hh * 0.14,
                      _c("cloth_lit"), alpha=0.42)

    elif kind == "coif":
        # Чепец: садится на макушку, полоса надо лбом — ВЫШЕ бровей.
        # В предыдущей версии эллипс опускался до `cy - hh*0.06`, то есть
        # ровно на линию глаз, и лицо превращалось в чёрную маску.
        layer.ellipse(cx, crown + hh * 0.26, hw * 1.06, hh * 0.46, _c("cloth_mid"))
        layer.ellipse(cx, brim + hh * 0.02, hw * 1.00, hh * 0.14,
                      _c("cloth_lit"), alpha=0.50)
        for side in (-1, 1):
            layer.capsule(cx + side * hw * 0.86, cy - hh * 0.60,
                          cx + side * hw * 1.00, cy - hh * 0.34,
                          hw * 0.12, _c("cloth_mid"))


def _paint_collar(layer, rng, cx, cy, hw, hh, kind, build="average"):
    """Плечи и одежда. Разные вырезы — чтобы силуэты не повторялись.

    `build` задаёт ширину плеч и толщину фигуры: щуплый житель и кузнец
    должны отличаться силуэтом даже при закрытом лице. Без этого все
    персонажи стояли с одинаковыми плечами, как манекены.
    """
    shape = BUILD_SHAPE.get(build, BUILD_SHAPE["average"])
    sw = shape["shoulder"]          # ширина плеч
    cl = shape["collar"]            # толщина воротника

    bottom = cy + hh * 2.6
    shoulder = cy + hh * (0.95 if kind == "high" else 1.06)
    layer.polygon([
        (cx - hw * 3.0 * sw, bottom),
        (cx - hw * 1.60 * sw, shoulder),
        (cx - hw * 0.60, cy + hh * 1.28),
        (cx + hw * 0.60, cy + hh * 1.28),
        (cx + hw * 1.60 * sw, shoulder),
        (cx + hw * 3.0 * sw, bottom),
    ], _c("cloth_mid"))
    # складка на плече
    layer.polygon([
        (cx - hw * 2.2 * sw, bottom), (cx - hw * 1.2 * sw, shoulder + hh * 0.2),
        (cx - hw * 0.9, cy + hh * 1.5), (cx - hw * 1.6 * sw, bottom),
    ], _c("cloth_lit"), alpha=0.12)

    if kind == "collar":
        layer.polygon([
            (cx - hw * 0.78 * cl, cy + hh * 1.14),
            (cx, cy + hh * 1.72),
            (cx + hw * 0.78 * cl, cy + hh * 1.14),
            (cx, cy + hh * 1.34),
        ], _c("cloth_dark"))
    elif kind == "high":
        layer.polygon([
            (cx - hw * 0.66 * cl, cy + hh * 1.04),
            (cx - hw * 0.52 * cl, cy + hh * 1.46),
            (cx + hw * 0.52 * cl, cy + hh * 1.46),
            (cx + hw * 0.66 * cl, cy + hh * 1.04),
        ], _c("cloth_dark"))
    elif kind == "rag":
        layer.polygon([
            (cx - hw * 0.90, cy + hh * 1.20),
            (cx - hw * 0.40, cy + hh * 1.60),
            (cx + hw * 0.10, cy + hh * 1.28),
            (cx + hw * 0.62, cy + hh * 1.62),
            (cx + hw * 0.92, cy + hh * 1.18),
            (cx + hw * 0.30, cy + hh * 1.40),
            (cx - hw * 0.30, cy + hh * 1.42),
        ], _c("cloth_dark"), alpha=0.90)


def _paint_amulet(layer, rng, kind, cx, cy, hw, hh):
    if kind == "none":
        return
    ny = cy + hh * 1.34
    if kind == "cross":
        layer.capsule(cx, cy + hh * 1.06, cx, ny + hh * 0.26, hh * 0.015, _c("iron"))
        layer.capsule(cx - hw * 0.15, ny, cx + hw * 0.15, ny, hh * 0.019, _c("iron_lit"))
        layer.capsule(cx, ny - hh * 0.13, cx, ny + hh * 0.15, hh * 0.019, _c("iron_lit"))
    elif kind == "pendant":
        layer.capsule(cx - hw * 0.20, cy + hh * 1.04, cx, ny, hh * 0.010, _c("leather"))
        layer.capsule(cx + hw * 0.20, cy + hh * 1.04, cx, ny, hh * 0.010, _c("leather"))
        layer.ellipse(cx, ny, hw * 0.18, hh * 0.12, _c("bone"))
        layer.ellipse(cx, ny, hw * 0.09, hh * 0.06, _c("iron"))
    elif kind == "teeth":
        for i in range(3):
            t = -0.12 + 0.12 * i
            layer.capsule(cx + t * hw, cy + hh * 1.10, cx + t * hw * 0.7, ny,
                          hh * 0.008, _c("leather"), alpha=0.9)
            layer.polygon([
                (cx + t * hw - hw * 0.035, ny),
                (cx + t * hw + hw * 0.035, ny),
                (cx + t * hw, ny + hh * 0.10),
            ], _c("bone"))


# ─────────────────────────── Сборка ───────────────────────────

def build_portrait(seed, size=512, sex=None, build=None):
    """Собирает портрет.

    Три независимые оси, чтобы лица не повторялись:
    - `archetype` — характер и возраст (худой, тяжёлый, древний, измождённый);
    - `sex` — анатомия: пропорции черепа, брови, челюсть, борода, причёски;
    - `build` — телосложение: ширина плеч, посадка и толщина фигуры.

    Раньше пола не было вовсе, и все жители выходили бородатыми мужиками
    с одинаковыми плечами. Теперь пол и телосложение выбираются первыми
    и передаются во все нижележащие слои, которые от них зависят.
    """
    rng = np.random.default_rng(seed)
    u = float(size)
    ss = 2

    archetype = _sample(rng, ARCHETYPES)
    sex = sex or _sample(rng, SEXES)
    build = build or _sample(rng, BUILDS)
    shape = BUILD_SHAPE[build]

    # --- габариты черепа ---
    hw = u * _u(rng, 0.138, 0.162)
    hh = u * _u(rng, 0.166, 0.194)
    taper = _u(rng, 0.50, 0.72)
    if archetype == "gaunt":
        hw *= 0.93; hh *= 1.07; taper = 0.50
    elif archetype == "heavy":
        hw *= 1.16; hh *= 0.95; taper = 0.86
    elif archetype == "hollow":
        hh *= 1.03; taper = 0.52
    elif archetype == "bloated":
        hw *= 1.18; hh *= 0.98; taper = 0.90
    elif archetype == "sharp":
        hw *= 0.95; taper = 0.54
    elif archetype == "saintly":
        hw *= 0.90; hh *= 1.03; taper = 0.56
    elif archetype == "witchy":
        taper = 0.46
    elif archetype == "ancient":
        hw *= 0.94; hh *= 1.05; taper = 0.58

    # Пол: женский череп уже и мягче, подбородок острее, лицо компактнее.
    # Не «красивее» — просто другая анатомия.
    if sex == "female":
        hw *= _u(rng, 0.90, 0.96)
        hh *= _u(rng, 0.96, 1.02)
        taper = min(taper, _u(rng, 0.44, 0.58))

    # Телосложение: широкие плечи тянут голову «мельче» в кадре
    head_scale = {"slight": 0.98, "average": 1.00, "broad": 1.02, "stout": 1.00}[build]

    asym = _u(rng, -0.12, 0.12)
    shear = _u(rng, -0.13, 0.13)
    if archetype in ("broken", "bloated"):
        shear = _u(rng, 0.18, 0.32) * (1 if rng.random() < 0.5 else -1)

    # центр лица — чуть ниже центра кадра, сверху место под волосы/убор
    cy = u * (0.055 + shape["drop"] * 0.5)
    cx = u * _u(rng, -0.014, 0.014)
    hw *= head_scale
    hh *= head_scale

    pts = _face_outline(hw, hh, taper, shear, asym)
    pts = [(x + cx, y + cy) for x, y in pts]

    # --- световое направление (общее на весь портрет) ---
    light = (_u(rng, 0.28, 0.62), _u(rng, 0.10, 0.44))

    # ═══ 1. Кожа: тон + рельеф + морщины ═══
    age = _u(rng, 0.0, 1.0)
    if archetype == "ancient":
        age = max(age, 0.82)
    elif archetype in ("saintly", "witchy"):
        age = min(age, 0.42)
    elif archetype == "bloated":
        age = min(max(age, 0.4), 0.8)

    skin_tone = _c(_sample(rng, ["skin_cold", "skin_grey", "skin_sallow", "skin_dead"]))
    if archetype == "saintly":
        skin_tone = _c("skin_cold")
    skin_tone = ac.mix(skin_tone, _c("skin_rot"), _u(rng, 0.0, 0.22))

    tone = _skin_base(size, rng, seed, skin_tone, archetype, hw, hh, cx, cy)
    relief = _sculpt(size, rng, archetype, hw, hh, cx, cy, asym, sex)
    relief += _wrinkles(size, rng, age, hw, hh, cx, cy, asym)
    skin_rgb = _apply_relief(tone, relief, light)

    # --- фон ---
    back = Layer(size, ss=ss, background=(*_c("void"), 1.0))
    back.ellipse(_u(rng, -0.18, 0.18) * u, -0.08 * u, 0.60 * u, 0.54 * u,
                 _c("void_glow"))
    back_rgba = back.downscale()
    noise = ac.fbm(size, base_res=6, octaves=4, seed=seed + 7)
    back_rgba[..., :3] = np.clip(
        back_rgba[..., :3] * (0.72 + noise[..., None] * 0.56), 0.0, 1.0)

    # --- слой кожи: рельефная кожа внутри силуэта + тёмная кайма по краю ---
    mask = _mask_from_polygon(size, pts, 0.0, 0.0)
    mask_soft = _blur(mask, 1.4)
    # кожа чуть выходит за силуэт — иначе по краю остаётся светлая щель
    outer = np.array(_c("skin_mid"), dtype=np.float32)[None, None, :] * \
        np.ones((size, size, 1), dtype=np.float32)
    skin_final = outer * (1.0 - mask_soft[..., None]) + \
        skin_rgb * mask_soft[..., None]
    base = np.dstack([skin_final, mask_soft])
    # обводка поверх — узкая тёмная кайма по краю силуэта
    edge = np.clip(mask - _blur(mask, 2.2), 0.0, 1.0)
    base[..., :3] = base[..., :3] * (1.0 - edge[..., None] * 0.85) + \
        np.array(_c("outline"), dtype=np.float32)[None, None, :] * (edge * 0.85)[..., None]

    # ═══ 2. Черты лица — отдельный слой ═══
    feats = Layer(size, ss=ss)

    hair_color = _c(_sample(rng, ["hair_dark", "hair_ash", "hair_rust"]))
    if age > 0.50:
        hair_color = ac.mix(hair_color, _c("hair_grey"), (age - 0.50) * 1.7)
    if rng.random() < 0.35:
        hair_color = ac.mix(hair_color, _c("hair_grey"), _u(rng, 0.1, 0.4))

    # убор выбирается заранее: он влияет на то, видны ли уши
    head_kind = _sample(rng, HEAD_WEAR)
    # женщинам капюшон и чепец идут чаще, шляпа — реже
    if sex == "female" and head_kind == "hat" and rng.random() < 0.6:
        head_kind = _sample(rng, ["headscarf", "coif", "none", "hood"])

    # уши — ДО волос: если причёска или убор их накрывают, торчит только мочка
    hair_style = _sample(rng, FEMALE_HAIR if sex == "female" else HAIR_STYLES)
    ears_covered = hair_style not in ("bald", "receding") or head_kind in (
        "hood", "hat", "headscarf", "coif")
    _paint_ears(feats, rng, cx, cy, hw, hh, hair_color, ears_covered)

    # волосы (только выше бровей)
    hairline = _u(rng, 0.0, 0.35)
    if archetype == "ancient" or age > 0.7:
        hairline += 0.18
    if archetype in ("saintly",) and rng.random() < 0.5:
        hairline += 0.10
    if sex == "female":
        # у женщин волосы почти всегда есть: залысины и «лысину» убираем
        hairline = min(hairline, 0.16)
    _paint_hair(feats, rng, hair_style, hair_color, cx, cy, hw, hh, asym, hairline)

    # борода — только у мужчин. Женщине борода не «реже», а невозможна.
    if sex == "male":
        beard_kind = _sample(rng, BEARD_KINDS)
        if age < 0.25 and rng.random() < 0.6:
            beard_kind = "none"
    else:
        beard_kind = "none"
    _paint_beard(feats, rng, beard_kind, hair_color, cx, cy, hw, hh, asym)

    # нос
    nose_y = cy + hh * _u(rng, -0.06, 0.04)
    _paint_nose(feats, rng, _sample(rng, NOSE_KINDS), cx, nose_y, hw, hh, asym)

    # глаза — линия глаз ниже носа не должна быть, но и не в самом верху
    eye_dx = hw * _u(rng, 0.44, 0.52) * (1.0 + asym * 0.10)
    eye_y = cy + hh * _u(rng, -0.02, 0.06)
    eye_rx = hw * _u(rng, 0.125, 0.160 if sex == "female" else 0.175)
    eye_ry = hh * _u(rng, 0.048, 0.070)
    eye_kind = _sample(rng, EYE_KINDS)
    redness = _u(rng, 0.0, 0.95)

    for side in (-1, 1):
        _paint_eye(feats, rng, eye_kind, cx + side * eye_dx * (1 + asym * side * 0.14),
                   eye_y + hh * (_u(rng, -0.008, 0.008)),
                   eye_rx * _u(rng, 0.94, 1.06), eye_ry * _u(rng, 0.92, 1.08),
                   side, redness, asym)

    # брови: женские тоньше, длиннее и с меньшим наклоном — это главный
    # визуальный признак пола в лице, сильнее черепа
    anger = _u(rng, 0.0, 1.0)
    if sex == "female":
        anger *= 0.55
    brow_y = eye_y - eye_ry * _u(rng, 2.0, 2.9)
    brow_span = hw * (_u(rng, 0.42, 0.52) if sex == "female" else _u(rng, 0.36, 0.46))
    brow_thick = hh * (_u(rng, 0.014, 0.022) if sex == "female"
                       else _u(rng, 0.024, 0.038))
    brow_color = ac.mix(hair_color, _c("hair_dark"), 0.35)
    for side in (-1, 1):
        _paint_brow(feats, rng, cx + side * eye_dx * 0.5, brow_y + asym * side * hh * 0.01,
                    brow_span, brow_thick, brow_color, anger, side, asym)

    # рот
    mouth_y = cy + hh * _u(rng, 0.46, 0.56)
    mood = _u(rng, 0.0, 1.0)
    _paint_mouth(feats, rng, cx, mouth_y, hw, hh, mood)

    # ссадины и рубцы
    n_scars = int(rng.integers(0, 3))
    if archetype == "broken":
        n_scars = int(rng.integers(1, 4))
    _paint_scars(feats, rng, cx, cy, hw, hh, n_scars)

    # убор (выбран заранее, до ушей)
    _paint_headwear(feats, rng, head_kind, cx, cy, hw, hh, hairline)

    # амулет
    _paint_amulet(feats, rng, _sample(rng, ["none", "none", "cross", "pendant", "teeth"]),
                  cx, cy, hw, hh)

    feats_rgba = feats.downscale()

    # ═══ 2b. Обрезка черт по голове ═══
    # Без этого волосы, борода и убор вылезают за силуэт и висят в пустоте
    # серыми ломтями — главный визуальный артефакт предыдущих прогонов.
    # Маска расширена: волосы и убор вправе выступать за череп, но не висеть
    # далеко в фоне. Всё, что ниже подбородка, обрезается жёстко.
    head_grow = np.zeros((size, size), dtype=np.float32)
    from PIL import ImageDraw as _ID
    _hg = Image.new("L", (size, size), 0)
    grown_pts = []
    for x, y in pts:
        dx, dy = x - cx, y - cy
        n = max(math.hypot(dx, dy), 1e-6)
        # расширяем вбок на 22%, а вверх — на 30% (объём причёски)
        rx = 1.0 + 0.22
        ry = 1.30 if dy < 0 else 1.02
        grown_pts.append((cx + dx * rx, cy + dy * ry))
    _ID.Draw(_hg).polygon(
        [(size * 0.5 + x, size * 0.5 + y) for x, y in grown_pts], fill=255)
    head_grow = np.asarray(_hg, dtype=np.float32) / 255.0
    head_grow = _blur(head_grow, 1.6)
    # черты под головой: воротник и амулет ниже подбородка не режем
    keep = np.clip(head_grow + 0.0, 0.0, 1.0)
    # нижняя часть (шея/ворот/амулет) — зона cy + hh*1.1 и ниже остаётся вся
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    below = np.clip((yy - (size * 0.5 + cy + hh * 1.05)) / 12.0, 0.0, 1.0)
    keep = np.maximum(keep, below)
    feats_rgba[..., 3] *= keep

    # ═══ 3. Плечи и одежда ═══
    cloth = Layer(size, ss=ss)
    collar_kind = _sample(rng, ["collar", "high", "rag", "plain"])
    _paint_collar(cloth, rng, cx, cy, hw, hh, collar_kind, build)
    cloth_rgba = cloth.downscale()

    # ═══ 4. Композит: фон → одежда → кожа → черты ═══
    canvas = np.array(back_rgba, copy=True)

    def over(dst, src):
        a = src[..., 3:4]
        dst[..., :3] = dst[..., :3] * (1.0 - a) + src[..., :3] * a
        dst[..., 3] = np.clip(dst[..., 3] + a[..., 0] * (1.0 - dst[..., 3]), 0.0, 1.0)
        return dst

    canvas[..., 3] = 1.0
    canvas = over(canvas, cloth_rgba)
    canvas = over(canvas, base.astype(np.float32))
    canvas = over(canvas, feats_rgba)

    # ═══ 5. Свет, кромка, виньетка, зерно ═══
    rgb = ac.shade(
        np.dstack([canvas[..., :3], np.ones_like(canvas[..., 3])]),
        rim_strength=0.26,
        rim_color=np.array(_c("rim"), dtype=np.float32),
        ambient=0.70, direct=0.52, gradient=0.5,
        texture=ac.fbm(size, base_res=22, octaves=3, seed=seed + 3),
        texture_strength=0.045,
        rim_width=2,
        light_axis=light,
    )
    canvas[..., :3] = rgb[..., :3]

    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    nx = (xx / size - 0.5) * 2.0
    ny = (yy / size - 0.5) * 2.0
    vign = np.clip(1.0 - 0.62 * (nx * nx * 0.74 + ny * ny * 1.0), 0.16, 1.0)
    canvas[..., :3] *= vign[..., None]

    grain = ac.fbm(size, base_res=110, octaves=2, seed=seed + 11)
    canvas[..., :3] = np.clip(canvas[..., :3] * (0.94 + grain[..., None] * 0.12),
                              0.0, 1.0)
    canvas[..., 3] = 1.0

    # `head` возвращается наружу: сценическому слою нужны РЕАЛЬНЫЕ габариты
    # черепа, иначе шея тела не стыкуется с подбородком на широких лицах.
    head = dict(hw=hw, hh=hh, cx=cx, cy=cy, sex=sex, build=build)
    return canvas, head


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--count", type=int, default=4)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--size", type=int, default=512)
    parser.add_argument("--stage", action="store_true",
                        help="сценическая подача: поза, свет, фон-обстановка")
    parser.add_argument("--pose", default=None, help="навязать позу из POSES")
    parser.add_argument("--scene", default=None, help="навязать сцену из SCENES")
    parser.add_argument("--sex", default=None, choices=["male", "female"],
                        help="навязать пол (иначе случайно)")
    parser.add_argument("--build", default=None,
                        choices=["slight", "average", "broad", "stout"],
                        help="навязать телосложение (иначе случайно)")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    for i in range(args.count):
        seed = args.seed * 1000 + i
        # пол и телосложение выбираем ДО сборки — от них зависит череп и плечи
        rng_probe = np.random.default_rng(seed)
        _sample(rng_probe, ARCHETYPES); sex = args.sex or _sample(rng_probe, SEXES)
        build = args.build or _sample(rng_probe, BUILDS)
        rgba, head = build_portrait(seed, size=args.size, sex=sex, build=build)
        tag = f"_{sex}_{build}"
        if args.stage:
            import art_stage
            rgba, pose, scene = art_stage.stage_portrait(
                rgba, seed + 77, size=args.size,
                pose_name=args.pose, scene_name=args.scene,
                build_shape=BUILD_SHAPE[build],
                head_hw=head["hw"], head_hh=head["hh"])
            tag = f"_{pose}_{scene}{tag}"
        path = os.path.join(args.out, f"Portrait_{args.seed:02d}_{i + 1:02d}{tag}.png")
        Image.fromarray((np.clip(rgba, 0, 1) * 255).astype(np.uint8), "RGBA").save(path)
        print("saved", path)


if __name__ == "__main__":
    main()
