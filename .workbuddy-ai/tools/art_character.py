"""Процедурный top-down персонаж: покадровые спрайты покоя и ходьбы.

Почему покадрово, а не сборкой из частей. ADR 0007 собирал фигуру из
отдельных спрайтов (голова/торс/руки/ноги) и анимировал их поворотами.
Владелец проекта этот вариант отверг. Покадровая отрисовка даёт то, чего
сборка дать не может: слитный силуэт человека сверху, где конечности
в фазе шага выходят за корпус и перекрываются телом в правильном порядке.

Ориентация — одна, лицом «вниз» по изображению. Четыре направления
получаются поворотом узла Visual: для истинного вида сверху это корректно,
потому что при повороте человека вокруг вертикальной оси его проекция
на землю действительно поворачивается.

── Что именно видно строго сверху ───────────────────────────────────────
Это главное решение всей фигуры, и первые версии на нём провалились.

Смотря на человека ровно сверху, видно: купол головы (самая высокая точка),
плечевой пояс по бокам от неё и кисти рук у бёдер. Корпус, живот и бёдра
проецируются ПОД голову и не видны вообще. Поэтому:

  * силуэт ШИРОКИЙ и ПЛОСКИЙ — размах плеч примерно вдвое больше глубины;
  * голова ВЫСТУПАЕТ за плечи, а не врезана в них: если утопить её внутрь
    корпуса, фигура читается как чаша с дырой, а не как человек;
  * в покое стопы стоят прямо под бёдрами и полностью скрыты — видно
    только голову, плечи и кисти;
  * в фазе шага стопа проецируется вперёд и назад от бедра, поэтому
    выходит за силуэт впереди и позади. «Длина ноги» на спрайте — это не
    длина ноги, а величина шага.

Именно последнее отличает честный вид сверху от 3/4: там ноги торчат
из-под корпуса в любой фазе, и шаг читается только по покачиванию.

Контур рисуется двумя проходами: сначала все части с запасом в цвете
обводки, затем заливка поверх. Так кайма ровная по периметру и не
разъедается эрозией на мелких деталях.
"""

import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from art_common import Layer, fbm, jitter, rgb, save, shade, soft_shadow  # noqa: E402

FRAME = 128
OUTLINE = 2.0

# ── Пропорции (пиксели кадра; PPU 128 => кадр 128x128 = 1 юнит) ───────────
#
# Схема намеренно «иконочная», а не анатомическая. В референсах персонаж
# занимает 3–4% высоты экрана: внутренняя штриховка на таком размере не
# видна вообще, читается только силуэт. Поэтому фигура собирается из
# предельно различимых масс — круг головы, плечевой пояс, полы пальто, —
# а не из анатомически точных, но неразличимых форм.

# Голова сидит ПО ЦЕНТРУ корпуса, а не сдвинута назад.
#
# Это не мелочь. Раньше она стояла на восемь пикселей выше центра, и при
# развороте на 180° уезжала в низ кадра: фигура выглядела перевёрнутой,
# хотя поворот был арифметически верным. Сверху голова — самая высокая
# точка тела, её проекция совпадает с центром корпуса, поэтому смещать
# её некуда. Направление задаёт не смещение головы, а передняя кромка
# капюшона и наклон полосы пальто вперёд.
HEAD_R = 12.0
HEAD_Y = -3.0
HOOD_RX = 13.0        # затылок капюшона — выступает назад
HOOD_RY = 8.0
HOOD_Y = -7.0

SHOULDER_HW = 20.0    # плечевой пояс
SHOULDER_HH = 9.0
SHOULDER_Y = 1.0

HEM_HW = 17.0         # полы пальто
HEM_HH = 5.5
HEM_Y = 10.0

ARM_PIVOT_X = 19.5
ARM_PIVOT_Y = -2.0
ARM_OUT = 2.5
ARM_FWD = 10.0
HAND_R = 5.6
ARM_R = 5.8
ARM_SWING_MAX = 14.0

LEG_PIVOT_X = 11.0
LEG_PIVOT_Y = 10.0
LEG_R = 5.6
FOOT_RX = 7.5
FOOT_RY = 4.8
LEG_SWING_MAX = 32.0


def _rot(x, y, tilt_deg):
    rad = math.radians(tilt_deg)
    return (x * math.cos(rad) - y * math.sin(rad),
            x * math.sin(rad) + y * math.cos(rad))


