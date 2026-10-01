"""Общие примитивы для процедурной генерации арта Woodberry.

Всё считается в float-массивах numpy 0..1 и рисуется с суперсэмплингом,
поэтому края получаются гладкими, а тени — мягкими. Палитра подобрана
по референсам Darkwood и Scorched Sun: холодный, обесцвеченный, тёмный.
"""

import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

# ─────────────────────────── Палитра ───────────────────────────
# Значения сняты с референсных скриншотов (тёмный верхний свет, тёплые пятна
# света на холодной базе). Держим их в одном месте, чтобы арт был согласован.

PALETTE = {
    "void":        (0x05, 0x07, 0x0A),
    "night":       (0x0B, 0x0F, 0x14),
    "cold_shadow": (0x14, 0x1A, 0x22),
    "rim_cool":    (0x7D, 0x93, 0xAD),
    "lamp_warm":   (0xFF, 0xB3, 0x5C),

    # Персонаж. Части намеренно разведены по светлоте: сверху всё сливается
    # в одно пятно, если голова, плечи и корпус одного тона.
    "coat":        (0x34, 0x37, 0x2C),
    "coat_light":  (0x44, 0x48, 0x3A),   # плечевой пояс ловит свет
    "coat_dark":   (0x1E, 0x20, 0x19),   # полы пальто
    "sleeve":      (0x26, 0x29, 0x21),
    "glove":       (0x16, 0x13, 0x0F),
    "trouser":     (0x24, 0x22, 0x1E),
    "boot":        (0x0E, 0x0C, 0x0B),
    "hood":        (0x24, 0x26, 0x1E),   # капюшон темнее корпуса, но не в ноль
    "hood_lit":    (0x41, 0x45, 0x36),   # блик на куполе сверху-слева
    "skin":        (0xA8, 0x90, 0x78),
    "outline":     (0x0B, 0x0D, 0x0E),

    # Окружение. Значения сняты с референсов: холодно, темно, почти без
    # насыщенности. Яркий тёплый пол и зелёный мох в кадр не попадают —
    # сцена читается как схема при слабом свете, а не как фотография.
    "dirt":        (0x22, 0x1F, 0x1A),
    "dirt_lit":    (0x33, 0x2E, 0x27),
    "moss":        (0x1E, 0x29, 0x1B),
    "moss_lit":    (0x2A, 0x38, 0x21),
    "leaf":        (0x38, 0x2E, 0x20),
    "wood_dark":   (0x1A, 0x19, 0x18),
    "wood_mid":    (0x2B, 0x2A, 0x27),
    "wood_lit":    (0x3E, 0x3C, 0x36),
    "plaster":     (0x2A, 0x29, 0x26),
    "plaster_lit": (0x3C, 0x3A, 0x34),
    "trunk":       (0x14, 0x11, 0x0E),
    "canopy":      (0x0E, 0x13, 0x0C),
    "canopy_lit":  (0x22, 0x2B, 0x1C),
    "glass":       (0x2A, 0x36, 0x42),
    "glass_lit":   (0x5A, 0x74, 0x88),
    "metal":       (0x2E, 0x2E, 0x30),
    "metal_lit":   (0x4A, 0x4A, 0x4E),
}


def rgb(name):
    """Цвет палитры как float-кортеж 0..1."""
    value = PALETTE[name]
    return (value[0] / 255.0, value[1] / 255.0, value[2] / 255.0)


def mix(a, b, t):
    """Линейная интерполяция двух цветов."""
    return tuple(a[i] * (1.0 - t) + b[i] * t for i in range(3))


def jitter(color, rng, amount=0.06):
    """Небольшой случайный сдвиг яркости — против «пластиковой» заливки."""
    k = 1.0 + (rng.random() - 0.5) * 2.0 * amount
    return tuple(min(1.0, max(0.0, c * k)) for c in color)


# ─────────────────────────── Шум ───────────────────────────

def _upsample_wrap(grid, size):
    """Билинейный апсемпл с заворотом по краям — даёт бесшовную текстуру."""
    res = grid.shape[0]
    t = (np.arange(size, dtype=np.float32) + 0.5) * (res / float(size)) - 0.5
    i0 = np.floor(t).astype(np.int64)
    frac = (t - i0).astype(np.float32)
    i0m = i0 % res
    i1m = (i0 + 1) % res

    rows = grid[i0m, :] * (1.0 - frac)[:, None] + grid[i1m, :] * frac[:, None]
    out = rows[:, i0m] * (1.0 - frac)[None, :] + rows[:, i1m] * frac[None, :]
    return out


def fbm(size, base_res=4, octaves=5, seed=0, persistence=0.5):
    """Фрактальный шум в [0,1], бесшовный по обеим осям."""
    rng = np.random.default_rng(seed)
    total = np.zeros((size, size), dtype=np.float32)
    amplitude = 1.0
    norm = 0.0
    res = base_res

    for _ in range(octaves):
        grid = rng.random((res, res)).astype(np.float32)
        total += _upsample_wrap(grid, size) * amplitude
        norm += amplitude
        amplitude *= persistence
        res *= 2

    total /= max(norm, 1e-6)
    lo, hi = float(total.min()), float(total.max())
    return (total - lo) / max(hi - lo, 1e-6)


