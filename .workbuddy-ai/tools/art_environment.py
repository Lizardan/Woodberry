"""Процедурное окружение: земля, пол дома, стены, окна, деревья, туман.

Все тайлы бесшовны по обеим осям (или по X для стен) — этого требует
`SpriteRenderer.drawMode = Tiled` из `docs/specs/core-gameplay.md`.
Бесшовность обеспечивается тем, что шум строится на периодической сетке
(`fbm`), а швы и стыки досок кладутся ровно на границы текстуры.

Палитра и приёмы сняты с референсов: пол дома — холодные вертикальные
доски (Scorched Sun), лес — почти чёрные силуэты крон (Darkwood),
земля — тёмная подстилка с мхом и палыми листьями.
"""

import math
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from art_common import (Layer, _blur, _shift, fbm, jitter, mix, rgb, ridge,  # noqa: E402
                        save, shade, to_image)


# ─────────────────────────── Земля ───────────────────────────

def ground(size=256, seed=11):
    """Лесная подстилка: земля, мох, палые листья, редкие камни."""
    noise = fbm(size, base_res=3, octaves=6, seed=seed)
    detail = fbm(size, base_res=12, octaves=5, seed=seed + 41)
    moss_field = fbm(size, base_res=2, octaves=4, seed=seed + 137)

    dirt = np.array(rgb("dirt"), dtype=np.float32)
    dirt_lit = np.array(rgb("dirt_lit"), dtype=np.float32)
    moss = np.array(rgb("moss"), dtype=np.float32)
    moss_lit = np.array(rgb("moss_lit"), dtype=np.float32)

    base = dirt[None, None, :] * (0.55 + 0.9 * noise)[..., None]
    base = base + (dirt_lit - dirt)[None, None, :] * (detail ** 2)[..., None] * 0.8

    # Пятна мха — мягкие, с рваной границей.
    moss_mask = np.clip((moss_field - 0.52) * 4.0, 0.0, 1.0)
    moss_mask = moss_mask * (0.5 + 0.5 * detail)
    moss_color = moss[None, None, :] * (0.7 + 0.7 * detail)[..., None] \
        + (moss_lit - moss)[None, None, :] * (detail ** 3)[..., None] * 1.2
    base = base * (1.0 - moss_mask)[..., None] + moss_color * moss_mask[..., None]

    # Палые листья: мелкие вытянутые пятна.
    leaf_field = fbm(size, base_res=24, octaves=3, seed=seed + 909)
    leaf_mask = np.clip((leaf_field - 0.66) * 5.0, 0.0, 1.0)
    leaf = np.array(rgb("leaf"), dtype=np.float32)[None, None, :]
    base = base * (1.0 - leaf_mask * 0.55)[..., None] + leaf * (leaf_mask * 0.55)[..., None]

    # Камни: очень редкие холодные пятна.
    stone_field = fbm(size, base_res=8, octaves=3, seed=seed + 313)
    stone_mask = np.clip((stone_field - 0.80) * 8.0, 0.0, 1.0)
    stone = np.array([0.20, 0.20, 0.21], dtype=np.float32)[None, None, :]
    base = base * (1.0 - stone_mask)[..., None] + stone * stone_mask[..., None]

    grain = (fbm(size, base_res=48, octaves=2, seed=seed + 77) - 0.5) * 0.16
    base = base * (1.0 + grain)[..., None]

    base = np.clip(base, 0.0, 1.0)
    rgba = np.dstack([base, np.ones((size, size), dtype=np.float32)])
    return rgba


# ─────────────────────────── Пол дома ───────────────────────────

