# -*- coding: utf-8 -*-
"""Переносит готовый арт со стенда в папки-пулы проекта.

ЗАЧЕМ СКРИПТ, А НЕ РУЧНОЙ ПЕРЕНОС. Кадров под две сотни, и у каждого
имя на стенде (`p_Roles_Priest_b_ink.png`) обязано превратиться в имя
проекта (`Priest_02_Ink.png`) по ПРАВИЛУ, а не по памяти. Правило
написано в `F:\\ai\\portraits\\roster.py` (см. docstring
`_character_entries`), и повторять его руками — значит однажды положить
кадр не в ту папку и заметить это через месяц.

Что делает:
  1. читает список id из файла (по умолчанию `_run.txt`);
  2. для каждого id строит путь назначения в `Assets/Woodberry/Art/Dialogue/`;
  3. проверяет, что исходник существует и соответствует стандарту
     (персонаж 480x720 RGBA, фон 856x480 без прозрачности);
  4. копирует (не перемещает: стенд остаётся рабочим);
  5. печатает отчёт и ВЫХОДИТ С ОШИБКОЙ, если хоть один кадр не прошёл.

Запуск:
    python copy_dialogue_art.py                 # только проверить, не копируя
    python copy_dialogue_art.py --write
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys

from PIL import Image

STAND = r"F:\ai\portraits\out"
STYLE = "ink"
PROJECT = r"F:\Unity Projects\Woodberry"
DST_ROOT = os.path.join(PROJECT, "Assets", "Woodberry", "Art", "Dialogue")

# Группа в id -> путь под папкой Backgrounds/. У персонажей группа совпадает
# с именем папки и в переводе не нуждается.
BG_GROUP_PATH = {
    "City": "City",
    "VillageInteriors": "Village/Interiors",
    "VillageExteriors": "Village/Exteriors",
    "VillageWilds": "Village/Wilds",
}

PORTRAIT_SIZE = (480, 720)
BACKGROUND_SIZE = (856, 480)


def destination(asset_id: str) -> tuple[str, str]:
    """Путь назначения и роль («character» / «background») для id стенда."""
    if asset_id.startswith("p_"):
        _, group, pool, letter = asset_id.split("_")
        # БУКВА — НОМЕР КАДРА В ПУЛЕ, И ЕЁ МОЖЕТ БЫТЬ СКОЛЬКО УГОДНО.
        # Раньше здесь стояло `"01" if letter == "a" else "02"`, и третьего
        # кадра роль физически не могла получить: c, d и e тоже стали бы
        # _02 и затёрли бы друг друга. Теперь a→01, b→02, c→03, ... Пулы,
        # где кадр уже был, заказывают не с `a`, и тогда первый полученный
        # кадр обязан стать своим номером, а не _01.
        if len(letter) != 1 or not letter.isalpha():
            raise ValueError(f"неизвестная буква варианта: {asset_id}")
        index = ord(letter.lower()) - ord("a") + 1
        if not 1 <= index <= 99:
            raise ValueError(f"вариант вне диапазона 01..99: {asset_id}")
        number = f"{index:02d}"
        name = f"{pool}_{number}_{STYLE.capitalize()}.png"
        return os.path.join(DST_ROOT, "Portraits", group, pool, name), "character"

    if asset_id.startswith("bg_"):
        _, group, place = asset_id.split("_")
        name = f"{place}_01_{STYLE.capitalize()}.png"
        return (os.path.join(DST_ROOT, "Backgrounds", BG_GROUP_PATH[group],
                             place, name), "background")

    raise ValueError(f"неизвестный префикс id: {asset_id}")


def check(path: str, role: str) -> list[str]:
    problems = []
    try:
        im = Image.open(path)
    except Exception as e:
        return [f"не читается ({e})"]
    want = PORTRAIT_SIZE if role == "character" else BACKGROUND_SIZE
    if im.size != want:
        problems.append(f"размер {im.size[0]}x{im.size[1]} вместо "
                        f"{want[0]}x{want[1]}")
    if role == "character":
        if im.mode != "RGBA":
            problems.append(f"режим {im.mode}, ожидается RGBA")
        else:
            a = im.getchannel("A")
            w, h = im.size
            corners = [a.getpixel((2, 2)), a.getpixel((w - 3, 2)),
                       a.getpixel((2, h - 3)), a.getpixel((w - 3, h - 3))]
            if max(corners) > 10:
                problems.append(f"углы не прозрачны (альфа {corners})")
            bottom = [a.getpixel((x, h - 3)) for x in range(0, w, 4)]
            if max(bottom) < 10:
                problems.append("силуэт не доходит до нижнего края")
    else:
        if im.mode == "RGBA" and im.getchannel("A").getextrema()[0] < 250:
            problems.append("фон полупрозрачный")
    return problems


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ids", default="_run.txt",
                    help="файл со списком id через пробел")
    ap.add_argument("--write", action="store_true",
                    help="действительно копировать (без него — только проверка)")
    args = ap.parse_args()

    with open(args.ids, encoding="utf-8") as f:
        ids = f.read().split()

    missing, bad, copied, skipped = [], [], 0, 0
    for asset_id in ids:
        src = os.path.join(STAND, f"{asset_id}_{STYLE}.png")
        dst, role = destination(asset_id)
        if not os.path.isfile(src):
            missing.append(asset_id)
            continue
        problems = check(src, role)
        if problems:
            bad.append((asset_id, problems))
            continue
        if args.write:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            if os.path.isfile(dst):
                skipped += 1
            else:
                shutil.copy2(src, dst)
                copied += 1

    print(f"всего id:        {len(ids)}")
    print(f"нет на стенде:   {len(missing)}")
    print(f"не по стандарту: {len(bad)}")
    mode = "скопировано" if args.write else "готово к копированию"
    print(f"{mode}:  {copied}  (уже было: {skipped})")

    if missing:
        print("\nНЕТ НА СТЕНДЕ:")
        for m in missing:
            print("   ", m)
    if bad:
        print("\nНЕ ПРОШЛИ ПРОВЕРКУ:")
        for asset_id, problems in bad:
            print(f"    {asset_id}: {'; '.join(problems)}")

    # Ненулевой код — чтобы неудачный прогон нельзя было принять за успех.
    return 1 if (missing or bad) else 0


if __name__ == "__main__":
    sys.exit(main())