def ridge(size, seed=0, **kwargs):
    """Гребневый шум — для веток, трещин и прожилок."""
    value = fbm(size, seed=seed, **kwargs)
    return 1.0 - np.abs(value * 2.0 - 1.0)


# ─────────────────────────── Растровая работа ───────────────────────────

class Layer:
    """RGBA-слой в float 0..1 с рисованием через PIL в суперсэмпле."""

    def __init__(self, size, ss=4, background=(0.0, 0.0, 0.0, 0.0)):
        self.size = size
        self.ss = ss
        self.px = size * ss
        self._img = Image.new("RGBA", (self.px, self.px), tuple(int(c * 255) for c in background))
        self._draw = ImageDraw.Draw(self._img)

    # координаты задаются в единицах финального размера, начало — центр
    def _pt(self, x, y):
        return (self.px * 0.5 + x * self.ss, self.px * 0.5 + y * self.ss)

    def _scaled(self, value):
        return value * self.ss

    def _paint(self, rasterize, color, alpha):
        """Заливка формы с честным смешиванием.

        `ImageDraw` пишет пиксели RGBA НАПРЯМУЮ, а не накладывает их.
        Поэтому заливка с alpha < 1 не ослабляет цвет, а заменяет им то, что
        лежало ниже, — вплоть до полного стирания при alpha = 0. Полупрозрачные
        детали (планки на ящике, кромка стола) из-за этого прорезали в фигуре
        дыры. Накладываем через отдельный слой и `alpha_composite`, и только
        когда прозрачность действительно нужна: полностью непрозрачные формы
        рисуются напрямую, иначе деревья с сотнями листьев считались бы вечно.
        """
        if alpha >= 1.0:
            rasterize(self._draw, self._rgba(color, 1.0))
            return

        scratch = Image.new("RGBA", (self.px, self.px), (0, 0, 0, 0))
        rasterize(ImageDraw.Draw(scratch), self._rgba(color, alpha))
        self._img = Image.alpha_composite(self._img, scratch)
        self._draw = ImageDraw.Draw(self._img)

    def ellipse(self, cx, cy, rx, ry, color, alpha=1.0, inflate=0.0):
        x0, y0 = self._pt(cx - rx - inflate, cy - ry - inflate)
        x1, y1 = self._pt(cx + rx + inflate, cy + ry + inflate)
        box = [x0, y0, x1, y1]
        self._paint(lambda d, f: d.ellipse(box, fill=f), color, alpha)

    def rect(self, cx, cy, hw, hh, color, alpha=1.0, radius=0.0, inflate=0.0):
        x0, y0 = self._pt(cx - hw - inflate, cy - hh - inflate)
        x1, y1 = self._pt(cx + hw + inflate, cy + hh + inflate)
        box = [x0, y0, x1, y1]
        rounded = self._scaled(max(radius, inflate))
        if radius > 0 or inflate > 0:
            self._paint(lambda d, f: d.rounded_rectangle(box, radius=rounded, fill=f),
                        color, alpha)
        else:
            self._paint(lambda d, f: d.rectangle(box, fill=f), color, alpha)

    def polygon(self, points, color, alpha=1.0, inflate=0.0):
        if inflate > 0:
            cx = sum(p[0] for p in points) / len(points)
            cy = sum(p[1] for p in points) / len(points)
            grown = []
            for x, y in points:
                dx, dy = x - cx, y - cy
                length = math.hypot(dx, dy)
                if length < 1e-6:
                    grown.append((x, y))
                else:
                    grown.append((x + dx / length * inflate, y + dy / length * inflate))
            points = grown

        pts = [self._pt(x, y) for x, y in points]
        self._paint(lambda d, f: d.polygon(pts, fill=f), color, alpha)

    def capsule(self, x0, y0, x1, y1, r, color, alpha=1.0, inflate=0.0):
        """Толстая линия с круглыми концами — базовая форма конечностей."""
        p0 = self._pt(x0, y0)
        p1 = self._pt(x1, y1)
        radius = self._scaled(r + inflate)

        def rasterize(d, f):
            d.line([p0, p1], fill=f, width=int(round(radius * 2.0)))
            for point in (p0, p1):
                d.ellipse(
                    [point[0] - radius, point[1] - radius,
                     point[0] + radius, point[1] + radius],
                    fill=f)

        self._paint(rasterize, color, alpha)

    def _rgba(self, color, alpha):
        return (
            int(min(1.0, max(0.0, color[0])) * 255),
            int(min(1.0, max(0.0, color[1])) * 255),
            int(min(1.0, max(0.0, color[2])) * 255),
            int(min(1.0, max(0.0, alpha)) * 255),
        )

    def array(self):
        return np.asarray(self._img, dtype=np.float32) / 255.0

    def downscale(self):
        """Уменьшает до финального размера — здесь и происходит сглаживание."""
        img = self._img.resize((self.size, self.size), Image.LANCZOS)
        return np.asarray(img, dtype=np.float32) / 255.0


