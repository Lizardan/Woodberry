// Одноразовый MCP-клиент для Unity MCP (mcpforunityserver) через stdio.
// Использование: node unity-mcp.mjs <calls.json> <out.json>
// calls.json: [{"tool":"read_console","args":{...}}, ...]
import { spawn } from 'node:child_process';
import fs from 'node:fs';

const callsFile = process.argv[2];
const outFile = process.argv[3];
const calls = JSON.parse(fs.readFileSync(callsFile, 'utf8'));

const UVX = 'C:\\Users\\Lizardan\\.local\\bin\\uvx.exe';
const child = spawn(
  UVX,
  ['--from', 'mcpforunityserver==10.2.0', 'mcp-for-unity', '--transport', 'stdio'],
  { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true }
);

let buf = '';
let stderr = '';
const pending = new Map();
const results = [];

child.stderr.on('data', (d) => { stderr += d.toString(); });

function send(obj) {
  child.stdin.write(JSON.stringify(obj) + '\n');
}

child.stdout.on('data', (d) => {
  buf += d.toString();
  let idx;
  while ((idx = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, idx).trim();
    buf = buf.slice(idx + 1);
    if (!line) continue;
    let msg;
    try { msg = JSON.parse(line); } catch { continue; }
    if (msg.id !== undefined && pending.has(msg.id)) {
      const resolve = pending.get(msg.id);
      pending.delete(msg.id);
      resolve(msg);
    }
  }
});

function call(id, method, params, timeoutMs = 180000) {
  return new Promise((resolve, reject) => {
    pending.set(id, resolve);
    send({ jsonrpc: '2.0', id, method, params });
    setTimeout(() => {
      if (pending.has(id)) { pending.delete(id); reject(new Error('timeout: ' + method)); }
    }, timeoutMs);
  });
}

function textOf(result) {
  if (!result) return null;
  if (Array.isArray(result.content)) {
    return result.content.map((c) => (c.type === 'text' ? c.text : JSON.stringify(c))).join('\n');
  }
  return JSON.stringify(result).slice(0, 200000);
}

(async () => {
  try {
    const init = await call(1, 'initialize', {
      protocolVersion: '2024-11-05',
      capabilities: {},
      clientInfo: { name: 'dsh-client', version: '1.0' },
    }, 120000);
    results.push({ step: 'initialize', error: init.error || null });
    send({ jsonrpc: '2.0', method: 'notifications/initialized', params: {} });

    let id = 100;
    for (const c of calls) {
      if (c.method === 'sleep') {
        await new Promise((r) => setTimeout(r, c.ms || 1000));
        results.push({ step: 'sleep', ms: c.ms || 1000 });
        continue;
      }
      if (c.method === 'tools/list') {
        const res = await call(id++, 'tools/list', {});
        const tools = (res.result && res.result.tools) || [];
        const names = tools.map((t) => t.name);
        const entry = { step: 'tools/list', count: names.length, names };
        if (c.schema) {
          entry.schemas = tools.map((t) => ({
            name: t.name,
            description: (t.description || '').slice(0, 700),
            properties: t.inputSchema ? Object.keys(t.inputSchema.properties || {}) : [],
            required: (t.inputSchema && t.inputSchema.required) || [],
          }));
        }
        results.push(entry);
        continue;
      }
      const args = Object.assign({}, c.args || {});
      if (c.codeFile) {
        args.code = fs.readFileSync(c.codeFile, 'utf8');
      }
      const res = await call(id++, 'tools/call', { name: c.tool, arguments: args });
      results.push({
        tool: c.tool,
        args: c.args || {},
        isError: res.result ? !!res.result.isError : null,
        error: res.error || null,
        text: textOf(res.result),
      });
    }
  } catch (e) {
    results.push({ step: 'fatal', error: String((e && e.stack) || e) });
  }
  fs.writeFileSync(outFile, JSON.stringify({ results, stderr: stderr.slice(-4000) }, null, 2));
  try { child.kill(); } catch {}
  process.exit(0);
})();
