"""Сценическая подача портрета: поза, разворот, свет, фон-обстановка.

Портрет из `art_portrait.py` — это «лицо анфас в пустоте». Реальные портреты
в духе Darkwood устроены иначе: корпус развёрнут в 3/4, голова чуть повёрнута,
свет падает от конкретного источника (окно, костёр, лампа), а за спиной —
сценическая среда, а не виньетка.

Этот модуль добавляет поверх готового портрета три вещи:

1. **Посадку корпуса.** Плечи разворачиваются, добавляется наклон и поворот,
   голова смещается и слегка поворачивается. Делается через аффинное
   искажение нижней части кадра, а не перерисовкой.
2. **Сценический свет.** Вместо равномерного света сбоку — один источник
   с направлением, цветом и затуханием. Он вырезает половину лица из тени.
3. **Фон-обстановку.** Световое пятно, туман, вертикальный градиент, тьма
   по краям — по пяти-шести шаблонам сцены.

Так достигается то, чего не было: персонажи перестают быть «фото на паспорт»
и становятся «в кадре».
"""

from __future__ import annotations

import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

import art_common as ac
from art_common import Layer


# ─────────────────────────── Позы ───────────────────────────
# Поза — это не только разворот, но и ПОЛОЖЕНИЕ ТЕЛА. В Darkwood персонаж
# не «висит» анфас: он сидит на стуле, облокотился на стол, лежит на кровати.
# Поэтому у позы два слоя:
#   1. `tilt/turn/head/dx/dy/scale` — как искажается голова и плечи;
#   2. `body` — какое тело дорисовывается под головой.
#
# Тело рисуется процедурно (`paint_body`), поэтому оно согласовано с позой
# и масштабом, а не приклеено картинкой.

POSES = [
    "confront",     # стоит прямо, чуть наклон вперёд
    "turn_left",    # корпус влево, голова к камере
    "turn_right",   # зеркально
    "hunch",        # стоит сгорбившись
    "tower",        # выпрямлен, смотрит сверху
    "side",         # почти профиль
    "lean_in",      # подался вперёд
    "shrink",       # сжался, отстранился
    "seated",       # СИДИТ на стуле, руки на коленях
    "seated_lean",  # сидит, облокотившись на стол
    "at_table",     # стоит, опершись обеими руками на стол
    "lying",        # ЛЕЖИТ, голова на подушке, вид сверху-сбоку
    "drinking",     # сидит с кружкой
    "reading",      # сидит над книгой/бумагой
    "hands_behind", # стоит, руки за спиной
    "arms_crossed", # стоит со скрещёнными руками
]

POSE_DATA = {
    "confront":     dict(tilt=0.02, turn=0.06, head=0.02, dx=0.008, dy=0.00, scale=1.00,
                         body="stand"),
    "turn_left":    dict(tilt=0.03, turn=0.30, head=0.18, dx=-0.020, dy=0.01, scale=0.99,
                         body="stand_turn"),
    "turn_right":   dict(tilt=0.03, turn=-0.30, head=-0.18, dx=0.020, dy=0.01, scale=0.99,
                         body="stand_turn"),
    "hunch":        dict(tilt=0.16, turn=0.12, head=0.07, dx=0.004, dy=0.050, scale=0.96,
                         body="stand_hunch"),
    "tower":        dict(tilt=-0.08, turn=-0.05, head=-0.03, dx=-0.004, dy=-0.035, scale=1.07,
                         body="stand"),
    "side":         dict(tilt=0.02, turn=0.46, head=0.34, dx=-0.035, dy=0.00, scale=0.98,
                         body="stand_turn"),
    "lean_in":      dict(tilt=0.20, turn=0.09, head=0.02, dx=0.006, dy=0.032, scale=1.12,
                         body="stand_lean"),
    "shrink":       dict(tilt=0.12, turn=-0.16, head=-0.11, dx=0.018, dy=0.060, scale=0.90,
                         body="stand_hunch"),
    "seated":       dict(tilt=0.02, turn=0.10, head=0.03, dx=0.006, dy=0.10, scale=0.98,
                         body="seated"),
    "seated_lean":  dict(tilt=0.10, turn=0.22, head=0.10, dx=-0.014, dy=0.08, scale=0.96,
                         body="seated_lean"),
    "at_table":     dict(tilt=0.16, turn=0.06, head=0.00, dx=0.004, dy=0.05, scale=1.02,
                         body="at_table"),
    "lying":        dict(tilt=0.00, turn=0.24, head=0.16, dx=-0.010, dy=0.06, scale=0.94,
                         body="lying"),
    "drinking":     dict(tilt=0.04, turn=0.16, head=0.06, dx=0.008, dy=0.10, scale=0.97,
                         body="seated_drink"),
    "reading":      dict(tilt=0.14, turn=0.08, head=-0.04, dx=0.004, dy=0.09, scale=0.98,
                         body="seated_read"),
    "hands_behind": dict(tilt=-0.02, turn=-0.06, head=-0.02, dx=-0.006, dy=-0.01, scale=1.01,
                         body="stand_behind"),
    "arms_crossed": dict(tilt=0.04, turn=0.10, head=0.04, dx=0.006, dy=0.02, scale=1.00,
                         body="stand_crossed"),
}