# ─────────────────────────── Свет и контур ───────────────────────────

def _blur(mask, radius):
    if radius <= 0:
        return mask
    img = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8), "L")
    img = img.filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img, dtype=np.float32) / 255.0


def _shift(mask, dx, dy):
    return np.roll(np.roll(mask, dy, axis=0), dx, axis=1)


def _erode(mask, radius):
    if radius <= 0:
        return mask
    img = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8), "L")
    img = img.filter(ImageFilter.MinFilter(int(radius) * 2 + 1))
    return np.asarray(img, dtype=np.float32) / 255.0


def shade(rgba, rim_strength=0.45, rim_color=None, ambient=0.80, direct=0.42,
          gradient=0.46, texture=None, texture_strength=0.10,
          rim_width=2, light_axis=(0.55, 0.45)):
    """Объёмная заливка плоского силуэта.

    Приём намеренно простой и предсказуемый: направленный градиент по
    габаритам силуэта плюс подсветка узкой кромки только со стороны
    источника. Раньше здесь был размытый «сдвинутый силуэт», и на плотной
    фигуре он заливал светом весь корпус — фигура превращалась в блоб.

    Контур здесь НЕ рисуется: он делается двумя проходами отрисовки
    (сначала форма с запасом в цвете обводки, потом заливка). Так кайма
    получается ровной по всему периметру, а не съедается эрозией.
    """
    alpha = rgba[..., 3]
    base = rgba[..., :3]
    height, width = alpha.shape

    if rim_color is None:
        rim_color = np.array(rgb("rim_cool"), dtype=np.float32)

    mask = alpha > 0.02
    if not mask.any():
        return rgba

    rows = np.where(mask.any(axis=1))[0]
    cols = np.where(mask.any(axis=0))[0]
    y0, y1 = int(rows[0]), int(rows[-1])
    x0, x1 = int(cols[0]), int(cols[-1])

    ys, xs = np.mgrid[0:height, 0:width].astype(np.float32)
    gx = np.clip((xs - x0) / max(x1 - x0, 1), 0.0, 1.0)
    gy = np.clip((ys - y0) / max(y1 - y0, 1), 0.0, 1.0)

    # 1.0 в левом верхнем углу габаритов, 0.0 в правом нижнем.
    lit = 1.0 - np.clip(light_axis[0] * gx + light_axis[1] * gy, 0.0, 1.0)

    mult = ambient + direct * lit

    out = base * mult[..., None]

    # Узкая кромка: только у самого края и только со стороны источника.
    if rim_strength > 0 and rim_width > 0:
        inner = _erode(alpha, rim_width)
        rim = np.clip(alpha - inner, 0.0, 1.0)
        rim = _blur(rim, 0.7)
        facing = np.clip(1.35 - 1.9 * (light_axis[0] * gx + light_axis[1] * gy), 0.0, 1.0)
        out = out + rim_color[None, None, :] * (rim * facing * rim_strength)[..., None]

    if texture is not None and texture_strength > 0:
        tex = 1.0 + (texture - 0.5) * 2.0 * texture_strength
        out = out * tex[..., None]

    out = np.clip(out, 0.0, 1.0)
    return np.clip(np.dstack([out, alpha]), 0.0, 1.0)


def to_image(rgba):
    """float RGBA 0..1 -> PIL Image (RGB, альфа сохранена)."""
    data = (np.clip(rgba, 0, 1) * 255).astype(np.uint8)
    return Image.fromarray(data, "RGBA")


def save(rgba, path, ss=1):
    """Сохраняет слой, при необходимости уменьшая с суперсэмплингом."""
    img = to_image(rgba)
    if ss > 1:
        img = img.resize((img.width // ss, img.height // ss), Image.LANCZOS)
    img.save(path, "PNG")
    return img.size


def soft_shadow(size, rx, ry, cx=0.0, cy=0.0, strength=0.55, blur=8.0):
    """Мягкая тень-эллипс как отдельный RGBA-слой."""
    layer = Layer(size, ss=2)
    layer.ellipse(cx, cy, rx, ry, (0.0, 0.0, 0.0), 1.0)
    rgba = layer.downscale()
    a = _blur(rgba[..., 3], blur)
    rgba[..., 3] = np.clip(a * strength, 0.0, 1.0)
    rgba[..., :3] = 0.0
    return rgba


def polar_to_cartesian(angle_deg, length):
    """Угол в градусах, 0 = вниз по изображению (+Y), по часовой стрелке."""
    rad = math.radians(angle_deg)
    return math.sin(rad) * length, math.cos(rad) * length
