#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""artgen.py — драйвер-мост между окном `Woodberry → Art Studio` и стендом.

ЧТО ЭТО. Долгоживущий процесс на python стенда. Unity пишет ему команды
строками в `ArtStudio/commands.jsonl`, драйвер отвечает событиями в
`ArtStudio/events.jsonl`. Полный контракт — `docs/specs/art-generation.md`;
этот файл обязан ему соответствовать, а не наоборот.

ГДЕ ЛЕЖИТ И ПОЧЕМУ. Код версионируется и лежит рядом с остальным
инструментарием генерации (`.workbuddy-ai/tools/`), а НЕ в рабочей папке
`ArtStudio/` — та под `.gitignore`, и код драйвера потерялся бы при первой
же уборке. Окно принимает `ArtStudio/driver/artgen.py` как совместимый
drop-in, но источник истины — этот файл.

ПОЧЕМУ ОТДЕЛЬНЫЙ ПРОЦЕСС, А НЕ КОД В UNITY. Генерация требует ComfyUI,
torch и PIL из окружения стенда (`F:\\ai\\comfyenv`). Unity не может ни
импортировать их, ни держать GPU-задачу в своём потоке — редактор бы
замерзал. Поэтому Unity только просит, а работу делает драйвер.

ЗАПУСК (обычно его поднимает окно арт-студии):
    python artgen.py                 # рабочий цикл; только новые команды
    python artgen.py --once          # обработать и готовые команды, затем выйти
    python artgen.py --no-server     # не трогать ComfyUI (офлайн-проверка)

Рабочий цикл обслуживает только команды, написанные после его запуска, а
`--once` читает файл с начала: это пакетный режим для офлайн-проверки, где
команды кладут до старта. Почему это важно — в докстроке `CommandTail`.

