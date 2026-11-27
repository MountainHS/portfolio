#!/usr/bin/env node
// Node 22+ and an installed Chromium browser. Start the local preview first.
import { spawn } from 'node:child_process';
import { access, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseArgs } from 'node:util';
import { checkDeckPage, readPrintDeck } from './deck-checks.mjs';

const usage = 'node tools/check-deck.mjs <project-id> | --all [--url <local preview>] [--browser <path>] [--output <json>] [--print] [--pdf <path>]';
const pause = (ms) => new Promise((done) => setTimeout(done, ms));
const probes = ['swapped-layout', 'missing-highlight', 'missing-code', 'wrong-code-text', 'wrong-retro-pair', 'uneven-link-card', 'uneven-screen-cell', 'scene-count-toc', 'diagram-card-overlap'];

export async function run(args, { flatReport = false } = {}) {
  const { positionals, values } = parseArgs({ args, allowPositionals: true, options: {
    all: { type: 'boolean' }, url: { type: 'string' }, browser: { type: 'string' },
    output: { type: 'string' }, print: { type: 'boolean' }, pdf: { type: 'string' },
    probe: { type: 'string' }, help: { type: 'boolean' }
  } });
  if (values.help) { console.log(usage); return; }
  if (positionals.length + Number(Boolean(values.all)) !== 1) throw new Error(usage);
  if (values.pdf && values.all) throw new Error('--pdf requires a single project.');
  if (values.probe && (values.all || !probes.includes(values.probe))) {
    throw new Error(`--probe requires a single project and one of: ${probes.join(', ')}.`);
  }
  const base = new URL(values.url || 'http://127.0.0.1:4600');
  if (!['http:', 'https:'].includes(base.protocol) || !['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname)) {
    throw new Error('Use an HTTP(S) server on localhost for the deck check.');
  }
  const available = (await readdir(new URL('../data/projects/', import.meta.url), { withFileTypes: true }))
    .filter((entry) => entry.isFile() && entry.name.endsWith('.json'))
    .map((entry) => basename(entry.name, '.json')).sort();
  const ids = values.all ? available : positionals;
  if (!ids.length || !ids.every((id) => available.includes(id))) throw new Error(`Projects available: ${available.join(', ')}`);

  const projects = [];
  await withBrowser(values.browser, async (cdp, consoleErrors) => {
    for (const id of ids) projects.push(await checkProject(id, base, values, cdp, consoleErrors));
  });
  const checkedAt = new Date().toISOString();
  const passed = projects.every((project) => project.passed);
  const report = flatReport ? { checkedAt, ...projects[0], projects, passed } : { checkedAt, passed, projects };
  if (values.output) await writeFile(resolve(values.output), `${JSON.stringify(report, null, 2)}\n`);
  const summary = projects.map((project) => {
    const checks = [...project.commonChecks, ...project.checks];
    return {
      projectId: project.projectId, passed: project.passed, scenes: project.sceneIds.length,
      projectChecks: project.checks.length, commonChecks: project.commonChecks.length,
      codeReviewScenes: project.codeReview?.length ?? 0,
      findings: checks.filter((check) => !check.pass || check.level === 'note'),
      warnings: checks.filter((check) => !check.pass && check.level === 'warning').length,
      ...(flatReport ? { checks: checks.map(({ name, pass }) => ({ name, pass })) } : {}),
      consoleErrors: project.consoleErrors
    };
  });
  const legacySummary = flatReport ? { url: projects[0].url, checks: summary[0].checks, consoleErrors: projects[0].consoleErrors } : {};
  console.log(JSON.stringify({ checkedAt, passed, projects: summary, ...legacySummary, output: values.output || null }, null, 2));
  process.exitCode = passed ? 0 : 1;
}

async function withBrowser(requested, runChecks) {
  const candidates = [
    process.env.PROGRAMFILES && join(process.env.PROGRAMFILES, 'Google/Chrome/Application/chrome.exe'),
    process.env['PROGRAMFILES(X86)'] && join(process.env['PROGRAMFILES(X86)'], 'Microsoft/Edge/Application/msedge.exe')
  ].filter(Boolean);
  let browserPath = requested;
  for (const candidate of candidates) {
    if (browserPath) break;
    try { await access(candidate); browserPath = candidate; }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  if (!browserPath) throw new Error('Pass an installed Chromium executable with --browser.');

  const profile = await mkdtemp(join(tmpdir(), 'portfolio-deck-check-'));
  const browser = spawn(browserPath, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
  let browserError;
  browser.on('error', (error) => { browserError = error; });
  const browserClosed = new Promise((done) => browser.once('close', done));
  let socket;
  try {
    let port;
    for (let i = 0; i < 100; i++) {
      if (browserError) throw browserError;
      if (browser.exitCode !== null) throw new Error(`Browser exited during startup: ${browser.exitCode}`);
      try { port = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
      catch (error) { if (error.code !== 'ENOENT') throw error; }
      await pause(100);
    }
    if (!port) throw new Error('Browser DevTools endpoint did not start in 10 seconds.');
    const response = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT', signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw new Error(`Browser DevTools returned HTTP ${response.status}.`);
    const target = await response.json();
    socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((done, reject) => {
      const timeout = setTimeout(() => reject(new Error('Browser WebSocket connection timed out.')), 10000);
      socket.addEventListener('open', () => { clearTimeout(timeout); done(); }, { once: true });
      socket.addEventListener('error', () => { clearTimeout(timeout); reject(new Error('Browser WebSocket connection failed.')); }, { once: true });
    });
    const errors = [];
    const pending = new Map();
    let sequence = 0;
    socket.addEventListener('message', ({ data }) => {
      const message = JSON.parse(data);
      const task = pending.get(message.id);
      if (task) {
        pending.delete(message.id);
        clearTimeout(task.timeout);
        if (message.error) task.reject(new Error(`${task.method}: ${message.error.message}`));
        else task.done(message.result);
      } else if (message.method === 'Runtime.exceptionThrown' ||
          (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error')) errors.push(message);
    });
    socket.addEventListener('close', () => {
      for (const task of pending.values()) {
        clearTimeout(task.timeout);
        task.reject(new Error(`Browser disconnected during ${task.method}.`));
      }
      pending.clear();
    });
    const cdp = (method, params = {}) => new Promise((done, reject) => {
      if (socket.readyState !== WebSocket.OPEN) return reject(new Error(`Browser is disconnected before ${method}.`));
      const id = ++sequence;
      const timeout = setTimeout(() => { pending.delete(id); reject(new Error(`${method} timed out.`)); }, 60000);
      pending.set(id, { method, done, reject, timeout });
      socket.send(JSON.stringify({ id, method, params }));
    });
    await cdp('Page.enable');
    await cdp('Runtime.enable');
    await cdp('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
    await runChecks(cdp, errors);
  } finally {
    socket?.close();
    browser.kill();
    await browserClosed;
    if (dirname(resolve(profile)) !== resolve(tmpdir()) || !basename(profile).startsWith('portfolio-deck-check-')) {
      throw new Error(`Refusing to remove an unexpected browser profile path: ${profile}`);
    }
    await rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
  }
}

async function evaluate(cdp, fn, ...parameters) {
  const result = await cdp('Runtime.evaluate', {
    expression: `(${fn.toString()})(${parameters.map((arg) => JSON.stringify(arg)).join(',')})`,
    awaitPromise: true, returnByValue: true, timeout: 45000
  });
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
  return result.result.value;
}

async function navigate(cdp, url) {
  const opened = await cdp('Page.navigate', { url: url.href });
  if (opened.errorText) throw new Error(`Cannot open ${url.href}: ${opened.errorText}`);
  await evaluate(cdp, async (expected) => {
    const stop = Date.now() + 35000;
    const current = () => `${location.origin}${location.pathname}${location.search}`;
    while (current() !== expected || !window.PFScene?.whenReady) {
      if (Date.now() >= stop) throw new Error(`Deck did not initialize at ${expected}`);
      await new Promise((done) => setTimeout(done, 50));
    }
    while (PFScene.ready !== 'yes') {
      if (Date.now() >= stop) throw new Error(`Deck readiness timed out at ${expected}`);
      await new Promise((done) => setTimeout(done, 50));
    }
    await PFScene.whenReady();
  }, `${url.origin}${url.pathname}${url.search}`);
}

async function checkProject(projectId, base, options, cdp, errors) {
  const start = errors.length;
  const url = new URL('project.html', base);
  url.searchParams.set('p', projectId);
  url.searchParams.set('view', 'scroll');
  const project = { projectId, url: url.href, sceneIds: [], commonChecks: [], checks: [] };
  const clean = () => [...project.commonChecks, ...project.checks].every((check) => check.pass || check.level === 'warning') && start === errors.length;
  const add = (list, name, pass, expected, evidence, sceneId = null) => list.push({ name, pass, expected, evidence, sceneId });
  let pdfData;
  try {
    await navigate(cdp, url);
    const screen = await evaluate(cdp, checkDeckPage, projectId, options.probe || null);
    project.sceneIds = screen.ids;
    project.commonChecks = screen.checks;
    project.codeReview = screen.codeReview;
    project.retroReview = screen.retroReview;

    if (options.print || options.pdf) {
      try {
        await cdp('Emulation.setEmulatedMedia', { media: 'print' });
        const print = await evaluate(cdp, readPrintDeck);
        project.print = { ids: print.ids, links: print.links };
        project.commonChecks.push(...print.checks);
        if (options.pdf && clean()) {
          const pdf = await cdp('Page.printToPDF', { preferCSSPageSize: true, printBackground: true, displayHeaderFooter: false });
          pdfData = Buffer.from(pdf.data, 'base64');
        }
      } finally {
        await cdp('Emulation.setEmulatedMedia', { media: '' });
      }
    }

    for (const [oldKey, expectedKey] of Object.entries(screen.aliases)) {
      const oldId = `d-${oldKey}`, expectedId = `d-${expectedKey}`;
      const aliasUrl = new URL(url);
      aliasUrl.searchParams.set('check-alias', oldId);
      aliasUrl.hash = `s=${oldId}`;
      await navigate(cdp, aliasUrl);
      const alias = await evaluate(cdp, () => {
        const report = PFScene.report();
        return { scene: report.ids[report.cur - 1], hash: location.hash };
      });
      add(project.checks, `old scene URL ${oldId} resolves to ${expectedId}`, alias.scene === expectedId, expectedId, alias, expectedId);
    }
    if (pdfData && clean()) {
      await writeFile(resolve(options.pdf), pdfData, { flag: 'wx' });
      project.print.pdf = resolve(options.pdf);
    }
  } catch (error) {
    add(project.commonChecks, 'deck check completed', false, 'All requested checks ran', error.message);
  }
  for (const check of [...project.commonChecks, ...project.checks]) {
    check.page = project.sceneIds.indexOf(check.sceneId) + 1 || null;
  }
  project.consoleErrors = errors.slice(start);
  project.passed = clean();
  return project;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  await run(process.argv.slice(2));
}
