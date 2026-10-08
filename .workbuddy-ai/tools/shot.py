import json, socket, struct, sys

HOST, PORT = "127.0.0.1", 6400

s = socket.create_connection((HOST, PORT), timeout=30)
f = s.makefile("rb")
hello = f.readline()
print("HELLO:", hello)

def send(cmd, params=None):
    payload = json.dumps({"type": cmd, "params": params or {}}).encode("utf-8")
    s.sendall(struct.pack(">Q", len(payload)) + payload)
    header = f.read(8)
    (n,) = struct.unpack(">Q", header)
    data = f.read(n)
    return json.loads(data)

res = send("manage_scene", {"action": "screenshot", "filename": "u2d_scene1.png"})
print(json.dumps(res, indent=2, ensure_ascii=False)[:2000])
