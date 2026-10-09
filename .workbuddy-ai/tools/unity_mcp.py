"""Минимальный клиент к Unity MCP bridge (com.coplaydev.unity-mcp).

Мост Unity слушает TCP на локальном порту (исторически 6400, но версия
пакета может сдвинуть его: живой прогон 2026-10-09 показал 6401) и после
подключения отдаёт строку рукопожатия "WELCOME UNITY-MCP 1 FRAMING=1\n".
Дальше обмен идёт кадрами: 8 байт big-endian uint64 (длина) + UTF-8 JSON.

Порт не хардкодится: сначала берётся UNITY_MCP_PORT, иначе порт ищется
перебором PORT_RANGE по рукопожатию. Хардкод 6400 молча ломается, когда
пакет поднимает мост на соседнем порту, — и верификация выглядит как
«мост мёртв», хотя он жив.

Запрос:  {"type": "<command>", "params": {...}}
Ответ:   {"status": "success", "result": ...} | {"status": "error", "error": "..."}

Использование:
    python unity_mcp.py '<command>' '<json params>'
    python unity_mcp.py --raw '<полный json запроса>'
"""

import hashlib
import json
import os
import socket
import struct
import sys
import time

HOST = "127.0.0.1"

# Предпочтительный порт: 0/None — искать перебором PORT_RANGE.
PORT = int(os.environ.get("UNITY_MCP_PORT", "0") or "0") or None
PORT_RANGE = range(6400, 6411)
TIMEOUT = 300.0

# Сколько раз перебирать порты и с какой паузой: домен-релоад Unity
# пересоздаёт мост, и в это окно он не отвечает ни на одном порту.
PROBE_ATTEMPTS = 10
PROBE_DELAY = 1.0

_resolved_port = None


def _read_exact(sock, count):
    buf = bytearray()
    while len(buf) < count:
        chunk = sock.recv(count - len(buf))
        if not chunk:
            raise IOError("соединение закрыто до получения всех байт")
        buf.extend(chunk)
    return bytes(buf)


def _read_frame(sock):
    header = _read_exact(sock, 8)
    (length,) = struct.unpack(">Q", header)
    if length == 0 or length > 512 * 1024 * 1024:
        raise IOError("некорректная длина кадра: %d" % length)
    return _read_exact(sock, length).decode("utf-8")


def _write_frame(sock, payload):
    data = payload.encode("utf-8")
    sock.sendall(struct.pack(">Q", len(data)) + data)


def _project_data_path():
    """`<проект>/Assets` с прямыми слэшами — так этот путь видит Unity."""
    current = os.path.abspath(os.getcwd())
    for _ in range(6):
        assets = os.path.join(current, "Assets")
        if os.path.isdir(assets):
            return assets.replace("\\", "/")
        parent = os.path.dirname(current)
        if parent == current:
            break
        current = parent
    return None


def _status_ports():
    """Порты из файлов пакета `~/.unity-mcp`, в порядке предпочтения.

    Пакет пишет порт в `unity-mcp-port-<sha1(Assets)[:8]>.json` (и в общий
    `unity-mcp-port.json`) и прямо говорит, что клиенты ходят за портом по
    этому файлу. Перебор диапазона — запасной путь: он не отличает чужой
    Unity с тем же диапазоном портов от нашего.
    """
    directory = os.path.join(os.path.expanduser("~"), ".unity-mcp")
    if not os.path.isdir(directory):
        return []

    try:
        names = sorted(os.listdir(directory))
    except OSError:
        return []

    preferred = None
    data_path = _project_data_path()
    if data_path:
        digest = hashlib.sha1(data_path.encode("utf-8")).hexdigest()[:8]
        preferred = f"unity-mcp-port-{digest}.json"

    ordered = []
    if preferred in names:
        ordered.append(preferred)
    if "unity-mcp-port.json" in names:
        ordered.append("unity-mcp-port.json")
    ordered += [name for name in names
                if name.startswith("unity-mcp-port") and name.endswith(".json")
                and name not in ordered]

    ports = []
    for name in ordered:
        try:
            with open(os.path.join(directory, name), encoding="utf-8") as handle:
                data = json.load(handle)
        except (OSError, ValueError):
            continue
        port = data.get("unity_port")
        if isinstance(port, int) and port not in ports:
            ports.append(port)
    return ports


def _probe(port, timeout=0.6):
    """true — на порту мост Unity MCP (отдал рукопожатие FRAMING=1)."""
    try:
        with socket.create_connection((HOST, port), timeout=timeout) as sock:
            sock.settimeout(2.0)
            handshake = b""
            while not handshake.endswith(b"\n"):
                chunk = sock.recv(1)
                if not chunk:
                    return False
                handshake += chunk
            return b"FRAMING=1" in handshake
    except Exception:  # noqa: BLE001 — закрытый порт это ожидаемый исход
        return False


def resolve_port(force=False):
    """Порт живого моста: UNITY_MCP_PORT, иначе перебор PORT_RANGE.

    Результат кэшируется, но проверяется пробой: после перезапуска
    редактора порт мог смениться, а кэш — остаться.

    Перебор повторяется несколько секунд: домен-релоад пересоздаёт мост, и
    в это окно ни один порт не отвечает. Без ожидания верификация падает
    ровно в тот момент, когда редактор ещё поднимается, и это выглядит как
    «мост мёртв», хотя он вот-вот вернётся.
    """
    global _resolved_port

    for attempt in range(PROBE_ATTEMPTS):
        if not force and _resolved_port is not None and _probe(_resolved_port):
            return _resolved_port

        if PORT is not None:
            _resolved_port = PORT
            return PORT

        for port in _status_ports():
            if _probe(port):
                _resolved_port = port
                return port

        for port in PORT_RANGE:
            if _probe(port):
                _resolved_port = port
                return port

        if attempt < PROBE_ATTEMPTS - 1:
            time.sleep(PROBE_DELAY)

    raise RuntimeError(
        "мост Unity MCP не найден (порты %d-%d закрыты или не отвечают "
        "рукопожатием)" % (PORT_RANGE.start, PORT_RANGE.stop - 1))


def call(command, params=None, timeout=TIMEOUT, retries=1):
    """Отправляет команду и возвращает распарсенный ответ (dict)."""
    last_error = None
    for attempt in range(retries + 1):
        try:
            port = resolve_port()
            with socket.create_connection((HOST, port), timeout=5.0) as sock:
                sock.settimeout(timeout)
                handshake = b""
                while not handshake.endswith(b"\n"):
                    chunk = sock.recv(1)
                    if not chunk:
                        raise IOError("нет рукопожатия")
                    handshake += chunk
                if b"FRAMING=1" not in handshake:
                    raise IOError("неизвестный протокол: %r" % handshake)

                request = {"type": command, "params": params or {}}
                _write_frame(sock, json.dumps(request, ensure_ascii=False))
                raw = _read_frame(sock)
                try:
                    return json.loads(raw)
                except ValueError:
                    return {"status": "raw", "result": raw}
        except Exception as exc:  # noqa: BLE001 — наверх нужен текст, а не трейс
            last_error = exc
            if attempt < retries:
                time.sleep(1.0)
    raise RuntimeError("вызов %s не удался: %s" % (command, last_error))


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    if argv[1] == "--raw":
        request = json.loads(argv[2])
        response = call(request.get("type"), request.get("params"))
    else:
        params = json.loads(argv[2]) if len(argv) > 2 else {}
        response = call(argv[1], params)

    print(json.dumps(response, ensure_ascii=False, indent=2))
    return 0 if response.get("status") == "success" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
