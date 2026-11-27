// These functions run in the preview document through the Chromium DevTools session.
export async function checkDeckPage(projectId, probe) {
  const all = (root, selector) => [...root.querySelectorAll(selector)];
  const json = (value) => JSON.stringify(value);
  const checks = [];
  const add = (name, pass, expected, evidence, sceneId = null, level = 'required') => {
    checks.push({ name, pass: Boolean(pass), expected, evidence, sceneId, level });
  };
  const report = PFScene.report();
  const response = await fetch(`data/projects/${projectId}.json`);
  if (!response.ok) throw new Error(`Project data returned HTTP ${response.status}.`);
  const project = await response.json();
  const deck = project.deck;
  const scene = (id) => document.getElementById(id);
  const pages = new Map(report.ids.map((id, i) => [id, i + 1]));
  const aliases = deck?.aliases || {};
  const modelIds = new Set(deck?.slides.map((slide) => `d-${slide.key}`));
  const sceneParts = (id) => report.ids.filter((key) => key === id ||
    (key.startsWith(`${id}-`) && /^(?:-(?:\d+|t\d+))+$/.test(key.slice(id.length)) && !modelIds.has(key)));
  const first = (selector) => {
    const element = document.querySelector(`#deck ${selector}`);
    if (!element) throw new Error(`The ${probe} probe requires a rendered ${selector}.`);
    return element;
  };

  if (probe === 'swapped-layout') {
    const layout = first('.dk-visual-notes');
    layout.prepend(layout.querySelector(':scope > .cards'));
  }
  if (probe === 'missing-highlight') {
    first('.code-marker').remove();
  }
  if (probe === 'missing-code') first('figure[data-kind="code"]').remove();
  if (probe === 'wrong-code-text') first('figure[data-kind="code"] pre > code').append('changed excerpt');
  if (probe === 'wrong-retro-pair') first('.rt td.p').append('changed plan');
  if (probe === 'uneven-link-card') {
    const card = first('.lcard a');
    card.style.flex = 'none';
    card.style.height = 'auto';
    card.style.alignSelf = 'flex-start';
    card.querySelector('.url')?.append(' changed-long-address-'.repeat(4));
  }
  if (probe === 'scene-count-toc') first('#toc .toc-list a .d').textContent = '장면 7장';
  if (probe === 'uneven-paired-row') {
    // 예전 렌더러처럼 두 패널이 행 틀을 따로 쓰게 하고, 첫 패널 제목 줄만 높임
    const panels = [...first('.dk-paired').querySelectorAll(':scope > .paired-panel')];
    for (const panel of panels) Object.assign(panel.style, { gridRow: 'auto', gridTemplateRows: 'none' });
    for (const flow of first('.dk-paired').querySelectorAll('.paired-panel > .flow')) Object.assign(flow.style, { gridRow: 'auto', gridTemplateRows: 'none' });
    panels[0].querySelector('.panel-header').style.paddingBottom = '40px';
  }
  if (probe === 'uneven-screen-cell') {
    const media = first('.scr-row > .fe-card > .vis > figure.vbox[data-kind="image"] > .vmedia');
    media.style.height = `${parseFloat(media.style.height) / 2}px`;
  }

  add('screen deck is ready', report.ready === 'yes' && report.scenes > 0 && report.scenes === pages.size &&
    report.info.id === projectId && ['cover', 'toc', 'end'].every((id) => pages.has(id)),
  'Ready deck with unique scene IDs, cover, table of contents, and closing',
  { ready: report.ready, actualProject: report.info.id, sceneCount: report.scenes, uniqueIds: pages.size });
  const issuesByScene = new Map();
  const issue = (sceneId, detail) => {
    if (!issuesByScene.has(sceneId)) issuesByScene.set(sceneId, []);
    issuesByScene.get(sceneId).push(detail);
  };
  for (const item of report.audit) issue(report.ids[item.scene - 1] || null, item);
  for (const field of ['errors', 'overflow', 'unresolved']) {
    for (const item of report.info[field]) issue(pages.has(item.id) ? item.id : null, { field, item });
  }
  add('screen audit', !issuesByScene.size, 'No clipping, broken or unresolved visuals, or renderer errors', { affectedScenes: issuesByScene.size });
  for (const [id, issues] of issuesByScene) add('screen issue', false, 'No audit or renderer issue', issues, id);
  for (const note of report.info.notes) add(note.type, true, 'Review renderer note if relevant', note, note.id || null, 'note');

  const links = all(document, '#deck a[data-pf-ref], #deck a[href^="#"]').map((link) => {
    const hash = decodeURIComponent(link.hash.slice(1));
    const target = link.dataset.pfRef || hash;
    const pageLabel = link.querySelector('.pf-n-screen');
    const targetPage = pages.get(scene(target)?.closest('.pf-scene')?.id);
    return { sceneId: link.closest('.pf-scene')?.id, target, targetPage, href: link.getAttribute('href'),
      label: pageLabel?.textContent, pass: Boolean(targetPage && hash === target &&
        (!pageLabel || pageLabel.textContent === `p.${targetPage}`)) };
  });
  for (const [key, targetKey] of Object.entries(aliases)) {
    const target = `d-${targetKey}`;
    links.push({ sceneId: null, target, alias: `d-${key}`, pass: pages.has(target) && !pages.has(`d-${key}`) });
  }
  const broken = links.filter((link) => !link.pass);
  add('screen internal links', links.length > 0 && !broken.length, 'Internal links and old-address destinations resolve to mounted scenes',
    { checked: links.length, broken: broken.length });
  for (const link of broken) add('broken internal link', false, 'Existing target with correct page number', link, link.sceneId);

  // 표지·맺음 링크 카드: 한 묶음 안의 카드는 폭·높이가 같아야 함(주소 줄바꿈 수와 상관없이)
  const linkGroups = all(document, '#deck .pi-links, #deck .en-links').map((group) => {
    const cards = all(group, ':scope > .lcard > a').map((card) => {
      const box = card.getBoundingClientRect();
      return { label: card.querySelector('.lc-t')?.textContent || '', width: Math.round(box.width * 10) / 10, height: Math.round(box.height * 10) / 10 };
    });
    const spread = (key) => (cards.length ? Math.max(...cards.map((c) => c[key])) - Math.min(...cards.map((c) => c[key])) : 0);
    return { sceneId: group.closest('.pf-scene')?.id || null, cards, widthSpread: spread('width'), heightSpread: spread('height') };
  });
  const unevenGroups = linkGroups.filter((group) => group.widthSpread > 1 || group.heightSpread > 1);
  add('link cards same size', !unevenGroups.length, 'Every link card in a cover or closing group has the same width and height (within 1px)',
    { groups: linkGroups.length, uneven: unevenGroups.length });
  for (const group of unevenGroups) add('uneven link cards', false, 'Same width and height within the group', group, group.sceneId);

  // [포크] screens 사진 칸: 칸마다 사진 틀(top·폭·높이)과 FEATURE·제목·설명 줄의 top이 같아야 함(표의 칸처럼)
  const spreadOf = (values) => (values.length ? Math.max(...values) - Math.min(...values) : 0);
  const screenRows = all(document, '#deck .scr-row').map((row) => {
    const scale = row.closest('.pf-scene').getBoundingClientRect().width / 1280;
    const cards = all(row, ':scope > .fe-card');
    const medias = cards.map((card) => card.querySelector(':scope > .vis > figure.vbox[data-kind="image"] > .vmedia')).filter(Boolean);
    const frames = medias.map((media) => media.getBoundingClientRect());
    const textTops = ['.fe-k', 'h3', 'p:not(.fe-k)'].map((selector) =>
      cards.map((card) => card.querySelector(`:scope > ${selector}`)).filter(Boolean).map((node) => node.getBoundingClientRect().top));
    const spreads = { top: spreadOf(frames.map((box) => box.top)), width: spreadOf(frames.map((box) => box.width)),
      height: spreadOf(frames.map((box) => box.height)), text: Math.max(...textTops.map(spreadOf)) };
    const imageOnly = medias.length === cards.length && cards.length > 1;
    return { sceneId: row.closest('.pf-scene').id, imageOnly, tol: 2 * scale, spreads };
  }).filter((row) => row.imageOnly);
  const unevenScreens = screenRows.filter((row) => Object.values(row.spreads).some((value) => value > row.tol));
  add('screen cells share one frame and text rows', !unevenScreens.length,
    'Every image cell in a screens row has the same frame top, width and height, and FEATURE/title/text start on the same rows',
    { rows: screenRows.length, uneven: unevenScreens.length });
  for (const row of unevenScreens) add('uneven screen cells', false, 'Same frame and text rows across cells', row, row.sceneId);

  const rect = (node) => node?.getBoundingClientRect().toJSON();
  const isVisible = (box) => box?.width > 0 && box?.height > 0;
  const normalize = (text) => text.replace(/\s+/g, ' ').trim();
  const hasSourceVisual = (ref) => ['code', 'mermaid', 'table'].includes(ref.kind);
  const needsSource = deck?.slides.some((slide) => slide.visuals?.some(hasSourceVisual));
  let source, refNumbers;
  if (needsSource) {
    const doc = await fetch(PF.srcPath(projectId, project.doc));
    if (!doc.ok) throw new Error(`Project source returned HTTP ${doc.status}.`);
    source = PF.md.parse(await doc.text(), projectId);
    refNumbers = PF.md.sectionNumbers(source);
  }
  const dropRefs = (value) => PF.md.dropRefs(value, refNumbers);
  const htmlText = (html) => {
    const template = document.createElement('template');
    template.innerHTML = dropRefs(html);
    return template.content.textContent;
  };
  const inlineText = (value) => normalize(htmlText(PF.inline(typeof value === 'object' ? value?.text || '' : value || '')));
  const label = (value) => normalize(value ? PF.label(value) : '');

  for (const chapter of deck?.chapters || []) {
    const id = `ch-${chapter.key}`;
    const slides = deck.slides.filter((slide) => slide.chapter === chapter.key);
    if (!slides.length) continue;
    const actual = scene(id) ? all(scene(id), '.toc-list a').map((row) => ({
      title: normalize(row.querySelector('.t')?.textContent || ''), target: row.dataset.pfRef,
      description: normalize(row.querySelector('.d')?.textContent || ''),
      numbered: Boolean(row.querySelector('.n')?.textContent.trim() && row.querySelector('.pf-n-screen')?.textContent.trim())
    })) : [];
    const individual = slides.map((slide) => ({
      title: inlineText(slide.title) || inlineText(chapter.title), target: `d-${slide.key}`,
      description: chapter.hideSceneTabs ? '' : normalize(PF.tab(slide.tab) || ''), numbered: true
    }));
    const candidates = [individual];
    const deepest = Math.max(...slides.map((slide) => (slide.sectionPath || []).length)) - 2;
    for (let depth = deepest; depth >= 1; depth--) {
      const groups = [];
      for (const slide of slides) {
        const path = slide.sectionPath || [];
        const title = path[Math.min(depth, path.length - 1)] || '';
        const target = `d-${slide.key}`, count = sceneParts(target).length;
        const previous = groups.at(-1);
        if (previous?.title === title) previous.count += count;
        else groups.push({ title, target, count });
      }
      if (groups.length < slides.length) candidates.push(groups.map(({ title, target, count }) => ({
        title: inlineText(title), target, description: '', numbered: true
      })));
    }
    add('chapter contents reflect scenes or section groups', candidates.some((expected) => json(expected) === json(actual)),
      candidates, actual, id);
  }

  // 목차 장 줄 = 그 장 장면 keywords를 라운드 로빈으로 모은 3~5개(assets/project-deck.js chapterKeywordLine과 같은 규칙)
  const chapterKeywords = (chapterSlides) => {
    const keywordLists = chapterSlides.map((slide) => (Array.isArray(slide.keywords) ? slide.keywords : []));
    const longest = Math.max(0, ...keywordLists.map((list) => list.length));
    const picked = [];
    for (let round = 0; round < longest; round++) {
      for (const list of keywordLists) {
        const word = normalize(String(list[round] || ''));
        if (word && !picked.includes(word) && picked.length < 5) picked.push(word);
      }
    }
    return picked;
  };
  const tocRows = new Map(all(scene('toc') || document.createElement('div'), '.toc-list a').map((row) => [row.dataset.pfRef,
    normalize(row.querySelector('.d')?.textContent || '')]));
  for (const chapter of deck?.chapters || []) {
    const id = `ch-${chapter.key}`;
    if (!pages.has(id)) continue;
    const expected = chapterKeywords(deck.slides.filter((slide) => slide.chapter === chapter.key));
    const shown = (tocRows.get(id) || '').split(' · ').filter(Boolean);
    add('contents rows show chapter keywords', shown.length >= 3 && shown.length <= 5 && json(shown) === json(expected),
      '3~5 keywords gathered from the chapter scene keywords (fill deck.slides[].keywords if fewer than 3)',
      { chapter: chapter.title, expected, shown }, 'toc');
  }
  const sceneCountText = /장면\s*\d+\s*[장개]/;
  const countScenes = ['toc', ...all(document, '#deck .pf-scene.t-CH').map((node) => node.id)]
    .filter((sceneId) => scene(sceneId) && (sceneCountText.test(scene(sceneId).textContent) || scene(sceneId).querySelector('.toprow .rl')));
  add('no scene counts on contents or chapter covers', !countScenes.length,
    'Contents and chapter covers show no "장면 N장/N개" counts', { scenes: countScenes });

  const retroIds = report.ids.filter((id) => /^retro(?:-\d+)?$/.test(id));
  const achieved = deck?.retro?.hideAchieved ? [] : project.retro?.achieved || [];
  const regrets = project.retro?.regrets || [], plans = project.retro?.plans || [];
  const omittedPairs = new Set(deck?.retro?.omitPairs || []);
  const retroReview = {
    section: project.retro?.section || '', sceneIds: retroIds,
    achieved: retroIds.flatMap((id) => all(scene(id), '.rt-ach li').map((item) => normalize(item.textContent))),
    pairs: retroIds.flatMap((id) => all(scene(id), '.rt tbody tr').map((row) => ({
      sceneId: id, page: pages.get(id), regret: normalize(row.cells[0]?.textContent || ''),
      plan: normalize(row.cells[1]?.textContent || '')
    })))
  };
  const expectedPairs = Array.from({ length: Math.max(regrets.length, plans.length) }, (_, i) => ({
    regret: inlineText(regrets[i]), plan: inlineText(plans[i])
  })).filter((_, i) => !omittedPairs.has(i + 1));
  add('retrospective follows source order and display settings',
    Boolean(retroIds.length) === Boolean(achieved.length || expectedPairs.length) &&
    json(achieved.map(inlineText)) === json(retroReview.achieved) &&
    json(expectedPairs) === json(retroReview.pairs.map(({ regret, plan }) => ({ regret, plan }))),
    { achieved: achieved.map(inlineText), pairs: expectedPairs }, retroReview, retroIds[0]);
  const codeReview = report.ids.flatMap((id) => {
    const root = scene(id);
    const code = all(root, 'figure[data-kind="code"] code').map((block) => ({
      excerpt: block.textContent,
      range: [+block.closest('figure').dataset.sourceStart, +block.closest('figure').dataset.sourceEnd],
      caption: block.closest('figure').querySelector('figcaption')?.textContent.trim() || '',
      highlights: all(block, '.code-line[data-highlight-points]').map((row) => ({
        line: +row.dataset.sourceLine, points: row.dataset.highlightPoints.split(' ').map(Number)
      }))
    }));
    if (!code.length && !root.classList.contains('l-code')) return [];
    return [{
      sceneId: id, page: pages.get(id),
      title: normalize(root.querySelector('.pf-title')?.textContent || ''),
      lead: normalize(root.querySelector('.pf-lead')?.textContent || ''),
      explanations: all(root, '.pf-body h3, .pf-body p')
        .filter((node) => !node.closest('figure'))
        .map((node) => {
          const card = node.closest('.ncard, .fstep');
          return { point: card?.querySelector('.num')?.textContent.trim() || null,
            label: card?.querySelector('.lb, .labelchip')?.textContent.trim() || '',
            text: normalize(node.innerText) };
        }).filter((item) => item.text),
      code
    }];
  });
  // [포크] 코드 캡션은 언어 이름이 아니라 실제 코드 위치(tools/code-locations.py → data/code-locations/)
  const bareLanguages = new Set(['C#', 'JavaScript', 'TypeScript', 'JSON', 'YAML', 'XML', 'HTML', 'CSS', 'Python', 'Shell', 'PowerShell', 'HLSL', 'Shader', 'C++', 'C', 'Lua']);
  for (const review of codeReview) {
    const unlocated = review.code.filter((code) => !code.caption || bareLanguages.has(code.caption.split(' · ')[0]));
    if (unlocated.length) add('code caption shows source location', false,
      'Class.Method() · File:lines (run python tools/code-locations.py <folder>, or add a // Class.Method() first-line comment)',
      unlocated.map(({ caption, range }) => ({ caption, range })), review.sceneId);
  }
  for (const slide of deck?.slides || []) {
    const id = `d-${slide.key}`;
    const root = scene(id);
    if (!root) {
      add('configured scene is rendered', false, 'Scene exists', null, id);
      continue;
    }
    const scale = root.getBoundingClientRect().width / 1280;
    const tol = 2 * scale;
    const parts = sceneParts(id).map(scene);
    if (slide.codeEvidence === 'all-points') {
      const expected = (slide.points || []).map((_, i) => i + 1);
      const actual = [...new Set(parts.flatMap((part) => all(part, '.code-marker').map((mark) => +mark.dataset.point)))].sort((a, b) => a - b);
      add('every code explanation card has a matching execution-line highlight', json(actual) === json(expected),
        expected, { actual, fragments: parts.map((part) => part.id) }, id);
    }
    // [포크] 데이터의 요점이 화면에 나왔는지(요점을 그리지 않는 배치가 조용히 버리는 것을 잡음, 2026-11-27 Hive wave-structure)
    const squeeze = (text) => String(text || '').replace(/[\s*`]/g, '');
    const sceneText = squeeze(parts.map((part) => part.textContent).join(''));
    const missingPoints = (slide.points || []).map((point) => squeeze(point.text).slice(0, 8)).filter((head) => head && sceneText.includes(head) === false);
    add('points rendered', missingPoints.length === 0, 'Every slide point text appears in the scene', { missingPoints, presentation: slide.presentation }, id);
    if (slide.presentation === 'flow-table') {
      const cards = all(root, '.ft-card p');
      const bracketed = (slide.points || []).map((point, i) => ({ i, wants: /\S\s*\([^()]+\)$/.test(point.text || '') }));
      const badBreaks = bracketed.filter(({ i, wants }) => wants && cards[i]?.querySelector('.ft-note')?.textContent.startsWith('(') !== true).map(({ i }) => i + 1);
      add('flow-table bracket note starts its own line', badBreaks.length === 0, 'Trailing (…) in a card is a separate line', { badBreaks }, id);
    }
    const body = rect(root.querySelector('.pf-body'));
    const fill = rect(root.querySelector('.pf-fill'));
    if (slide.presentation === 'visual-pair-table') {
      const row = root.querySelector('.dk-visual-pair-table .visual-pair-row');
      const visuals = row ? all(row, ':scope > .vis figure') : [];
      const table = root.querySelector('.dk-visual-pair-table > .vis table');
      const first = visuals.map(rect), rowBox = rect(row), bottom = rect(table);
      add('two visuals share a row above centered table', first.length === 2 && first.every(isVisible) &&
        isVisible(bottom) && isVisible(rowBox) &&
        Math.abs((first[0].top + first[0].bottom - first[1].top - first[1].bottom) / 2) <= tol &&
        first.every((visual) => visual.bottom <= bottom.top + tol) &&
        first[0].right <= first[1].left + tol &&
        Math.abs((rowBox.left + rowBox.right - bottom.left - bottom.right) / 2) <= tol,
      'Two nonoverlapping visuals with aligned centers, with the table centered below', { first, rowBox, bottom }, id);
    }
    if (isVisible(fill) && fill.height <= body.height && !root.querySelector('.dk-columns, .dk-pair, .comparison-table, .pf-zoomwrap')) {
      add('body content is vertically centered', Math.abs(fill.top + fill.height / 2 - body.top - body.height / 2) <= 3 * scale,
        'Centered content when it fits the body', { body, fill }, id);
    }
    if (slide.presentation === 'steps') {
      const group = rect(root.querySelector('.dk-steps'));
      const gap = group && body ? Math.abs(group.top + group.height / 2 - body.top - body.height / 2) / scale : null;
      add('steps are centered in the body', isVisible(group) && isVisible(body) && gap <= 3,
        'Center difference at most 3px in deck coordinates', { centerDifference: gap }, id);
      const nodes = all(root, '.step-diagram svg > g[transform] rect').map(rect);
      const points = all(root, '.step-explanations > p');
      add('step explanations align with their diagram nodes', nodes.length === slide.points.length &&
        points.length === nodes.length && points.every((point, i) => {
          const box = rect(point);
          return Math.abs(box.left + box.width / 2 - nodes[i].left - nodes[i].width / 2) <= 3 * scale &&
            getComputedStyle(point).textAlign === 'center' && normalize(point.textContent) === inlineText(slide.points[i]);
        }), slide.points.map(inlineText), points.map((point) => normalize(point.textContent)), id);
    }
    if (slide.presentation === 'cycle') {
      const nodes = all(root, '.cycle-node rect').map(rect);
      const arrows = all(root, '.cycle-edge path');
      const back = root.querySelector('.cycle-return path');
      add('cycle nodes align and the last arrow returns to the start', nodes.length > 1 &&
        nodes.every((node) => isVisible(node) && Math.abs(node.top - nodes[0].top) <= tol) &&
        arrows.length === nodes.length && arrows.every((path) => path.hasAttribute('marker-end')) &&
        back && back.getPointAtLength(0).x > back.getPointAtLength(back.getTotalLength()).x,
      'Aligned nodes, one arrow per node, and a right-to-left return', { nodes, arrows: arrows.length }, id);
    }
    if (slide.presentation === 'columns') {
      const columns = all(root, '.dk-column');
      const actual = columns.map((column, columnIndex) => {
        const figure = column.querySelector('figure');
        const heading = column.querySelector(':scope > h3'), text = column.querySelector(':scope > p');
        return { index: figure ? (figure.dataset.visualIndex === undefined ? columnIndex : Number(figure.dataset.visualIndex)) : null,
          label: normalize(heading?.textContent || ''), text: normalize(text?.textContent || ''),
          diagram: rect(figure), heading: rect(heading), explanation: rect(text) };
      });
      const expected = slide.points.map((point, index) => ({ index, label: label(point.label), text: inlineText(point) }));
      add('each column shows its own diagram above its explanation', actual.length === expected.length &&
        actual.every(({ index, label, text, diagram, heading, explanation }, i) =>
          json({ index, label, text }) === json(expected[i]) && isVisible(diagram) && isVisible(explanation) &&
          diagram.bottom <= (heading?.top ?? explanation.top) + tol &&
          (!heading || heading.bottom <= explanation.top + tol)), expected, actual, id);
      const rows = columns.map((column) => ['.vis', 'h3', 'p'].map((selector) =>
        rect(column.querySelector(`:scope > ${selector}`))));
      add('matched diagram, title and explanation table rows align', rows.length === expected.length &&
        rows.every((cells) => cells.every(isVisible)) &&
        rows.every((cells) => cells.every((cell, row) => Math.abs(cell.top - rows[0][row].top) <= tol)),
      'Corresponding diagrams, titles and explanations occupy aligned rows', rows, id);
    }
    if (slide.presentation === 'feature-rows') {
      const rows = all(root, '.dk-feature-rows > .feature-row');
      const visuals = rows.map((row) => all(row, '.feature-row-media figure[data-kind="image"]'));
      const images = visuals.flat().map((figure) => figure.querySelector('img'));
      const expected = (slide.visuals || []).map((visual) => normalize(visual.caption?.text || ''));
      const actual = images.map((img) => normalize(img?.getAttribute('alt') || ''));
      const cells = rows.map((row) => ({ note: rect(row.querySelector('.feature-row-note')),
        media: rect(row.querySelector('.feature-row-media')) }));
      const topMedia = visuals[0]?.map((figure) => rect(figure.querySelector('.vmedia'))) || [];
      add('two feature rows pair descriptions with ordered images', rows.length === 2 &&
        visuals[0]?.length === 2 && visuals[1]?.length === 1 && json(actual) === json(expected) &&
        rows.every((row, i) => normalize(row.querySelector('h3')?.textContent || '') === label(slide.points[i].label) &&
          normalize(row.querySelector('.feature-row-note > p')?.textContent || '') === inlineText(slide.points[i])) &&
        cells.every(({ note, media }) => isVisible(note) && isVisible(media) && note.right <= media.left + tol) &&
        topMedia.length === 2 && topMedia.every(isVisible) &&
        Math.abs(topMedia[0].top - topMedia[1].top) <= tol &&
        Math.abs(topMedia[0].height - topMedia[1].height) <= tol,
      'Two aligned feature rows; first has two equally high images, second has one image',
      { actual, expected, cells, topMedia }, id);
    }
    if (slide.presentation === 'side-notes' || slide.presentation === 'visual-notes') {
      const layout = root.querySelector(`.dk-${slide.presentation}`);
      const visual = rect(layout?.querySelector(':scope > .vis, :scope > .point-label-flow'));
      const cards = layout ? all(layout, ':scope > .cards .ncard').map(rect) : [];
      const visualFirst = slide.presentation === 'visual-notes';
      const stacked = cards.length > 0 && cards.length === slide.points.length && cards.every((card, i) =>
        isVisible(card) && Math.abs(card.left - cards[0].left) <= tol && (!i || card.top >= cards[i - 1].bottom - tol));
      const side = isVisible(visual) && cards.every((card) => visualFirst ? visual.right <= card.left + tol : card.right <= visual.left + tol);
      add('diagram and stacked cards placement', stacked && side,
        `${visualFirst ? 'Diagram left, cards right' : 'Cards left, diagram right'} with ${slide.points.length} stacked cards and no overlap`, { visual, cards }, id);
      const diagram = rect(layout?.querySelector(':scope > .vis > figure, :scope > .point-label-flow')) || visual;
      const cardGroup = rect(layout?.querySelector(':scope > .cards'));
      const centered = isVisible(diagram) && isVisible(cardGroup) &&
        Math.abs((diagram.top + diagram.bottom - cardGroup.top - cardGroup.bottom) / 2) <= 12 * scale;
      add('diagram and explanation group vertical centers align', centered,
        'Diagram and stacked explanation cards share a vertical midpoint', { diagram, cardGroup }, id);
      if (layout && !slide.visuals?.length) {
        const expected = slide.points.map((point) => normalize(point.label || ''));
        const actual = all(layout, '.point-label-flow li').map((item) => normalize(item.textContent));
        add('text flow preserves its configured step labels', json(actual) === json(expected), expected, actual, id);
      }
    }
    if (slide.presentation === 'paired') {
      const panelNodes = all(root, '.dk-paired > .paired-panel');
      const panels = panelNodes.map(rect);
      add('paired panels placement', panels.length === 2 && panels.every(isVisible) &&
        panels[0].right <= panels[1].left + tol && Math.abs(panels[0].top - panels[1].top) <= tol,
      'Two adjacent panels with a shared top alignment', panels, id);
      const expected = slide.panels.map((panel) => ({ title: inlineText(panel.title), lead: inlineText(panel.lead),
        labels: (panel.points || []).map((point) => label(point.label)) }));
      const actual = panelNodes.map((panel) => ({ title: normalize(panel.querySelector('h3')?.textContent || ''),
        lead: normalize(panel.querySelector('.panel-lead')?.textContent || ''),
        labels: all(panel, '.ncard, .fstep').map((point) => normalize(point.querySelector('.lb, .labelchip')?.textContent || '')) }));
      add('paired panels preserve their own headings and point labels', json(actual) === json(expected), expected, actual, id);
      // [포크] 두 패널의 본문(그림·카드·흐름 칸)이 같은 높이에서 시작하고 행마다 맞는지. 흐름 칸 수가 다르면 짧은 쪽이 세로 가운데
      const bodyOf = (panel) => panel.querySelector(':scope > .vis, :scope > .cards, :scope > .flow');
      const bodies = panelNodes.map((panel) => rect(bodyOf(panel)));
      const cardGroups = panelNodes.map((panel) => rect(panel.querySelector(':scope > .cards')));
      const stepRows = panelNodes.map((panel) => all(panel, ':scope > .flow > .fstep').map(rect));
      const near = (a, b) => Math.abs(a - b) <= tol;
      const hasVisual = Boolean(slide.visuals?.length);
      const sameCount = stepRows[0].length === stepRows[1].length;
      const bothBodies = bodies.every(isVisible);
      let alignedBody = true;
      let rule = 'No body in one panel';
      if (bothBodies && hasVisual) {
        rule = 'Tables start at one height and card groups start at one height';
        alignedBody = near(bodies[0].top, bodies[1].top) && (!cardGroups.every(isVisible) || near(cardGroups[0].top, cardGroups[1].top));
      } else if (bothBodies && sameCount) {
        rule = 'Flow steps share each row: same top and same height';
        alignedBody = stepRows[0].every((step, i) => near(step.top, stepRows[1][i].top) && near(step.height, stepRows[1][i].height));
      } else if (bothBodies) {
        rule = 'Shorter flow is vertically centered on the longer flow';
        alignedBody = Math.abs((bodies[0].top + bodies[0].bottom - bodies[1].top - bodies[1].bottom) / 2) <= tol;
      }
      add('paired panel bodies align row by row', alignedBody, rule, { bodies, cardGroups, stepRows }, id);
    }
    if (slide.presentation === 'stage-table') {
      const rows = all(root, 'table.stage-table tr').map((row) => all(row, ':scope > th, :scope > td').map(rect));
      const count = slide.points.length;
      const aligned = rows.length === 3 && rows.every((row) => row.length === count && row.every((cell, i) =>
        isVisible(cell) && Math.abs(cell.left - rows[0][i].left) <= tol && Math.abs(cell.width - rows[0][i].width) <= tol &&
        Math.abs(cell.width - rows[0][0].width) <= tol && Math.abs(cell.top - row[0].top) <= tol));
      const cells = all(root, '.stage-explanations td');
      const lines = cells.map((cell) => all(cell, '.point-line').map((line) => ({ text: normalize(line.textContent), box: rect(line) })));
      const expected = slide.points.map((point) => point.lines?.map(inlineText) || []);
      const matches = json(lines.map((group) => group.map((line) => line.text))) === json(expected) && lines.every((group) =>
        group.every(({ box }, i) => isVisible(box) && (!i || box.top >= group[i - 1].box.bottom - tol)));
      add('stage table columns and explicit line breaks', aligned && matches,
        { columns: count, rows: 3, lines: expected }, { rows, lines }, id);
    }

    const flowPoints = slide.presentation === 'paired' && !slide.visuals?.length ? slide.panels.flatMap((panel) => panel.points || []) :
      !slide.presentation && slide.layout === 'problem' ? slide.points || [] : [];
    const labels = flowPoints.filter((point) => /^(?:\d+차\s+)?해결$/.test(point.label)).map((point) => label(point.label));
    if (labels.length) {
      const chips = all(root, '.labelchip');
      const sample = document.createElement('span');
      sample.style.backgroundColor = 'var(--accent-fill)';
      root.appendChild(sample);
      const accent = getComputedStyle(sample).backgroundColor;
      sample.remove();
      const actual = chips.map((chip) => ({ label: normalize(chip.textContent), accent: chip.classList.contains('accent'),
        background: getComputedStyle(chip).backgroundColor }));
      add('solution badges use the configured accent', json(actual.filter((chip) => chip.accent).map((chip) => chip.label)) === json(labels) &&
        accent !== 'rgba(0, 0, 0, 0)' && actual.every((chip) => chip.accent === (chip.background === accent)),
        { labels, background: accent }, actual, id);
    }

    const cards = all(root, '.cards .ncard .num').map((node) => +node.textContent);
    for (const [index, ref] of (slide.visuals || []).entries()) {
      if (!hasSourceVisual(ref)) continue;
      const picked = PF.md.pick(source, ref, slide.sectionPath);
      const figures = parts.flatMap((part) => all(part, `figure[data-kind="${ref.kind}"][data-visual-index="${index}"]`));
      if (ref.kind === 'mermaid') {
        const cardFlow = slide.presentation === 'flow-table';   // [포크] 카드로 그린 구조도(SVG 없음): 카드 4개가 보이면 통과
        const diagrams = cardFlow ? all(root, '.ft-card') : ['steps', 'cycle'].includes(slide.presentation)
          ? all(root, `.${slide.presentation === 'steps' ? 'step' : 'cycle'}-diagram > svg`)
          : figures.map((figure) => figure.querySelector('svg'));
        add('referenced diagram renders', picked.status === 'ok' && diagrams.length > 0 && diagrams.every((svg) =>
          isVisible(rect(svg)) && (cardFlow || isVisible(svg.getBBox()))),
        'A visible SVG from the configured source reference', { index, sourceStatus: picked.status, diagrams: diagrams.length }, id);
        if (ref.view?.appendToNode) {
          const { id: nodeId, text } = ref.view.appendToNode;
          const nodes = diagrams.filter(Boolean).flatMap((svg) => all(svg, '[data-node], .node[id]')).filter((node) =>
            node.dataset.node === nodeId || node.id === nodeId || node.id.replace(/^flowchart-/, '').replace(/-\d+$/, '') === nodeId);
          const actual = nodes.map((node) => normalize(node.textContent));
          add('diagram annotation is on its configured node', actual.length > 0 && actual.every((value) => value.includes(normalize(text))),
            { nodeId, text }, { index, actual }, id);
        }
        continue;
      }
      if (ref.kind === 'table') {
        const changes = [...(ref.view?.omitFromCells || []), ...(ref.view?.appendToCells || [])];
        const cells = [...new Map(changes.map(({ row, column }) => [`${row}:${column}`, { row, column }])).values()];
        if (!cells.length) continue;
        const evidence = cells.map(({ row, column }) => {
          const expected = picked.el?.querySelectorAll('tbody tr')[row - 1]?.children[column - 1]?.cloneNode(true);
          for (const change of ref.view?.omitFromCells || []) {
            if (change.row !== row || change.column !== column || !expected) continue;
            const walker = document.createTreeWalker(expected, NodeFilter.SHOW_TEXT);
            while (walker.nextNode()) walker.currentNode.textContent = walker.currentNode.textContent.replace(change.text, '');
          }
          for (const change of ref.view?.appendToCells || []) {
            if (change.row === row && change.column === column) expected?.insertAdjacentHTML('beforeend', `<br>${PF.inline(change.text)}`);
          }
          const actual = figures.flatMap((figure) => all(figure, `tbody tr[data-source-row="${row - 1}"]`))
            .map((tr) => all(tr, ':scope > td:not(.comparison-flow), :scope > th')[column - 1])
            .filter(Boolean).map((td) => normalize(td.textContent));
          return { row, column, expected: expected ? normalize(htmlText(expected.innerHTML)) : null, actual };
        });
        add('table edits affect the configured source cells', picked.status === 'ok' &&
          evidence.every(({ expected, actual }) => expected !== null && actual.length && actual.every((value) => value === expected)),
        'Specified omissions and additions applied to the original cells', { index, evidence }, id);
        continue;
      }
      const sourceCode = picked.el?.querySelector('code');
      const original = sourceCode?.textContent.replace(/\n$/, '').split('\n') || [];
      const [start, end] = ref.lines || [1, original.length];
      const highlights = ref.view?.highlights || [];
      const evidence = [];
      let next = start;
      let intact = picked.status === 'ok' && figures.length > 0 && Boolean(sourceCode);
      let marksIntact = true;
      for (const figure of figures) {
        const chunk = figure.querySelector('pre > code');
        if (!chunk) { intact = false; continue; }
        const first = +figure.dataset.sourceStart, last = +figure.dataset.sourceEnd;
        const expected = document.createElement('code');
        expected.textContent = original.slice(first - 1, last).join('\n');
        const textIntact = chunk.textContent === htmlText(expected.outerHTML);
        intact &&= first === next && last >= first && last <= end && textIntact;
        evidence.push({ sceneId: figure.closest('.pf-scene').id, lines: [first, last], textIntact });
        next = last + 1;
        if (!highlights.length) continue;
        const rows = all(chunk, '.code-line');
        const gutters = all(chunk.parentElement, '.code-gutter-line');
        const markers = all(chunk.parentElement, '.code-marker').map((marker) => ({ point: +marker.dataset.point,
          line: +marker.parentElement.dataset.sourceLine, text: +marker.textContent }));
        const wanted = highlights.filter((highlight) => first <= highlight.end && last >= highlight.start)
          .map((highlight) => ({ point: highlight.point, line: Math.max(first, highlight.start), text: highlight.point }));
        const sort = (list) => list.sort((a, b) => a.line - b.line || a.point - b.point);
        const aligned = gutters.length === rows.length && rows.every((row, i) => Math.abs(
          rect(row).top + rect(row).height / 2 - rect(gutters[i]).top - rect(gutters[i]).height / 2) <= tol);
        const highlightsMatch = rows.every((row, i) => {
          const n = first + i;
          const active = highlights.filter((item) => n >= item.start && n <= item.end).map((item) => item.point);
          const previous = highlights.filter((item) => n - 1 >= item.start && n - 1 <= item.end).map((item) => item.point);
          const boundary = i > 0 && active.length > 0 && previous.length > 0 && active.join(' ') !== previous.join(' ');
          return +row.dataset.sourceLine === n && (row.dataset.highlightPoints || '') === active.join(' ') &&
            row.classList.contains('code-group-start') === boundary &&
            gutters[i].classList.contains('code-group-start') === boundary;
        });
        marksIntact &&= rows.length === last - first + 1 && aligned && highlightsMatch &&
          wanted.every(({ point }) => cards.includes(point)) && json(sort(markers)) === json(sort(wanted));
        Object.assign(evidence.at(-1), { aligned, highlightsMatch, expectedMarkers: wanted, markers });
      }
      add('code excerpts match the configured source lines', intact && next === end + 1,
        { index, lines: [start, end] }, { sourceStatus: picked.status, fragments: evidence }, id);
      if (highlights.length) add('source code highlights and card numbers', intact && next === end + 1 && marksIntact,
        'Continuous source ranges, aligned gutters and matching card and highlight numbers', { index, cards, fragments: evidence }, id);
    }
  }
  return { ids: report.ids, aliases, checks, codeReview, retroReview };
}

export async function readPrintDeck() {
  dispatchEvent(new Event('beforeprint'));
  const ready = PFScene.whenReady();
  let timeout;
  try {
    await Promise.race([ready, new Promise((_, reject) => {
      timeout = setTimeout(() => reject(new Error('Print readiness timed out.')), 35000);
    })]);
  } finally { clearTimeout(timeout); }
  await document.fonts.ready;
  await new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done)));
  const report = PFScene.report();
  const scenes = [...document.querySelectorAll('#deck > .pf-scene:not(.pf-skip)')];
  const ids = scenes.map((scene) => scene.id);
  const pages = new Map(ids.map((id, i) => [id, i + 1]));
  const checks = [];
  const add = (name, pass, expected, evidence, sceneId = null, level = 'required') => {
    checks.push({ name, pass, expected, evidence, sceneId, level });
  };
  add('print deck is ready', report.media === 'print' && report.ready === 'yes' && ids.length > 0 &&
    ids.length === report.printScenes && pages.size === ids.length,
  'Ready print rendering with one page per included scene', { media: report.media, ready: report.ready, pages: ids.length });
  const links = [];
  for (const scene of scenes) {
    for (const link of scene.querySelectorAll('a[href^="#"]')) {
      if (!link.getClientRects().length) continue;
      const target = decodeURIComponent(link.hash.slice(1));
      const targetSceneId = document.getElementById(target)?.closest('.pf-scene')?.id;
      const targetPage = pages.get(targetSceneId);
      const pageLabel = link.querySelector('.pf-n-print');
      const evidence = { sceneId: scene.id, page: pages.get(scene.id), target, targetSceneId, targetPage, label: pageLabel?.textContent };
      if (!targetPage || (pageLabel && pageLabel.textContent !== `p.${targetPage}`)) {
        add('broken print link', false, 'Existing print destination with correct page number', evidence, scene.id);
      } else links.push(evidence);
    }
  }
  add('print internal links carry destinations', links.length > 0, 'At least one internal link', { links: links.length });
  const grouped = Map.groupBy(report.audit, (item) => item.scene);
  const subpixelBoundary = (item) => {
    const over = item.type === 'outside' && / 본문 상자 밖 ([\d.]+)px$/.exec(item.detail);
    return Boolean(over && Number(over[1]) <= 1);
  };
  const roundedBodyHeight = (item) => {
    const size = item.type === 'body-scroll' && /^본문 넘침 (\d+)×(\d+) > (\d+)×(\d+)$/.exec(item.detail);
    return Boolean(size && size[1] === size[3] && Number(size[2]) - Number(size[4]) === 1);
  };
  for (const [page, items] of grouped) {
    // Integer scrollHeight can round up the same fractional print boundary. Keep that
    // evidence visible and fail every other scroll, overlap and clipping finding.
    const subpixel = items.some(subpixelBoundary) && items.every((item) => subpixelBoundary(item) || roundedBodyHeight(item));
    add('print audit', subpixel, 'No clipping or visible overflow', items, report.ids[page - 1] || null,
      subpixel ? 'note' : 'required');
  }
  return { ids, links, checks };
}