# Где на кадре лежит лицо. Источники света привязываются к этой точке:
# источник, который с ней не пересекается, оставляет персонажа в темноте.
FACE_AT = {
    "confront":   (0.500, 0.42),
    "turn_left":  (0.470, 0.42),
    "turn_right": (0.530, 0.42),
    "hunch":      (0.500, 0.47),
    "tower":      (0.500, 0.38),
    "side":       (0.455, 0.42),
    "lean_in":    (0.505, 0.44),
    "shrink":     (0.515, 0.48),
    "seated":     (0.500, 0.44),
    "seated_lean": (0.485, 0.44),
    "at_table":   (0.500, 0.44),
    "lying":      (0.455, 0.50),
    "drinking":   (0.505, 0.44),
    "reading":    (0.500, 0.46),
    "hands_behind": (0.500, 0.42),
    "arms_crossed": (0.500, 0.42),
}
for _k, _v in FACE_AT.items():
    POSE_DATA[_k]["face"] = _v


# ─────────────────────────── Сцены ───────────────────────────
# Тип источника света и обстановка за спиной. Именно это отличает «портрет»
# от «персонажа в мире»: у Darkwood за каждым героем своя сцена.

SCENES = [
    "window",     # холодный свет из окна сбоку, пыль в воздухе
    "fire",       # тёплый костёр снизу-сбоку
    "lamp",       # узкий тёплый источник сверху
    "moon",       # холодный свет сверху, сильный контраст
    "cellar",     # почти темно, слабый источник снизу
    "fog",        # рассеянный туман, мягкий свет, нет резкой границы
    "candle",     # одна свеча перед лицом, снизу вверх
    "gloom",      # ровный мрак, только кромка откуда-то слева
]


def _lerp(a, b, t):
    return a + (b - a) * t


def _ease(t):
    return t * t * (3.0 - 2.0 * t)


def _light_color(scene):
    """Цвет кромки света для сцены — тёплый у огня, холодный у луны."""
    if scene in ("fire", "candle", "lamp"):
        return (0.92, 0.72, 0.44)
    if scene in ("moon", "window"):
        return (0.62, 0.76, 0.98)
    if scene == "fog":
        return (0.78, 0.80, 0.80)
    return (0.70, 0.72, 0.78)


# ─────────────────────────── Геометрия кадра ───────────────────────────

def warp_body(rgba, pose, u):
    """Разворачивает корпус и голову, не перерисовывая персонажа.

    Разворот делается не сдвигом, а **косой проекцией**: верхние строки
    сдвигаются вбок пропорционально расстоянию от макушки, поэтому плечи
    реально уходят в перспективу и силуэт читается как объёмный корпус,
    а не как «домик», съехавший в сторону. Голова при этом доворачивается
    слабее — она остаётся обращена к зрителю, как и нужно в диалоговом
    портрете (иначе говорящий смотрит в стену).
    """
    h, w = rgba.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    ny = yy / h                       # 0 вверху, 1 внизу

    tilt, turn, head, dx, dy, scale = (
        pose["tilt"], pose["turn"], pose["head"], pose["dx"], pose["dy"], pose["scale"])

    # Вес по вертикали: у макушки 0, внизу 1. Квадрат даёт мягкий переход —
    # шея не ломается.
    body_w = np.clip((ny - 0.16) / 0.84, 0.0, 1.0)
    body_w = body_w * body_w

    # Косая проекция: сдвиг строк вбок. Это и есть «поворот корпуса» —
    # дальняя от камеры сторона уходит вглубь.
    shear = -turn * body_w * u * 0.95

    # голова доворачивается своим рычагом и в свою сторону
    head_w = np.clip(1.0 - body_w * 2.2, 0.0, 1.0)
    shear += -head * head_w * u * 0.34

    # наклон вперёд/назад и общий сдвиг позы
    ty = -tilt * body_w * u * 0.55 + dy * u

    cy0 = u * (0.66 + dy)
    cx0 = u * 0.5

    sx = (xx - cx0 - shear - dx * u) / scale + cx0
    sy = (yy - cy0 - ty) / scale + cy0

    # За пределами кадра сэмплировать НЕЧЕГО. Без этой маски клип ниже
    # растягивал крайний столбец на всю ширину — по краю кадра появлялась
    # светлая вертикальная полоса, которую легко принять за брак печати.
    valid = (sx >= 0) & (sx <= w - 1) & (sy >= 0) & (sy <= h - 1)

    ix = np.clip(sx, 0, w - 1)
    iy = np.clip(sy, 0, h - 1)
    x0 = np.floor(ix).astype(np.int32)
    y0 = np.floor(iy).astype(np.int32)
    x1 = np.clip(x0 + 1, 0, w - 1)
    y1 = np.clip(y0 + 1, 0, h - 1)
    fx = (ix - x0)[..., None]
    fy = (iy - y0)[..., None]

    top = rgba[y0, x0] * (1 - fx) + rgba[y0, x1] * fx
    bot = rgba[y1, x0] * (1 - fx) + rgba[y1, x1] * fx
    out = top * (1 - fy) + bot * fy

    out[~valid] = 0.0
    return out


# ─────────────────────────── Сценический свет ───────────────────────────

