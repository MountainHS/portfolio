// 슬라이드(project.html?p=<폴더 이름>) — 프로젝트 문서를 바탕으로 한 16:9 발표 슬라이드(결정 G1·G2·G6·G11)
//   장면 순서: 표지(cover) → 목차(toc) → [장 표지(ch-<장 key>) → 장면(d-<key>, 나뉘면 d-<key>-2…)] × 장 → 결과와 회고(retro) → 맺음(end)
//   장면 목록은 data/projects/<id>.json의 deck(/portfolio-build가 원문을 읽고 고름). 그림·코드·표·이미지는 원문 md 블록을 가리키기만 하고
//   여기서 원문을 찾아 **그대로** 그립니다(PF.md, 규칙은 .claude/skills/portfolio-build/schema.md 「슬라이드」). 장면 글은 deck의 글 조각뿐입니다.
//   deck이 없는 예전 데이터는 highlights·troubleshooting·screenshots·retro로 자동 구성하고 장면마다 띠로 알립니다.
//   옛 주소(#s=hl-n·ts-n·shots·more, #pf-*)는 가까운 장면으로 넘기고, 옛 프로젝트 문서 제목 앵커는 상세 문서로 보냅니다(결정 G3).
//   상세 문서 링크는 표지와 맺음 장면에만 둡니다(결정 G1). 새 문장·수치를 만들지 않습니다(장면 이름·라벨은 UI 글자 — 문구 표 PF.t).
//   data의 탭·카드 라벨은 PF.tab()·PF.label()로 보여 주기만 바꾸고, 칩 색(FLOW)은 data 값 그대로 판정합니다.
//   주석의 설계 근거(docs/design/…, 결정 F·G·Q 번호)는 템플릿 유지관리용 maintainer 브랜치에 있습니다. 이 레포에 없어도 정상입니다.
(() => {
  const Q = new URLSearchParams(location.search);
  const id = Q.get('p');
  const deck = document.getElementById('deck');
  const info = (PFScene.info = { page: 'project', id, t: {}, summary: {}, overflow: [], errors: [], unresolved: [], notes: [], mode: '' });
  const mark = (k) => { info.t[k] = Math.round(performance.now()); };
  const { esc, shortUrl, DOT, link } = PF;
  // 절 번호·파일:줄 괄호 참조는 슬라이드에서 숨김(이 포크 전용, PF.md.dropRefs). 번호 → 제목 id는 문서를 읽은 뒤 채움
  let refNumbers;
  const dropRefs = (text) => PF.md.dropRefs(text, refNumbers);
  const inline = (s) => dropRefs(PF.inline(s));
  const MAXL = 24;                                                     // 코드 한 장면 24줄까지(이 포크: 20 → 24), 넘으면 "계속" 장면

  // ── 옛 주소(결정 G3) ──
  //   #s=… · #<숫자> · #s-n · 새 장면 id → 슬라이드가 받음(옛 장면 id는 그린 뒤 remapHash가 바꿈) / #pf-doc → 상세 문서 /
  //   #pf-highlights 등 → 옛 장면 id로 바꾼 뒤 같은 처리 / 그 밖(옛 프로젝트 문서 제목 앵커) → 상세 문서의 같은 조각
  const SCENE = /^(cover|toc|ch-[a-z][a-z0-9-]*|d-[a-z0-9-]+|retro(-\d+)?|end|notice|hl-\d+(-\d+)?|ts-\d+(-\d+)?|shots(-\d+)?|more)$/;
  const LEGACY = { 'pf-highlights': 'hl-1', 'pf-troubleshooting': 'ts-1', 'pf-screenshots': 'shots', 'pf-retro': 'retro' };
  const redirected = (() => {
    if (!id || !location.hash || location.hash === '#') return false;
    const rawEnc = location.hash.slice(1);
    let raw = rawEnc; try { raw = decodeURIComponent(rawEnc); } catch { /* 그대로 */ }
    if (/^s=/.test(raw) || /^-?\d+$/.test(raw) || /^s-\d+$/.test(raw) || SCENE.test(raw)) return false;
    if (raw === 'pf-doc') { location.replace(PF.docUrl(id)); return true; }
    if (LEGACY[raw]) { try { history.replaceState(null, '', `${location.pathname}${location.search}#s=${LEGACY[raw]}`); } catch { /* */ } return false; }
    location.replace(PF.docUrl(id, rawEnc));
    return true;
  })();
  if (redirected) return;

  PFScene.track(start().catch((err) => {
    info.errors.push(String(err && err.stack || err));
    deck.replaceChildren(PF.msgScene({ title: '페이지를 그리지 못했습니다', html: `<p>${esc(err && err.message || err)}</p>` }));
  }).finally(async () => {
    await PF.loadStrings();                                            // 아래 띠(HUD)도 문구 표로 — 안내 장면으로 끝난 경우에도
    info.strings = PF.stringsReport();
    PF.dropMeasure(); mmdHost?.remove();
    PFScene.mount({ deck: '#deck', hint: link({ href: id ? PF.docUrl(id) : 'index.html', label: esc(PF.t('deck.mobile-doc')), kind: 'page' }),
      pptx: window.PFPptx && info.meta ? (b) => PFPptx.save(b) : null }); mark('mounted');   // [PPTX로 저장]은 슬라이드를 그렸을 때만(assets/deck-pptx.js)
  }));

  async function start() {
    mark('start');
    if (PF.isFile()) { deck.replaceChildren(PF.msgScene({ title: '로컬 서버로 열어 주세요', html: PF.FILE_HELP })); return; }
    const strings = PF.loadStrings();
    const back = `<p>${link({ href: 'index.html', label: '← 포트폴리오로', kind: 'page' })}</p>`;
    if (!id) { info.errors.push('no-project: 주소에 ?p=<폴더 이름>이 없습니다'); deck.replaceChildren(PF.msgScene({ title: '프로젝트를 고르세요', html: `<p>주소 끝에 <code>?p=폴더이름</code>을 붙여 주세요.</p>${back}` })); return; }
    const pf = await PF.loadJSON('data/portfolio.json').catch(() => ({}));
    PF.applyTheme(pf.settings?.accent);
    let p;
    try { p = await PF.loadJSON(`data/projects/${encodeURIComponent(id)}.json`); }
    catch (err) { info.errors.push(`data/projects/${id}.json: ${err && err.message || err}`); deck.replaceChildren(PF.msgScene({ title: '프로젝트를 찾을 수 없습니다', html: `<p>${esc(err.message)}</p>${back}` })); return; }
    let md = null;
    try { md = await PF.loadText(PF.srcPath(p.id, p.doc)); }
    catch (e) {
      info.errors.push('doc: ' + e.message);
      deck.replaceChildren(PF.msgScene({ title: '프로젝트 문서 파일을 찾지 못했습니다', eyebrow: 'PROJECT',
        html: `<p>데이터에 적힌 프로젝트 문서(<code>src/${esc(p.id)}/${esc(p.doc || '')}</code>)를 열 수 없습니다. <code>src/</code> 폴더를 확인한 뒤 <code>/portfolio-build</code>를 다시 실행해 주세요.</p><p class="pf-note-err">${esc(e.message)}</p>${back}` }));
      return;
    }
    const P = PF.md.parse(md, p.id);
    info.headings = P.headings.length;
    refNumbers = PF.md.sectionNumbers(P);
    await strings;
    mark('data');
    const render = () => renderAll(pf, p, P);
    await render();
    PF.refitOnLateFont(render);
  }

  // ── 장면 모델: deck(schemaVersion 2) 또는 자동 구성 ──
  const firstOf = (P, section) => { const hs = PF.md.findSection(P, [section]); return hs.length === 1 ? PF.md.firstVisual(P, hs[0]) : null; };
  function modelOf(p, P) {
    if (p.schemaVersion >= 2 && p.deck?.slides?.length) {
      info.mode = 'deck';
      const chapters = (p.deck.chapters || []).map((c) => ({ ...c, slides: [] }));
      const byKey = new Map(chapters.map((c) => [c.key, c]));
      p.deck.slides.forEach((sl) => {
        let c = byKey.get(sl.chapter);
        if (!c) { c = { key: sl.chapter || 'etc', title: '', sectionPath: [], slides: [] }; byKey.set(c.key, c); chapters.push(c); }
        c.slides.push(sl);
      });
      return chapters.filter((c) => c.slides.length);
    }
    // 예전 데이터 자동 구성(schema.md 「예전 데이터」): 절이 정확히 1곳으로 풀릴 때만 그 절의 첫 그림을 붙임
    info.mode = 'auto';
    const T = (text) => (text ? { text, verbatim: true } : null);
    const vis = (section) => { const v = firstOf(P, section); return v ? [{ ...v, _el: v.el, sectionPath: [section] }] : []; };
    const out = [];
    const hl = p.highlights || [], ts = p.troubleshooting || [], shots = p.screenshots || [];
    if (hl.length) out.push({ key: 'hl', title: '핵심 구현', slides: hl.map((h, i) => ({ key: `hl-${i + 1}`, layout: 'visual', tab: '구현', title: T(h.title), sectionPath: h.section ? [h.section] : [],
      points: [['기능', h.what], ['구현', h.how], ['이유', h.why]].filter(([, t]) => t).map(([label, text]) => ({ label, text })), visuals: h.section ? vis(h.section) : [] })) });
    if (ts.length) out.push({ key: 'ts', title: '이슈 해결', slides: ts.map((t, i) => ({ key: `ts-${i + 1}`, layout: 'problem', tab: '문제 해결', title: T(t.title), sectionPath: t.section ? [t.section] : [],
      points: [['문제', t.problem], ['원인', t.cause], ['해결', t.solution], ['결과', t.result], ['배운 점', t.lesson]].filter(([, x]) => x).map(([label, text]) => ({ label, text })), visuals: t.section ? vis(t.section) : [] })) });
    if (shots.length) {
      const sl = [];
      for (let i = 0; i < shots.length; i += 3) sl.push({ key: i ? `shots-${i / 3 + 1}` : 'shots', layout: 'screens', tab: '화면', title: T('실제 화면'), sectionPath: [],
        points: shots.slice(i, i + 3).map((s) => ({ label: '', text: s.caption || '' })), visuals: shots.slice(i, i + 3).map((s) => ({ kind: 'image', direct: true, src: s.src, w: s.w, h: s.h })) });
      out.push({ key: 'screens', title: '실제 화면', slides: sl });
    }
    return out;
  }

  // ── 원문 블록 → 장면에 넣을 준비(이미지 크기·다이어그램 SVG를 미리 구함) ──
  let mmdHost = null;
  const svgCache = new Map();
  const mermaidViews = new WeakMap();
  // 원문 참조·지문은 유지하고, 노드 주석 또는 선택한 subgraph의 발표용 배치를 적용합니다.
  function mermaidView(el, view) {
    if (!view?.subgraph && !view?.appendToNode && !view?.compactSequence) return el;
    let views = mermaidViews.get(el);
    if (!views) { views = new Map(); mermaidViews.set(el, views); }
    const key = JSON.stringify(view);
    if (views.has(key)) return views.get(key);
    const lines = el.querySelector('code').textContent.split('\n');
    const clone = el.cloneNode(true);
    let body = lines.join('\n');
    if (view.subgraph) {
      const start = lines.findIndex((line) => line.trim().startsWith(`subgraph ${view.subgraph}[`));
      if (start < 0) throw new Error(`Mermaid subgraph를 찾지 못했습니다: ${view.subgraph}`);
      const end = lines.findIndex((line, i) => i > start && line.trim() === 'end');
      if (end < 0) throw new Error(`Mermaid subgraph가 끝나지 않았습니다: ${view.subgraph}`);
      body = [`flowchart ${view.direction || 'TD'}`, ...lines.slice(start + 1, end)].join('\n');
    }
    if (view.appendToNode) {
      const { id: node, text } = view.appendToNode;
      if (!/^[A-Za-z][\w-]*$/.test(node)) throw new Error(`Mermaid 노드 ID가 올바르지 않습니다: ${node}`);
      const pattern = new RegExp(`\\b${node}\\["([^"\\n]+)"\\]`);
      if (!pattern.test(body)) throw new Error(`Mermaid 노드를 찾지 못했습니다: ${node}`);
      body = body.replace(pattern, (_, label) => `${node}["${label}<br/>${esc(text)}"]`);
    }
    // 가로 비교 행에서는 긴 간선 라벨도 여러 줄로 읽습니다. 글자는 원문 그대로입니다.
    const wrapLabel = (label) => label.split(/<br\s*\/?>/).map((line) => {
      let width = 0;
      return line.split(' ').map((word) => { const next = width ? width + word.length + 1 : word.length; const prefix = width ? (next > 12 ? '<br/>' : ' ') : ''; width = next > 12 ? word.length : next; return prefix + word; }).join('');
    }).join('<br/>');
    const wrapped = view.direction === 'LR' ? body.replace(/"([^"\n]+)"/g, (_, label) => `"${wrapLabel(label)}"`) : body;
    const config = view.compactSequence ? { sequence: { mirrorActors: false, diagramMarginX: 10, diagramMarginY: 6, actorMargin: 22, width: 130, height: 36, boxMargin: 4, boxTextMargin: 4, noteMargin: 4, messageMargin: 16 } }
      : view.subgraph ? { flowchart: { nodeSpacing: 18, rankSpacing: 24 } } : null;
    clone.querySelector('code').textContent = config ? [`%%{init: ${JSON.stringify(config)}}%%`, wrapped].join('\n') : wrapped;
    views.set(key, clone);
    return clone;
  }
  async function renderMermaid(els) {
    if (!els.length) return;
    if (!window.mermaid) { els.forEach((el) => svgCache.set(el, { error: 'Mermaid를 불러오지 못했습니다' })); return; }
    mermaid.initialize({ startOnLoad: false, theme: 'default', look: 'classic', layout: 'dagre', securityLevel: 'loose', fontFamily: 'Pretendard, sans-serif', suppressErrorRendering: true });
    if (!mmdHost) { mmdHost = document.createElement('div'); mmdHost.setAttribute('aria-hidden', 'true'); Object.assign(mmdHost.style, { position: 'absolute', left: '-20000px', top: '0', width: '1280px', visibility: 'hidden' }); document.body.appendChild(mmdHost); }
    let n = 0;
    for (const el of els) {
      if (svgCache.has(el)) continue;
      const text = el.querySelector('code')?.textContent || '';
      try { const { svg } = await mermaid.render(`dk-mmd-${n++}`, text, mmdHost); svgCache.set(el, { svg }); }
      catch (err) { svgCache.set(el, { error: String(err?.message || err).split('\n')[0].slice(0, 120) }); info.errors.push('mermaid: ' + (err?.message || err)); }
    }
    mmdHost.replaceChildren();
  }
  async function resolveVisuals(model, P, p) {
    const mm = [];
    for (const c of model) for (const sl of c.slides) {
      sl._v = (await Promise.all((sl.visuals || []).map(async (ref) => {
        if (ref.direct) { const d = ref.w && ref.h ? { w: ref.w, h: ref.h } : await PF.imageSize(PF.srcPath(p.id, ref.src)); return { ref, status: 'ok', src: PF.srcPath(p.id, ref.src), d }; }
        const r = ref._el ? { status: 'ok', el: ref._el } : PF.md.pick(P, ref, sl.sectionPath || []);
        if (r.status === 'missing' || r.status === 'ambiguous') { info.unresolved.push(`${sl.key}: ${r.detail}`); return { ref, ...r }; }
        if (ref.kind === 'mermaid') { const mermaidEl = mermaidView(r.el, ref.view); mm.push(mermaidEl); return { ref, ...r, mermaidEl }; }
        if (ref.kind === 'image') { const src = r.el.getAttribute('src'); const d = ref.w && ref.h ? { w: ref.w, h: ref.h } : await PF.imageSize(src); return { ref, ...r, src, d }; }
        // 표 안 그림: 불러오기 전 높이로 재면 나중에 표가 커져 조용히 잘림 → 크기를 먼저 받아 두고 복제본에 width·height를 적음(vboxHtml)
        if (ref.kind === 'table' && r.el) return { ref, ...r, imgDims: await Promise.all([...r.el.querySelectorAll('img')].map((i) => PF.imageSize(i.getAttribute('src')))) };
        return { ref, ...r };
      }))).map((v, index) => ({ ...v, index, edited: sl.edited }));          // 원문 참조 순서를 렌더링된 조각에서도 유지
    }
    await renderMermaid(mm);
  }

  // ── 시각 자료 HTML(.vbox). 코드는 줄 범위를 잘라 조각마다 강조 ──
  const LANG = { csharp: 'C#', cs: 'C#', js: 'JavaScript', javascript: 'JavaScript', ts: 'TypeScript', json: 'JSON', yaml: 'YAML', yml: 'YAML', xml: 'XML', html: 'HTML', css: 'CSS', python: 'Python', py: 'Python', bash: 'Shell', sh: 'Shell', powershell: 'PowerShell', hlsl: 'HLSL', shader: 'Shader', cpp: 'C++', c: 'C', lua: 'Lua' };
  const codeLines = (el) => { const t = (el.querySelector('code')?.textContent || '').replace(/\n$/, ''); return t.split('\n'); };
  const langOf = (el) => /language-([\w+#-]+)/.exec(el.querySelector('code')?.className || '')?.[1]?.toLowerCase() || '';
  function codeHtml(el, a, b, highlights = []) {
    if (highlights.length) {
      const gutter = [], rendered = [], marked = new Set();
      for (let n = a; n <= b; n++) {
        const active = highlights.filter((h) => n >= h.start && n <= h.end);
        const marks = active.filter((h) => {
          if (n !== Math.max(a, h.start) || marked.has(h.point)) return false;
          marked.add(h.point); return true;
        });
        gutter.push(`<span class="code-gutter-line" data-source-line="${n}">${marks.map((h) => `<span class="code-marker" data-point="${h.point}">${h.point}</span>`).join('')}</span>`);
        const one = document.createElement('div'); one.innerHTML = codeHtml(el, n, n);
        rendered.push(`<span class="code-line${active.length ? ' highlighted' : ''}" data-source-line="${n}"${active.length ? ` data-highlight-points="${[...new Set(active.map((h) => h.point))].join(' ')}"` : ''}>${one.querySelector('code').innerHTML}</span>`);
      }
      return `<span class="code-gutter" aria-hidden="true">${gutter.join('')}</span><code class="code-with-highlights" data-source-start="${a}" data-source-end="${b}">${rendered.join('\n')}</code>`;
    }
    const lang = langOf(el), text = codeLines(el).slice(a - 1, b).join('\n');
    const code = document.createElement('code');
    code.textContent = text;
    if (lang && window.hljs && hljs.getLanguage(lang)) { code.className = `language-${lang}`; try { hljs.highlightElement(code); } catch { /* 강조 없이 */ } }
    return dropRefs(code.outerHTML);
  }
  function vboxHtml(v, chunk) {
    if (v.status === 'missing' || v.status === 'ambiguous') return `<figure class="vbox err">${PF.vband(`visual-${v.status}`, v.detail, v.edited)}</figure>`;
    const warn = v.status === 'changed' ? PF.vband('visual-changed', v.detail, v.edited) : '';
    const cap = (t) => (t ? `<figcaption class="cap">${t}</figcaption>` : '');
    const k = v.ref.kind;
    if (k === 'image') {
      const alt = v.ref.caption?.text || v.el?.getAttribute('alt') || '';
      return `<figure class="vbox" data-kind="image"${v.d ? ` data-w="${v.d.w}" data-h="${v.d.h}"` : ''}><div class="vmedia">${PF.imgTag(v.src, alt, v.d)}</div>${cap(inline(alt))}</figure>`;
    }
    if (k === 'mermaid') {
      const r = svgCache.get(v.mermaidEl || v.el) || {};
      if (!r.svg) return `<figure class="vbox err">${PF.vband('visual-missing', `다이어그램을 그리지 못했습니다: ${r.error || ''}`)}</figure>`;
      return `<figure class="vbox" data-kind="mermaid" data-visual-index="${v.index}"><div class="vmedia">${r.svg}</div>${cap(v.ref.caption?.text ? inline(v.ref.caption.text) : '')}${warn}</figure>`;
    }
    if (k === 'code') {
      const all = codeLines(v.el).length, [a, b] = chunk;
      const lang = LANG[langOf(v.el)] || '';
      const part = a > 1 || b < all ? PF.t('deck.code-lines', { a, b, all }) : '';
      const highlights = v.ref.view?.highlights || [];
      return `<figure class="vbox" data-kind="code" data-visual-index="${v.index}" data-source-start="${a}" data-source-end="${b}"><div class="vmedia"><div class="fit-in"><pre${highlights.length ? ' class="code-annotated"' : ''}>${codeHtml(v.el, a, b, highlights)}</pre></div></div>${cap([lang, part].filter(Boolean).map(esc).join(' · '))}${warn}</figure>`;
    }
    if (k === 'table') {
      const t = v.el.cloneNode(true);
      t.classList.add('md-table');
      for (const cell of v.ref.view?.omitFromCells || []) {
        const td = t.querySelectorAll('tbody tr')[cell.row - 1]?.children[cell.column - 1];
        if (!td) throw new Error(`표 셀을 찾지 못했습니다: ${cell.row}, ${cell.column}`);
        const walker = document.createTreeWalker(td, NodeFilter.SHOW_TEXT), matches = [];
        while (walker.nextNode()) if (walker.currentNode.textContent.includes(cell.text)) matches.push(walker.currentNode);
        if (matches.length !== 1 || matches[0].textContent.split(cell.text).length !== 2) throw new Error(`표 셀에서 생략할 문구가 유일하지 않습니다: ${cell.row}, ${cell.column}, ${cell.text}`);
        matches[0].textContent = matches[0].textContent.replace(cell.text, '');
      }
      for (const cell of v.ref.view?.appendToCells || []) {
        const td = t.querySelectorAll('tbody tr')[cell.row - 1]?.children[cell.column - 1];
        if (!td) throw new Error(`표 셀을 찾지 못했습니다: ${cell.row}, ${cell.column}`);
        td.insertAdjacentHTML('beforeend', `<br>${inline(cell.text)}`);
      }
      t.querySelectorAll('img').forEach((i, n) => {                       // 행을 나누기 전에(원본과 같은 순서로) 크기를 적음
        const d = v.imgDims?.[n];
        if (d) { const h = Math.min(d.h, 120); i.width = Math.round((h * d.w) / d.h); i.height = h; }   // CSS max-height 120px와 같은 크기
        i.loading = 'lazy'; i.decoding = 'async';
      });
      const rows = [...t.querySelectorAll('tbody tr')];
      t.dataset.sourceRows = rows.length;
      rows.forEach((r, i) => { r.dataset.sourceRow = i; if (chunk && (i < chunk[0] || i >= chunk[1])) r.remove(); });
      return `<figure class="vbox" data-kind="table" data-visual-index="${v.index}"><div class="vmedia"><div class="fit-in">${dropRefs(t.outerHTML)}</div></div>${cap(v.ref.caption?.text ? inline(v.ref.caption.text) : '')}${warn}</figure>`;
    }
    return '';
  }

  // ── 장면 틀(deck.slides[].layout) ──
  const txt = (t) => dropRefs((t && typeof t === 'object' ? t.text : t) || '');
  const pointHtml = (pt) => {
    if (!pt.lines) return inline(txt(pt));
    if (pt.lines.length !== 2 || pt.lines.join(' ') !== txt(pt)) throw new Error('요점 lines는 원문 text와 같은 내용의 두 줄이어야 합니다');
    return pt.lines.map((line) => `<span class="point-line">${inline(line)}</span>`).join('');
  };
  const ncards = (points, { row = false, num = true, two = false } = {}) => (points.length
    ? `<div class="cards${row ? ' row' : ''}${two ? ' two' : ''}" style="--n:${Math.min(points.length, 4)}">${points.map((pt, i) =>
      `<div class="ncard${num ? '' : ' nonum'}">${num ? `<span class="num" aria-hidden="true">${i + 1}</span>` : ''}<div>${pt.label ? `<span class="lb">${esc(PF.label(pt.label))}</span>` : ''}<p>${pointHtml(pt)}</p></div></div>`).join('')}</div>` : '');
  const FLOW = { '문제': '', '원인': '', '해결': 'accent', '결과': 'ok', '배운 점': '' };   // data 값 → 칩 색(보이는 글자만 PF.label로 바뀜)
  const flowTone = (label) => /^\d+차\s+해결$/.test(label) ? 'accent' : FLOW[label] || '';
  const flowHtml = (points, dir) => `<div class="flow ${dir}" style="--n:${Math.min(points.length, 4)}">${points.map((pt) =>
    `<div class="fstep${pt.label === '결과' ? ' res' : ''}">${pt.label ? `<span class="labelchip${flowTone(pt.label) ? ' ' + flowTone(pt.label) : ''}">${esc(PF.label(pt.label))}</span>` : ''}<p>${pointHtml(pt)}</p></div>`).join('')}</div>`;
  function cycleHtml(sl) {
    const v = sl._v[0];
    if (v.status !== 'ok' && v.status !== 'changed') return `<div class="dk">${vboxHtml(v)}</div>`;
    const source = (v.mermaidEl || v.el).querySelector('code').textContent;
    const nodes = [...source.matchAll(/\b([A-Za-z][\w]*)\["([^"]+)"\]/g)].map((m) => ({ id: m[1], label: m[2] }));
    const connections = source.replace(/\b([A-Za-z][\w]*)\["([^"]+)"\]/g, '$1');
    const edges = [...connections.matchAll(/\b([A-Za-z][\w]*)\s*-->\|"([^"]+)"\|\s*([A-Za-z][\w]*)/g)].map((m) => ({ from: m[1], label: m[2], to: m[3] }));
    if (nodes.length !== 4 || edges.length !== 4 || edges.some((edge, i) => edge.from !== nodes[i].id || edge.to !== nodes[(i + 1) % 4].id)) throw new Error('cycle 배치에는 원문의 순서대로 연결된 노드 4개와 순환 간선 4개가 필요합니다');
    const labelHtml = (label) => label.split(/<br\s*\/?>/).map(esc).join('<br/>');
    const marker = `cycle-arrow-${sl.key}`;
    const boxes = nodes.map((node, i) => `<g class="cycle-node" data-node="${esc(node.id)}" transform="translate(${i * 270},0)"><rect x="12" y="82" width="216" height="116" rx="6"/><foreignObject x="22" y="90" width="196" height="100"><div xmlns="http://www.w3.org/1999/xhtml" class="step-label">${labelHtml(node.label)}</div></foreignObject></g>`).join('');
    const forward = edges.slice(0, 3).map((edge, i) => `<g class="cycle-edge"><foreignObject x="${i * 270 + 132}" y="4" width="246" height="70"><div xmlns="http://www.w3.org/1999/xhtml" class="cycle-edge-label">${labelHtml(edge.label)}</div></foreignObject><path d="M${i * 270 + 228},140 H${(i + 1) * 270 + 10}" marker-end="url(#${marker})"/></g>`).join('');
    const back = `<g class="cycle-edge cycle-return"><path d="M930 198 V245 H120 V200" marker-end="url(#${marker})"/><foreignObject x="190" y="256" width="700" height="54"><div xmlns="http://www.w3.org/1999/xhtml" class="cycle-edge-label">${labelHtml(edges[3].label)}</div></foreignObject></g>`;
    const accessible = edges.map((edge, i) => `${nodes[i].label}: ${edge.label} → ${nodes[(i + 1) % 4].label}`).join('; ').replace(/<br\s*\/?>/g, ' ');
    return `<div class="dk dk-cycle"><div class="cycle-diagram"><svg viewBox="0 0 1080 314" role="img" aria-label="${esc(accessible)}"><defs><marker id="${marker}" markerWidth="7" markerHeight="7" refX="6" refY="3.5" orient="auto"><path d="M0 0 L7 3.5 L0 7 Z"/></marker></defs>${boxes}${forward}${back}</svg></div></div>`;
  }
  function stageTableHtml(sl, vs) {
    if (vs.length !== 3 || sl.points.length !== 3) throw new Error('stage-table 배치에는 그림과 단계 설명이 각각 3개 필요합니다');
    return `<div class="dk dk-stage-table"><table class="stage-table"><thead><tr>${sl.points.map((pt) => `<th scope="col">${esc(PF.label(pt.label))}</th>`).join('')}</tr></thead><tbody><tr class="stage-diagrams">${vs.map((v) => `<td><div class="vis">${v}</div></td>`).join('')}</tr><tr class="stage-explanations">${sl.points.map((pt) => `<td><p>${pointHtml(pt)}</p></td>`).join('')}</tr></tbody></table></div>`;
  }
  function stepsHtml(sl) {
    const v = sl._v[0];
    if (v.status !== 'ok' && v.status !== 'changed') return `<div class="dk">${vboxHtml(v)}</div>`;
    const source = v.el.querySelector('code').textContent;
    const labels = [...source.matchAll(/\b[A-Za-z][\w]*\["([^"]+)"\]/g)].map((m) => m[1]);
    if (labels.length !== 4 || sl.points.length !== 4) throw new Error('steps 배치에는 원문 노드와 설명이 각각 4개 필요합니다');
    const marker = `step-arrow-${sl.key}`;
    const nodes = labels.map((label, i) => `<g transform="translate(${i * 270},0)"><rect x="12" y="8" width="246" height="70" rx="6"/><foreignObject x="22" y="14" width="226" height="58"><div xmlns="http://www.w3.org/1999/xhtml" class="step-label">${esc(label)}</div></foreignObject></g>`).join('');
    const arrows = [0, 1, 2].map((i) => `<path d="M${i * 270 + 258},43 H${(i + 1) * 270 + 10}"/>`).join('');
    const svg = `<svg viewBox="0 0 1080 120" role="img" aria-label="${esc(labels.join(' → '))}"><defs><marker id="${marker}" markerWidth="7" markerHeight="7" refX="6" refY="3.5" orient="auto"><path d="M0 0 L7 3.5 L0 7 Z"/></marker></defs>${nodes}<g class="step-edges" marker-end="url(#${marker})">${arrows}<path d="M945 78 V108 H135 V80"/></g></svg>`;
    return `<div class="dk dk-steps"><div class="step-diagram">${svg}</div><div class="step-explanations">${sl.points.map((pt) => `<p>${inline(txt(pt))}</p>`).join('')}</div></div>`;
  }
  function comparisonHtml(vs) {
    const holder = document.createElement('div'); holder.innerHTML = vs[0];
    const table = holder.querySelector('table');
    if (!table) return `<div class="dk">${vs[0]}</div>`;
    const rows = [...table.querySelectorAll('tbody tr')];
    if (+table.dataset.sourceRows !== vs.length - 1) throw new Error('comparison 배치의 표 행 수와 다이어그램 수가 다릅니다');
    const head = document.createElement('th'); head.scope = 'col'; head.textContent = '데이터 흐름';
    table.querySelector('thead tr').children[0].after(head);
    rows.forEach((row) => { const cell = document.createElement('td'); cell.className = 'comparison-flow'; cell.innerHTML = vs[+row.dataset.sourceRow + 1]; row.children[0].after(cell); });
    table.classList.add('comparison-table');
    return `<div class="dk dk-stack"><div class="vis">${holder.innerHTML}</div></div>`;
  }
  function bodyHtml(sl, vs, variant) {
    const points = sl.points || [];
    const vis = (list) => `<div class="vis">${list.join('')}</div>`;
    const tight = variant.tight ? ' tight' : '';
    if (sl.presentation === 'paired') {
      if (sl.panels.length !== 2 || (vs.length && vs.length !== 2)) throw new Error('paired 배치에는 패널 2개와 시각자료 0개 또는 2개가 필요합니다');
      return `<div class="dk dk-paired${tight}">${sl.panels.map((panel, i) => `<section class="paired-panel" data-panel="${i + 1}"><header class="panel-header"><h3>${inline(txt(panel.title))}</h3>${panel.lead ? `<p class="panel-lead">${inline(txt(panel.lead))}</p>` : ''}</header>${vs.length ? `${vis([vs[i]])}${ncards(panel.points || [], { num: false })}` : flowHtml(panel.points || [], 'col')}</section>`).join('')}</div>`;
    }
    if (sl.presentation === 'visual-notes') return `<div class="dk dk-visual-notes${tight}">${vis(vs)}${ncards(points)}</div>`;
    if (sl.presentation === 'steps') return stepsHtml(sl);
    if (sl.presentation === 'cycle') return cycleHtml(sl);
    if (sl.presentation === 'stage-table') return stageTableHtml(sl, vs);
    if (sl.presentation === 'side-notes') {
      if (points.length !== 3) throw new Error('side-notes 배치에는 요점 3개가 필요합니다');
      const diagram = vs.length ? vis(vs) : `<ol class="point-label-flow" aria-label="${esc(points.map((pt) => pt.label).join(' → '))}">${points.map((pt) => `<li>${esc(PF.label(pt.label))}</li>`).join('')}</ol>`;
      return `<div class="dk dk-side-notes${tight}">${ncards(points, { num: false })}${diagram}</div>`;
    }
    if (sl.presentation === 'comparison') return comparisonHtml(vs);
    if (sl.presentation === 'pair') return `<div class="dk dk-pair${tight}">${vs.map((v) => vis([v])).join('')}</div>`;
    if (sl.presentation === 'columns') return `<div class="dk dk-columns${tight}" style="--n:${vs.length}">${vs.map((v, i) => `<section class="dk-column">${vis([v])}${points[i]?.label ? `<h3>${esc(PF.label(points[i].label))}</h3>` : ''}${points[i] ? `<p>${inline(txt(points[i]))}</p>` : ''}</section>`).join('')}</div>`;
    if (sl.presentation === 'stack') return `<div class="dk dk-stack${tight}">${vis(vs)}${ncards(points, { row: true, num: false })}</div>`;
    if (!vs.length) {
      if (sl.layout === 'problem') return `<div class="dk dk-text${tight}">${flowHtml(points, variant.stack || variant.tight ? 'row' : 'col')}</div>`;
      return `<div class="dk dk-text${tight}">${ncards(points, { two: points.length >= 3 && !variant.stack, num: sl.layout !== 'overview' })}</div>`;
    }
    if (sl.layout === 'screens') {
      return `<div class="dk scr-row${tight}" style="--n:${vs.length}">${vs.map((v, i) => `<div class="fe-card">${vis([v])}${points[i] ? `<p class="fe-k">${esc(PF.t('en.feature'))} ${String(i + 1).padStart(2, '0')}</p>${points[i].label ? `<h3>${esc(PF.label(points[i].label))}</h3>` : ''}${txt(points[i]) ? `<p>${inline(txt(points[i]))}</p>` : ''}` : ''}</div>`).join('')}</div>`;
    }
    if (sl.layout === 'problem') {
      if (variant.stack || !points.length) return `<div class="dk dk-stack${tight}">${vis(vs)}${points.length ? flowHtml(points, 'row') : ''}</div>`;
      return `<div class="dk dk-side flip${tight}" style="--vw:${variant.vw}fr">${vis(vs)}${flowHtml(points, 'col')}</div>`;
    }
    // 요점이 없으면(나눈 장면 등) 옆 칸이 비지 않게 시각 자료를 가로 전체로
    if (sl.layout === 'full' || variant.stack || !points.length) return `<div class="dk dk-stack${tight}">${vis(vs)}${ncards(points, { row: true, num: sl.layout !== 'full' })}</div>`;
    return `<div class="dk dk-side${tight}" style="--vw:${variant.vw}fr">${vis(vs)}${ncards(points, { num: true })}</div>`;
  }

  // 장면 하나(나뉠 수 있음): 코드 줄 범위 MAXL줄 단위, 표는 넘치면 행을 반으로
  //   코드 블록마다 자기 줄 범위(ref.lines, 없으면 전체)를 따로 나눕니다 — 한 장면에 코드가 둘이어도 서로의 범위로 잘리지 않게
  function slideScenes(sl, sid, meta) {
    const vis = sl._v || [];
    const ranges = new Map();                                          // 코드 시각 자료 → [[a, b], …]
    vis.forEach((v) => {
      if (v.ref.kind !== 'code' || !(v.status === 'ok' || v.status === 'changed')) return;
      const n = codeLines(v.el).length;
      let [a, b] = Array.isArray(v.ref.lines) && v.ref.lines.length === 2 ? v.ref.lines : [1, n];
      a = Math.max(1, Math.min(a, n)); b = Math.max(a, Math.min(b, n));
      const chunks = [], len = b - a + 1, parts = Math.ceil(len / MAXL);
      for (let i = 0; i < parts; i++) { const s0 = a + Math.round((len * i) / parts), e0 = a + Math.round((len * (i + 1)) / parts) - 1; chunks.push([s0, e0]); }
      if (parts > 1) PF.note(sid, 'long-code', len, `코드 ${len}줄을 장면 ${parts}개로 나눔`);
      ranges.set(v, chunks);
    });
    // 코드 조각을 문서 순서대로 장면에 담습니다. 한 장면의 코드는 모두 합쳐 MAXL줄 안팎 — 넘으면 다음 조각부터 "계속" 장면으로
    const pages = [[]];                                                // 장면마다 [코드 시각 자료, 줄 범위] 목록
    let used = 0, longOne = false;
    vis.forEach((v) => (ranges.get(v) || []).forEach((ch, i, all) => {
      const len = ch[1] - ch[0] + 1;
      let pg = pages[pages.length - 1];
      if (pg.length && (used + len > MAXL || pg.some(([x]) => x === v))) { pg = []; pages.push(pg); used = 0; }
      pg.push([v, ch]); used += len;
      if (all.length > 1) longOne = true;
    }));
    if (pages.length > 1 && !longOne) {
      const total = [...ranges.values()].reduce((a, c) => a + c[0][1] - c[0][0] + 1, 0);
      PF.note(sid, 'long-code', total, `코드 블록 ${ranges.size}개(모두 ${total}줄)를 장면 ${pages.length}개로 나눔`);
    }
    const out = [];
    pages.forEach((pg, ci) => {
      const mine = new Map(pg);
      const part = pages.length > 1 ? `${ci + 1}/${pages.length}` : '';
      const myId = ci ? `${sid}-${ci + 1}` : sid;
      const list = vis.filter((v) => mine.has(v) || (!ci && !ranges.has(v)));       // 나뉜 뒤쪽 장면은 코드만
      const me = { ...sl, points: ci ? [] : sl.points || [], layout: ci ? 'full' : sl.layout };
      out.push(...fitSlide(me, list, myId, part, (v) => mine.get(v) || null, meta));
    });
    return out;
  }
  //   codeChunk(v) = 그 코드의 이번 줄 범위 · tsplit = { v, rows: [a, b) } 행을 나누는 표 하나(다른 표는 그대로 둠)
  function fitSlide(sl, vis, sid, part, codeChunk, meta, tsplit = null, depth = 0) {
    const vs = vis.map((v) => vboxHtml(v, v.ref.kind === 'code' ? codeChunk(v) : v.ref.kind === 'table' && tsplit && tsplit.v === v ? tsplit.rows : null));
    const codeWidths = vis.some((v) => v.ref.kind === 'code' && v.ref.view?.highlights?.length) ? [{ vw: 2.2 }, { vw: 2.4 }, { vw: 2.2, tight: true }, { vw: 2.4, tight: true }] : [];
    const V = sl.presentation || sl.layout === 'full' || sl.layout === 'screens' ? [{}, { tight: true }]
      : [{ vw: 1.4 }, { vw: 1.7 }, { vw: 1.15 }, { vw: 1.4, tight: true }, ...codeWidths, { stack: true }, { stack: true, tight: true }];
    const build = (v) => {
      const s = PF.scene({ id: sid, tpl: 'D', eyebrow: meta.chapter, rlabel: meta.rlabel, title: txt(sl.title) || meta.chapter, sub: part, lead: part && part !== `1/${part.split('/')[1]}` ? '' : txt(sl.lead), foot: meta.foot, sec: meta.chapter, group: meta.chapter, stitle: txt(sl.title) });
      s.classList.add(`l-${sl.layout || 'visual'}`);
      if (meta.err) s.dataset.pfErr = meta.err;
      if (sl.edited) s.dataset.pfEdited = '1';                       // 장면 고치기로 고친 장면(미리보기 개요에 ✎)
      PF.body(s).innerHTML = `<div class="pf-fill">${bodyHtml(sl, vs, v)}</div>`;
      if (meta.auto) s.querySelector('.pf-foot > span').insertAdjacentHTML('beforeend', '<span class="pf-autoband" data-pf-ui="note">예전 데이터로 자동 구성 — /portfolio-build를 다시 실행하면 슬라이드가 됩니다</span>');
      PF.fitVisuals(s);
      s.querySelectorAll('.comparison-flow .vbox[data-kind="mermaid"]').forEach((box) => {
        const svg = box.querySelector('svg'), vb = svg.viewBox.baseVal;
        const fin = box.closest('table').closest('.fit-in');
        const width = (parseFloat(fin.style.width) * 0.42 - 22) * +fin.dataset.pfFz;
        box.dataset.eff = (16 * Math.min(width, 132 * vb.width / vb.height) / vb.width).toFixed(1);
        box.dataset.lim = 'multi'; box.dataset.vb = `${Math.round(vb.width)}×${Math.round(vb.height)}`;
      });
      return s;
    };
    // 들어가는 변형 중 시각 자료가 가장 크게 보이는 것(같으면 앞의 것). 가로로 긴 다이어그램은 위아래 배치가, 세로로 긴 것은 옆 배치가 이김
    let best = null;
    for (const v of V) { const s = build(v); if (!PF.fitsM(s)) continue; const sc = PF.visScore(s); if (!best || sc > best.sc + 0.05) best = { s, sc }; }
    if (best) return [best.s];
    // 표가 하한에서도 안 들어가면 행을 반으로 나눠 "계속" 장면으로
    //   표가 여럿이면 행이 가장 많은 표 하나만 나눕니다(같으면 앞의 것). 나머지 표는 앞 장면에 온전히 남음
    const nrows = (v) => v.el.querySelectorAll('tbody tr').length;
    const tbl = tsplit ? tsplit.v : vis.filter((v) => v.ref.kind === 'table' && v.el).reduce((m, v) => (!m || nrows(v) > nrows(m) ? v : m), null);
    if (tbl && vis.includes(tbl) && depth < 3) {
      const rows = tsplit ? tsplit.rows : [0, nrows(tbl)];
      if (rows[1] - rows[0] >= 4) {
        const mid = rows[0] + Math.ceil((rows[1] - rows[0]) / 2);
        const a = fitSlide(sl, vis, sid, part, codeChunk, meta, { v: tbl, rows: [rows[0], mid] }, depth + 1);
        const b = fitSlide({ ...sl, points: [], lead: null, layout: 'full' }, vis.filter((v) => v === tbl || (sl.presentation === 'comparison' && v.ref.kind === 'mermaid')), `${sid}-t${depth + 1}`, part, codeChunk, meta, { v: tbl, rows: [mid, rows[1]] }, depth + 1);
        return [...a, ...b];
      }
    }
    return [PF.shrinkOrClip(build(V[V.length - 1]), PFScene.refUrl(sid), info.overflow)];
  }

  async function renderAll(pf, p, P) {
    info.notes = []; info.overflow = []; info.unresolved = [];
    const name = pf.profile?.name || '';
    const foot = `${name ? name + ' · ' : ''}${p.title}`;
    info.meta = { title: p.title, name };                               // PPTX 파일 이름·작성자(assets/deck-pptx.js)
    document.title = `${p.title} · ${PF.t('name.deck')}${name ? ' · ' + name : ''}`;
    info.fonts = await PF.loadFonts();
    mark('fonts');
    const model = modelOf(p, P);
    await resolveVisuals(model, P, p);
    mark('visuals');
    const auto = info.mode === 'auto';
    const ext = PF.extLinks(p);
    const used = new Set(['cover', 'toc', 'retro', 'end']);

    // 표지 = 프로젝트 소개 템플릿(PF.projectIntro, 결정 G4). 상세 문서 링크는 표지·맺음에만(G1)
    const cards = [
      ...ext.map((l) => ({ k: PF.extEn(l), label: l.label, href: l.url, kind: 'external', visible: shortUrl(l.url) })),
      { k: PF.t('en.techdoc'), label: PF.t('name.doc'), href: PF.docUrl(p.id), kind: 'page', visible: PF.visibleUrl(PF.docUrl(p.id)) },
      { k: PF.t('en.portfolio'), label: PF.t('name.portfolio'), href: 'index.html', kind: 'page', visible: PF.visibleUrl('') },
    ];
    const cover = await PF.projectIntro(p, { id: 'cover', titleTag: 'h1', eyebrow: PF.t('deck.cover-eyebrow'), links: cards, foot, sec: PF.t('deck.cover'), stats: info.overflow });
    if (auto) cover.querySelector('.pf-foot > span').insertAdjacentHTML('beforeend', '<span class="pf-autoband" data-pf-ui="note">예전 데이터로 자동 구성 — /portfolio-build를 다시 실행하면 슬라이드가 됩니다</span>');

    // 장 → 장 표지 + 장면들
    const chapterScenes = [];
    const tocItems = [];
    model.forEach((c, ci) => {
      const ckey = PF.keyOk(c.key) ? c.key : `c${ci + 1}`;
      let chId = `ch-${ckey}`; if (used.has(chId)) chId = `ch-c${ci + 1}`; used.add(chId);
      const slides = [];
      c.slides.forEach((sl, si) => {
        let sid = PF.keyOk(sl.key) ? `d-${sl.key}` : `d${ci + 1}-${si + 1}`, err = '';
        if (!PF.keyOk(sl.key)) err = `key-invalid: "${sl.key || ''}"`;
        if (used.has(sid)) { err = `key-invalid: "${sl.key}" 중복`; sid = `d${ci + 1}-${si + 1}`; }
        used.add(sid);
        const rl = [PF.tab(sl.tab), `${si + 1}/${c.slides.length}`].filter(Boolean).join(' · ');
        const scenes = slideScenes(sl, sid, { chapter: c.title || '', rlabel: rl, foot, auto, err });
        scenes.forEach((s) => used.add(s.id));
        slides.push({ sl, scenes });
      });
      const chs = chapterScene(c, ci, chId, slides, foot, auto, p.title);
      chapterScenes.push(chs, ...slides.flatMap((x) => x.scenes));
      tocItems.push({ c, chId, n: slides.reduce((a, x) => a + x.scenes.length, 0) });
    });
    const retro = buildRetro(p.retro || {}, p, foot, auto);
    const end = endScene(p, foot, ext, auto);
    const toc = tocScene(p, foot, tocItems, retro, auto);
    const all = [cover, toc, ...chapterScenes, ...retro, end];
    PF.noteFill(all, { fill: !auto });                                  // 예전 데이터 자동 구성은 채움률 참고를 내지 않음(다시 빌드 안내 1건만)
    if (auto) PF.note('cover', 'auto-mode', 1, '예전 데이터로 자동 구성(다시 빌드하면 슬라이드)');
    else if (all.length > 40) PF.note('cover', 'many-scenes', all.length, `장면 ${all.length}장(권장 30~40장)`);
    Object.assign(info.summary, { scenes: all.length, chapters: model.length, slides: model.reduce((a, c) => a + c.slides.length, 0), mode: info.mode });
    mark('summary');
    remapHash(p, all, model);
    deck.replaceChildren(...all);
    deck.querySelectorAll('img').forEach((i) => { if (!i.hasAttribute('loading')) i.loading = 'lazy'; i.decoding = 'async'; });
    mark('assembled');
  }

  // 옛 장면 id(#s=hl-n …)·없는 장면 id(#s=d-<key>·ch-<key>) → 새 장면 id. 마운트 전에 주소만 바꿈(키트가 그 장면으로 이동)
  function remapHash(p, scenes, model) {
    let raw = location.hash.slice(1); try { raw = decodeURIComponent(raw); } catch { /* */ }
    const m = /^(?:s=)?(.+)$/.exec(raw); if (!m) return;
    const want = m[1];
    if (scenes.some((s) => s.id === want)) return;
    let to = '';
    const hm = /^(hl|ts)-(\d+)/.exec(want);
    if (hm) {
      const list = hm[1] === 'hl' ? p.highlights || [] : p.troubleshooting || [];
      const it = list[+hm[2] - 1];
      to = info.mode === 'auto' ? `d-${hm[1]}-${hm[2]}` : it ? PF.deckSceneFor(p, it.section) : '';
      if (!to && it?.section) {                                          // 같은 장(H2)의 장 표지
        const ch = model.find((c) => (c.slides || []).some((x) => (x.sectionPath || []).some((t) => PF.normTitle(t) === PF.normTitle(it.section))));
        if (ch) to = scenes.find((s) => s.dataset.group === ch.title && s.classList.contains('t-CH'))?.id || '';
      }
    } else if (/^shots/.test(want)) to = scenes.find((s) => s.classList.contains('l-screens'))?.id || '';
    else if (want === 'more') to = 'end';
    else if (/^retro/.test(want)) to = 'retro';
    else if (/^d-/.test(want)) {                                       // 합쳐진 장면은 원래 링크도 합친 곳으로
      const alias = p.deck?.aliases?.[want.slice(2)] || p.deck?.aliases?.[want.slice(2).replace(/-(?:\d+|t\d+)$/, '')];
      to = alias ? `d-${alias}` : PF.deckSceneNear(p, want, (id) => scenes.some((s) => s.id === id));
    }
    if (!to || !scenes.some((s) => s.id === to)) to = /^(hl|ts|shots|more|retro|d-|ch-)/.test(want) ? 'toc' : '';
    if (to) try { history.replaceState(null, '', `${location.pathname}${location.search}#s=${encodeURIComponent(to)}`); } catch { /* */ }
  }

  // 목록 한 줄(같은 슬라이드 안 이동 = 같은 탭). 쪽 번호(p.N)는 키트가 채움
  //   ld = 장 표지 넉넉한 변형에서 그 장면의 키워드 한 줄(keywords를 ' · '로 이음. 없으면 빈칸 — lead로 되돌아가지 않음)
  const item = (n, t, d, ref, ld = '') => `<li><a href="#${esc(ref)}" data-pf-ref="${esc(ref)}"><span class="n">${esc(n)}</span><span class="tx"><span class="t">${inline(t)}</span>${d ? `<span class="d">${esc(d)}</span>` : ''}${ld ? `<span class="ld">${inline(ld)}</span>` : ''}</span><span class="pg"><span class="pf-n-screen">p.00</span><span class="pf-n-print"></span></span></a></li>`;
  const band = (s, auto) => { if (auto) s.querySelector('.pf-foot > span').insertAdjacentHTML('beforeend', '<span class="pf-autoband" data-pf-ui="note">예전 데이터로 자동 구성</span>'); return s; };

  // 장 표지: 장면이 적으면(4개 이하) 넉넉한 변형 — 목차 크기 목록 + 장면마다 키워드 한 줄(결정 I7). 안 들어가면 보통 → 촘촘
  // → 두 단(이 포크: 장면 수 제한을 없애 한 장에 장면이 많을 때, 목록을 위→아래 두 줄기로 나누고 목록 쪽을 넓힘)
  // → 그래도 넘치면 장면 대신 절 묶음 목록(이 포크: sectionGroupRows)
  const ROOMY_MAX = 4;
  // 장면의 키워드(deck.slides[].keywords, 문자열 배열)를 장 표지 한 줄로 이음. 배열이 아니거나 비면 ''
  const keywordLine = (sl) => (Array.isArray(sl.keywords) ? sl.keywords.filter(Boolean).join(' · ') : '');
  function chapterScene(c, ci, chId, slides, foot, auto, ptitle) {
    // rows = 목록 줄 [번호, 제목, 설명, 이동할 장면 id, 키워드 줄]
    const make = (mode, rows) => {
      const s = PF.scene({ id: chId, tpl: 'CH', kind: 'bare', cls: mode === 'compact' ? 'compact' : '', foot, sec: c.title, group: c.title, stitle: c.title });
      const roomy = mode === 'roomy';
      const cols = mode === 'cols';
      const list = rows.map((r) => item(r[0], r[1], r[2], r[3], roomy ? r[4] : '')).join('');
      PF.body(s).innerHTML = `<div class="toprow"><span class="pf-eyebrow long">${esc(ptitle)}</span><span class="rl">${esc(PF.t('deck.ch-count', { n: slides.length }))}</span></div>
        <div class="ch-mid${cols ? ' wide' : ''}"><div><p class="ch-no">${esc(PF.t('en.chapter'))} ${String(ci + 1).padStart(2, '0')}</p><h2 class="ch-t">${esc(c.title)}</h2>${DOT}</div>
        <div class="ch-list t-P2${mode === 'normal' ? '' : cols ? ' compact cols' : ' ' + mode}"><ol class="toc-list${roomy ? '' : ' sub'}"${cols ? ` style="grid-template-rows: repeat(${Math.ceil(rows.length / 2)}, auto)"` : ''}>${list}</ol></div></div>`;
      return band(s, auto);
    };
    const sceneRows = slides.map((x, i) => [String(i + 1), txt(x.sl.title) || c.title, c.hideSceneTabs ? '' : PF.tab(x.sl.tab), x.scenes[0].id, keywordLine(x.sl)]);
    const modes = slides.length <= ROOMY_MAX ? ['roomy', 'normal', 'compact'] : ['normal', 'compact', 'cols'];
    let last = null;
    for (const m of modes) { last = make(m, sceneRows); if (PF.fitsM(last)) return last; }
    // 장면 목록이 두 단으로도 안 들어가면 절로 묶어 보여 줌(이 포크, 2026-11-22). 깊은 절부터 얕은 절 순서로 시도
    for (const groupRows of sectionGroupRows(slides)) {
      for (const m of ['normal', 'compact', 'cols']) { last = make(m, groupRows); if (PF.fitsM(last)) return last; }
    }
    return PF.shrinkOrClip(last, PFScene.refUrl(chId), info.overflow);
  }

  // 장 표지의 절 묶음 목록들. 깊이(sectionPath 칸 번호)마다 이웃한 장면을 같은 절끼리 한 줄로 묶음
  //   깊이는 맨 마지막 칸(장면 자기 절) 바로 위부터 장(0) 바로 아래(1)까지. 묶어도 줄이 줄지 않는 깊이는 뺌
  //   한 줄 = [절 번호 순서, 절 제목, 장면 수, 그 절 첫 장면 id, '']
  function sectionGroupRows(slides) {
    const deepest = Math.max(...slides.map((x) => (x.sl.sectionPath || []).length)) - 2;
    const result = [];
    for (let depth = deepest; depth >= 1; depth--) {
      const groups = [];
      slides.forEach((x) => {
        const path = x.sl.sectionPath || [];
        const title = path[Math.min(depth, path.length - 1)] || '';
        const prev = groups[groups.length - 1];
        const sameAsPrev = prev && prev.title === title;
        if (sameAsPrev) { prev.scenes += x.scenes.length; return; }
        groups.push({ title, ref: x.scenes[0].id, scenes: x.scenes.length });
      });
      const fewerLines = groups.length < slides.length;
      if (fewerLines == false) continue;
      result.push(groups.map((g, i) => [String(i + 1), g.title, PF.t('deck.toc-count', { n: g.scenes }), g.ref, '']));
    }
    return result;
  }

  function tocScene(p, foot, items, retro, auto) {
    const rows = [...items.map((x, i) => [String(i + 1).padStart(2, '0'), x.c.title, PF.t('deck.toc-count', { n: x.n }), x.chId]),
      retro.length && [String(items.length + 1).padStart(2, '0'), PF.t('deck.retro'), PF.t('deck.retro-desc'), retro[0].id],
      [String(items.length + (retro.length ? 2 : 1)).padStart(2, '0'), PF.t('deck.more'), PF.t('deck.more-desc'), 'end']].filter(Boolean);
    const make = (compact) => {
      const s = PF.scene({ id: 'toc', tpl: 'P2', kind: 'bare', cls: compact ? 'compact' : '', foot, sec: PF.t('name.toc'), stitle: PF.t('name.toc') });
      const half = rows.length > 7 ? Math.ceil(rows.length / 2) : rows.length;
      const col = (list) => `<section><ol class="toc-list">${list.map((r) => item(...r)).join('')}</ol></section>`;
      PF.body(s).innerHTML = `<div class="toc-top"><p class="toc-h">${esc(PF.t('en.contents'))}</p><p class="toc-sub">${esc(p.title)}</p></div>${DOT}<div class="toc-cols${rows.length > 7 ? '' : ' one'}">${col(rows.slice(0, half))}${rows.length > half ? col(rows.slice(half)) : ''}</div>`;
      return band(s, auto);
    };
    for (const compact of [false, true]) { const s = make(compact); if (PF.fitsM(s)) return s; }
    return PF.shrinkOrClip(make(true), PFScene.refUrl('toc'), info.overflow);
  }

  // 결과와 회고: ✓ 달성 카드 │ 아쉬운 점 → 개선 계획 표. 안 들어가면 두 장으로
  function buildRetro(r, p, foot, auto) {
    const ach = p.deck?.retro?.hideAchieved ? [] : r.achieved || [], reg = r.regrets || [], plan = r.plans || [];
    const n = Math.max(reg.length, plan.length);
    const omitted = new Set(p.deck?.retro?.omitPairs || []);
    const pairs = Array.from({ length: n }, (_, i) => ({ regret: reg[i] || '', plan: plan[i] || '' }))
      .filter((_, i) => !omitted.has(i + 1));
    if (!ach.length && !pairs.length) return [];
    const achHtml = ach.length ? `<div class="rt-ach card"><p class="rt-h ok">✓ ${esc(PF.t('deck.achieved'))}</p><ul>${ach.map((x) => `<li>${inline(x)}</li>`).join('')}</ul></div>` : '';
    const pairHtml = pairs.length ? `<table class="mini"><thead><tr><th scope="col">△ ${esc(PF.t('deck.regrets'))}</th><th scope="col">→ ${esc(PF.t('deck.plans'))}</th></tr></thead><tbody>${pairs.map(({ regret, plan }) => `<tr><td>${inline(regret)}</td><td class="p">${inline(plan)}</td></tr>`).join('')}</tbody></table>` : '';
    const make = (html, one, i, cnt) => {
      const s = PF.scene({ id: i === 0 ? 'retro' : `retro-${i + 1}`, tpl: 'P6', eyebrow: PF.t('deck.retro-eyebrow'), rlabel: p.title, title: PF.t('deck.retro'), sub: cnt > 1 ? `${i + 1}/${cnt}` : '', foot,
        sec: PF.t('deck.retro'), group: PF.t('deck.retro') });
      PF.body(s).innerHTML = `<div class="pf-fill"><div class="rt${one ? ' one' : ''}">${html}</div></div>`;
      return band(s, auto);
    };
    const s = make(achHtml + pairHtml, !achHtml || !pairHtml, 0, 1);
    if (PF.fitsM(s)) return [s];
    const two = [achHtml, pairHtml].filter(Boolean).map((h, i, a) => make(h, true, i, a.length));
    return two.map((x) => (PF.fitsM(x) ? x : PF.shrinkOrClip(x, PFScene.refUrl(x.id), info.overflow)));
  }

  // 맺음: 프로젝트 상세 문서(전문) · 바깥 링크 · 포트폴리오. 슬라이드 → 상세 문서 링크는 표지와 여기에만(G1)
  function endScene(p, foot, ext, auto) {
    const card = (k, label, href, kind, visible, primary) => `<div class="lcard${primary ? ' primary' : ''}">${link({ href, kind, label: `<span class="lc-k">${esc(k)}</span><span class="lc-t">${esc(label)}</span><span class="url">${esc(visible)}</span>` })}</div>`;
    const make = () => {
      const s = PF.scene({ id: 'end', tpl: 'EN', kind: 'bare', foot, sec: PF.t('deck.more'), stitle: PF.t('deck.end-title') });
      PF.body(s).innerHTML = `<div class="toprow"><span class="pf-eyebrow">${esc(PF.t('en.more'))}</span><span class="rl">${esc(PF.teamLabel(p.team))}</span></div>
        <div class="en-mid"><div><h2 class="en-t">${esc(p.title)}</h2>${p.tagline ? `<p class="tagline">${inline(p.tagline)}</p>` : ''}${DOT}</div>
        <div class="en-links">${card(PF.t('en.techdoc'), PF.t('deck.end-doc'), PF.docUrl(p.id), 'page', PF.visibleUrl(PF.docUrl(p.id)), true)}
          ${ext.map((l) => card(PF.extEn(l), l.label, l.url, 'external', shortUrl(l.url))).join('')}
          ${card(PF.t('en.portfolio'), PF.t('ui.back'), 'index.html', 'page', PF.visibleUrl(''))}</div></div>`;
      return band(s, auto);
    };
    return PF.guard(make(), PFScene.refUrl('end'), info.overflow);
  }
})();