def interior_floor(size=256, planks=4, seed=23):
    """Дощатый пол: вертикальные доски с холодным отсветом сверху.

    Швы досок кладутся ровно на границы текстуры, иначе тайл не бесшовный.

    Досок мало и контраст низкий намеренно. Частые контрастные швы дают
    «зебру»: на кадре видна не комната, а полосатая заливка, и она перебивает
    и персонажа, и мебель. Пол обязан быть фоном.
    """
    plank_w = size // planks
    rng = np.random.default_rng(seed)

    base = np.zeros((size, size, 3), dtype=np.float32)
    wood_dark = np.array(rgb("wood_dark"), dtype=np.float32)
    wood_mid = np.array(rgb("wood_mid"), dtype=np.float32)
    wood_lit = np.array(rgb("wood_lit"), dtype=np.float32)

    grain = fbm(size, base_res=6, octaves=5, seed=seed + 5)
    fine = fbm(size, base_res=64, octaves=3, seed=seed + 71)
    wear = fbm(size, base_res=2, octaves=3, seed=seed + 199)

    for i in range(planks):
        x0 = i * plank_w
        tone = 0.5 + 0.5 * rng.random()
        # Внутри доски тон темнее к краям — даёт жёлоб между досками.
        local = np.zeros((size, plank_w), dtype=np.float32)
        edge = np.minimum(np.arange(plank_w), plank_w - 1 - np.arange(plank_w)) / (plank_w * 0.5)
        edge = np.clip(edge, 0.0, 1.0) ** 0.45
        local[:] = edge[None, :]

        plank = wood_mid[None, None, :] * (0.72 + 0.45 * tone)
        plank = plank + (wood_lit - wood_mid)[None, None, :] \
            * (grain[:, x0:x0 + plank_w] ** 2)[..., None] * 0.6
        plank = plank * (0.74 + 0.34 * local)[..., None]
        plank = plank + (wood_dark - wood_mid)[None, None, :] * (1.0 - local)[..., None] * 0.45
        base[:, x0:x0 + plank_w] = plank

    # Швы: тонкая тёмная линия на границе каждой доски.
    for i in range(planks):
        x = i * plank_w
        base[:, x] *= 0.62
        base[:, (x + 1) % size] *= 0.80

    # Общая затёртость и пыль.
    base = base * (0.72 + 0.5 * wear)[..., None]
    base = base * (1.0 + (fine - 0.5) * 0.22)[..., None]

    # Холодный отсвет. Профиль ОБЯЗАН быть симметричным относительно центра
    # тайла: односторонний градиент даёт разрыв на стыке строк, и на полу
    # проступает сетка из горизонтальных линий каждые два юнита.
    ramp = np.abs(np.linspace(-1.0, 1.0, size, dtype=np.float32))[:, None]
    base = base * (1.02 - 0.20 * ramp)[..., None]

    # Пол — единственная крупная поверхность в кадре, и он же задаёт, насколько
    # комната вообще читается. Затемнять его «ради атмосферы» нельзя: темноту
    # рисует слой обзора, а не текстура, и зажатый пол просто превращает
    # комнату в серое пятно.
    base = np.clip(base * 1.45, 0.0, 1.0)
    return np.dstack([base, np.ones((size, size), dtype=np.float32)])


# ─────────────────────────── Стена ───────────────────────────

def wall(width=128, height=64, seed=31):
    """Стена сверху: видна только её толща. Бесшовна по X."""
    rng = np.random.default_rng(seed)
    timber = fbm(width, base_res=4, octaves=5, seed=seed + 3)
    # Растягиваем шум по высоте: волокна идут вдоль стены.
    timber = np.repeat(timber[:1, :], height, axis=0) * 0.6 \
        + np.repeat(fbm(width, base_res=2, octaves=3, seed=seed + 9)[:1, :], height, axis=0) * 0.4
    timber = np.repeat(timber[:1, :], height, axis=0)
    grain = np.repeat(fbm(width, base_res=48, octaves=2, seed=seed + 51)[:1, :], height, axis=0)

    wood_dark = np.array(rgb("wood_dark"), dtype=np.float32)
    wood_mid = np.array(rgb("wood_mid"), dtype=np.float32)
    wood_lit = np.array(rgb("wood_lit"), dtype=np.float32)

    base = wood_mid[None, None, :] * (0.70 + 0.7 * timber)[..., None]
    base = base + (wood_lit - wood_mid)[None, None, :] * (grain ** 2)[..., None] * 0.7

    # Профиль по толщине стены: тёмные края, светлая середина.
    ys = np.arange(height, dtype=np.float32)
    t = np.abs(ys - (height - 1) * 0.5) / ((height - 1) * 0.5)
    profile = np.clip(1.0 - t, 0.0, 1.0) ** 0.55
    base = base * (0.34 + 0.86 * profile)[:, None, None]
    base = base + (wood_dark - wood_mid)[None, None, :] * (t ** 3)[:, None, None] * 1.4

    # Стыки брёвен: тёмные вертикальные насечки.
    for x in range(0, width, 32):
        base[:, x] *= 0.45

    base = np.clip(base, 0.0, 1.0)
    return np.dstack([base, np.ones((height, width), dtype=np.float32)])