def scene_light(size, u, rng, scene, pose):
    """Карта освещения сцены: яркость и цветовое смещение по кадру.

    Ключевое отличие от «просто фона»: источник света СТРОГО привязан к
    персонажу. Лицо живёт в центре кадра вокруг `(0.5, 0.42)`, и любой
    источник, который не пересекается с этой зоной, оставляет героя в
    темноте — именно это и случилось с первой версией сцены `lamp`,
    где пятно ушло в правый верхний угол и портрет растворился.
    Поэтому пятно света всегда строится ОТ лица, а не от угла.
    """
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    nx = xx / size
    ny = yy / size

    # зона лица — к ней привязываем источники света
    fx, fy = pose.get("face", (0.5, 0.42))

    def softspot(cx, cy, rx, ry, power=1.0):
        d = ((nx - cx) / rx) ** 2 + ((ny - cy) / ry) ** 2
        return np.exp(-d * 1.7) ** power

    lum = np.zeros((size, size), dtype=np.float32)
    tint = np.zeros((size, size, 3), dtype=np.float32)

    # лёгкое дрожание, чтобы одна и та же сцена не была одинаковой
    jx = float(rng.uniform(-0.05, 0.05))
    jy = float(rng.uniform(-0.04, 0.04))

    if scene == "window":
        # холодный свет сбоку, попадает на лицо и уходит в комнату
        lum = softspot(fx - 0.16 + jx, fy - 0.10 + jy, 0.46, 0.52, 1.05) * 1.10
        lum += softspot(0.82, 0.22, 0.40, 0.40, 1.6) * 0.16
        tint[..., 2] += 0.10; tint[..., 1] += 0.04
    elif scene == "fire":
        # тёплый костёр снизу-сбоку: снизу подбородок и скулы выхвачены
        lum = softspot(fx - 0.12 + jx, fy + 0.30 + jy, 0.62, 0.46, 1.0) * 1.20
        lum += softspot(fx, fy, 0.40, 0.36, 1.4) * 0.35
        tint[..., 0] += 0.20; tint[..., 1] += 0.07; tint[..., 2] -= 0.05
    elif scene == "lamp":
        # узкий тёплый источник НАД лицом: свет падает на лоб и верх щёк
        lum = softspot(fx + 0.06 + jx, fy - 0.26 + jy, 0.38, 0.38, 1.25) * 1.15
        lum += softspot(fx, fy + 0.10, 0.30, 0.30, 1.5) * 0.30
        tint[..., 0] += 0.17; tint[..., 1] += 0.09; tint[..., 2] -= 0.04
    elif scene == "moon":
        # холодный свет сверху, жёсткий контраст
        lum = softspot(fx - 0.10 + jx, fy - 0.34 + jy, 0.58, 0.50, 1.0) * 0.95
        lum += softspot(fx, fy, 0.36, 0.34, 1.6) * 0.28
        tint[..., 2] += 0.18; tint[..., 1] += 0.07
    elif scene == "cellar":
        # почти темно, слабый источник снизу
        lum = softspot(fx, fy + 0.40 + jy, 0.66, 0.42, 0.85) * 0.85
        lum += softspot(fx, fy, 0.44, 0.40, 1.5) * 0.30
        tint[..., 0] += 0.05; tint[..., 2] += 0.04
    elif scene == "fog":
        # рассеянный туман: ровный свет, без резкой границы
        lum = 0.52 + softspot(fx, fy, 0.95, 0.95, 1.0) * 0.26
        fogd = np.sqrt((nx - 0.5) ** 2 + (ny - 0.5) ** 2)
        lum -= np.clip(fogd - 0.35, 0, 1) * 0.26
        tint[..., 0] += 0.05; tint[..., 1] += 0.06; tint[..., 2] += 0.07
    elif scene == "candle":
        # одна свеча ПЕРЕД лицом, снизу вверх — самый выразительный свет
        lum = softspot(fx + 0.05 + jx, fy + 0.22 + jy, 0.34, 0.34, 1.4) * 1.30
        lum += softspot(fx, fy - 0.04, 0.30, 0.28, 1.6) * 0.35
        tint[..., 0] += 0.24; tint[..., 1] += 0.08; tint[..., 2] -= 0.06
    elif scene == "gloom":
        # ровный мрак, кромка света слева у самого лица
        lum = softspot(fx - 0.34 + jx, fy + jy, 0.70, 0.80, 0.8) * 0.90
        lum += softspot(fx, fy, 0.34, 0.34, 1.8) * 0.22
        tint[..., 2] += 0.06

    lum = np.clip(lum, 0.0, 1.6)
    return lum, tint


