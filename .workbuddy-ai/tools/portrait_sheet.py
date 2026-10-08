"""Контактный лист портретов — сетка для оценки набора целиком.

Раскладывает готовые PNG из папки в одну страницу. Нужен, чтобы смотреть
не по одному файлу, а сразу видеть разброс: разные ли лица, нет ли
повторяющихся или бракованных.
"""

from __future__ import annotations

import argparse
import os

import numpy as np
from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True)
    ap.add_argument("--pattern", default="Portrait_08_")
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--cell", type=int, default=256)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    files = sorted(
        os.path.join(args.dir, f)
        for f in os.listdir(args.dir)
        if f.startswith(args.pattern) and f.endswith(".png")
    )
    if not files:
        raise SystemExit("нет файлов по шаблону " + args.pattern)

    cell = args.cell
    cols = args.cols
    rows = (len(files) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * cell), (8, 9, 11, 255))

    for i, path in enumerate(files):
        img = Image.open(path).convert("RGBA").resize((cell, cell), Image.LANCZOS)
        sheet.paste(img, ((i % cols) * cell, (i // cols) * cell), img)

    sheet.save(args.out)
    print("sheet:", args.out, sheet.size, len(files), "портретов")


if __name__ == "__main__":
    main()
