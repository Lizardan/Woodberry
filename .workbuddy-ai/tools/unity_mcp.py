"""Минимальный клиент к Unity MCP bridge (com.coplaydev.unity-mcp).

Мост Unity слушает TCP 127.0.0.1:6400 и после подключения отдаёт строку
рукопожатия "WELCOME UNITY-MCP 1 FRAMING=1\n". Дальше обмен идёт кадрами:
8 байт big-endian uint64 (длина) + UTF-8 JSON.

Запрос:  {"type": "<command>", "params": {...}}
Ответ:   {"status": "success", "result": ...} | {"status": "error", "error": "..."}

Использование:
    python unity_mcp.py '<command>' '<json params>'
    python unity_mcp.py --raw '<полный json запроса>'
"""

import json
import socket
import struct
import sys
import time

HOST = "127.0.0.1"
PORT = 6400
TIMEOUT = 300.0


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


def call(command, params=None, timeout=TIMEOUT, retries=1):
    """Отправляет команду и возвращает распарсенный ответ (dict)."""
    last_error = None
    for attempt in range(retries + 1):
        try:
            with socket.create_connection((HOST, PORT), timeout=5.0) as sock:
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