def apply_scene_light(rgba, lum, tint, strength=0.85):
    """Сажает персонажа в сценический свет.

    Свет модулирует и фигуру, и фон одним множителем — поэтому персонаж
    «принадлежит» кадру, а не наклеен поверх. Тени уходят в холодный тон,
    светлые участки подкрашиваются тёплым (или наоборот, по сцене).

    Базовый уровень (`floor`) намеренно не нулевой: полностью чёрные участки
    читаются не как «мрак», а как дыра в картинке. Даже самая тёмная сцена
    оставляет ~18% яркости — этого хватает, чтобы силуэт угадывался.
    """
    rgb = rgba[..., :3]

    lit = np.clip(lum * strength, 0.0, 1.4)
    clear = np.clip(lit, 0.0, 1.0)

    # холод в тенях: чем меньше света, тем сильнее уход в синеву
    cold = np.array([0.038, 0.044, 0.062], dtype=np.float32)
    # Базовый уровень 0.30, а не 0.20: в самых тёмных сценах силуэт фигуры
    # обязан угадываться, иначе тело исчезает совсем и персонаж выглядит
    # как отрезанная голова, висящая в пустоте.
    out = rgb * (0.30 + lit[..., None] * 1.00)
    out = out + cold[None, None, :] * (1.0 - clear)[..., None] * 0.70
    out = out + tint * (0.30 + 0.70 * clear)[..., None]

    return np.clip(np.dstack([out, rgba[..., 3]]), 0.0, 1.0)


# ─────────────────────────── Тело ───────────────────────────
# Тело рисуется под головой и согласовано с позой: плечи, руки, колени,
# спинка стула. Именно оно превращает «портрет» в «персонажа в сцене»:
# сидящий житель за столом и стоящий у стены — это разные кадры, даже если
# лицо одно и то же.

# Масштаб фигуры. Значения подобраны так, чтобы РАЗМАХ ПЛЕЧ (≈ 2.7 * hw)
# оставлял в кадре воздух по краям: при крупном черепе без этого множителя
# плечи уходят за обе границы кадра и ни одна поза не читается.
BODY_HEAD = (1.33, 1.13)