def window(width=256, height=64, seed=37):
    """Стена с окном: проём, рама, стекло, отблеск. Бесшовна по X."""
    rgba = wall(width, height, seed=seed)
    base = rgba[..., :3]

    inner_w = int(width * 0.46)
    x0 = (width - inner_w) // 2
    x1 = x0 + inner_w

    glass = np.array(rgb("glass"), dtype=np.float32)
    glass_lit = np.array(rgb("glass_lit"), dtype=np.float32)
    metal = np.array(rgb("metal"), dtype=np.float32)

    # Проём: почти чёрный, чтобы читался как дыра в стене.
    base[:, x0:x1, :] = np.array([0.02, 0.025, 0.035], dtype=np.float32)

    # Стекло внутри проёма.
    gy0, gy1 = int(height * 0.18), int(height * 0.82)
    glass_field = fbm(width, base_res=6, octaves=4, seed=seed + 13)[:, x0:x1]
    pane = glass[None, None, :] * (0.6 + 0.9 * glass_field)[..., None]
    pane = pane + (glass_lit - glass)[None, None, :] * (glass_field ** 3)[..., None] * 1.6
    base[gy0:gy1, x0:x1, :] = pane[gy0:gy1]

    # Рама: переплёт крест-накрест.
    mid_x = (x0 + x1) // 2
    mid_y = (gy0 + gy1) // 2
    for y in range(gy0, gy1):
        base[y, mid_x - 1:mid_x + 1, :] = metal * 1.15
    for x in range(x0, x1):
        base[mid_y - 1:mid_y + 1, x, :] = metal * 1.15
    for x in range(x0, x1):
        base[gy0 - 1:gy0 + 1, x, :] = metal
        base[gy1 - 2:gy1, x, :] = metal
    for y in range(gy0, gy1):
        base[y, x0 - 1:x0 + 1, :] = metal
        base[y, x1 - 2:x1, :] = metal

    rgba[..., :3] = np.clip(base, 0.0, 1.0)
    return rgba


# ─────────────────────────── Лес ───────────────────────────

def tree(size=256, seed=5, branches=13, spread=0.92):
    """Крона сверху: почти чёрный силуэт с читаемой структурой ветвей."""
    rng = np.random.default_rng(seed)
    layer = Layer(size, ss=3)
    half = size * 0.5

    # Ствол — короткий толстый отросток от центра.
    trunk = rgb("trunk")
    layer.capsule(0.0, 0.0, 0.0, half * 0.16, half * 0.10, trunk)

    # Ветви: расходящиеся сужающиеся линии.
    tips = []
    for i in range(branches):
        angle = (i / branches) * 2.0 * math.pi + rng.random() * 0.32
        length = half * spread * (0.55 + 0.45 * rng.random())
        mid_angle = angle + (rng.random() - 0.5) * 0.5
        mx = math.cos(mid_angle) * length * 0.5
        my = math.sin(mid_angle) * length * 0.5
        ex = math.cos(angle) * length
        ey = math.sin(angle) * length
        layer.capsule(0.0, 0.0, mx, my, half * 0.045, trunk)
        layer.capsule(mx, my, ex, ey, half * 0.030, trunk)
        tips.append((ex, ey))

        # Развилки второго порядка.
        for _ in range(2):
            fork_angle = angle + (rng.random() - 0.5) * 0.9
            fl = length * (0.35 + 0.3 * rng.random())
            layer.capsule(mx, my,
                          mx + math.cos(fork_angle) * fl,
                          my + math.sin(fork_angle) * fl,
                          half * 0.018, trunk)

    # Листва: множество мелких пятен вдоль ветвей, плотнее к центру.
    canopy = rgb("canopy")
    for _ in range(240):
        angle = rng.random() * 2.0 * math.pi
        radius = half * spread * (rng.random() ** 0.55)
        cx = math.cos(angle) * radius
        cy = math.sin(angle) * radius
        leaf_r = half * (0.05 + 0.075 * rng.random())
        layer.ellipse(cx, cy, leaf_r, leaf_r * 0.85,
                      jitter(canopy, rng, 0.18))

    rgba = layer.downscale()

    # Рваный край: модулируем альфу шумом, иначе крона — ровный круг.
    edge_noise = fbm(size, base_res=8, octaves=4, seed=seed + 300)
    radial = np.hypot(*np.meshgrid(np.arange(size) - half, np.arange(size) - half))
    radial = radial / half
    falloff = np.clip(1.15 - radial / spread, 0.0, 1.0)
    falloff = falloff * (0.55 + 0.85 * edge_noise)
    rgba[..., 3] = np.clip(rgba[..., 3] * falloff, 0.0, 1.0)

    tex = fbm(size, base_res=10, octaves=4, seed=seed + 61)
    return shade(
        rgba,
        rim_strength=0.30,
        rim_color=np.array(rgb("canopy_lit"), dtype=np.float32),
        ambient=0.66,
        direct=0.60,
        texture=tex,
        texture_strength=0.22,
        rim_width=3,
    )


