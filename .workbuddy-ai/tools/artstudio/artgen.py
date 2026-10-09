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
import importlib
import json
import os
import random
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime

# Версия протокола. Обязана совпадать с ArtProtocol.Version в Unity:
# расхождение — ошибка конфигурации, её надо показать, а не проглотить.
PROTOCOL = 1

POLL_SECONDS = 0.15

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
        # Готовый арт стенда. Совпадает с `BASE_OUT/out` в shoot.py
        # (BASE_OUT = env PORTRAIT_OUT или standDir).
        self.out_dir = data.get("outDir") or os.path.join(self.stand_dir, "out")


class EventWriter:
    """Пишет `events.jsonl`: монотонный seq с 1, одна строка = один flush."""

    def __init__(self, path: str) -> None:
        self.path = path
        self.seq = 0
        os.makedirs(os.path.dirname(path), exist_ok=True)
        # Свежий прогон: seq начинается с 1, файл не растёт бесконечно.
        with open(self.path, "w", encoding="utf-8"):
            pass

    def emit(self, kind: str, **fields) -> None:
        self.seq += 1
        record = {"seq": self.seq, "kind": kind}
        for key, value in fields.items():
            if value is not None:
                record[key] = value

        line = json.dumps(record, ensure_ascii=False)
        with open(self.path, "a", encoding="utf-8") as f:
            f.write(line + "\n")
            f.flush()


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

    def __init__(self, config: Config, paths: Paths) -> None:
        self.config = config
        self.paths = paths
        self._roster = None
        self._copy_art = None

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

    def __init__(self, config: Config, paths: Paths) -> None:
        self.config = config
        self.paths = paths
        self.pid: int | None = None

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
        try:
            os.kill(pid, 0)
            return True
        except OSError:
            return False

    def start(self) -> None:
        if self.is_up():
            self.pid = self.find_running_pid()
            log("ComfyUI уже отвечает — переиспользуем")
            return

        main_py = os.path.join(self.config.comfy_dir, "main.py")
        if not os.path.isfile(main_py):
            raise RuntimeError(f"не найден ComfyUI: {main_py}")

        host, _, port = self.config.comfy_host.partition(":")
        command = [
            self.config.python, main_py,
            "--use-pytorch-cross-attention", "--highvram",
            "--disable-async-offload", "--cache-none",
            "--extra-model-paths-config", self.config.extra_model_paths,
            "--listen", host or "127.0.0.1", "--port", port or "8188",
        ]
        log("поднимаю ComfyUI: " + " ".join(command))
        flags = 0x00000008 if os.name == "nt" else 0  # DETACHED_PROCESS
        proc = subprocess.Popen(command, cwd=self.config.comfy_dir,
                                stdout=subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL,
                                creationflags=flags)
        self.pid = proc.pid

        for _ in range(120):
            if self.is_up():
                return
            if proc.poll() is not None:
                raise RuntimeError("ComfyUI завершился сразу после запуска")
            time.sleep(2)
        raise RuntimeError("ComfyUI не ответил за 240 с")

    def stop(self) -> None:
        pid = self.find_running_pid()
        if pid is None:
            return
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        else:
            os.kill(pid, 15)
        self.pid = None


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
        self.comfy = Comfy(self.config, self.paths)
        self.allow_server = allow_server

        self.seen_nonces: set[str] = set()
        self.running = True

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

    def cmd_ping(self, _command: dict) -> None:
        self.events.emit("ready", protocol=PROTOCOL, text=f"протокол {PROTOCOL}")
        # Свежее состояние ComfyUI в ответ на ping. Окно дочитывает
        # events.jsonl с начала, а драйвер живёт дольше сервера: без
        # освежения окно поднимало бы из истории `server_state: готов`,
        # когда ComfyUI уже погашен, и кнопка «Сервер» гасила бы то,
        # чего нет. `ready` этой дыры не закрывает — он про драйвер.
        self.events.emit("server_state", text=self.comfy.state())

    def cmd_stop(self, _command: dict) -> None:
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

    # ---------------------------------------------------------- генерация

    def cmd_generate(self, command: dict) -> None:
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
            self.events.emit("item_progress", id=stand_id, percent=3)

        try:
            self.generate_frames(ids, style, light)
        except Exception as e:  # noqa: BLE001
            for stand_id in ids:
                self.events.emit("item_failed", id=stand_id, text=str(e))
            raise

    def generate_frames(self, ids: list[str], style: str, light: str | None) -> None:
        kind = "character" if ids[0].startswith("p_") else "background"
        mode = "chars" if kind == "character" else "bg"
        role = kind

        # `--out` СОЗНАТЕЛЬНО НЕ ПЕРЕДАЁТСЯ.
        # Соврать легко, а стоит дорого: при вырезании фона стенд зовёт
        # `seg.py` пакетно по ВСЕЙ папке `raw` и пишет результат в `--out`,
        # игнорируя `--only`. Подсунешь ему свой каталог — туда приедут
        # вырезанные кадры всех пулов, а не только заказанных (проверено:
        # заказ одного кадра дал 101 файл). Поэтому берём готовый файл из
        # каталога стенда по id.
        # Сдвиг сида на каждый прогон. Без него тот же кадр генерируется тем
        # же сидом и возвращается ТА ЖЕ картинка — это было первое, что
        # показала живая перегенерация по браку. Стенд принимает сдвиг
        # аргументом, число уходит в лог: по нему прогон повторяется.
        seed_shift = random.randrange(1, 1_000_000)
        log(f"сдвиг сида: {seed_shift}")
        self.events.emit("log", level="info", text=f"сдвиг сида: {seed_shift}")

        command = [self.config.python, "shoot.py", mode,
                   "--style", style,
                   "--seed-shift", str(seed_shift),
                   "--only", *ids]
        if light and kind == "background":
            # У персонажа свет свой (`ch['light']`); у фона — аргумент стенда.
            command += ["--light", light]

        log("генерация: " + " ".join(command))
        total = len(ids)
        proc = subprocess.Popen(
            command, cwd=self.config.stand_dir,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="replace", bufsize=1)

        index = 0
        for line in proc.stdout:
            line = line.rstrip()
            if not line:
                continue
            log("  " + line)
            self.events.emit("log", level="info", text=line)

            # Стенд печатает `[i/N] <id> — <имя>` перед кадром.
            if line.startswith("[") and "]" in line:
                index += 1
                percent = round(index * 100 / total) - 5
                self.events.emit("item_progress",
                                 id=ids[min(index - 1, total - 1)],
                                 percent=max(5, min(percent, 95)))

        code = proc.wait()
        log(f"shoot.py завершился с кодом {code}")

        for stand_id in ids:
            source = os.path.join(self.config.out_dir, f"{stand_id}_{style}.png")
            if not os.path.isfile(source):
                self.events.emit("item_failed", id=stand_id,
                                 text="кадр не создан (нет файла на выходе)")
                continue
            self.accept_candidate(stand_id, source, style, role)

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

    def _stand_id_for_mark(self, key: str) -> str | None:
        """Путь из журнала → id стенда. None — кадр не нашёлся в каталоге.

        Два случая. Кандидат в `review/...` — id разбирается из имени файла,
        ровно как при `approve`. Кадр в проекте — id ищется по каталогу, и
        сравниваются те же пути, что вернул `destination()`, а не строки:
        правило сопоставления id ↔ путь живёт в одном месте.
        """
        if self._is_candidate_mark(key):
            return self._id_from_candidate(key)

        target = os.path.normcase(os.path.abspath(key))
        for entry in self.stand.catalog():
            for stand_id in entry["all"]:
                destination, _role = self.stand.destination(stand_id)
                if os.path.normcase(os.path.abspath(destination)) == target:
                    return stand_id
        return None

    def regen_targets(self, marks: list[str]) -> tuple[list[str], list[str]]:
        """Что перегенерировать по помеченному браку: (id, причины пропуска).

        Кадр в проекте занял свой номер, и перегенерация кладёт НОВЫЙ номер
        (см. спеку): существующий файл не перезаписывается никогда. Кандидат
        в `review/` номера в проекте не занимает, поэтому он перегенерируется
        под тем же id — это «попробовать ещё раз» для того же человека.
        """
        targets: list[str] = []
        skipped: list[str] = []

        for key in marks:
            stand_id = self._stand_id_for_mark(key)
            if stand_id is None:
                skipped.append(f"{os.path.basename(key)}: нет в каталоге стенда")
                continue

            if not self._is_candidate_mark(key):
                pool = self.stand.prefix_of(stand_id)
                free = self.stand.free_ids(pool, 1)
                if not free:
                    skipped.append(f"{pool}: нет свободного кадра")
                    continue
                stand_id = free[0]

            if stand_id not in targets:
                targets.append(stand_id)

        return targets, skipped

    def cmd_regen_marked(self, _command: dict) -> None:
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
            self.events.emit("item_progress", id=stand_id, percent=3)

        # Персонажи и фоны генерируются разными режимами стенда, поэтому
        # пачку режем по видам: иначе режим первого кадра уехал бы на все.
        groups = [
            [stand_id for stand_id in targets if stand_id.startswith("p_")],
            [stand_id for stand_id in targets if not stand_id.startswith("p_")],
        ]
        for group in groups:
            if not group:
                continue
            try:
                self.generate_frames(group, style, light)
            except Exception as e:  # noqa: BLE001
                for stand_id in group:
                    self.events.emit("item_failed", id=stand_id, text=str(e))
                raise

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