def paint_body(layer, rng, cx, cy, hw, hh, kind, build_shape, u):
    """Рисует тело в соответствии с позой.

    ВАЖНАЯ ГРАБЛЯ (съела несколько итераций). Координаты `Layer` отсчитываются
    ОТ ЦЕНТРА кадра, а не от левого верхнего угла: вниз — плюс. Поэтому:

    * «низ фигуры обрезан краем кадра» — это `+0.50*u`, а не `u*0.50` в
      старой (ошибочной) арифметике, где тело уезжало в нижние 25% и
      выглядело как борода под подбородком;
    * «верх фигуры» (макушка плеч, спинка стула) — это ОТРИЦАТЕЛЬНЫЙ y.
      Стул спинкой «выше плеч» рисуется как `neck_y - hh*1.5`, иначе
      спинка оказывается под столом и поза не читается вовсе.

    ВТОРАЯ ГРАБЛЯ той же природы. Плечи стоящего человека шире черепа в
    два-три раза, и при черепе `hw ≈ 0.15*u` размах плеч выходит за кадр
    целиком. Тогда ни одной позы не видно, все стоят «по пояс». Поэтому
    фигура строится вокруг «средней» головы (`BODY_HEAD`), а не вокруг
    фактического черепа персонажа: персонаж с крупным лицом получает
    нормальные плечи, а не плечи во весь кадр.
    """
    hw = hw * BODY_HEAD[0]
    hh = hh * BODY_HEAD[1]

    sw = build_shape.get("shoulder", 1.0)
    cl = build_shape.get("collar", 1.0)

    # Цвета одежды намеренно светлее прежних: сценический свет гасит фигуру
    # множителем, и на «тёмном по тёмному» позы не видно совсем.
    cloth_dark = (0.135, 0.140, 0.132)
    cloth_mid = (0.225, 0.228, 0.216)
    cloth_lit = (0.325, 0.320, 0.296)
    trim = (0.170, 0.150, 0.115)
    leather = (0.205, 0.160, 0.120)
    skin_hand = (0.560, 0.470, 0.382)
    wood = (0.196, 0.156, 0.108)
    wood_lit = (0.310, 0.250, 0.172)

    # кромки кадра в системе центра
    floor = u * 0.50 - u * 0.008     # низ фигуры срезан краем
    neck_y = cy + hh * 1.02          # шея стыкуется с подбородком портрета
    # Спинка стула поднимается ВЫШЕ плеч — это и читается как «сидит».
    backrest_top = neck_y - hh * 1.05
    lap = cy + hh * 2.10             # уровень колен сидящего

    def shoulders(bottom_y, top_y, width):
        """Плечевой пояс: сверху уже, книзу шире (трапеция)."""
        layer.polygon([
            (cx - hw * width * 2.7, bottom_y),
            (cx - hw * width * 1.55, top_y + hh * 0.32),
            (cx - hw * width * 1.28, top_y),
            (cx, top_y - hh * 0.06),
            (cx + hw * width * 1.28, top_y),
            (cx + hw * width * 1.55, top_y + hh * 0.32),
            (cx + hw * width * 2.7, bottom_y),
        ], cloth_mid)
        # блик на плече со стороны света
        layer.polygon([
            (cx - hw * width * 2.4, bottom_y),
            (cx - hw * width * 1.45, top_y + hh * 0.36),
            (cx - hw * width * 1.20, top_y + hh * 0.06),
            (cx - hw * width * 0.30, top_y - hh * 0.02),
            (cx - hw * width * 0.34, bottom_y),
        ], cloth_lit)
        # воротник: тёмная подкладка под подбородком
        layer.polygon([
            (cx - hw * 0.62 * cl, neck_y + hh * 0.06),
            (cx, neck_y + hh * 0.52),
            (cx + hw * 0.62 * cl, neck_y + hh * 0.06),
            (cx, neck_y + hh * 0.26),
        ], cloth_dark)
        # шея, если воротник низкий
        layer.capsule(cx, neck_y + hh * 0.24, cx, neck_y + hh * 0.30,
                      hw * 0.30, (0.36, 0.30, 0.25))

    def chair(back_x, top_y, seat_y, extra=1.0):
        """Спинка стула за спиной. Стойки идут ВВЕРХ от колен."""
        half = hw * 1.72 * sw * extra
        # спинка рисуется ДО плеч: она за спиной, а не перед персонажем
        layer.polygon([
            (cx - half, top_y + hh * 0.02),
            (cx + half, top_y + hh * 0.02),
            (cx + half, top_y + hh * 0.34),
            (cx - half, top_y + hh * 0.34),
        ], wood_lit)
        for side in (-1, 1):
            layer.capsule(cx + side * half, seat_y,
                          cx + side * half, top_y + hh * 0.26,
                          hw * 0.135, wood)
        # перекладины
        for frac in (0.22, 0.60):
            layer.capsule(cx - half, top_y + hh * (0.34 + frac),
                          cx + half, top_y + hh * (0.34 + frac),
                          hw * 0.075, wood)

    if kind in ("stand", "stand_turn", "stand_hunch", "stand_behind",
                "stand_crossed", "stand_lean"):
        top = neck_y + hh * 0.14
        if kind == "stand_hunch":
            top = neck_y + hh * 0.02
        elif kind == "stand_lean":
            top = neck_y + hh * 0.10
        shoulders(floor, top, sw)
        for side in (-1, 1):
            if kind == "stand_behind":
                # руки за спиной: видны только предплечья по бокам
                layer.capsule(cx + side * hw * 0.80, top + hh * 0.20,
                              cx + side * hw * 1.08, floor - hh * 0.30,
                              hw * 0.24, cloth_dark)
            elif kind == "stand_crossed":
                # предплечья перекрещены перед грудью, кисти по краям
                layer.capsule(cx - hw * 1.20, top + hh * 0.44,
                              cx + hw * 0.44, top + hh * 0.56,
                              hw * 0.235, cloth_dark)
                layer.capsule(cx + hw * 1.20, top + hh * 0.60,
                              cx - hw * 0.44, top + hh * 0.72,
                              hw * 0.215, cloth_dark)
                layer.ellipse(cx - hw * 0.42, top + hh * 0.52, hw * 0.16, hh * 0.09,
                              skin_hand)
                layer.ellipse(cx + hw * 0.46, top + hh * 0.70, hw * 0.15, hh * 0.09,
                              skin_hand)
            elif kind == "stand_lean":
                # обе руки опираются на невидимую стойку перед персонажем
                layer.capsule(cx + side * hw * 1.00, top + hh * 0.16,
                              cx + side * hw * 1.42, cy + hh * 2.30,
                              hw * 0.245, cloth_dark)
            else:
                layer.capsule(cx + side * hw * 1.02, top + hh * 0.12,
                              cx + side * hw * 1.24, floor - hh * 0.24,
                              hw * 0.265, cloth_dark)
                # кисти у бёдер
                layer.ellipse(cx + side * hw * 1.24, floor - hh * 0.20,
                              hw * 0.155, hh * 0.075, skin_hand)

    elif kind in ("seated", "seated_lean", "seated_drink", "seated_read"):
        # СИДЯЩИЙ. Ключ к читаемости: спинка стула ВИДНА ВЫШЕ ПЛЕЧ,
        # а колени перекрывают низ кадра. Иначе сидящий неотличим от стоящего.
        top = neck_y + hh * 0.20
        seat = cy + hh * 2.05
        chair(cx, neck_y - hh * 1.45, seat, extra=1.0)
        # бёдра уходят к краю кадра — «сидит» читается по перекрытию
        layer.polygon([
            (cx - hw * 2.05 * sw, floor + u * 0.02),
            (cx + hw * 2.05 * sw, floor + u * 0.02),
            (cx + hw * 1.70 * sw, seat - hh * 0.40),
            (cx - hw * 1.70 * sw, seat - hh * 0.40),
        ], cloth_dark)
        shoulders(seat + hh * 0.10, top, sw * 1.04)
        for side in (-1, 1):
            layer.capsule(cx + side * hw * 1.00, top + hh * 0.20,
                          cx + side * hw * 0.62, seat - hh * 0.10,
                          hw * 0.235, cloth_dark)
        if kind == "seated_lean":
            # облокотился: одно предплечье на стол, стол перекрывает колени
            layer.capsule(cx - hw * 1.00, top + hh * 0.24,
                          cx - hw * 1.62, seat - hh * 0.55, hw * 0.245, cloth_dark)
            layer.capsule(cx + hw * 1.00, top + hh * 0.24,
                          cx + hw * 1.58, seat - hh * 0.60, hw * 0.245, cloth_dark)
            layer.rect(cx, seat - hh * 0.34, hw * 3.1, hh * 0.20, wood)
            layer.rect(cx, seat - hh * 0.52, hw * 3.1, hh * 0.055, wood_lit)
            layer.ellipse(cx - hw * 1.62, seat - hh * 0.52, hw * 0.16, hh * 0.08,
                          skin_hand)
            layer.ellipse(cx + hw * 1.58, seat - hh * 0.57, hw * 0.16, hh * 0.08,
                          skin_hand)
        elif kind == "seated_drink":
            # кружка в руке у самого подбородка
            layer.capsule(cx - hw * 0.96, top + hh * 0.26,
                          cx - hw * 0.50, seat - hh * 0.14, hw * 0.235, cloth_dark)
            layer.capsule(cx + hw * 0.92, top + hh * 0.20,
                          cx + hw * 0.44, cy + hh * 0.90, hw * 0.215, cloth_dark)
            layer.rect(cx + hw * 0.42, cy + hh * 0.96, hw * 0.20, hh * 0.135,
                       leather, radius=hw * 0.05)
            layer.rect(cx + hw * 0.42, cy + hh * 0.83, hw * 0.21, hh * 0.035,
                       (0.42, 0.34, 0.24), radius=hw * 0.04)
        elif kind == "seated_read":
            layer.capsule(cx - hw * 0.94, top + hh * 0.26,
                          cx - hw * 0.42, seat - hh * 0.52, hw * 0.215, cloth_dark)
            layer.capsule(cx + hw * 0.94, top + hh * 0.26,
                          cx + hw * 0.42, seat - hh * 0.52, hw * 0.215, cloth_dark)
            # раскрытая книга: две страницы «домиком», светлая кромка сверху
            for side, (x0, x1, tone) in enumerate((
                    (-0.70, -0.02, (0.455, 0.412, 0.330)),
                    (0.02, 0.70, (0.400, 0.362, 0.290)))):
                layer.polygon([
                    (cx + hw * x0, seat - hh * 0.60),
                    (cx + hw * x1, seat - hh * 0.50),
                    (cx + hw * x1, seat - hh * 0.12),
                    (cx + hw * x0, seat - hh * 0.24),
                ], tone)
            layer.capsule(cx, seat - hh * 0.56, cx, seat - hh * 0.14,
                          hw * 0.022, (0.22, 0.19, 0.15))
        else:
            # руки на коленях
            layer.capsule(cx - hw * 0.98, top + hh * 0.22,
                          cx - hw * 0.54, seat - hh * 0.34, hw * 0.235, cloth_dark)
            layer.capsule(cx + hw * 0.98, top + hh * 0.22,
                          cx + hw * 0.54, seat - hh * 0.34, hw * 0.235, cloth_dark)
            for side in (-1, 1):
                layer.ellipse(cx + side * hw * 0.54, seat - hh * 0.30,
                              hw * 0.155, hh * 0.075, skin_hand)

    elif kind == "at_table":
        # оперся обеими руками на стол; стол перекрывает корпус от пояса
        top = neck_y + hh * 0.16
        table = cy + hh * 2.55
        shoulders(table + hh * 0.16, top, sw)
        for side in (-1, 1):
            layer.capsule(cx + side * hw * 1.02, top + hh * 0.18,
                          cx + side * hw * 1.62, table - hh * 0.10,
                          hw * 0.245, cloth_dark)
            layer.ellipse(cx + side * hw * 1.62, table - hh * 0.10,
                          hw * 0.16, hh * 0.08, skin_hand)
        layer.rect(cx, table, hw * 3.4, hh * 0.22, wood)
        layer.rect(cx, table - hh * 0.19, hw * 3.4, hh * 0.06, wood_lit)
        # кружка на столе
        layer.rect(cx + hw * 1.05, table - hh * 0.34, hw * 0.19, hh * 0.13,
                   leather, radius=hw * 0.05)

    elif kind == "lying":
        # Лежит: корпус уходит вбок от головы, подушка под головой.
        pillow_y = cy + hh * 1.10
        layer.ellipse(cx - hw * 0.28, pillow_y, hw * 2.3, hh * 0.52,
                      (0.335, 0.322, 0.284))
        layer.polygon([
            (cx - hw * 0.55, pillow_y - hh * 0.05),
            (cx + hw * 3.2, pillow_y + hh * 0.34),
            (cx + hw * 3.2, floor),
            (cx - hw * 0.55, floor),
        ], cloth_mid)
        layer.polygon([
            (cx + hw * 0.30, pillow_y + hh * 0.10),
            (cx + hw * 2.6, pillow_y + hh * 0.40),
            (cx + hw * 2.6, floor),
            (cx + hw * 0.30, floor),
        ], cloth_lit)
        layer.capsule(cx + hw * 0.7, pillow_y + hh * 0.48,
                      cx + hw * 2.5, pillow_y + hh * 0.36, hw * 0.28, cloth_dark)

    return layer


