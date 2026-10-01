"""Собирает превью спрайт-листов на нейтральном фоне.

Прозрачный PNG в просмотрщике не читается: тёмная фигура на чёрном фоне
сливается. Кладём кадры на серую подложку и раскладываем в сетку, чтобы
кадр не вытягивался в длинную полосу и детали были видны.
"""

import os
import sys

import numpy as np
from PIL import Image

NEUTRAL = (0x6E, 0x6E, 0x72)


def load(path):
    img = Image.open(path).convert("RGBA")
    return np.asarray(img, dtype=np.float32) / 255.0


def flatten(rgba, bg=NEUTRAL):
    a = rgba[..., 3:4]
    return rgba[..., :3] * a + np.array(bg, dtype=np.float32) / 255.0 * (1.0 - a)


def grid(panels, columns, pad=4, bg=NEUTRAL):
    rows = []
    for start in range(0, len(panels), columns):
        chunk = panels[start:start + columns]
        height = max(p.shape[0] for p in chunk)
        width = max(p.shape[1] for p in chunk)
        cells = []
        for p in chunk:
            cell = np.full((height, width, 3),
                           np.array(bg, dtype=np.float32) / 255.0, dtype=np.float32)
            cell[:p.shape[0], :p.shape[1]] = p
            cells.append(cell)
            cells.append(np.full((height, pad, 3),
                                 np.array(bg, dtype=np.float32) / 255.0, dtype=np.float32))
        row = np.concatenate(cells, axis=1)
        rows.append(row)
        rows.append(np.full((pad, row.shape[1], 3),
                            np.array(bg, dtype=np.float32) / 255.0, dtype=np.float32))
    return np.concatenate(rows, axis=0)


def main():
    src = sys.argv[1]
    dst = sys.argv[2]
    scale = int(sys.argv[3]) if len(sys.argv) > 3 else 3
    columns = int(sys.argv[4]) if len(sys.argv) > 4 else 4

    panels = []
    for name in ("Player_TopDown_Idle.png", "Player_TopDown_Walk.png"):
        path = os.path.join(src, name)
        if not os.path.exists(path):
            continue
        sheet = load(path)
        frame = sheet.shape[0]
        count = sheet.shape[1] // frame
        for i in range(count):
            panels.append(flatten(sheet[:, i * frame:(i + 1) * frame]))

    if not panels:
        print("нет спрайтов")
        return

    composed = grid(panels, columns)
    img = Image.fromarray((np.clip(composed, 0, 1) * 255).astype(np.uint8), "RGB")
    img = img.resize((img.width * scale, img.height * scale), Image.NEAREST)
    img.save(dst)
    print("preview:", dst, img.size, "кадров:", len(panels))


if __name__ == "__main__":
    main()
