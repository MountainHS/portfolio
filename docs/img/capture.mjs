// 문서용 스크린샷을 다시 찍습니다 (템플릿 유지관리용 — 수강생 레포에서는 실행할 필요가 없습니다).
//
//   node docs/img/capture.mjs            전부 다시 찍기
//   node docs/img/capture.mjs deck-code  이름을 준 것만
//
// 필요한 것: Node 22 이상(내장 WebSocket), Chrome 또는 Edge. 다른 위치의 브라우저는 CHROME=<실행 파일 경로>로 지정합니다.
// 하는 일: 레포 루트를 임시 서버로 내보내고 → headless Chrome을 띄워 → 페이지가 다 그려질 때까지(PF*.whenReady) 기다린 뒤 찍습니다.
// 렌더러나 샘플 데이터가 바뀌면 이 스크립트를 다시 실행하고, 바뀐 PNG를 커밋합니다.
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { readFile, writeFile, mkdtemp, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, normalize, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..', '..');
const P = '1-action-platformer';                       // 스크린샷에 쓰는 샘플 프로젝트

// 포트폴리오 페이지에서 절 하나를 화면 맨 위로 올리고, 그 절의 그림을 다 받을 때까지 기다림
const section = (id) => `
  document.querySelectorAll('img[loading="lazy"]').forEach((i) => { i.loading = 'eager'; });
  const el = document.getElementById(${JSON.stringify(id)});
  if (!el) throw new Error('절을 찾지 못함: ${id}');
  el.scrollIntoView({ behavior: 'instant', block: 'start' });
  await Promise.all([...el.querySelectorAll('img')].map((i) => i.complete ? 0 : new Promise((r) => { i.onload = i.onerror = r; })));`;

// [파일 이름, 주소, 폭, 높이, { run: 찍기 전에 페이지에서 실행할 JS, theme: 견본 테마로, mobile: 휴대폰 화면, scale }]
const SHOTS = [
  ['index-top', 'index.html', 1280, 800],
  ['index-project', 'index.html', 1280, 900, { run: section(`p-${P}`) }],
  ['index-features', 'index.html', 1280, 900, { run: section(`p-${P}-core`) }],
  ['index-mobile', 'index.html', 390, 844, { mobile: true, scale: 2 }],
  ['deck-cover', `project.html?p=${P}#s=cover`, 1280, 720],
  ['deck-structure', `project.html?p=${P}#s=d-enemy-fsm`, 1280, 720],
  ['deck-code', `project.html?p=${P}#s=d-axis-move`, 1280, 720],
  ['deck-choice', `project.html?p=${P}#s=d-move-choice`, 1280, 720],
  ['deck-problem', `project.html?p=${P}#s=d-corner-stuck`, 1280, 720],
  ['deck-retro', `project.html?p=${P}#s=retro`, 1280, 720],
  ['doc-top', `doc.html?p=${P}`, 1280, 900],
  ['theme-after', 'index.html', 1280, 800, { theme: true }],
];

const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.md': 'text/markdown; charset=utf-8', '.tsv': 'text/tab-separated-values; charset=utf-8',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.gif': 'image/gif', '.webp': 'image/webp', '.svg': 'image/svg+xml' };

let useSampleTheme = false;
const server = createServer(async (req, res) => {
  try {
    let path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    if (path.endsWith('/')) path += 'index.html';
    let file = normalize(join(ROOT, path));
    if (!file.startsWith(ROOT)) { res.writeHead(403).end(); return; }
    if (useSampleTheme && path === '/theme/theme.css') file = join(HERE, 'theme-sample.css');
    const body = await readFile(file);
    res.writeHead(200, { 'Content-Type': TYPES[extname(file).toLowerCase()] || 'application/octet-stream', 'Cache-Control': 'no-store' }).end(body);
  } catch { res.writeHead(404).end('not found'); }
});