# ─────────────────────────── Фон-обстановка ───────────────────────────

def build_scene_bg(size, u, rng, seed, scene, lum, tint):
    """Фон-обстановка: не виньетка, а сценическая среда.

    Пять-шесть шаблонов: световое пятно, дымка/пыль, горизонтальные полосы
    (доски, стены), вертикальные тени (столбы, ветви), темнота по краям.
    """
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    nx = xx / size
    ny = yy / size

    # база — тёмный холодный тон
    base = np.array([0.020, 0.024, 0.030], dtype=np.float32)
    img = np.ones((size, size, 3), dtype=np.float32) * base[None, None, :]

    # световое пятно как «дальний план»: коридор, комната, туман
    glow = ac.fbm(size, base_res=3, octaves=4, seed=seed + 31)
    img = img + (lum[..., None] * 0.115) * (0.65 + glow[..., None] * 0.7)
    img = img + tint * lum[..., None] * 0.10

    # фактура среды
    if scene in ("window", "fog", "moon"):
        # пыль в воздухе / взвесь
        dust = ac.fbm(size, base_res=48, octaves=3, seed=seed + 32)
        img += (dust[..., None] - 0.5) * lum[..., None] * 0.16
    if scene in ("cellar", "gloom"):
        # горизонтальные доски / камень
        bands = 0.5 + 0.5 * np.sin(ny * 26.0 * math.pi + float(rng.uniform(0, 6)))
        img *= (0.80 + bands[..., None] * 0.30)
    if scene in ("cellar", "window"):
        # вертикальные тени: столбы, косяки
        ncols = int(rng.integers(2, 5))
        for i in range(ncols):
            cx = float(rng.uniform(0.05, 0.95))
            wdt = float(rng.uniform(0.02, 0.07))
            col = np.exp(-((nx - cx) / wdt) ** 2)
            img *= (1.0 - col[..., None] * 0.55)
    if scene in ("fire", "candle", "lamp"):
        # копоть и дым: неоднородность тёплого тона
        smoke = ac.fbm(size, base_res=8, octaves=4, seed=seed + 33)
        img += (smoke[..., None] - 0.5) * 0.05

    # темнота по краям — но мягкая, это не виньетка, а уход в глубину
    edge = np.clip(np.sqrt((nx - 0.5) ** 2 + (ny - 0.52) ** 2) - 0.28, 0, 1)
    img *= (1.0 - _ease(np.clip(edge * 1.25, 0, 1))[..., None] * 0.90)

    # световые лучи, если источник — окно или луна
    if scene in ("window", "moon"):
        beams = np.zeros((size, size), dtype=np.float32)
        nbeams = int(rng.integers(2, 5))
        for i in range(nbeams):
            ang = float(rng.uniform(0.5, 1.5))
            off = float(rng.uniform(-0.4, 0.4))
            d = np.abs(nx * math.cos(ang) - ny * math.sin(ang) + off)
            beams += np.exp(-(d / float(rng.uniform(0.02, 0.05))) ** 2) * \
                float(rng.uniform(0.25, 0.6))
        img += beams[..., None] * lum[..., None] * 0.135 * \
            np.array([0.9, 0.95, 1.0], dtype=np.float32)[None, None, :]

    img = np.clip(img, 0.0, 1.0)
    return np.dstack([img, np.ones((size, size), dtype=np.float32)])