def bush(size=128, seed=17):
    """Куст сверху: плотное тёмное пятно с рваным краем."""
    rng = np.random.default_rng(seed)
    layer = Layer(size, ss=3)
    half = size * 0.5
    canopy = rgb("canopy")
    for _ in range(70):
        angle = rng.random() * 2.0 * math.pi
        radius = half * 0.72 * (rng.random() ** 0.6)
        cx = math.cos(angle) * radius
        cy = math.sin(angle) * radius
        leaf_r = half * (0.10 + 0.11 * rng.random())
        layer.ellipse(cx, cy, leaf_r, leaf_r * 0.88, jitter(canopy, rng, 0.2))

    rgba = layer.downscale()
    edge_noise = fbm(size, base_res=8, octaves=4, seed=seed + 300)
    radial = np.hypot(*np.meshgrid(np.arange(size) - half, np.arange(size) - half)) / half
    falloff = np.clip(1.15 - radial / 0.78, 0.0, 1.0)
    falloff = falloff * (0.5 + 0.9 * edge_noise)
    rgba[..., 3] = np.clip(rgba[..., 3] * falloff, 0.0, 1.0)

    tex = fbm(size, base_res=8, octaves=3, seed=seed + 61)
    return shade(
        rgba,
        rim_strength=0.26,
        rim_color=np.array(rgb("canopy_lit"), dtype=np.float32),
        ambient=0.62,
        direct=0.62,
        texture=tex,
        texture_strength=0.24,
        rim_width=2,
    )


# ─────────────────────────── Туман ───────────────────────────

def fog(size=256, seed=91):
    """Бесшовный слой тумана: мягкие клочья, низкий контраст."""
    a = fbm(size, base_res=2, octaves=6, seed=seed)
    b = fbm(size, base_res=5, octaves=5, seed=seed + 500)
    density = np.clip((a * 0.65 + b * 0.35 - 0.35) * 1.9, 0.0, 1.0)
    density = density ** 1.4

    color = np.array(mix(rgb("night"), rgb("cold_shadow"), 0.55), dtype=np.float32)
    rgb_layer = np.repeat(color[None, None, :], size, axis=0)
    rgb_layer = np.repeat(rgb_layer, size, axis=1)

    return np.dstack([rgb_layer, density.astype(np.float32)])


# ─────────────────────────── Запись ───────────────────────────

def write(rgba, path):
    size = save(rgba, path)
    print("  ", os.path.basename(path), size)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)

    write(ground(256, seed=11), os.path.join(out_dir, "Ground_Forest.png"))
    write(interior_floor(256, 8, seed=23), os.path.join(out_dir, "Floor_Interior.png"))
    write(wall(128, 64, seed=31), os.path.join(out_dir, "Wall_Plank.png"))
    write(window(256, 64, seed=37), os.path.join(out_dir, "Wall_Window.png"))
    write(tree(256, seed=5, branches=13), os.path.join(out_dir, "Tree_A.png"))
    write(tree(256, seed=8, branches=10, spread=0.86), os.path.join(out_dir, "Tree_B.png"))
    write(tree(192, seed=12, branches=15, spread=0.95), os.path.join(out_dir, "Tree_C.png"))
    write(bush(128, seed=17), os.path.join(out_dir, "Bush_A.png"))
    write(bush(128, seed=29), os.path.join(out_dir, "Bush_B.png"))
    write(fog(256, seed=91), os.path.join(out_dir, "Fog_Noise.png"))


if __name__ == "__main__":
    main()
