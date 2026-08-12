// Tiny zero-dependency server for the Equipment Designer.
//
//   node Tools/serve.js        then open http://localhost:8642
//
// Why bother, when the HTML opens fine from disk?
//   * auto-save — the page POSTs to /api/design and this writes Tools/equipment-design.json,
//     so your work lands in a real file instead of only browser localStorage
//   * it serves Assets/, so sprite images load
//   * a stable http origin, so localStorage survives moving the file
const fs = require('fs');
const path = require('path');
const http = require('http');

const ROOT = path.resolve(__dirname, '..');
const DESIGN = path.join(__dirname, 'equipment-design.json');
const PORT = process.env.PORT || 8642;

const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.png': 'image/png',
  '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.css': 'text/css; charset=utf-8',
};

const send = (res, code, body, type) => {
  res.writeHead(code, { 'Content-Type': type || 'text/plain; charset=utf-8' });
  res.end(body);
};

http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);

  // ---- design API ----
  if (url === '/api/design') {
    if (req.method === 'GET') {
      if (!fs.existsSync(DESIGN)) return send(res, 200, '{}', MIME['.json']);
      return send(res, 200, fs.readFileSync(DESIGN, 'utf8'), MIME['.json']);
    }
    if (req.method === 'POST') {
      let body = '';
      req.on('data', c => { body += c; if (body.length > 5e7) req.destroy(); });
      req.on('end', () => {
        try {
          JSON.parse(body);                                   // validate before writing
          fs.writeFileSync(DESIGN, body, 'utf8');
          const n = (JSON.parse(body).items || []).length;
          process.stdout.write(`\r  saved ${n} items → Tools/equipment-design.json    `);
          send(res, 200, '{"ok":true}', MIME['.json']);
        } catch (e) {
          send(res, 400, JSON.stringify({ error: String(e) }), MIME['.json']);
        }
      });
      return;
    }
  }

  // ---- static files ----
  let rel = url === '/' ? 'Tools/EquipmentDesigner.html' : url.replace(/^\/+/, '');
  const file = path.join(ROOT, rel);
  if (!file.startsWith(ROOT)) return send(res, 403, 'Forbidden');   // path-traversal guard
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) return send(res, 404, 'Not found');

  send(res, 200, fs.readFileSync(file), MIME[path.extname(file).toLowerCase()] || 'application/octet-stream');
}).listen(PORT, () => {
  console.log(`\n  Equipment Designer  →  http://localhost:${PORT}`);
  console.log(`  Auto-saving to      →  Tools/equipment-design.json`);
  console.log(`  Ctrl+C to stop.\n`);
});