# ─────────────────────────── Сборка ───────────────────────────

def stage_portrait(rgba, seed, size=512, pose_name=None, scene_name=None,
                   build_shape=None, head_hw=None, head_hh=None):
    """Помещает готовый портрет в сцену: тело + поза + свет + обстановка.

    Портрет приходит анфас; здесь он получает тело в нужной позе (стоит,
    сидит, лежит, облокотился), разворот корпуса, сценический свет и среду
    за спиной. Функция чистая относительно seed: одна и та же пара
    (seed портрета, seed сцены) даёт один и тот же кадр.

    `head_hw`/`head_hh` — реальные полугабариты черепа из портрета. Их надо
    передавать обязательно: без них тело строится по «среднему» черепу и
    стык шеи с подбородком разъезжается на широких и мелких лицах.
    """
    rng = np.random.default_rng(seed)
    u = float(size)

    pose_name = pose_name or POSES[int(rng.integers(0, len(POSES)))]
    scene_name = scene_name or SCENES[int(rng.integers(0, len(SCENES)))]
    pose = POSE_DATA[pose_name]
    shape = build_shape or {"shoulder": 1.0, "collar": 1.0}

    lum, tint = scene_light(size, u, rng, scene_name, pose)
    # Свет сцены синтезируется от лица. Но тело живёт НИЖЕ лица, и пятно,
    # посчитанное только от лица, до него не достаёт: в `moon` и `cellar`
    # в нижней половине кадра остаётся 0.04 при 0.68 на лице — фигура просто
    # исчезает. Поэтому к сцене добавляется нижний «контровой» подсвет:
    # слабый рассеянный свет, который читает силуэт, не выбивая лицо.
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    below = np.clip((yy / size - 0.34) / 0.66, 0.0, 1.0) ** 1.4
    lum = lum + below * 0.52
    bg = build_scene_bg(size, u, rng, seed, scene_name, lum, tint)

    # Тело рисуется ОТДЕЛЬНЫМ слоем под головой и согласовано с позой.
    # Без него персонаж — «голова на плечиках», все стоят одинаково.
    body = Layer(size, ss=1)
    # Голова в кадре сидит на (0.5, 0.055*u) — начало координат Layer
    # в центре кадра, поэтому центр головы это y = +0.055*u.
    hcx, hcy = 0.0, u * 0.055
    hw = head_hw if head_hw else u * 0.150
    hh = head_hh if head_hh else u * 0.180
    # В развёрнутых позах голова уезжает вместе с телом, и в кадре остаётся
    # затылок. Компенсируем: сдвигаем ОБА слоя вбок, голову сильнее — так
    # сохраняется читаемый разворот корпуса и при этом лицо не теряется.
    body_cx = -pose["turn"] * u * 0.34
    head_cx = -pose["turn"] * u * 0.15
    paint_body(body, rng, body_cx + hcx, hcy, hw, hh, pose["body"], shape, u)
    body_rgba = body.downscale()

    # фигуру разворачиваем, но только её: фон остаётся в своей перспективе
    warped = warp_body(rgba, pose, u)
    body_warped = warp_body(body_rgba, pose, u)

    # компенсация разворота: голова сдвигается вбок относительно корпуса,
    # иначе при `turn = 0.30+` в кадре остаётся затылок вместо лица.
    #
    # Сдвиг делается СРЕЗОМ, а не `np.roll`: `roll` заворачивает уехавшие
    # столбцы на противоположную сторону, и по краю кадра появляется
    # светлая вертикальная полоса — в готовом кадре она читается как
    # брак печати. Поэтому сначала сдвиг, потом обнуление открывшегося края.
    if abs(head_cx) > 1e-6:
        shift = int(round(head_cx))
        warped = np.roll(warped, shift, axis=1)
        if shift > 0:
            warped[:, :shift] = 0.0
        else:
            warped[:, shift:] = 0.0

    # сцена освещает всё вместе
    bg_lit = apply_scene_light(bg, lum, tint, strength=0.90)
    body_lit = apply_scene_light(body_warped, lum, tint, strength=0.82)
    fig_lit = apply_scene_light(warped, lum, tint, strength=0.85)

    # ТЕЛО — не фон. Сцена освещает его слабо (источник обычно над лицом),
    # и в тёмных сценах фигура полностью растворялась. Поэтому корпусу
    # добавляется собственная базовая подсветка: он должен читаться всегда,
    # иначе персонаж выглядит как отрезанная голова в пустоте.
    #
    # Свет на одежду НАПРАВЛЕННЫЙ, а не плоский: блик идёт сверху-слева
    # по плечам и рукавам. Плоская заливка даёт одинаково серую массу, по
    # которой не понять, где плечо, а где стул, и поза снова не читается.
    body_amb = np.clip(body_warped[..., 3:4], 0, 1)
    bny, bnx = np.mgrid[0:size, 0:size].astype(np.float32)
    bnx = bnx / size
    bny = bny / size
    # 1 — «освещённая» сторона (верх-лево), 0 — нижне-правая тень
    bshadow = np.clip(1.0 - (bnx * 0.85 + bny * 0.95), 0.0, 1.0)
    body_form = (0.36 + bshadow * 0.32)[..., None]
    body_lit[..., :3] = np.clip(
        body_lit[..., :3] + body_amb * body_form *
        np.array([0.30, 0.305, 0.320], dtype=np.float32),
        0, 1)

    # порядок: фон → тело → голова
    a_body = body_lit[..., 3:4]
    base = bg_lit[..., :3] * (1.0 - a_body) + body_lit[..., :3] * a_body
    a = fig_lit[..., 3:4]
    out = base * (1.0 - a) + fig_lit[..., :3] * a

    # Кромка света по контуру фигуры. Без неё тело тонет в фоне и любой
    # персонаж читается как «голова в темноте» — поза не видна вовсе.
    # Кромка идёт от источника света, поэтому плечи и стол дают силуэт.
    fig_alpha = np.clip(body_warped[..., 3] + warped[..., 3] * (1 - body_warped[..., 3]),
                        0, 1)
    inner = np.asarray(
        Image.fromarray((fig_alpha * 255).astype(np.uint8), "L")
        .filter(ImageFilter.MinFilter(7)), dtype=np.float32) / 255.0
    rim = np.clip(fig_alpha - inner, 0.0, 1.0)
    rim = np.asarray(
        Image.fromarray((rim * 255).astype(np.uint8), "L")
        .filter(ImageFilter.GaussianBlur(1.4)), dtype=np.float32) / 255.0
    rim_col = np.array(_light_color(scene_name), dtype=np.float32)
    out = out + rim[..., None] * rim_col[None, None, :] * 1.25

    # виньетка помягче прежней — сцена уже даёт глубокую темноту
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    nx = (xx / size - 0.5) * 2.0
    ny = (yy / size - 0.5) * 2.0
    vign = np.clip(1.0 - 0.34 * (nx * nx * 0.78 + ny * ny * 1.0), 0.30, 1.0)
    out = out * vign[..., None]

    # зерно и лёгкая аберрация по краям — «плёнка». Фигуре зерно достаётся
    # в ослабленном виде: на мелких деталях (кисти, кружка, книга) шум
    # превращается в «мусор», и предмет перестаёт читаться.
    grain = ac.fbm(size, base_res=120, octaves=2, seed=seed + 41)
    grain_fig = 0.972 + grain[..., None] * 0.056
    grain_bg = 0.93 + grain[..., None] * 0.14
    out = np.clip(out * (grain_fig * fig_alpha[..., None] +
                         grain_bg * (1.0 - fig_alpha[..., None])),
                  0.0, 1.0)

    return np.dstack([out, np.ones((size, size), dtype=np.float32)]), pose_name, scene_name