function findChrome() {
  const list = [process.env.CHROME,
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', 'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', 'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser'];
  const hit = list.find((p) => p && existsSync(p));
  if (!hit) throw new Error('Chrome/Edge를 찾지 못했습니다. CHROME=<실행 파일 경로>로 지정해 주세요.');
  return hit;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function main() {
  const only = process.argv.slice(2);
  const shots = SHOTS.filter(([name]) => !only.length || only.includes(name));
  if (!shots.length) throw new Error(`그런 이름이 없습니다: ${only.join(', ')} (가능: ${SHOTS.map((s) => s[0]).join(', ')})`);

  // 표지 장면에 주소가 글자로 보이므로 포트를 고정합니다(찍을 때마다 그림이 달라지지 않게). 쓰는 중이면 PORT=<번호>로 바꿉니다.
  const port0 = +(process.env.PORT || 4699);
  await new Promise((r, j) => { server.once('error', j); server.listen(port0, '127.0.0.1', r); });
  const base = `http://127.0.0.1:${port0}/`;
  const profile = await mkdtemp(join(tmpdir(), 'pf-capture-'));
  const chrome = spawn(findChrome(), ['--headless=new', '--disable-gpu', '--no-first-run', '--hide-scrollbars', '--mute-audio',
    '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });

  try {
    // Chrome이 고른 디버깅 포트
    let port = 0;
    for (let i = 0; i < 100 && !port; i++) {
      await sleep(100);
      try { port = +(await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; } catch { /* 아직 */ }
    }
    if (!port) throw new Error('Chrome 디버깅 포트를 읽지 못했습니다.');
    const tab = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json();
    const ws = new WebSocket(tab.webSocketDebuggerUrl);
    await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });

    let seq = 0;
    const waiting = new Map();
    const events = [];
    ws.onmessage = (e) => {
      const m = JSON.parse(e.data);
      if (m.id) { const w = waiting.get(m.id); waiting.delete(m.id); if (w) (m.error ? w.j(new Error(m.error.message)) : w.r(m.result)); } else {
        for (const ev of events.splice(0)) (ev.name === m.method ? ev.r() : events.push(ev));
      }
    };
    const send = (method, params = {}) => new Promise((r, j) => { waiting.set(++seq, { r, j }); ws.send(JSON.stringify({ id: seq, method, params })); });
    const once = (name) => new Promise((r) => events.push({ name, r }));
    const go = async (url) => { const loaded = once('Page.loadEventFired'); await send('Page.navigate', { url }); await loaded; };
    await send('Page.enable');

    for (const [name, url, width, height, opt = {}] of shots) {
      useSampleTheme = !!opt.theme;
      await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: opt.scale || 1, mobile: !!opt.mobile });
      await go('about:blank');                         // 같은 문서의 #만 바뀌면 다시 읽지 않으므로 매번 빈 화면에서 시작
      await go(base + url);
      const res = await send('Runtime.evaluate', { awaitPromise: true, returnByValue: true, expression: `(async () => {
        const t0 = Date.now();
        const api = () => window.PFPage || window.PFScene || window.PFDoc;
        while (!api()) { if (Date.now() - t0 > 20000) throw new Error('페이지가 준비되지 않음'); await new Promise((r) => setTimeout(r, 100)); }
        await api().whenReady();
        ${opt.run || ''}
        await document.fonts.ready;
        await new Promise((r) => setTimeout(r, 800));
        const rep = api().report();
        return { errors: rep.errors || [], audit: (rep.audit || []).length };
      })()` });
      if (res.exceptionDetails) throw new Error(`${name}: ${res.exceptionDetails.exception?.description || res.exceptionDetails.text}`);
      const info = res.result.value || {};
      const { data } = await send('Page.captureScreenshot', { format: 'png' });
      const buf = Buffer.from(data, 'base64');
      await writeFile(join(HERE, `${name}.png`), buf);
      console.log(`${name}.png  ${width}x${height}${opt.scale ? ` @${opt.scale}x` : ''}  ${(buf.length / 1024).toFixed(0)}KB` +
        (info.errors?.length ? `  ⚠ 오류 ${info.errors.length}` : '') + (info.audit ? `  ⚠ 빨간 띠 ${info.audit}` : ''));
    }
    ws.close();
  } finally {
    chrome.kill();
    server.close();
    await sleep(300);
    await rm(profile, { recursive: true, force: true }).catch(() => {});
  }
}

main().catch((e) => { console.error(e.message || e); process.exit(1); });