ЧЕГО ЭТОТ ФАЙЛ НЕ ДЕЛАЕТ: не меняет промпты, сетку осей и модели — это
стенд (`roster.py`, `scene.py`, `shoot.py`). Он только соединяет их с окном.
"""

from __future__ import annotations

import argparse
import base64
import importlib
import json
import os
import random
import re
import shutil
import socket
import struct
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
from datetime import datetime

# Версия протокола. Обязана совпадать с ArtProtocol.Version в Unity:
# расхождение — ошибка конфигурации, её надо показать, а не проглотить.
PROTOCOL = 1

POLL_SECONDS = 0.15

#: Запуск дочернего процесса БЕЗ окна консоли. Unity стартует драйвер скрыто
#: (`CreateNoWindow`), и любой его наследник обязан быть скрыт так же: иначе
#: на экране всплывает чёрное окно консоли, которое нельзя закрыть, не убив
#: генерацию. Флаг ставится на КАЖДЫЙ спавн — наследование здесь не работает,
#: потому что у драйвера собственной консоли нет.
CREATE_NO_WINDOW = 0x08000000

#: Отсоединённый процесс: своя консоль не создаётся вообще, окно не мелькает.
DETACHED_PROCESS = 0x00000008

#: Сколько ждать порт ComfyUI, если процесс по маркеру жив: здоровый сервер
#: поднимает порт за 20–40 с. Ждать нужно, иначе можно убить сервер, который
#: вот-вот ответит.
START_GRACE_SECONDS = 45.0

#: Сервер считается холодным, если генерации не было дольше этого времени:
#: модели вытеснены из VRAM, и прогрев (`shoot.py` без `--no-warmup`) снова
#: стоит ~40 с отдельным кадром.
WARM_WINDOW_SECONDS = 900.0

#: Строка стенда, открывающая прогревочный кадр. Прогрев — не работа над
#: кадром заявки, и его шаги сэмплера нельзя приписывать первому id.
WARMUP_MARK = "--- ПРОГРЕВ"

# ------------------------------------------------------- ЭТАПЫ И МАРКЕРЫ
#
# ЗАЧЕМ. Полосы прогресса в окне стояли не потому, что работа встала, а
# потому что стенд о долгих местах молчал: пакетная вырезка фона берёт всю
# папку raw (100+ файлов) и минуты не сообщает ничего. По часам это
# неотличимо от зависания, и человек ждёт впустую.
#
# Поэтому `shoot.py` печатает построчные маркеры, а драйвер разбирает их и
# отдаёт в Unity. Формат договорённый с `mark()` в `shoot.py`; менять его
# молча нельзя — окно просто перестанет понимать, что происходит.
STAGE_WARMUP = "warmup"   # прогрев моделей (кадра заявки ещё нет)
STAGE_PREPARE = "prepare" # подготовка кадра: очередь, веса, энкодер
STAGE_SAMPLE = "sample"   # сэмплинг, шаги сэмплера из websocket
STAGE_CUT = "cut"         # вырезка фона (seg.py по всей папке raw)
STAGE_CHECK = "check"     # замер покрытия и проверка кадра
STAGE_COPY = "copy"       # приёмка и копирование в проект

#: `@@stage cut 37 118 имя.png` — стенд сообщает, на чём остановился.
STAGE_LINE = re.compile(r"^@@stage\s+(\S+)\s+(\d+)\s+(\d+)\s*(.*)$")

#: `@@frame p_Roles_Priest_b drawn` — кадр отрисован и лежит в raw.
#: Без этого окно не знает, какой кадр стенд рисует сейчас: `item_done`
#: приходит только после проверки, а рисование идёт кадр за кадром.
FRAME_DONE_LINE = re.compile(r"^@@frame\s+(\S+)\s+drawn")

#: Предел журнала ComfyUI до ротации. Сервер печатает много (шаги сэмплера
#: идут прогресс-строкой), а нужен последний прогон, поэтому старый журнал
#: уезжает в `.prev`, а не копится безгранично.
LOG_ROTATE_BYTES = 8 * 1024 * 1024

#: Предел журнала драйвера до ротации. Драйвер пишет в него каждую строку
#: стенда, и за несколько сессий файл перерастает всё остальное в `logs/`.
#: Как и у ComfyUI, нужен последний прогон, а не вся история.
DRIVER_LOG_ROTATE_BYTES = 4 * 1024 * 1024

#: Предел журнала команд до ротации. Команды копятся от сессии к сессии, а
#: драйвер в рабочем режиме читает только написанные ПОСЛЕ старта — значит
#: старые строки мертвы и лишь раздувают файл. Хвост сохраняем: он нужен
#: офлайн-разбору (`--once`).
COMMANDS_ROTATE_BYTES = 2 * 1024 * 1024

#: Строка ComfyUI с итогом кадра. По ней видно настоящую цену кадра на
#: сервере — включая загрузку моделей, которую окно иначе не показывает.
PROMPT_DONE_MARK = "Prompt executed in"

#: Сколько свободной VRAM должно остаться на ТОЛЬКО ЧТО ПОДНЯТОМ сервере.
#:
#: ПОЧЕМУ ИМЕННО В ЭТОТ МОМЕНТ. После первой генерации свободной VRAM
#: законно остаётся ~5.7 ГБ: энкодер (3.6 ГБ), диффузионная модель (6.4 ГБ)
#: и VAE (0.16 ГБ) держатся в памяти намеренно — ровно этого и добивались,
#: сняв `--cache-none`. Проверять порог на прогретом сервере значит ругаться
#: на исправную работу. А вот сразу после старта, до первой генерации, весов
#: в VRAM ещё нет: там обязано быть свободно почти всё. Если это не так —
#: память держит кто-то чужой, и кадры пойдут в разы медленнее.
VRAM_MIN_MB = 12000

#: Флаги запуска ComfyUI. `--cache-none` СОЗНАТЕЛЬНО НЕ ВХОДИТ.
#:
#: ЧТО БЫЛО И ЧТО СТАЛО (замер 2026-10-10, 12 кадров 480x720, 10 шагов).
#: С `--cache-none` сервер выбрасывает веса после КАЖДОГО кадра и грузит
#: 3.6 ГБ энкодера и 6.4 ГБ диффузионной модели заново: в журнале сервера
#: «Requested to load» 15 раз на 5 кадров. Без флага — 3 раза на 3 кадра,
#: модели остаются в VRAM. Цена: 29.8 с на кадр против 12.0 с, то есть
#: **2.5x**, и почти всё это — перезагрузка, а не счёт: кадр с ОДНИМ шагом
#: занимает 26.2 с, с десятью — 28.5 с.
#:
#: ПОЧЕМУ ФЛАГ ВСЁ-ТАКИ МОЖНО СНЯТЬ. `README` стенда требует его от «цветного
#: шума». Замер это не подтвердил: обе серии по 12 кадров дали РОВНО 4 шумных
#: кадра, и это одни и те же персонажи (03_oldman, 07_girl, 10_apprentice,
#: 11_alchemist) — шум зависит от сида, а не от кэша. `shoot.py` такие кадры
#: и так ловит (`is_noise`) и повторяет с другим сидом. Если на другом стенде
#: кэш всё-таки вернёт мусор — вернуть `--cache-none` можно одной строкой в
#: `ArtStudio/config.json`, не трогая код.
COMFY_FLAGS_DEFAULT = [
    "--use-pytorch-cross-attention",
    "--highvram",
    "--disable-async-offload",
]


def spawn_flags(detached: bool = False) -> int:
    """Флаги создания процесса. На Windows — всегда без окна консоли."""
    if os.name != "nt":
        return 0
    return DETACHED_PROCESS if detached else CREATE_NO_WINDOW

# Имя переменной, которой драйвер передаёт стенду путь к файлу доп. пулов.
# Живёт в двух местах: здесь и в `roster.py` (`EXTRA_POOLS_ENV`), потому что
# драйвер обязан выставить её ДО импорта ростера — иначе имя пришлось бы
# брать у модуля, который ещё не импортирован. Менять — только парой.
EXTRA_POOLS_ENV = "PORTRAIT_EXTRA_POOLS"

if not sys.stdout.isatty() and hasattr(sys.stdout, "reconfigure"):
    # Перенаправленный stdout иначе берёт локаль cp1251 и падает на русских
    # сообщениях. Ошибка была бы «тихой»: лог просто обрывался бы.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

LOG_PATH: str | None = None


def log(message: str) -> None:
    stamp = datetime.now().strftime("%H:%M:%S")
    line = f"[{stamp}] {message}"
    print(line, flush=True)
    if LOG_PATH:
        # Unity запускает драйвер без захвата вывода, поэтому журнал идёт в
        # файл — иначе после сбоя не остаётся ни строчки для разбора.
        try:
            with open(LOG_PATH, "a", encoding="utf-8") as f:
                f.write(line + "\n")
        except OSError:
            pass


def strip_ansi(text: str) -> str:
    """Убирает ANSI-раскраску из строки журнала.

    ЗАЧЕМ. ComfyUI красит лог ВСЕГДА (`app/logger.py`, `ColoredFormatter`):
    проверки на терминал нет, `NO_COLOR` не читается. Поэтому в файле рядом с
    числом живёт хвост `\\x1b[0m`, и разбор «сколько секунд» вытаскивал из
    него лишний ноль — время кадра показывалось как 0.0.
    """
    out = []
    i = 0
    while i < len(text):
        if text[i] == "\x1b":
            i += 1
            if i < len(text) and text[i] == "[":
                i += 1
                # Управляющая последовательность кончается байтом 0x40..0x7E.
                while i < len(text) and not ("@" <= text[i] <= "~"):
                    i += 1
                i += 1
            continue
        out.append(text[i])
        i += 1
    return "".join(out)


def rotate_if_large(path: str, limit: int) -> None:
    """Уносит переросший журнал в `<имя>.prev`, освобождая текущий файл.

    ОДИН предшественник, а не история с датами: цель — чтобы каталог `logs`
    не рос безгранично, а не чтобы хранить все прогоны. Разбор всегда идёт
    по последнему, и он же остаётся на месте.

    Молча ничего не делает, когда файла нет или он в норме: уборка не имеет
    права мешать запуску драйвера.
    """
    try:
        if os.path.getsize(path) <= limit:
            return
    except OSError:
        return

    previous = path + ".prev"
    try:
        if os.path.exists(previous):
            os.remove(previous)
        os.replace(path, previous)
    except OSError:
        # Журнал занят другим процессом или недоступен — это не повод падать.
        pass


def find_project_root(start: str) -> str:
    """Поднимается вверх до папки, где лежит `Assets` (корень проекта)."""
    current = os.path.abspath(start)
    for _ in range(6):
        if os.path.isdir(os.path.join(current, "Assets")):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            break
        current = parent
    return os.path.abspath(os.path.join(start, "..", "..", ".."))


class Paths:
    """Пути рабочей папки и проекта. Ни одного абсолютного хардкода."""

    def __init__(self, project_root: str) -> None:
        self.project_root = project_root
        self.root = os.path.join(project_root, "ArtStudio")
        self.config = os.path.join(self.root, "config.json")
        self.commands = os.path.join(self.root, "commands.jsonl")
        self.events = os.path.join(self.root, "events.jsonl")
        self.driver_info = os.path.join(self.root, "driver.json")
        self.feedback = os.path.join(self.root, "feedback.json")
        self.extra_pools = os.path.join(self.root, "extra_pools.json")
        self.logs = os.path.join(self.root, "logs")
        self.review = os.path.join(self.root, "review")
        self.review_replaced = os.path.join(self.review, "replaced")
        self.dialogue = os.path.join(project_root, "Assets", "Woodberry",
                                     "Art", "Dialogue")
        self.tools = os.path.join(project_root, ".workbuddy-ai", "tools")


class Config:
    """Значения из `ArtStudio/config.json` с разумными умолчаниями стенда."""

    def __init__(self, path: str) -> None:
        data = {}
        if os.path.isfile(path):
            try:
                with open(path, encoding="utf-8") as f:
                    data = json.load(f)
            except (OSError, ValueError) as e:
                log(f"config.json не прочитан ({e}); взяты умолчания")

        self.stand_dir = data.get("standDir") or r"F:\ai\portraits"
        self.python = data.get("python") or r"F:\ai\comfyenv\Scripts\python.exe"
        self.comfy_host = data.get("comfyHost") or "127.0.0.1:8188"
        self.style = data.get("style") or "ink"
        # null — свет не переопределяем: у персонажа свой (ch['light']),
        # у фона остаётся аргумент стенда по умолчанию.
        self.light = data.get("light")
        self.comfy_dir = data.get("comfyDir") or r"F:\ai\ComfyUI"
        self.extra_model_paths = data.get("extraModelPaths") or \
            r"F:\ai\models\extra_model_paths.yaml"
        # Флаги запуска ComfyUI. Настраиваемые, потому что цена ошибки здесь
        # измеряется секундами на КАЖДЫЙ кадр (см. `COMFY_FLAGS_DEFAULT`).
        self.comfy_flags = list(data.get("comfyFlags") or COMFY_FLAGS_DEFAULT)
        # Готовый арт стенда. Совпадает с `BASE_OUT/out` в shoot.py
        # (BASE_OUT = env PORTRAIT_OUT или standDir).
        self.out_dir = data.get("outDir") or os.path.join(self.stand_dir, "out")


class EventWriter:
    """Пишет `events.jsonl`: монотонный seq с 1, одна строка = один flush.

    Блокировка нужна, потому что прогресс сэмплера шлёт ОТДЕЛЬНЫЙ поток
    (<see cref="ComfyProgress"/>), а команды — главный. Без неё два `emit`
    могли бы перемешаться в середине строки и `seq` поехал бы назад.
    """

    def __init__(self, path: str) -> None:
        self.path = path
        self.seq = 0
        self._lock = threading.Lock()
        os.makedirs(os.path.dirname(path), exist_ok=True)
        # Свежий прогон: seq начинается с 1, файл не растёт бесконечно.
        #
        # Дескриптор держим ОТКРЫТЫМ. Событий за прогон тысячи (каждая строка
        # стенда плюс шаги сэмплера), и открытие-закрытие файла на каждое из
        # них — лишние системные вызовы в горячем пути, который и так
        # конкурирует с чтением stdout `shoot.py`.
        self._handle = open(self.path, "w", encoding="utf-8")

    def emit(self, kind: str, **fields) -> None:
        with self._lock:
            self.seq += 1
            record = {"seq": self.seq, "kind": kind}
            for key, value in fields.items():
                if value is not None:
                    record[key] = value

            self._handle.write(json.dumps(record, ensure_ascii=False) + "\n")
            # flush обязателен: Unity читает файл по смещению, и без сброса
            # события висели бы в буфере до закрытия процесса.
            self._handle.flush()


class ComfyProgress:
    """Прогресс шагов сэмплера с websocket ComfyUI.

    ЗАЧЕМ ЭТО. Раньше прогресс кадра выводился только из stdout стенда по
    строке `[i/N] <id>`: внутри кадра (30–410 с!) полоса стояла, и прогон
    выглядел зависшим. ComfyUI шлёт шаги сэмплера событием `progress` по
    `/ws` (`{"type":"progress","data":{"value":v,"max":m}}`); слушаем его
    и превращаем в `item_progress`.

    ПОЧЕМУ СВОЙ КЛИЕНТ, А НЕ БИБЛИОТЕКА. `websocket-client`/`websockets` в
    окружении стенда нет, а тянуть зависимость ради чтения текстовых кадров
    незачем. Нужен только один маршрут (`/ws`), только приём и только JSON.

    ПОЧЕМУ ПОТОК. Главный цикл драйвера блокирующе читает stdout `shoot.py`,
    а тот молчит внутри кадра — значит emit прогресса обязан идти из другого
    потока, иначе события некому было бы писать.

    ПОЧЕМУ ID «ТЕКУЩЕГО» КАДРА, А НЕ prompt_id. ComfyUI выполняет задачи
    строго по одной, а стенд генерирует кадры строго последовательно. Значит
    любой `progress` относится к кадру, который стенд печатал последним, —
    сопоставлять prompt_id с id не нужно (и `shoot.py` его всё равно не
    печатает целиком).

    Отказ не ломает генерацию: не поднялся ComfyUI, нет `/ws`, порвалась
    связь — драйвер просто теряет детальный прогресс и живёт как раньше.
    """

    def __init__(self, host: str, events: EventWriter) -> None:
        self.host = host
        self.events = events
        self._current_id: str | None = None
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self._buf = b""
        self._last_emit = 0.0
        self.connected = False
        self.error: str | None = None

    # ------------------------------------------------------------- чужой API

    def start(self) -> None:
        self._stop.clear()
        self.connected = False
        self.error = None
        self._thread = threading.Thread(target=self._run, name="comfy-progress",
                                        daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        thread = self._thread
        if thread is not None:
            thread.join(timeout=4.0)
            self._thread = None

    def set_current(self, stand_id: str | None) -> None:
        """Какой кадр стенд делает сейчас. `progress` приписывается ему."""
        with self._lock:
            self._current_id = stand_id
            self._last_emit = 0.0

    # ------------------------------------------------------------- поток

    def _run(self) -> None:
        """Цикл рукопожатия с повтором.

        ПОЧЕМУ С ПОВТОРОМ. Прогресс открывает соединение в момент старта
        генерации, а ComfyUI поднимает порт 20–40 с. Один заход без повтора
        означал бы, что первый прогон сессии вообще без детального прогресса,
        а после падения сервера связь не восстанавливается до перезапуска
        драйвера. Отказ не ломает генерацию: без соединения драйвер теряет
        только детализацию (см. докстроку класса).
        """
        while not self._stop.is_set():
            sock = None
            try:
                sock = self._connect()
                self._buf = b""
                self.connected = True
                self.error = None
                while not self._stop.is_set():
                    head = self._recv(sock, 2)
                    if head is None:
                        continue
                    opcode = head[0] & 0x0F
                    masked = bool(head[1] & 0x80)
                    length = head[1] & 0x7F
                    if length == 126:
                        length = struct.unpack(">H", self._recv(sock, 2))[0]
                    elif length == 127:
                        length = struct.unpack(">Q", self._recv(sock, 8))[0]
                    mask = self._recv(sock, 4) if masked else b""
                    payload = self._recv(sock, length) if length else b""
                    if mask:
                        payload = bytes(payload[i] ^ mask[i % 4]
                                        for i in range(len(payload)))

                    if opcode == 0x08:  # close
                        break
                    if opcode == 0x09:  # ping -> pong
                        self._send_frame(sock, 0x0A, payload)
                        continue
                    if opcode == 0x01:  # text
                        self._handle_text(payload)
            except Exception as e:  # noqa: BLE001 — прогресс не роняет драйвер
                self.error = str(e)
            finally:
                if sock is not None:
                    try:
                        sock.close()
                    except OSError:
                        pass
                self.connected = False

            # Пауза перед повтором: без неё недоступный сервер крутил бы
            # цикл на скорости диска.
            if not self._stop.is_set():
                self._stop.wait(1.0)

    def _connect(self):
        host, _, port = self.host.partition(":")
        port = int(port or 8188)
        sock = socket.create_connection((host, port), timeout=5.0)
        sock.settimeout(2.0)
        key = base64.b64encode(os.urandom(16)).decode()
        handshake = (
            f"GET /ws HTTP/1.1\r\n"
            f"Host: {host}:{port}\r\n"
            f"Upgrade: websocket\r\n"
            f"Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            f"Sec-WebSocket-Version: 13\r\n\r\n"
        )
        sock.sendall(handshake.encode("ascii"))

        buf = b""
        while b"\r\n\r\n" not in buf:
            chunk = sock.recv(4096)
            if not chunk:
                raise OSError("websocket закрылся на рукопожатии")
            buf += chunk
            if len(buf) > 65536:
                raise OSError("websocket: слишком длинный заголовок ответа")
        head, _, rest = buf.partition(b"\r\n\r\n")
        status = head.split(b"\r\n", 1)[0]
        if b" 101 " not in status:
            raise OSError(f"websocket не подтвердил рукопожатие: {status!r}")
        self._buf = rest
        return sock

    def _recv(self, sock, n: int):
        """Ровно n байт. None — на границе кадра данных нет (проверяем stop)."""
        while len(self._buf) < n:
            try:
                chunk = sock.recv(65536)
            except socket.timeout:
                if not self._buf:
                    return None
                continue  # середина кадра: ждём остаток, не теряем его
            if not chunk:
                raise OSError("websocket закрыт сервером")
            self._buf += chunk
        data = self._buf[:n]
        self._buf = self._buf[n:]
        return data

    @staticmethod
    def _send_frame(sock, opcode: int, payload: bytes) -> None:
        mask = os.urandom(4)
        n = len(payload)
        header = bytes([0x80 | opcode])
        if n < 126:
            header += bytes([0x80 | n])
        elif n < 65536:
            header += bytes([0x80 | 126]) + struct.pack(">H", n)
        else:
            header += bytes([0x80 | 127]) + struct.pack(">Q", n)
        masked = bytes(payload[i] ^ mask[i % 4] for i in range(n))
        sock.sendall(header + mask + masked)

    def _handle_text(self, payload: bytes) -> None:
        try:
            message = json.loads(payload.decode("utf-8"))
        except (ValueError, UnicodeDecodeError):
            return
        if not isinstance(message, dict) or message.get("type") != "progress":
            return
        data = message.get("data") or {}
        value, total = data.get("value"), data.get("max")
        if not isinstance(value, (int, float)) or not isinstance(total, (int, float)) \
                or total <= 0:
            return

        with self._lock:
            stand_id = self._current_id
            now = time.time()
            # Не чаще 3 раз в секунду: шаг сэмплера длится ~1.3 с, а файл
            # событий — это диск, и писать в него на каждый кадр прогресс-бара
            # ComfyUI (десятки раз в секунду на превью) незачем.
            if stand_id is not None and now - self._last_emit < 0.35:
                return
            self._last_emit = now

        if stand_id:
            percent = max(1, min(99, round(value * 100 / total)))
            self.events.emit("item_progress", id=stand_id, percent=percent,
                             stage=STAGE_SAMPLE,
                             text=f"шаг {int(value)}/{int(total)}")


class CommandTail:
    """Дочитывает `commands.jsonl` по смещению, только целые строки.

    Смещение стартует с конца файла: драйвер обслуживает команды, написанные
    ПОСЛЕ его запуска. Иначе новый прогон переигрывает всю историю: `ping`
    прошлых сессий превращаются в пачку `ready`, старый `stop` гасит драйвер
    через секунду после старта, старый `generate` запускает генерацию на
    каждой перекомпиляции, а `approve` пытается перенести уже одобренный
    кадр. Проверено живым прогоном 2026-10-09: 45 команд истории давали 21
    событие `ready` и мгновенную остановку.

    Команда, написанная в момент старта, могла быть дописана лишь наполовину:
    тогда смещение встаёт на начало незавершённой строки, а не за неё.
    """

    def __init__(self, path: str, skip_existing: bool = True) -> None:
        self.path = path
        self.offset = 0

        if skip_existing and os.path.isfile(path):
            self.offset = self._stable_end(path)

    @staticmethod
    def _stable_end(path: str) -> int:
        """Конец последней целой строки в файле."""
        with open(path, "rb") as f:
            f.seek(0, os.SEEK_END)
            size = f.tell()
            if size == 0:
                return 0
            f.seek(size - 1)
            if f.read(1) == b"\n":
                return size
            f.seek(0)
            tail = f.read()

        cut = tail.rfind(b"\n")
        return cut + 1 if cut >= 0 else 0

    def read_new(self) -> list[dict]:
        if not os.path.isfile(self.path):
            return []

        with open(self.path, "rb") as f:
            f.seek(0, os.SEEK_END)
            size = f.tell()
            if size <= self.offset:
                return []
            f.seek(self.offset)
            data = f.read()

        last_newline = data.rfind(b"\n")
        if last_newline < 0:
            # Хвост без перевода строки — команда ещё пишется. Вернёмся к ней.
            return []
        self.offset += last_newline + 1

        commands = []
        for raw in data[:last_newline].decode("utf-8", errors="replace").split("\n"):
            line = raw.strip()
            if not line:
                continue
            try:
                parsed = json.loads(line)
            except ValueError:
                log(f"битая строка команды пропущена: {line[:120]}")
                continue
            if isinstance(parsed, dict) and parsed.get("cmd"):
                commands.append(parsed)
        return commands


class Stand:
    """Обёртка над скриптами стенда: каталог, генерация, проверка."""

    #: `[3/10] p_Roles_Priest_b — Имя` — маркер кадра в stdout стенда.
    FRAME_LINE = re.compile(r"^\[(\d+)\s*/\s*(\d+)\]\s+(\S+)")

    #: Строки шапки прогона стенда: модель, энкодер, VAE, сэмплер, LoRA.
    INFO_PREFIXES = ("Модель:", "LoRA:", "Энкодер:", "VAE:", "Сэмплер:")

    def __init__(self, config: Config, paths: Paths) -> None:
        self.config = config
        self.paths = paths
        self._roster = None
        self._copy_art = None
        self._scene = None

    # ------------------------------------------------------------- каталог

    def _import_roster(self):
        """Модуль стенда. Перечитывается, когда изменился файл доп. пулов.

        Почему не «один раз на процесс»: доп. пулы человек правит в окне
        студии и ждёт эффекта от «Обновить», а не от перезапуска драйвера.
        """
        stamp = self._extra_pools_stamp()
        if self._roster is not None and stamp == self._roster_stamp:
            return self._roster

        if self.config.stand_dir not in sys.path:
            sys.path.insert(0, self.config.stand_dir)
        import roster  # noqa: E402  (путь к стенду добавляется выше)
        if self._roster is not None:
            roster = importlib.reload(roster)
        self._roster = roster
        self._roster_stamp = stamp
        return roster

    def extra_problems(self) -> list[str]:
        """Проблемы файла доп. пулов — их показывает окно, а не журнал."""
        return list(getattr(self._import_roster(), "EXTRA_PROBLEMS", ()))

    def _import_scene(self):
        """Слои промпта стенда. Источник правды для вкладки «Промпты».

        Промпты живут в `scene.py` на стенде и меняются там же, где меняются
        кадры. Копия их в Unity протухла бы молча, поэтому окно спрашивает их
        у драйвера, а драйвер — у стенда.
        """
        if self._scene is not None:
            return self._scene

        if self.config.stand_dir not in sys.path:
            sys.path.insert(0, self.config.stand_dir)
        import scene  # noqa: E402  (путь к стенду добавляется выше)
        self._scene = scene
        return scene

    def prompts(self) -> dict:
        """Промпты стенда, сгруппированные для просмотра.

        ЗАЧЕМ ЭТО В ДРАЙВЕРЕ, А НЕ В UNITY. Промпты — свойство стенда, и
        окно должно показывать ровно то, что уйдёт в генерацию. Своя копия
        в Unity разъехалась бы с ним при первой же правке `scene.py`, и
        человек читал бы не то, чем рисует.

        ГРУППЫ. Стиль, свет, негатив, персонажи, места, параметры генерации —
        ровно те разрезы, по которым промпт собирается в `scene.py`.
        Полный текст освещения у каждого персонажа НЕ дублируется: ключ
        света один на всех, а сам текст лежит в группе «Свет».
        """
        scene = self._import_scene()

        # Стили — СПИСКОМ, а не словарём: в Unity `JsonUtility` не разбирает
        # словари, и стили молча потерялись бы целиком. Ключи сцены заранее
        # известны (`STYLE_INK`, `STYLE_PAINTERLY`), но их набор задан в
        # `scene.STYLES`, а не выдуман драйвером.
        styles = [
            {"key": key, "text": getattr(scene, "STYLE_" + key.upper(), "")}
            for key in scene.STYLES
        ]

        lights = [
            {"key": key, "text": text}
            for key, text in scene.LIGHT.items()
        ]

        characters = [
            {
                "id": ch.get("id", ""),
                "name": ch.get("name", ""),
                "light": ch.get("light", ""),
                "pose": ch.get("pose", ""),
                "subject": ch.get("subject", ""),
            }
            for ch in scene.CHARACTERS
        ]

        backgrounds = [
            {
                "id": bg.get("id", ""),
                "name": bg.get("name", ""),
                "scene": bg.get("scene", ""),
                "light": bg.get("light", ""),
            }
            for bg in scene.BACKGROUNDS
        ]

        gen = dict(scene.GEN)
        chroma = tuple(getattr(scene, "CHROMA_HEX", (128, 128, 128)))
        subject_size = tuple(scene.SUBJECT_SIZE)
        background_size = tuple(scene.BACKGROUND_SIZE)

        return {
            "style": styles,
            "light": lights,
            "negative": {
                "common": scene.NEGATIVE,
                "background": getattr(scene, "NEGATIVE_BG", ""),
            },
            "characters": characters,
            "backgrounds": backgrounds,
            # Ключ `gen`, а не `params`: `params` — зарезервированное слово в
            # C#, и разбор на стороне Unity обрезался бы на нём.
            "gen": {
                "steps": gen.get("steps"),
                "cfg": gen.get("cfg"),
                "scheduler": gen.get("scheduler"),
                "seed_base": gen.get("seed_base"),
                "subject_size": list(subject_size),
                "background_size": list(background_size),
                "chroma": list(chroma),
            },
        }

    def _extra_pools_stamp(self) -> tuple[int, int]:
        """Признак содержимого файла доп. пулов: время и размер.

        Хеша нет намеренно: правка того же размера в ту же наносекунду
        невозможна на практике, а чтение всего файла на каждый опрос — нет.
        """
        try:
            stat = os.stat(self.paths.extra_pools)
            return (stat.st_mtime_ns, stat.st_size)
        except OSError:
            return (0, 0)

    def _import_copy_art(self):
        """Правило «id стенда → путь проекта» живёт в одном месте.

        `copy_dialogue_art.destination()` — источник истины; но у него в
        модуле зашит корень проекта. Подменяем корень и стиль на наши, а
        правило берём ровно то же — иначе однажды пути разъедутся.
        """
        if self._copy_art is not None:
            return self._copy_art
        if self.paths.tools not in sys.path:
            sys.path.insert(0, self.paths.tools)
        import copy_dialogue_art as copy_art
        copy_art.DST_ROOT = self.paths.dialogue
        copy_art.STYLE = self.config.style
        self._copy_art = copy_art
        return copy_art

    def prefix_of(self, stand_id: str) -> str:
        """`p_Roles_Priest_a` → `p_Roles_Priest`; `bg_City_Guild` → как есть."""
        if stand_id.startswith("p_"):
            return stand_id.rsplit("_", 1)[0]
        return stand_id

    def catalog(self) -> list[dict]:
        """Пулы стенда с их кадрами и списком свободных id.

        Пул = префикс id. Свободный id — тот, чей номер в проекте ещё не
        занят: `a` → 01, `b` → 02. Это то же правило, по которому кадр
        получает имя в проекте (`copy_dialogue_art.destination`).
        """
        roster = self._import_roster()
        pools: dict[str, dict] = {}

        for ch in roster.CHARACTERS:
            pool = pools.setdefault(self.prefix_of(ch["id"]),
                                    {"kind": "character", "ids": []})
            pool["ids"].append(ch["id"])

        for bg in roster.BACKGROUNDS:
            pool = pools.setdefault(self.prefix_of(bg["id"]),
                                    {"kind": "background", "ids": []})
            pool["ids"].append(bg["id"])

        catalog = []
        for prefix, pool in pools.items():
            used = self._used_numbers(pool["ids"][0])
            free = [asset_id for asset_id in pool["ids"]
                    if self._number_of(asset_id) not in used]
            catalog.append({
                "pool": prefix,
                "kind": pool["kind"],
                "all": sorted(pool["ids"]),
                "free": sorted(free),
            })
        catalog.sort(key=lambda entry: entry["pool"])
        return catalog

    def _number_of(self, stand_id: str) -> int:
        """Номер проекта для id: `p_..._a` → 1, `bg_...` → 1 (у фона всегда 01)."""
        if stand_id.startswith("p_"):
            letter = stand_id.rsplit("_", 1)[1]
            return ord(letter.lower()) - ord("a") + 1
        return 1

    def _pool_dir(self, stand_id: str) -> str:
        destination, _ = self._import_copy_art().destination(stand_id)
        return os.path.dirname(destination)

    def _used_numbers(self, stand_id: str) -> set[int]:
        pool_dir = self._pool_dir(stand_id)
        used: set[int] = set()
        if not os.path.isdir(pool_dir):
            return used
        for name in os.listdir(pool_dir):
            if not name.lower().endswith(".png"):
                continue
            # <Pool>_<NN>_<Style>.png → номер в предпоследнем сегменте.
            parts = name.rsplit("_", 2)
            if len(parts) >= 3 and parts[1].isdigit():
                used.add(int(parts[1]))
        return used

    def free_ids(self, prefix: str, count: int) -> list[str]:
        for entry in self.catalog():
            if entry["pool"] == prefix:
                return entry["free"][:count]
        return []

    def destination(self, stand_id: str) -> tuple[str, str]:
        return self._import_copy_art().destination(stand_id)

    def validate(self, path: str, role: str) -> list[str]:
        """Тот же стандарт, что и у переноса арта: `copy_dialogue_art.check`."""
        return self._import_copy_art().check(path, role)

    def coverage(self, path: str) -> int:
        """Покрытие кадра непрозрачным силуэтом, 0..100."""
        from PIL import Image
        with Image.open(path) as im:
            if im.mode != "RGBA":
                return 100
            histogram = im.getchannel("A").histogram()
            opaque = sum(histogram[11:])
            total = im.size[0] * im.size[1]
            return round(opaque * 100 / total) if total else 0


class Comfy:
    """Жизненный цикл ComfyUI: поднять, погасить, узнать состояние."""

    def __init__(self, config: Config, paths: Paths,
                 events: EventWriter | None = None) -> None:
        self.config = config
        self.paths = paths
        self.events = events
        self.pid: int | None = None
        # Дескриптор журнала сервера. Ссылку держим обязательно: файл открыт
        # для дочернего процесса, и сборщик мусора, закрыв его, оборвал бы
        # вывод сервера на середине.
        self._log_handle = None

    @property
    def log_path(self) -> str:
        """Журнал ComfyUI. Раньше вывод сервера уходил в `DEVNULL`.

        ПОЧЕМУ ЭТО ВАЖНО. Разбор медленного прогона 2026-10-10 упёрся в
        стену: в `driver.log` видно «кадр 312 с», а почему — ни строчки.
        Всё, что объясняет цену кадра, печатает СЕРВЕР: сколько весов
        реально легло в VRAM (`full load: True/False`), выгружались ли они
        на каждом шаге, с какой скоростью шёл сэмплер (`it/s`). Без этого
        журнала причина неотличима от догадки.
        """
        return os.path.join(self.paths.logs, "comfyui.log")

    def _open_log(self):
        """Открывает журнал сервера на дозапись. None — не удалось."""
        try:
            os.makedirs(self.paths.logs, exist_ok=True)
            path = self.log_path
            if os.path.isfile(path) and os.path.getsize(path) > LOG_ROTATE_BYTES:
                try:
                    os.replace(path, path + ".prev")
                except OSError:
                    pass
            handle = open(path, "a", encoding="utf-8", errors="replace")
            handle.write("===== запуск ComfyUI "
                         f"{datetime.now().isoformat(timespec='seconds')} =====\n")
            handle.flush()
            return handle
        except OSError as e:
            log(f"не открыл журнал ComfyUI ({e}); вывод сервера потеряется")
            return None

    def _close_log(self) -> None:
        if self._log_handle is None:
            return
        try:
            self._log_handle.close()
        except OSError:
            pass
        self._log_handle = None

    def last_prompt_seconds(self) -> float | None:
        """Сколько сервер считал последний кадр (включая загрузку моделей).

        Читается хвост журнала, а не состояние ComfyUI: `/history` отдаёт
        время выполнения, но не отделяет загрузку от сэмплинга, а именно в
        этой разнице и живёт ответ на вопрос «почему долго».
        """
        try:
            with open(self.log_path, "rb") as f:
                f.seek(0, os.SEEK_END)
                size = f.tell()
                f.seek(max(0, size - 64 * 1024))
                tail = f.read().decode("utf-8", errors="replace")
        except OSError:
            return None
        value = None
        for line in tail.splitlines():
            if PROMPT_DONE_MARK not in line:
                continue
            part = strip_ansi(line).split(PROMPT_DONE_MARK)[-1]
            digits = "".join(c if (c.isdigit() or c == ".") else " "
                             for c in part)
            # Берём ПЕРВОЕ число: хвост строки может нести что угодно ещё,
            # а секунды стоят сразу после маркера.
            tokens = digits.split()
            if tokens:
                try:
                    value = float(tokens[0])
                except ValueError:
                    pass
        return value

    def listening_pid(self) -> int | None:
        """pid процесса, слушающего порт ComfyUI. None — не удалось узнать."""
        if os.name != "nt":
            return None
        port = self.config.comfy_host.partition(":")[2] or "8188"
        try:
            raw = subprocess.run(["netstat", "-ano", "-p", "tcp"],
                                 stdout=subprocess.PIPE,
                                 stderr=subprocess.DEVNULL,
                                 creationflags=spawn_flags(),
                                 timeout=15).stdout
        except (OSError, subprocess.SubprocessError):
            return None
        # netstat печатает в OEM-кодировке консоли (cp866 на русской Windows);
        # разбираем только ASCII-поля, поэтому декодируем с заменой.
        for line in raw.decode("cp866", errors="replace").splitlines():
            parts = line.split()
            if len(parts) < 5 or parts[0].upper() != "TCP":
                continue
            if parts[3].upper() != "LISTENING":
                continue
            if parts[1].endswith(":" + port):
                try:
                    return int(parts[4])
                except ValueError:
                    return None
        return None

    def vram_free_mb(self) -> int | None:
        """Свободная VRAM по данным сервера. None — не удалось спросить.

        Порт отвечает — значит сервер жив, но это НЕ значит, что GPU свободен:
        лишний процесс (осиротевший ComfyUI, игра, аппаратный браузер) держит
        память, и кадр считается в разы дольше при формально «готовом»
        стенде. Стенд сам называет этот случай (README: «60+ с/шаг»).
        """
        host = self.config.comfy_host
        if not host.startswith("http"):
            host = "http://" + host
        try:
            with urllib.request.urlopen(f"{host}/system_stats", timeout=5) as r:
                data = json.loads(r.read().decode("utf-8", errors="replace"))
        except (urllib.error.URLError, OSError, ValueError):
            return None
        devices = data.get("devices") or []
        if not devices:
            return None
        free = devices[0].get("vram_free")
        if free is None:
            return None
        try:
            return int(free) // (1024 * 1024)
        except (TypeError, ValueError):
            return None

    @staticmethod
    def _parent_map() -> dict[int, int]:
        """pid -> pid родителя, снимком процессов Windows.

        ПОЧЕМУ НЕ `wmic`: на новых Windows он удалён, и вызов молча отдаёт
        пусто. ПОЧЕМУ НЕ `tasklist`: он не показывает родителя. `CreateToolhelp32`
        — единственный способ без внешнего процесса и без WMI.
        """
        if os.name != "nt":
            return {}
        import ctypes
        from ctypes import wintypes

        TH32CS_SNAPPROCESS = 0x00000002

        class PROCESSENTRY32(ctypes.Structure):
            _fields_ = [
                ("dwSize", wintypes.DWORD),
                ("cntUsage", wintypes.DWORD),
                ("th32ProcessID", wintypes.DWORD),
                ("th32DefaultHeapID", ctypes.c_void_p),
                ("th32ModuleID", wintypes.DWORD),
                ("cntThreads", wintypes.DWORD),
                ("th32ParentProcessID", wintypes.DWORD),
                ("pcPriClassBase", ctypes.c_long),
                ("dwFlags", wintypes.DWORD),
                ("szExeFile", ctypes.c_char * 260),
            ]

        kernel32 = ctypes.windll.kernel32
        snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
        if snapshot == ctypes.c_void_p(-1).value:
            return {}
        result: dict[int, int] = {}
        try:
            entry = PROCESSENTRY32()
            entry.dwSize = ctypes.sizeof(PROCESSENTRY32)
            ok = kernel32.Process32First(snapshot, ctypes.byref(entry))
            while ok:
                result[int(entry.th32ProcessID)] = int(entry.th32ParentProcessID)
                ok = kernel32.Process32Next(snapshot, ctypes.byref(entry))
        finally:
            kernel32.CloseHandle(snapshot)
        return result

    def _is_our_process(self, pid: int) -> bool:
        """`pid` — наш сервер или его потомок.

        ПОЧЕМУ ПОТОМОК, А НЕ РАВЕНСТВО. `Scripts\\python.exe` в venv — это
        лаунчер: он порождает настоящий интерпретатор отдельным процессом.
        `Popen.pid` — лаунчер, а порт слушает ребёнок (проверено 2026-10-10:
        лаунчер 27928, сервер 24128). Сравнение по равенству дало бы ложную
        тревогу на каждом штатном старте.
        """
        if self.pid is None:
            return False
        if pid == self.pid:
            return True
        parents = self._parent_map()
        seen: set[int] = set()
        current = pid
        while current in parents and current not in seen:
            seen.add(current)
            current = parents[current]
            if current == self.pid:
                return True
            if current == 0:
                break
        return False

    def _verify_port_owner(self) -> None:
        """Порт отвечает НАШ сервер, а не чужой ComfyUI.

        ПОЧЕМУ ЭТО НУЖНО. Стенд сам предупреждает (`README`, `run.py`):
        осиротевший сервер держит VRAM, и кадр идёт 60+ с/шаг вместо 1.3.
        Порт при этом ОТВЕЧАЕТ, значит `is_up()` доволен, — но отвечает-то
        чужой процесс, а наш либо не поднялся (порт занят), либо поднялся и
        молча проигрывает драйку за GPU. Снаружи это «просто долго», и без
        этой проверки в журнале нет ни одной зацепки.
        """
        owner = self.listening_pid()
        if owner is None or self.pid is None or self._is_our_process(owner):
            return
        text = (f"порт {self.config.comfy_host} держит процесс {owner}, а наш "
                f"сервер — {self.pid}: два ComfyUI делят GPU, кадр будет "
                f"считаться в разы дольше. Проверь лишние python.exe")
        log("ВНИМАНИЕ: " + text)
        if self.events is not None:
            self.events.emit("log", level="warn", text=text)

    def _verify_free_vram(self) -> None:
        """Память GPU свободна, пока наш сервер ещё ничего не загрузил.

        ЗОВЁТСЯ ТОЛЬКО НА СВЕЖЕМ СТАРТЕ (`start`, ветка запуска). На прогретом
        сервере проверка бессмысленна и вредна: там ~5.7 ГБ свободно ПО ДЕЛУ,
        веса держатся в VRAM. До первой генерации весов нет, поэтому почти вся
        память обязана быть свободной; если это не так — её держит чужой
        процесс, и кадры пойдут в разы медленнее (README стенда: 60+ с/шаг).
        """
        free = self.vram_free_mb()
        if free is None or free >= VRAM_MIN_MB:
            return
        text = (f"на свежем сервере свободно всего {free} МБ VRAM из {VRAM_MIN_MB} "
                f"нужных — память занята чужим процессом (лишний ComfyUI, игра, "
                f"браузер), кадры будут считаться в разы дольше")
        log("ВНИМАНИЕ: " + text)
        if self.events is not None:
            self.events.emit("log", level="warn", text=text)

    def state(self) -> str:
        return "готов" if self.is_up() else "не поднят"

    def is_up(self) -> bool:
        host = self.config.comfy_host
        if not host.startswith("http"):
            host = "http://" + host
        try:
            with urllib.request.urlopen(f"{host}/system_stats", timeout=3):
                return True
        except (urllib.error.URLError, OSError):
            return False

    def find_running_pid(self) -> int | None:
        """pid живого ComfyUI: наш процесс или записанный стендом."""
        if self.pid and self._alive(self.pid):
            return self.pid
        marker = os.path.join(self.config.stand_dir, ".comfy_pid")
        if os.path.isfile(marker):
            try:
                with open(marker, encoding="utf-8") as f:
                    return int(f.read().strip())
            except (OSError, ValueError):
                return None
        return None

    @staticmethod
    def _alive(pid: int) -> bool:
        """Жив ли процесс — БЕЗ `os.kill`.

        ПОЧЕМУ НЕ `os.kill(pid, 0)`. На Windows семантика `os.kill` для
        прочих сигналов — это `TerminateProcess`, и поведение с нулём зависит
        от версии Python. Проверка живости не имеет права убивать того, кого
        проверяет, поэтому на Windows берём `OpenProcess` + `GetExitCodeProcess`:
        маркер `STILL_ACTIVE` и есть ответ на вопрос.
        """
        if pid <= 0:
            return False

        if os.name == "nt":
            import ctypes
            PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
            STILL_ACTIVE = 259
            kernel32 = ctypes.windll.kernel32
            handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,
                                          False, pid)
            if not handle:
                return False
            try:
                code = ctypes.c_ulong()
                ok = kernel32.GetExitCodeProcess(handle, ctypes.byref(code))
                return bool(ok) and code.value == STILL_ACTIVE
            finally:
                kernel32.CloseHandle(handle)

        try:
            os.kill(pid, 0)
            return True
        except OSError:
            return False

    def start(self) -> None:
        if self.is_up():
            self.pid = self.find_running_pid()
            log("ComfyUI уже отвечает — переиспользуем")
            self.publish_pid()
            return

        # Порт молчит, но процесс по маркеру жив. Два случая, и различить их
        # нужно, потому что цена ошибки велика: здоровый сервер поднимает порт
        # за 20–40 с, а осиротевший держит VRAM, и следующий сервер подерётся
        # с ним за GPU — кадр считается 21 с/шаг вместо 1.3 (описано в
        # `run.py`, наблюдалось в логе драйвера 2026-10-10). Поэтому сначала
        # ждём порт, и только потом считаем процесс осиротевшим.
        stale = self.find_running_pid()
        if stale is not None and self._alive(stale):
            log(f"маркер .comfy_pid указывает на живой pid {stale} — жду порт")
            if self._wait_port(START_GRACE_SECONDS):
                self.pid = stale
                log("сервер ответил сам — переиспользуем")
                return
            log(f"pid {stale} не отвечает за {START_GRACE_SECONDS:.0f} с — "
                f"считаю осиротевшим и гашу, иначе он будет драться за GPU")
            self._kill_tree(stale)

        main_py = os.path.join(self.config.comfy_dir, "main.py")
        if not os.path.isfile(main_py):
            raise RuntimeError(f"не найден ComfyUI: {main_py}")

        host, _, port = self.config.comfy_host.partition(":")
        command = [
            self.config.python, main_py,
            *self.config.comfy_flags,
            "--extra-model-paths-config", self.config.extra_model_paths,
            "--listen", host or "127.0.0.1", "--port", port or "8188",
        ]
        log("поднимаю ComfyUI: " + " ".join(command))
        # Вывод сервера — в файл, а не в `DEVNULL`: без него медленный прогон
        # неразбираем (см. `log_path`).
        self._close_log()
        self._log_handle = self._open_log()
        sink = self._log_handle if self._log_handle is not None \
            else subprocess.DEVNULL
        proc = subprocess.Popen(command, cwd=self.config.comfy_dir,
                                stdout=sink, stderr=subprocess.STDOUT,
                                creationflags=spawn_flags(detached=True))
        self.pid = proc.pid
        self.publish_pid()

        for _ in range(120):
            if self.is_up():
                self._verify_port_owner()
                self._verify_free_vram()
                return
            if proc.poll() is not None:
                raise RuntimeError("ComfyUI завершился сразу после запуска")
            time.sleep(2)
        raise RuntimeError("ComfyUI не ответил за 240 с")

    @property
    def marker_path(self) -> str:
        """Маркер живого сервера — тот же файл, что пишет стенд (`run.py`).

        Общий маркер, а не свой: `run.py stop` гасит pid ИЗ ЭТОГО файла, и
        сервер, поднятый драйвером, иначе жил бы с ним врозь — стенд не знал
        бы о нём, а драйвер терял бы его pid при собственном перезапуске.
        """
        return os.path.join(self.config.stand_dir, ".comfy_pid")

    def publish_pid(self) -> None:
        if not self.pid:
            return
        try:
            with open(self.marker_path, "w", encoding="utf-8") as f:
                f.write(str(self.pid))
        except OSError as e:
            log(f"не записал .comfy_pid: {e}")

    def _wait_port(self, timeout: float) -> bool:
        deadline = time.time() + timeout
        while time.time() < deadline:
            if self.is_up():
                return True
            time.sleep(2)
        return False

    def _kill_tree(self, pid: int) -> None:
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                           creationflags=spawn_flags())
        else:
            try:
                os.kill(pid, 15)
            except OSError:
                pass

    def stop(self) -> None:
        pid = self.find_running_pid()
        # Живость проверяем ПЕРЕД снятием: маркер переживает перезапуски Windows,
        # а pid за это время может достаться совсем другому процессу.
        if pid is not None and self._alive(pid):
            self._kill_tree(pid)
        try:
            os.remove(self.marker_path)
        except OSError:
            pass
        self.pid = None
        self._close_log()


class Driver:
    def __init__(self, project_root: str, allow_server: bool = True,
                 replay_history: bool = False) -> None:
        self.paths = Paths(project_root)
        os.makedirs(self.paths.logs, exist_ok=True)
        os.makedirs(self.paths.review, exist_ok=True)
        os.makedirs(self.paths.review_replaced, exist_ok=True)

        self.config = Config(self.paths.config)
        self.events = EventWriter(self.paths.events)
        self.commands = CommandTail(self.paths.commands,
                                    skip_existing=not replay_history)
        self.stand = Stand(self.config, self.paths)
        self.comfy = Comfy(self.config, self.paths, self.events)
        self.allow_server = allow_server

        self.seen_nonces: set[str] = set()
        self.running = True

        # Генерация идёт ОТДЕЛЬНЫМ ПОТОКОМ. Иначе долгий прогон занимал бы
        # главный цикл, и драйвер все минуты молчал бы на `ping`, `catalog` и
        # `stop`: окно видело бы живой процесс, но ни одного ответа, а
        # «остановить» не срабатывал бы вовсе. Заодно это делает возможной
        # отмену — `stop` рвёт дочерний `shoot.py`.
        self._gen_lock = threading.Lock()
        self._generating = False
        self._gen_thread: threading.Thread | None = None
        self._cancel = threading.Event()
        self._shoot_lock = threading.Lock()
        self._shoot_proc: subprocess.Popen | None = None

        # Конец последней генерации. По нему драйвер решает, прогрет ли сервер
        # (модели ещё в VRAM) и можно ли пропустить прогревочный кадр.
        self._last_generation_end: float | None = None

        # Прогресс шагов сэмплера с websocket ComfyUI. Best-effort: не
        # поднялся сервер — драйвер теряет только детализацию прогресса.
        self.progress = ComfyProgress(self.config.comfy_host, self.events)

        # Шапка последнего прогона стенда (модель, энкодер, VAE, сэмплер).
        # Пусто до первого запуска генерации.
        self.model_info: list[str] = []

        # Файл доп. пулов знает стенд, а не драйвер: путь передаётся ему
        # переменной окружения. Ставится ДО первого импорта ростера, а
        # `shoot.py`, запущенный драйвером, наследует окружение — поэтому и
        # генерация видит тот же файл, что и каталог.
        os.environ[EXTRA_POOLS_ENV] = self.paths.extra_pools

        # Снимок файла на момент последнего чтения ростера: по нему драйвер
        # понимает, что человек поправил доп. пулы и их надо перечитать.
        self._roster_stamp = None

        global LOG_PATH
        LOG_PATH = os.path.join(self.paths.logs, "driver.log")

        # Ротация ДО первой записи: иначе новый прогон дописывался бы к
        # журналу, который уже перерос предел, и файл рос бы дальше.
        rotate_if_large(LOG_PATH, DRIVER_LOG_ROTATE_BYTES)
        rotate_if_large(self.paths.commands, COMMANDS_ROTATE_BYTES)

    # ------------------------------------------------------------- запуск

    def write_driver_info(self) -> None:
        info = {
            "pid": os.getpid(),
            "protocol": PROTOCOL,
            "comfyPid": self.comfy.find_running_pid(),
            "started": datetime.now().isoformat(timespec="seconds"),
        }
        with open(self.paths.driver_info, "w", encoding="utf-8") as f:
            json.dump(info, f, ensure_ascii=False, indent=2)
            f.flush()

    def run(self, once: bool = False) -> int:
        log(f"драйвер запущен, протокол {PROTOCOL}, проект {self.paths.project_root}")
        self.write_driver_info()
        self.events.emit("ready", protocol=PROTOCOL, text=f"драйвер {os.getpid()}")
        self.events.emit("server_state", text=self.comfy.state())

        while self.running:
            for command in self.commands.read_new():
                self.dispatch(command)
            if once:
                break
            time.sleep(POLL_SECONDS)

        log("драйвер остановлен")
        return 0

    # ------------------------------------------------------------ команды

    def dispatch(self, command: dict) -> None:
        name = command.get("cmd")
        nonce = command.get("nonce")

        if nonce:
            if nonce in self.seen_nonces:
                log(f"повтор команды {name} (nonce уже применён) — пропуск")
                return
            self.seen_nonces.add(nonce)

        if command.get("protocol") != PROTOCOL:
            self.reject(f"версия протокола {command.get('protocol')} != {PROTOCOL}")

        handlers = {
            "ping": self.cmd_ping,
            "stop": self.cmd_stop,
            "server_start": self.cmd_server_start,
            "server_stop": self.cmd_server_stop,
            "catalog": self.cmd_catalog,
            "prompts": self.cmd_prompts,
            "generate": self.cmd_generate,
            "approve": self.cmd_approve,
            "regen_marked": self.cmd_regen_marked,
        }

        handler = handlers.get(name)
        if handler is None:
            # `reject` драйверу не нужен: брак живёт в `feedback.json`, его
            # пишет окно (см. спеку). Честный отказ лучше ложного ok.
            self.reject(f"команда '{name}' не поддерживается драйвером")
            return

        try:
            handler(command)
        except Exception as e:  # noqa: BLE001 — драйвер не должен падать от команды
            log(f"команда {name} упала: {e}")
            self.events.emit("error", text=f"{name}: {e}")

    def reject(self, text: str) -> None:
        self.events.emit("command_rejected", text=text)

    # ----------------------------------------------------- поток генерации

    def is_generating(self) -> bool:
        with self._gen_lock:
            return self._generating

    def launch(self, work, label: str) -> bool:
        """Запускает `work` в отдельном потоке. false — генерация уже идёт.

        Команды обрабатываются главным циклом по одной, поэтому проверка и
        установка флага неразрывны: второй `generate` получает отказ, а не
        встаёт вторым прогоном поверх первого (проверено замером стенда:
        параллельный прогон даёт 54 с на кадр вместо 28).
        """
        with self._gen_lock:
            if self._generating:
                return False
            self._generating = True
            self._cancel.clear()

        def runner() -> None:
            try:
                work()
            except Exception as e:  # noqa: BLE001 — поток не имеет права упасть молча
                log(f"{label} упала: {e}")
                self.events.emit("error", text=f"{label}: {e}")
            finally:
                self._last_generation_end = time.time()
                with self._gen_lock:
                    self._generating = False
                self._shoot_proc = None
                self.events.emit("log", level="info", text=f"{label}: готово")

        thread = threading.Thread(target=runner, name="generation", daemon=True)
        self._gen_thread = thread
        thread.start()
        return True

    def cancel_generation(self, timeout: float = 3.0) -> None:
        """Рвёт текущую генерацию: снимает `shoot.py` вместе с его детьми.

        ПОЧЕМУ ЭТО ВАЖНО. Раньше `stop` не мог прервать прогон, а окно
        дожимало драйвер силой. Убитый драйвер оставлял живой `shoot.py` с
        загруженной моделью: он продолжал жечь GPU, следующий прогон вставал
        в очередь к нему, и это выглядело как «генерация повисла».
        """
        if not self.is_generating():
            return

        log("отмена генерации")
        self._cancel.set()
        with self._shoot_lock:
            proc = self._shoot_proc
        if proc is not None and proc.poll() is None:
            self.kill_process_tree(proc)

        thread = self._gen_thread
        if thread is not None:
            thread.join(timeout=timeout)

    # ------------------------------------------------------------ процессы

    @staticmethod
    def kill_process_tree(proc: subprocess.Popen) -> None:
        """Снимает процесс ВМЕСТЕ с детьми: у `shoot.py` есть `seg.py`."""
        try:
            if os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"],
                               stdout=subprocess.DEVNULL,
                               stderr=subprocess.DEVNULL,
                               creationflags=spawn_flags())
            else:
                proc.kill()
        except Exception as e:  # noqa: BLE001
            log(f"не снял дочерний процесс: {e}")

    def server_is_warm(self) -> bool:
        """Прогрет ли сервер: генерация шла недавно — модели ещё в VRAM."""
        if self._last_generation_end is None:
            return False
        return time.time() - self._last_generation_end < WARM_WINDOW_SECONDS

    def cmd_ping(self, _command: dict) -> None:
        self.events.emit("ready", protocol=PROTOCOL, text=f"протокол {PROTOCOL}")
        # Свежее состояние ComfyUI в ответ на ping. Окно дочитывает
        # events.jsonl с начала, а драйвер живёт дольше сервера: без
        # освежения окно поднимало бы из истории `server_state: готов`,
        # когда ComfyUI уже погашен, и кнопка «Сервер» гасила бы то,
        # чего нет. `ready` этой дыры не закрывает — он про драйвер.
        self.events.emit("server_state", text=self.comfy.state())

    def cmd_stop(self, _command: dict) -> None:
        # Сначала рвём генерацию: без этого убитый драйвер оставляет живой
        # `shoot.py` с загруженной моделью, и следующий прогон встаёт в очередь
        # к нему — со стороны это выглядит как зависание.
        self.cancel_generation()
        if self.allow_server:
            try:
                self.comfy.stop()
            except Exception as e:  # noqa: BLE001
                log(f"не погасил ComfyUI: {e}")
        self.events.emit("server_state", text="погашен")
        self.running = False

    def cmd_server_start(self, _command: dict) -> None:
        if not self.allow_server:
            self.events.emit("server_state", text=self.comfy.state())
            return
        self.events.emit("server_state", text="поднимается")
        self.comfy.start()
        self.write_driver_info()
        self.events.emit("server_state", text="готов")

    def cmd_server_stop(self, _command: dict) -> None:
        if self.allow_server:
            self.comfy.stop()
        self.events.emit("server_state", text="погашен")

    def cmd_catalog(self, _command: dict) -> None:
        catalog = self.stand.catalog()
        free = sum(len(entry["free"]) for entry in catalog)
        self.events.emit("log", level="info",
                         text=f"каталог: {len(catalog)} пулов, свободных кадров {free}")

        # Проблема в файле доп. пулов — это отказ, который человек должен
        # увидеть: иначе правка файла выглядит как «ничего не произошло».
        for problem in self.stand.extra_problems():
            self.events.emit("error", text=f"доп. пулы: {problem}")

        self.events.emit("catalog", detail=json.dumps(catalog, ensure_ascii=False))

    def cmd_prompts(self, _command: dict) -> None:
        """Отдаёт промпты стенда для вкладки «Промпты» в окне.

        Отдельная команда, а не часть каталога: каталог меняется редко и описывает
        СВОБОДНЫ id, а промпты нужны всегда, когда человек смотрит на кадр, и
        весят заметно больше. Смешивать их — значит тащить полстенда текста при
        каждом обновлении каталога.

        Разбор упавшей не должен выглядеть как пустые промпты: пустота и ошибка
        читаются одинаково, поэтому при сбое уходит `error`, а не `prompts`.
        """
        try:
            prompts = self.stand.prompts()
        except Exception as e:  # noqa: BLE001 — команда не должна ронять драйвер
            self.events.emit("error", text=f"промпты: {e}")
            return

        self.events.emit("prompts", detail=json.dumps(prompts, ensure_ascii=False))

    # ---------------------------------------------------------- генерация

    def cmd_generate(self, command: dict) -> None:
        if self.is_generating():
            # Отказ ДО `item_added`: иначе окно показало бы задания, которых
            # никто не взял в работу.
            self.reject("генерация уже идёт — дождись её окончания")
            return

        pool = command.get("pool")
        count = int(command.get("count") or 1)
        if count < 1:
            count = 1

        ids = self.stand.free_ids(pool, count)
        if not ids:
            # Частый и честный случай: пул уже полон. Не молчим.
            self.reject(f"в пуле {pool} нет свободных кадров")
            return

        style = command.get("style") or self.config.style
        light = command.get("light") or self.config.light

        for stand_id in ids:
            self.events.emit("item_added", id=stand_id, pool=pool,
                             label=f"{pool} — {stand_id.rsplit('_', 1)[-1]}")

        # Без выдуманного `percent=3` для ожидающих кадров. Он был нужен,
        # чтобы полоса не стояла в нуле до старта, но враньём он же: кадр,
        # который ещё не начали рисовать, показывал ~17 % готовности, и
        # общая полоса начиналась не с нуля. Кадр встаёт в работу по первому
        # настоящему событию — окно само сбросит его полосу в ноль.

        def work() -> None:
            try:
                self.generate_frames(ids, style, light)
            except Exception as e:  # noqa: BLE001
                for stand_id in ids:
                    self.events.emit("item_failed", id=stand_id, text=str(e))

        if not self.launch(work, f"генерация {pool}"):
            # Проверка СТОИТ ЗАРАНЕЕ, в самом начале `cmd_generate`:
            # сюда попадаем только если занятость появилась между проверкой
            # и запуском, чего быть не должно.
            self.reject("генерация уже идёт")

    def generate_frames(self, ids: list[str], style: str, light: str | None) -> None:
        kind = "character" if ids[0].startswith("p_") else "background"
        mode = "chars" if kind == "character" else "bg"
        role = kind

        # `--out` СОЗНАТЕЛЬНО НЕ ПЕРЕДАЁТСЯ.
        # Соврать легко, а стоит дорого: при вырезании фона стенд зовёт
        # `seg.py` пакетно и пишет результат в `--out`. Подсунешь ему свой
        # каталог — туда приедут вырезанные кадры всех пулов, а не только
        # заказанных (проверено: заказ одного кадра дал 101 файл). Поэтому
        # берём готовый файл из каталога стенда по id.
        #
        # ЧТО БЫЛО ПРОВЕРЕНО 2026-10-10: стенд вырезал ВСЮ папку raw, игнорируя
        # `--only`. На заказе из трёх кадров полоса этапов честно показывала
        # «Вырезка фона: 37 из 129», а в названиях мелькали чужие пулы — то
        # есть человек платил GPU-временем за 126 кадров, которых не заказывал.
        # Теперь `seg.py` принимает `--only`, и вырезка идёт ровно по заказу.
        # Сдвиг сида на каждый прогон. Без него тот же кадр генерируется тем
        # же сидом и возвращается ТА ЖЕ картинка — это было первое, что
        # показала живая перегенерация по браку. Стенд принимает сдвиг
        # аргументом, число уходит в лог: по нему прогон повторяется.
        seed_shift = random.randrange(1, 1_000_000)
        log(f"сдвиг сида: {seed_shift}")
        self.events.emit("log", level="info", text=f"сдвиг сида: {seed_shift}")

        # `-u` — НЕ КОСМЕТИКА, А УСЛОВИЕ РАБОЧЕГО ПРОГРЕССА.
        # `shoot.py` перенастраивает только кодировку stdout, а перенаправленный
        # stdout в Python буферизуется блоками. Замер 2026-10-10: прогон на
        # 486 с пришёл в драйвер ОДНИМ куском в самом конце — все метки времени
        # одной секундой. То есть ни границы кадров, ни шаги сэмплера, ни
        # «сервер жив» не доходили во время работы, и прогресс в окне стоял.
        command = [self.config.python, "-u", "shoot.py", mode,
                   "--style", style,
                   "--seed-shift", str(seed_shift),
                   "--only", *ids]
        if light and kind == "background":
            # У персонажа свет свой (`ch['light']`); у фона — аргумент стенда.
            command += ["--light", light]

        # Прогрев — отдельный кадр 768x1152 (~40 с), нужный только холодному
        # серверу (без него первый кадр платит 410 с загрузки моделей). Если
        # генерация шла недавно, модели лежат в VRAM, и прогрев — это лишние
        # 40 с работы над кадром, которого никто не заказывал.
        if self.server_is_warm():
            command.append("--no-warmup")
            log("сервер тёплый — прогрев пропускаю (--no-warmup)")
        else:
            log("сервер холодный — стенд прогреет модели отдельным кадром")

        log("генерация: " + " ".join(command))

        # Первый кадр включает загрузку моделей (до 410 с) — это тоже работа
        # над первым кадром, поэтому «текущим» сразу объявляем его: иначе
        # стартовая пауза выглядела бы как «прогресс пропал».
        self.progress.set_current(ids[0] if ids else None)
        self.progress.start()

        # Отметка начала прогона. По ней приёмка отличает файл, сделанный
        # СЕЙЧАС, от одноимённого остатка прошлого прогона в `out`.
        run_started = time.time()

        env = dict(os.environ)
        # Дубль к `-u` на случай, если стенд перезапустит интерпретатор сам.
        env["PYTHONUNBUFFERED"] = "1"
        try:
            proc = subprocess.Popen(
                command, cwd=self.config.stand_dir, env=env,
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, encoding="utf-8", errors="replace", bufsize=1,
                creationflags=spawn_flags())
            with self._shoot_lock:
                self._shoot_proc = proc

            cancelled = False
            for line in proc.stdout:
                if self._cancel.is_set():
                    log("генерация отменена — снимаю shoot.py")
                    self.kill_process_tree(proc)
                    cancelled = True
                    break

                line = line.rstrip()
                if not line:
                    continue
                log("  " + line)
                self.events.emit("log", level="info", text=line)

                # Шапка прогона: модель, энкодер, VAE, сэмплер, LoRA. Окно
                # показывает их в панели состояния — спрашивать стенд
                # отдельно не нужно, данные уже напечатаны.
                if line.strip().startswith(self.stand.INFO_PREFIXES):
                    self.model_info.append(line.strip())
                    # СПИСКОМ, а не одной склеенной строкой. Окно показывает
                    # «Модель / LoRA / Энкодер / VAE / Сэмплер» отдельными
                    # строками — по одной в строке читается, через «·» в одну
                    # нечитаемую простыню. `text` оставлен для старых окон.
                    lines = self.model_info[-5:]
                    self.events.emit("driver_info",
                                     text=" · ".join(lines),
                                     detail=json.dumps(lines, ensure_ascii=False))

                # Прогрев — не кадр заявки. Его шаги сэмплера нельзя
                # приписывать первому id: иначе полоса показывает чужую
                # работу, а настоящий кадр начинается «с середины».
                if WARMUP_MARK in line:
                    self.progress.set_current(None)
                    if ids:
                        self.events.emit("item_progress", id=ids[0], percent=1,
                                         stage=STAGE_WARMUP,
                                         text="прогрев моделей")
                    continue

                # ЭТАП ОТ СТЕНДА. Вырезка фона и проверка идут по всей папке
                # raw и держат прогон десятки секунд; раньше они были видны
                # только в логе целиком, и в окне это выглядело как зависание.
                stage_match = STAGE_LINE.match(line)
                if stage_match:
                    stage_name, done, total, note = stage_match.groups()
                    self.events.emit(
                        "item_stage", stage=stage_name, done=int(done),
                        total=int(total), text=note.strip() or None)
                    continue

                # Кадр отрисован. Без этого окно не знает, какой кадр стенд
                # рисует: `item_done` приходит только после проверки, а
                # рисование идёт кадр за кадром минутами.
                frame_done = FRAME_DONE_LINE.match(line)
                if frame_done:
                    self.events.emit("item_drawn", id=frame_done.group(1))
                    continue

                # Стенд печатает `[i/N] <id> — <имя>` перед кадром. Id берём
                # из строки, а не по порядку: порядок ids не обязан совпадать
                # с тем, что отфильтровал стенд.
                match = self.stand.FRAME_LINE.match(line)
                if match:
                    stand_id = match.group(3)
                    self.progress.set_current(stand_id)
                    # Кадр начался: полоса уходит в начало. Детальный процент
                    # шлёт websocket-слушатель шагами сэмплера.
                    self.events.emit("item_progress", id=stand_id, percent=1,
                                     stage=STAGE_PREPARE,
                                     text="подготовка кадра")

            code = proc.wait()
            log(f"shoot.py завершился с кодом {code}"
                + (" (отменён)" if cancelled else ""))

            # Цена кадра с точки зрения СЕРВЕРА. `shoot.py` печатает полное
            # время, но в нём смешаны очередь, запуск и постобработка; строка
            # ComfyUI отделяет работу GPU. Расхождение этих двух чисел и есть
            # ответ на «почему долго» — оно уходит в окно, а не в файл.
            seconds = self.comfy.last_prompt_seconds()
            if seconds is not None:
                log(f"сервер: последний кадр {seconds:.1f} c")
                self.events.emit("log", level="info",
                                 text=f"ComfyUI: последний кадр {seconds:.1f} c")
        finally:
            with self._shoot_lock:
                self._shoot_proc = None
            self.progress.stop()

        for taken, stand_id in enumerate(ids):
            source = os.path.join(self.config.out_dir, f"{stand_id}_{style}.png")
            # Приёмка и копирование — тоже этап: между «всё нарисовано» и
            # «кадр в проекте» проходит проверка стандарта и копирование,
            # и без маркера эти секунды выглядят как остановка.
            self.events.emit("item_stage", stage=STAGE_COPY, done=taken,
                             total=len(ids), text=stand_id)

            if not self._is_fresh_output(source, run_started):
                self.events.emit("item_failed", id=stand_id,
                                 text="кадр не создан (на выходе нет свежего файла)")
                continue

            self.accept_candidate(stand_id, source, style, role)

    @staticmethod
    def _is_fresh_output(path: str, since: float) -> bool:
        """Файл создан ПОСЛЕ отметки `since`?

        ЗАЧЕМ ЭТО. `out` — накопительный каталог стенда: в нём лежат кадры
        всех прошлых прогонов. Если кадр сейчас не нарисовался (сбой, отмена,
        отбраковка по засветке), одноимённый файл от прошлого раза остался бы
        на месте — и приёмка забрала бы СТАРУЮ картинку, выдав её за новую.
        Поэтому проверяем время изменения, а не факт наличия.

        Допуск в секунду — от гранулярности файловой метки: свежий файл не
        должен забраковываться из-за округления вниз.
        """
        try:
            return os.path.getmtime(path) >= since - 1.0
        except OSError:
            return False

    def accept_candidate(self, stand_id: str, source: str,
                         style: str, role: str) -> None:
        problems = self.stand.validate(source, role)
        if problems:
            # Не прошедший стандарт кадр в review/ не попадает (см. спеку):
            # молча деградировать нельзя, причина уходит в окно.
            self.events.emit("item_failed", id=stand_id,
                             text="; ".join(problems))
            return

        pool_dir = os.path.join(self.paths.review, self._pool_folder(stand_id))
        os.makedirs(pool_dir, exist_ok=True)
        target = os.path.join(pool_dir, os.path.basename(source))
        # Копия, а не перенос: каталог `out` — готовая выдача стенда, и
        # забирать из неё файл значит менять состояние чужого инструмента.
        shutil.copy2(source, target)

        try:
            os.remove(source)
            log(f"временный выход удалён: {os.path.basename(source)}")
        except OSError as error:
            # Кандидат уже сохранён, поэтому ошибка уборки не должна
            # превращать успешную генерацию в failed; показываем её как
            # предупреждение и оставляем точный путь для диагностики.
            warning = f"не удалось удалить временный выход {source}: {error}"
            log(warning)
            self.events.emit("log", level="warn", text=warning)

        self.events.emit("item_done", id=stand_id, path=target, percent=100)
        self.events.emit("review_candidate", id=stand_id, path=target,
                         percent=self.stand.coverage(target))

    def _pool_folder(self, stand_id: str) -> str:
        """Имя папки-пула для кандидата: `p_Roles_Priest_a` → `Roles_Priest`."""
        if stand_id.startswith("p_"):
            return "_".join(stand_id.split("_")[1:-1])
        return "_".join(stand_id.split("_")[1:])

    # ------------------------------------------------------ перегенерация

    def rejected_marks(self) -> list[str]:
        """Ключи брака из `feedback.json` — файл пишет окно, читает драйвер.

        Ключ — путь, а не stand-id: у кадра в проекте id известен не всегда,
        а путь есть всегда (так же рассуждает `FeedbackStore` в Unity).
        Битый файл не должен ронять перегенерацию: причина уходит событием,
        а не исключением.
        """
        if not os.path.isfile(self.paths.feedback):
            return []

        try:
            with open(self.paths.feedback, encoding="utf-8") as f:
                data = json.load(f)
        except (OSError, json.JSONDecodeError) as e:
            self.events.emit("error", text=f"feedback.json не разобран: {e}")
            return []

        marks: list[str] = []
        for entry in data.get("entries") or []:
            key = (entry or {}).get("key")
            status = str((entry or {}).get("status") or "")
            if key and status.lower() == "rejected":
                marks.append(key)
        return marks

    def _is_candidate_mark(self, key: str) -> bool:
        """Ключ указывает на кандидата в `review/` (а не на кадр проекта)."""
        review = os.path.normcase(os.path.abspath(self.paths.review))
        path = os.path.normcase(os.path.abspath(key))
        return path.startswith(review + os.sep)

    def _stand_id_for_mark(self, key: str,
                           path_to_id: dict[str, str] | None = None) -> str | None:
        """Путь из журнала → id стенда. None — кадр не нашёлся в каталоге.

        Два случая. Кандидат в `review/...` — id разбирается из имени файла,
        ровно как при `approve`. Кадр в проекте — id ищется по каталогу, и
        сравниваются те же пути, что вернул `destination()`, а не строки:
        правило сопоставления id ↔ путь живёт в одном месте.
        """
        if self._is_candidate_mark(key):
            return self._id_from_candidate(key)

        # Карта «путь проекта → id» передаётся снаружи, когда меток несколько:
        # без неё каждая метка пересобирала каталог стенда целиком (обход
        # каталогов проекта по всем пулам) и заново считала пути всех id.
        if path_to_id is None:
            path_to_id = self._build_path_to_id()

        return path_to_id.get(os.path.normcase(os.path.abspath(key)))

    def _build_path_to_id(self, catalog: list[dict] | None = None) -> dict[str, str]:
        """Разово строит карту «абсолютный путь в проекте → id стенда».

        Правило «id ↔ путь» остаётся в одном месте — `destination()`, — а не
        дублируется здесь строками. Готовый каталог можно передать снаружи:
        тогда обход каталогов проекта делается один раз на всю команду.
        """
        mapping: dict[str, str] = {}
        for entry in catalog if catalog is not None else self.stand.catalog():
            for stand_id in entry["all"]:
                destination, _role = self.stand.destination(stand_id)
                mapping[os.path.normcase(os.path.abspath(destination))] = stand_id
        return mapping

    def regen_targets(self, marks: list[str]) -> tuple[list[str], list[str]]:
        """Что перегенерировать по помеченному браку: (id, причины пропуска).

        Кадр в проекте занял свой номер, и перегенерация кладёт НОВЫЙ номер
        (см. спеку): существующий файл не перезаписывается никогда. Кандидат
        в `review/` номера в проекте не занимает, поэтому он перегенерируется
        под тем же id — это «попробовать ещё раз» для того же человека.
        """
        targets: list[str] = []
        skipped: list[str] = []

        # Каталог стенда строится ОДИН раз на всю команду. Раньше его
        # пересобирали на каждом шаге: `_stand_id_for_mark` — на каждую метку,
        # `free_ids` — ещё раз на каждую метку. При 88 пулах это десятки
        # обходов каталогов проекта ради одной перегенерации.
        catalog = self.stand.catalog()
        path_to_id = self._build_path_to_id(catalog)
        free_by_pool = {entry["pool"]: list(entry["free"]) for entry in catalog}

        for key in marks:
            stand_id = self._stand_id_for_mark(key, path_to_id)
            if stand_id is None:
                skipped.append(f"{os.path.basename(key)}: нет в каталоге стенда")
                continue

            if not self._is_candidate_mark(key):
                pool = self.stand.prefix_of(stand_id)
                free = free_by_pool.get(pool) or []
                if not free:
                    skipped.append(f"{pool}: нет свободного кадра")
                    continue
                # Номер забирается из списка сразу. Раньше каждая метка
                # спрашивала «первый свободный» заново и получала ТОТ ЖЕ id:
                # две метки одного пула молча превращались в одну
                # перегенерацию (второй id отбрасывался как дубль).
                stand_id = free.pop(0)

            if stand_id not in targets:
                targets.append(stand_id)

        return targets, skipped

    def cmd_regen_marked(self, _command: dict) -> None:
        if self.is_generating():
            self.reject("генерация уже идёт — дождись её окончания")
            return

        marks = self.rejected_marks()
        if not marks:
            self.reject("нет помеченных браком кадров")
            return

        targets, skipped = self.regen_targets(marks)
        for reason in skipped:
            # Пропуск — не поломка, но и не «ничего не произошло»: человек
            # пометил кадр, а кадр не поехал.
            self.events.emit("error", text=f"перегенерация: {reason}")

        if not targets:
            self.reject("перегенерировать нечего: " + "; ".join(skipped))
            return

        style = self.config.style
        light = self.config.light
        log(f"перегенерация брака: {len(targets)} кадров — {', '.join(targets)}")

        for stand_id in targets:
            self.events.emit("item_added", id=stand_id,
                             pool=self.stand.prefix_of(stand_id),
                             label=f"перегенерация {stand_id.rsplit('_', 1)[-1]}")

        # Персонажи и фоны генерируются разными режимами стенда, поэтому
        # пачку режем по видам: иначе режим первого кадра уехал бы на все.
        groups = [
            [stand_id for stand_id in targets if stand_id.startswith("p_")],
            [stand_id for stand_id in targets if not stand_id.startswith("p_")],
        ]

        def work() -> None:
            for group in groups:
                if not group:
                    continue
                try:
                    self.generate_frames(group, style, light)
                except Exception as e:  # noqa: BLE001
                    for stand_id in group:
                        self.events.emit("item_failed", id=stand_id, text=str(e))
                    raise

        if not self.launch(work, f"перегенерация брака ({len(targets)})"):
            self.reject("генерация уже идёт")

    # ------------------------------------------------------------ ревью

    def cmd_approve(self, command: dict) -> None:
        target = command.get("target")
        if not target or not os.path.isfile(target):
            self.reject(f"кандидат не найден: {target}")
            return

        stand_id = self._id_from_candidate(target)
        if stand_id is None:
            self.reject(f"не разобрал id кандидата: {target}")
            return

        destination, _role = self.stand.destination(stand_id)
        if os.path.isfile(destination):
            # Место занято — значит номер не был свободен. Не перезаписываем
            # молча: помечаем и оставляем человеку решать.
            self.reject(f"место в проекте занято: {destination}")
            return

        os.makedirs(os.path.dirname(destination), exist_ok=True)
        shutil.move(target, destination)
        relative = os.path.relpath(destination, self.paths.project_root) \
            .replace("\\", "/")
        self.events.emit("approved", path=relative)
        log(f"кадр занял место в проекте: {relative}")

    def _id_from_candidate(self, path: str) -> str | None:
        name = os.path.basename(path)
        for suffix in (f"_{self.config.style}.png", ".png"):
            if name.endswith(suffix):
                candidate = name[: -len(suffix)]
                if candidate.startswith(("p_", "bg_")):
                    return candidate
        return None


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--project-root", default=None,
                        help="корень проекта (по умолчанию — поиском вверх)")
    parser.add_argument("--once", action="store_true",
                        help="обработать готовые команды и выйти")
    parser.add_argument("--no-server", action="store_true",
                        help="не трогать ComfyUI (офлайн-проверка протокола)")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv if argv is not None else sys.argv[1:])
    project_root = args.project_root or find_project_root(os.path.dirname(__file__))
    try:
        driver = Driver(project_root, allow_server=not args.no_server,
                        replay_history=args.once)
    except Exception as e:  # noqa: BLE001
        log(f"драйвер не поднялся: {e}")
        return 2
    return driver.run(once=args.once)


if __name__ == "__main__":
    raise SystemExit(main())
