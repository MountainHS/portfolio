#!/usr/bin/env node
// Node 22+; no npm packages. Start edit-server.py first, then:
// node tools/check-wgo-deck.mjs --url http://127.0.0.1:4600 --output <report.json>
// A restricted desktop sandbox may require local-network execution permission.
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, writeFile, rm, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';

const args = process.argv.slice(2);
const options = {};
for (let i = 0; i < args.length; i += 2) {
  if (!['--url', '--browser', '--output'].includes(args[i]) || !args[i + 1]) throw new Error('Usage: node tools/check-wgo-deck.mjs [--url http://127.0.0.1:4600] [--browser <executable>] [--output <json>]');
  options[args[i].slice(2)] = args[i + 1];
}
const base = new URL(options.url || 'http://127.0.0.1:4600');
if (!['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname)) throw new Error('This check only opens a local preview server.');
let browserPath = options.browser;
if (!browserPath) {
  for (const candidate of [process.env.PROGRAMFILES && join(process.env.PROGRAMFILES, 'Google/Chrome/Application/chrome.exe'), process.env['PROGRAMFILES(X86)'] && join(process.env['PROGRAMFILES(X86)'], 'Microsoft/Edge/Application/msedge.exe')].filter(Boolean)) {
    try { await access(candidate); browserPath = candidate; break; } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
}
if (!browserPath) throw new Error('Pass an installed Chromium executable with --browser.');
const profile = await mkdtemp(join(tmpdir(), 'wgo-layout-check-'));
const browser = spawn(browserPath, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
const pause = (ms) => new Promise((done) => setTimeout(done, ms));
let socket;
try {
  let port;
  for (let i = 0; i < 100; i++) {
    try { port = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    if (browser.exitCode !== null) throw new Error(`Browser exited during startup: ${browser.exitCode}`);
    await pause(100);
  }
  if (!port) throw new Error('Browser DevTools endpoint did not start in 10 seconds.');
  const target = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' }).then((r) => r.json());
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((done, reject) => { socket.addEventListener('open', done, { once: true }); socket.addEventListener('error', reject, { once: true }); });
  let sequence = 0;
  const pending = new Map(), consoleErrors = [];
  socket.addEventListener('message', ({ data }) => {
    const message = JSON.parse(data);
    if (message.id) {
      const task = pending.get(message.id); pending.delete(message.id);
      if (message.error) task.reject(new Error(JSON.stringify(message.error))); else task.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown' || (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error')) consoleErrors.push(message);
  });
  const cdp = (method, params = {}) => new Promise((resolve, reject) => { const id = ++sequence; pending.set(id, { resolve, reject }); socket.send(JSON.stringify({ id, method, params })); });
  await cdp('Page.enable'); await cdp('Runtime.enable');
  await cdp('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
  const url = new URL('project.html?p=4-wgo&view=scroll', base).href;
  await cdp('Page.navigate', { url });
  const result = await cdp('Runtime.evaluate', { expression: `(${checkPage.toString()})()`, awaitPromise: true, returnByValue: true, timeout: 45000 });
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  await cdp('Page.addScriptToEvaluateOnNewDocument', { source: `const originalFetch = window.fetch; window.fetch = async (...args) => { const response = await originalFetch(...args); if (!String(args[0]).includes('data/projects/4-wgo.json')) return response; const data = await response.json(); const points = data.deck.slides.find(s => s.key === 'ts-socket-leave').points; const point = points.find(p => p.label === '2차 해결'); if (!point) throw new Error('Missing numbered solution fixture'); point.label = '3차 해결'; return new Response(JSON.stringify(data), {status: response.status, headers: response.headers}); };` });
  await cdp('Page.navigate', { url });
  const variant = await cdp('Runtime.evaluate', { expression: `(${checkPage.toString()})(true)`, awaitPromise: true, returnByValue: true, timeout: 45000 });
  if (variant.exceptionDetails) throw new Error(JSON.stringify(variant.exceptionDetails));
  result.result.value.checks.push(...variant.result.value.checks);
  const report = { url, checkedAt: new Date().toISOString(), ...result.result.value, consoleErrors };
  report.passed = report.checks.every((check) => check.pass) && !consoleErrors.length;
  const json = JSON.stringify(report, null, 2);
  if (options.output) await writeFile(resolve(options.output), `${json}\n`);
  console.log(JSON.stringify({ url, passed: report.passed, checks: report.checks.map(({ name, pass }) => ({ name, pass })), consoleErrors, output: options.output || null }, null, 2));
  process.exitCode = report.passed ? 0 : 1;
} finally {
  socket?.close(); browser.kill();
  await new Promise((done) => browser.exitCode !== null ? done() : browser.once('exit', done));
  // Only this process's mkdtemp directory is removed.
  if (dirname(resolve(profile)) !== resolve(tmpdir()) || !basename(profile).startsWith('wgo-layout-check-')) throw new Error(`Refusing to remove an unexpected browser profile path: ${profile}`);
  await rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
}

async function checkPage(solutionVariant = false) {
  const delay = (ms) => new Promise((done) => setTimeout(done, ms));
  const deadline = Date.now() + 35000;
  while (!window.PFScene && Date.now() < deadline) await delay(50);
  if (!window.PFScene) throw new Error('PFScene was not initialized.');
  await Promise.race([PFScene.whenReady(), new Promise((_, reject) => setTimeout(() => reject(new Error('PFScene readiness timed out.')), 35000))]);
  const checks = [];
  const check = (name, pass, evidence) => checks.push({ name, pass: Boolean(pass), evidence });
  const scene = (id) => { const el = document.getElementById(id); if (!el) throw new Error(`Missing required scene #${id}`); return el; };
  const rect = (el) => { const r = el.getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height, cx: r.x + r.width / 2, cy: r.y + r.height / 2 }; };
  const spread = (values) => Math.max(...values) - Math.min(...values);
  const all = (root, selector) => [...root.querySelectorAll(selector)];
  const required = (root, selector) => { const el = root.querySelector(selector); if (!el) throw new Error(`Missing required ${selector} in #${root.id}`); return el; };
  const normalized = (text) => text.replace(/\s+/g, ' ').trim();
  const visibleText = (el) => { const clone = el.cloneNode(true); clone.querySelectorAll('style, script').forEach((node) => node.remove()); return normalized(clone.textContent); };
  const sockets = scene('d-ts-socket-leave');
  const chips = all(sockets, '.labelchip');
  const expectedSolutions = solutionVariant ? ['1차 해결', '3차 해결'] : ['1차 해결', '2차 해결'];
  const solutions = expectedSolutions.map((label) => chips.find((el) => normalized(el.textContent) === label));
  const color = (el) => { const style = getComputedStyle(el); return { background: style.backgroundColor, color: style.color }; };
  const solutionColors = solutions.map((el) => el && color(el));
  const others = chips.filter((el) => !solutions.includes(el));
  check(`${solutionVariant ? 'variant: ' : ''}p46 numbered solutions share accent; other chips retain their tones`, solutions.every((el) => el?.classList.contains('accent')) && new Set(solutionColors.map((c) => JSON.stringify(c))).size === 1 && others.every((el) => !el.classList.contains('accent') && color(el).background !== solutionColors[0].background), { labels: chips.map((el) => el.textContent), solutionColors, others: others.map((el) => ({ label: el.textContent, ...color(el) })) });
  if (solutionVariant) return { checks };
  const report = PFScene.report();
  check('48 ready scenes and clean layout audit', report.ready === 'yes' && report.scenes === 48 && report.printScenes === 48 && !report.audit.length && !report.clipped && !report.outsideDeck && !report.info.errors.length && !report.info.overflow.length && !report.info.unresolved.length, report);
  // Scenario-specific assertions below intentionally inspect rendered geometry,
  // rather than accepting a clean PFScene audit as evidence of user feedback.
  const overview = scene('ch-overview');
  const overviewLinks = all(overview, '.toc-list a');
  check('p3 overview index has titles, sequence numbers, page links and no tab labels', overviewLinks.length === 8 && !overview.querySelector('.toc-list .d') && overviewLinks.every((a) => a.querySelector('.n')?.textContent.trim() && a.querySelector('.t')?.textContent.trim() && /p\.\d+/.test(a.querySelector('.pg')?.textContent) && document.getElementById(a.dataset.pfRef)), overviewLinks.map((a) => ({ text: normalized(a.textContent), target: a.dataset.pfRef })));
  check('other chapters retain tab labels', all(scene('ch-structure'), '.toc-list .d').length === 4, all(scene('ch-structure'), '.toc-list .d').map((el) => el.textContent));

  const core = scene('d-core-loop'), body = required(core, '.pf-body'), steps = required(core, '.dk-steps');
  const bodyBox = rect(body), stepsBox = rect(steps), scale = rect(core).width / 1280;
  const boxes = all(core, '.step-diagram svg > g[transform] rect').map(rect);
  const explanations = all(core, '.step-explanations > p');
  const explanationBoxes = explanations.map(rect);
  check('p8 sparse loop group is vertically centered', stepsBox.height < bodyBox.height * .8 && Math.abs(stepsBox.cy - bodyBox.cy) <= 3 * scale, { bodyBox, stepsBox, scale });
  check('p8 four explanation columns center under their nodes', boxes.length === 4 && explanations.length === 4 && boxes.every((box, i) => Math.abs(box.cx - explanationBoxes[i].cx) <= 3 * scale) && explanations.every((p) => getComputedStyle(p).textAlign === 'center'), { boxes, explanationBoxes });
  // Exercise both sparse and near-full body content with the same existing wrapper.
  const fill = required(core, '.pf-fill'); const saved = fill.innerHTML;
  const centerFixtures = [];
  for (const ratio of [.2, .95]) {
    fill.innerHTML = `<div style="height:${body.clientHeight * ratio}px"></div>`;
    const filled = rect(fill), parent = rect(body);
    centerFixtures.push({ ratio, centerDelta: Math.abs(filled.cy - parent.cy) / scale, height: filled.height / scale });
  }
  fill.innerHTML = saved;
  check('body auto margins center both sparse and near-full content', centerFixtures.every((fixture) => fixture.centerDelta <= 3), centerFixtures);

  const network = scene('d-flow'), nodes = all(network, '.cycle-node rect').map(rect), edges = all(network, '.cycle-edge');
  const returnPath = required(network, '.cycle-return path');
  const returnPoints = [returnPath.getPointAtLength(0), returnPath.getPointAtLength(returnPath.getTotalLength())].map(({ x, y }) => ({ x, y }));
  check('p13 four primary nodes share baseline and retain a return arrow', nodes.length === 4 && spread(nodes.map((r) => r.cy)) <= 2 * scale && edges.length === 4 && returnPoints[0].x > returnPoints[1].x && returnPath.hasAttribute('marker-end'), { nodes, returnPoints });
  check('p13 post-project note remains inside its node', all(network, '.cycle-node').some((node) => /프로젝트 종료 후/.test(node.textContent)), all(network, '.cycle-node').map((node) => normalized(node.textContent)));

  for (const id of ['d-bridge-data', 'd-no-prediction']) {
    const root = scene(id), layout = required(root, '.dk-side-notes');
    const cards = all(layout, ':scope > .cards .ncard').map(rect);
    const diagram = required(layout, ':scope > .vis, :scope > .point-label-flow');
    const diagramBox = rect(diagram);
    check(`${id}: three left points stack beside a right diagram/flow`, cards.length === 3 && spread(cards.map((r) => r.x)) <= 2 * scale && cards.every((r, i) => !i || r.y >= cards[i - 1].y + cards[i - 1].height - scale) && diagramBox.x >= cards[0].x + cards[0].width, { cards, diagramBox });
  }
  const predictionFlow = all(scene('d-no-prediction'), '.point-label-flow li');
  check('p31 conditional flow retains unimplemented prediction decision', predictionFlow.length === 3 && predictionFlow[2].textContent === '예측 검토 · 미적용', predictionFlow.map((el) => el.textContent));
  const modelCell = required(scene('d-sync-model'), 'tbody tr:first-child td:first-child').textContent;
  check('p30 removes only the requested parenthetical', !modelCell.includes('받은 뼈대 방식') && normalized(modelCell).includes('각자 계산한 결과를 보냄'), normalized(modelCell));

  const stageTable = required(scene('d-snapshot-stage2'), 'table.stage-table');
  const rows = all(stageTable, 'tr');
  const geometry = rows.map((row) => all(row, ':scope > th, :scope > td').map(rect));
  const rowText = rows.map(visibleText);
  check('p38 real table has three shared rows and equal-width corresponding columns', rows.length === 3 && geometry.every((row) => row.length === 3 && spread(row.map((r) => r.width)) <= 2 * scale && spread(row.map((r) => r.y)) <= 2 * scale) && geometry[0].every((r, i) => geometry.every((row) => Math.abs(row[i].x - r.x) <= 2 * scale)) && stageTable.querySelectorAll('svg').length === 3, { geometry, rowText });
  check('p38 stage timing and actual project result remain', rowText[0].includes('1단계') && rowText[0].includes('2단계') && rowText[0].includes('3단계') && rowText[2].includes('9초') && rowText[2].includes('방 전체 정지가 사라') && rowText[2].includes('폴리싱'), rowText);
  return { checks };
}