def _draw(layer, rng, leg_l, leg_r, arm_l, arm_r, body_dy, head_dy, tilt,
          inflate, override):
    """Один проход отрисовки фигуры. override — залить всё одним цветом."""
    def color(name):
        return override if override is not None else jitter(rgb(name), rng)

    def put(fn, *args, **kwargs):
        kwargs["inflate"] = inflate
        fn(*args, **kwargs)

    # Порядок задаёт перекрытие. Конечности рисуются первыми и уходят ПОД
    # корпус — именно это делает вид сверху честным.
    for side, swing in ((-1.0, leg_l), (1.0, leg_r)):
        px, py = _rot(LEG_PIVOT_X * side, LEG_PIVOT_Y, tilt)
        fx, fy = _rot(LEG_PIVOT_X * side * 1.14, LEG_PIVOT_Y + swing, tilt)
        put(layer.capsule, px, py + body_dy, fx, fy + body_dy, LEG_R, color("trouser"))
        put(layer.ellipse, fx, fy + body_dy, FOOT_RX, FOOT_RY, color("boot"))

    for side, swing in ((-1.0, arm_l), (1.0, arm_r)):
        px, py = _rot(ARM_PIVOT_X * side, ARM_PIVOT_Y, tilt)
        hx, hy = _rot(ARM_PIVOT_X * side + ARM_OUT * side,
                      ARM_PIVOT_Y + ARM_FWD + swing, tilt)
        put(layer.capsule, px, py + body_dy, hx, hy + body_dy, ARM_R, color("sleeve"))
        put(layer.ellipse, hx, hy + body_dy, HAND_R, HAND_R, color("glove"))

    # Пальто: тёмные полы снизу, светлый плечевой пояс сверху. Смещение
    # тёмного низа вниз даёт подсвеченную сверху спину вместо плоской заливки.
    put(layer.rect, 0.0, HEM_Y + body_dy, HEM_HW, HEM_HH, color("coat_dark"), radius=5.0)
    put(layer.rect, 0.0, SHOULDER_Y + 1.4 + body_dy, SHOULDER_HW, SHOULDER_HH,
        color("coat"), radius=7.0)
    put(layer.rect, 0.0, SHOULDER_Y - 1.6 + body_dy, SHOULDER_HW - 2.0, SHOULDER_HH - 1.5,
        color("coat_light"), radius=6.0)

    # Тень под головой: узкий серп южнее купола. Без неё голова приклеена
    # к плечам и фигура читается как одно пятно. Смещена за нижнюю кромку
    # купола, иначе голова её полностью перекрывает.
    put(layer.ellipse, 0.0, HEAD_Y + HEAD_R * 0.95 + body_dy,
        HEAD_R * 0.86, 3.0, rgb("outline"))

    hx, hy = _rot(0.0, HEAD_Y, tilt)
    hy += head_dy
    # Затылок капюшона — выступает назад, поэтому силуэт не круглый.
    put(layer.ellipse, hx, hy - 3.5, HOOD_RX, HOOD_RY, color("hood"))
    # Купол: светлая подложка + смещённый тёмный верх даёт блик сверху-слева.
    put(layer.ellipse, hx, hy, HEAD_R + 1.0, HEAD_R + 1.0, color("hood_lit"))
    put(layer.ellipse, hx + 2.0, hy + 2.0, HEAD_R, HEAD_R, color("hood"))
    # Передний край капюшона — единственная подсказка направления сверху.
    put(layer.ellipse, hx, hy + HEAD_R * 0.60, HEAD_R * 0.58, HEAD_R * 0.24,
        color("coat"))


def render_frame(seed, leg_l, leg_r, arm_l, arm_r,
                 body_dy=0.0, head_dy=0.0, body_tilt=0.0):
    """Один кадр фигуры. Смещения — в пикселях кадра, углы — в градусах."""
    rng = np.random.default_rng(seed)
    layer = Layer(FRAME, ss=4)

    outline_color = rgb("outline")
    _draw(layer, rng, leg_l, leg_r, arm_l, arm_r, body_dy, head_dy, body_tilt,
          inflate=OUTLINE, override=outline_color)
    _draw(layer, rng, leg_l, leg_r, arm_l, arm_r, body_dy, head_dy, body_tilt,
          inflate=0.0, override=None)

    rgba = layer.downscale()
    tex = fbm(FRAME, base_res=6, octaves=4, seed=seed + 977)

    return shade(
        rgba,
        rim_strength=0.40,
        rim_color=np.array(rgb("rim_cool"), dtype=np.float32),
        ambient=0.72,
        direct=0.52,
        texture=tex,
        texture_strength=0.12,
        rim_width=2,
    )


def idle_frames(count=4):
    frames = []
    for i in range(count):
        phase = 2.0 * math.pi * i / count
        breathe = math.sin(phase)
        frames.append(render_frame(
            seed=100 + i,
            leg_l=0.0,
            leg_r=0.0,
            arm_l=1.6 * breathe,
            arm_r=1.6 * breathe,
            body_dy=-0.55 * breathe,
            head_dy=-1.1 * breathe,
            body_tilt=0.7 * breathe,
        ))
    return frames


def walk_frames(count=8):
    frames = []
    for i in range(count):
        phase = 2.0 * math.pi * i / count
        step = math.sin(phase)
        frames.append(render_frame(
            seed=200 + i,
            leg_l=LEG_SWING_MAX * step,
            leg_r=-LEG_SWING_MAX * step,
            arm_l=-ARM_SWING_MAX * step,
            arm_r=ARM_SWING_MAX * step,
            body_dy=-1.7 * abs(math.sin(phase)),
            head_dy=-1.2 * abs(math.sin(phase)),
            body_tilt=2.2 * step,
        ))
    return frames


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)

    idle = np.concatenate(idle_frames(4), axis=1)
    walk = np.concatenate(walk_frames(8), axis=1)
    shadow = soft_shadow(FRAME, rx=30.0, ry=22.0, cx=0.0, cy=2.0,
                         strength=0.62, blur=7.0)

    save(idle, os.path.join(out_dir, "Player_TopDown_Idle.png"))
    save(walk, os.path.join(out_dir, "Player_TopDown_Walk.png"))
    save(shadow, os.path.join(out_dir, "Player_TopDown_Shadow.png"))

    print("idle  ", idle.shape)
    print("walk  ", walk.shape)
    print("shadow", shadow.shape)


if __name__ == "__main__":
    main()
