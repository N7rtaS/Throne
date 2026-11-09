// Converts the text KV3 subset emitted by VRF for the survivors VData files.
// Typed strings retain their tag and value; unsupported syntax fails explicitly.
import fs from 'node:fs';
import path from 'node:path';
export function parseKV3(source) {
  const s = source.replace(/^\uFEFF/, '').replace(/^<!--[^]*?-->/, '');
  const tokens = []; let pos = 0;
  const re = /\s+|\/\/[^\n]*|"(?:\\.|[^"\\])*"|[{}\[\]=,:]|[^\s{}\[\]=,:]+/y;
  while (pos < s.length) {
    re.lastIndex = pos; const m = re.exec(s);
    if (!m) throw new Error(`Invalid token at ${pos}`);
    pos = re.lastIndex;
    if (!/^\s|^\/\//.test(m[0])) tokens.push(m[0]);
  }
  let i = 0;
  function take(expected) {
    const t = tokens[i++];
    if (t === undefined || (expected && t !== expected)) throw new Error(`Expected ${expected}, got ${t} at token ${i}`);
    return t;
  }
  function value() {
    const t = take();
    if (t === '{') {
      const o = {};
      while (tokens[i] !== '}') {
        let k = take(); if (k.startsWith('"')) k = JSON.parse(k);
        take('='); if (Object.hasOwn(o, k)) throw new Error(`Duplicate key ${k}`);
        o[k] = value(); if (tokens[i] === ',') i++;
      }
      take('}'); return o;
    }
    if (t === '[') {
      const a = []; while (tokens[i] !== ']') { a.push(value()); if (tokens[i] === ',') i++; }
      take(']'); return a;
    }
    if (tokens[i] === ':') { i++; return { $type: t, $value: value() }; }
    if (t.startsWith('"')) return JSON.parse(t);
    if (t === 'true' || t === 'false') return t === 'true';
    if (t === 'null') return null;
    if (/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(t)) {
      const n = Number(t); if (!Number.isFinite(n)) throw new Error(`Invalid number ${t}`); return n;
    }
    throw new Error(`Unsupported value ${t} at token ${i}`);
  }
  const result = value(); if (i !== tokens.length) throw new Error('Trailing tokens'); return result;
}
export function convertFolder(input, output) {
  fs.mkdirSync(output, { recursive: true }); const summary = [];
  for (const name of fs.readdirSync(input).filter(n => n.endsWith('.vdata'))) {
    const obj = parseKV3(fs.readFileSync(path.join(input, name), 'utf8'));
    const dest = path.join(output, name.replace(/\.vdata$/, '.json'));
    fs.writeFileSync(dest, JSON.stringify(obj, null, 2) + '\n');
    const saved = JSON.parse(fs.readFileSync(dest, 'utf8'));
    if (JSON.stringify(saved) !== JSON.stringify(obj)) throw new Error(`Roundtrip failed: ${name}`);
    summary.push({ file: name, type: obj.generic_data_type, entries: Object.values(obj).filter(v => v && typeof v === 'object' && !Array.isArray(v)).length });
  }
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n');
  return summary;
}
