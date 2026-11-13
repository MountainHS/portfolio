// 발표 덱 편집 모드 — project.html이 project-deck.js·doc-edit.js 다음에 싣습니다(이 포크 전용).
//   켜지는 조건: 주소가 localhost·127.0.0.1이고, 로컬 편집 서버(edit-server.py)의 /__edit/ping이 응답하고,
//   덱을 발표 덱 데이터(deck)로 그렸을 때만(예전 데이터 자동 구성 덱은 아님). GitHub Pages나 python -m http.server에서는 아무것도 하지 않습니다.
//   흐름: 덱이 다 그려지길 기다림 → data·기술 문서 md를 다시 읽음 → [편집 모드] 버튼 → 덱 위 글·그림 누르기 → 아래 입력창
//         ├ 표·코드·다이어그램 → 그 원문 md 줄 → /__edit/save (상세 문서 편집과 같은 길, 기술 문서만 고침)
//         └ 장면 제목·도입·요점·장 표지 키워드 → 글 한 칸 → /__edit/deck (deck-edits.json "deckText" + 같은 글이 있으면 기술 문서 줄, data도)
//         → 저장하면 새로고침(주소 #s=<장면>이 그대로라 같은 장면으로 돌아옴)
//   화면의 글·그림이 어느 장면인지는 원본 project-deck.js가 이미 다는 것으로 압니다: 장면 요소 id(d-<key>, slideForSceneId) · figure.vbox의 data-kind와 내용(visualRefForFigure).
//   표·코드·다이어그램만 고칠 수 있고 그림(image)은 뺐습니다.
//   덱은 늦게 온 글꼴로 다시 그려질 수 있어서, 고칠 칸을 미리 모아 두지 않고 누를 때 찾습니다.
(() => {
  const PING_URL = '/__edit/ping';
  const MD_SAVE_URL = '/__edit/save';
  const TEXT_SAVE_URL = '/__edit/deck';
  const RESTORE_KEY = 'pf-deck-edit-restore';         // 저장 뒤 새로고침에서 편집 모드를 다시 켜는 sessionStorage 키
  const POINT_SELECTOR = '.ncard, .fstep, .fe-card';  // 요점 카드·흐름 칸(문제 해결)·화면 카드 — 장면 안 순서 = data points 순서
  const EDITABLE_FIGURE_SELECTOR = 'figure.vbox:is([data-kind="table"], [data-kind="code"], [data-kind="mermaid"])';   // 그림(image)은 편집에서 뺌
  const IMAGE_CAPTION_SELECTOR = 'figure.vbox[data-kind="image"] > figcaption.cap';   // 그림 아래 설명 글(코드 캡션은 렌더러가 만든 라벨이라 제외)
  const VISUAL_NAMES = { table: '표', image: '그림', code: '코드', mermaid: '다이어그램' };
  // 덱이 원문 블록을 찾는 지문(md-blocks.js pick). 바꾸면 다시 빌드할 때까지 그 장면에 띠가 뜸
  const FINGERPRINT_PARTS = { table: '표 머리 칸을', image: '그림 경로를', code: '코드 첫 줄을', mermaid: '다이어그램 첫 줄을' };

  start();

  /** 편집 모드 진입점. 편집 서버가 있고 덱 데이터로 그린 덱일 때만, 다 그려진 뒤 버튼을 답니다. */
  async function start() {
    const isLocalAddress = ['localhost', '127.0.0.1'].includes(location.hostname);
    if (isLocalAddress === false) return;
    const hasEditServer = await pingEditServer();
    if (hasEditServer === false) return;

    await PFScene.whenReady();
    const isDeckData = PFScene.info.mode === 'deck';
    if (isDeckData === false) return;
    const source = await loadSource(PFScene.info.id);
    const session = { source, toggleButton: null, openEditor: null };
    listenForClicks(session);
    addToggleButton(session);
    restoreAfterSave(session);
  }

  /** 편집 서버가 떠 있는지 묻습니다. 응답이 없거나 404면 false(= 보통 미리보기 서버·Pages). */
  async function pingEditServer() {
    try {
      const response = await fetch(PING_URL, { cache: 'no-store' });
      return response.ok;
    } catch {
      return false;
    }
  }

  /**
   * 덱을 그린 것과 같은 data와 기술 문서 md를 다시 읽고, md 블록마다 줄 범위를 잽니다(상세 문서 편집의 mapBlocksToLines).
   * 반환: { id, project: data, mdPath: 서버에 보낼 md 경로, lines: md 줄 목록, parsed: PF.md.parse 결과, blocks: [{ start, end, elements }] }
   */
  async function loadSource(id) {
    const project = await (await fetch(`data/projects/${encodeURIComponent(id)}.json`, { cache: 'no-store' })).json();
    const md = await (await fetch(PF.srcPath(id, project.doc), { cache: 'no-store' })).text();
    const parsed = PF.md.parse(md, id);
    return {
      id,
      project,
      mdPath: `src/${id}/${project.doc}`,
      lines: md.replace(/\r\n?/g, '\n').split('\n'),
      parsed,
      blocks: PFDocEdit.mapBlocksToLines(md, parsed.frag),
    };
  }

  // ── 누른 곳 → 고칠 칸 ──

  /** 편집 모드에서 덱 위 글·그림을 누르면 입력창을 엽니다(링크 이동·장면 넘기기보다 먼저 받음). 고칠 칸이 아니면 그대로 둡니다. */
  function listenForClicks(session) {
    document.addEventListener('click', (event) => {
      const isEditing = document.body.classList.contains('pf-editing');
      if (isEditing === false) return;
      const isOnEditorControls = event.target.closest('.pf-edit-panel, .pf-edit-toggle') !== null;
      if (isOnEditorControls) return;
      const spot = findSpot(session, event.target);
      if (spot === null) return;
      event.preventDefault();
      event.stopPropagation();
      openEditor(session, spot);
    }, true);
  }

  /**
   * 누른 요소가 어느 고칠 칸인지 찾습니다.
   * 반환: { kind: 'md', element, label, block, note } (원문 블록) | { kind: 'text' | 'keywords', element, label, request, value } (장면 글) | null(고칠 칸 아님)
   */
  function findSpot(session, target) {
    const keywordLine = target.closest('.ch-list .ld');
    if (keywordLine !== null) return keywordSpot(session, keywordLine);
    const scene = target.closest('.pf-scene[id]');
    if (scene === null) return null;
    const slide = slideForSceneId(session, scene.id);
    if (slide === undefined) return null;

    const figure = target.closest(EDITABLE_FIGURE_SELECTOR);
    const imageCaption = target.closest(IMAGE_CAPTION_SELECTOR);
    const title = target.closest('.pf-title');
    const lead = target.closest('.pf-lead');
    const point = target.closest(POINT_SELECTOR);
    if (figure !== null) return visualSpot(session, slide, figure);
    if (imageCaption !== null) return captionSpot(session, slide, imageCaption);
    if (title !== null) return textSpot(session, slide, title, '장면 제목', { field: 'title' });
    if (lead !== null) return textSpot(session, slide, lead, '도입 글', { field: 'lead' });
    if (point !== null) {
      const number = [...scene.querySelectorAll(POINT_SELECTOR)].indexOf(point) + 1;
      return textSpot(session, slide, point, `요점 ${number}`, { field: 'point', n: number });
    }
    return null;
  }

  /**
   * 장면 요소의 id(원본 project-deck.js가 다는 d-<key>)에서 data의 장면(deck.slides[])을 찾습니다.
   * 나뉜 장면의 id는 d-<key>-2(코드 나뉨)·d-<key>-t1(표 나뉨)·둘의 조합이라, 꼬리가 그 꼴일 때만 key 뒤에 붙은 것으로 봅니다.
   * key 자체에 -숫자가 들어갈 수 있어 '-'로 자르지 않고, 정확히 같은 key를 먼저, 없으면 가장 긴 key를 고릅니다.
   * 반환: slide | undefined (표지·목차·장 표지·회고·맺음, key 오류 장면 d1-2 꼴은 undefined → 눌러도 아무 일 없음)
   */
  function slideForSceneId(session, sceneId) {
    const isDeckSlideId = sceneId.startsWith('d-');
    if (isDeckSlideId === false) return undefined;
    const rest = sceneId.slice(2);
    const slides = session.source.project.deck?.slides || [];
    const exact = slides.find((slide) => slide.key === rest);
    if (exact !== undefined) return exact;

    const splitSuffix = /^(-\d+|-t\d+)+$/;
    const prefixed = slides.filter((slide) => rest.startsWith(slide.key) && splitSuffix.test(rest.slice(slide.key.length)));
    prefixed.sort((first, second) => second.key.length - first.key.length);
    return prefixed[0];
  }

  /** 장 표지 목록의 키워드 한 줄 → 그 줄이 가리키는 장면의 keywords */
  function keywordSpot(session, keywordLine) {
    const sceneId = keywordLine.closest('a[data-pf-ref]')?.dataset.pfRef;
    if (sceneId === undefined) return null;
    const slide = slideForSceneId(session, sceneId);
    if (slide === undefined) return null;
    return textSpot(session, slide, keywordLine, '키워드', { field: 'keywords' });
  }

  /**
   * 장면 글 한 칸. 값은 data에서 읽습니다(화면 글자는 줄임표·강조가 섞일 수 있음).
   * place: { field: title | lead | point | keywords, n?: 요점 번호(1부터) }. 빈 칸이면 null(화면에 다른 글이 대신 보이는 자리).
   */
  function textSpot(session, slide, element, label, place) {
    const value = slideTextValue(slide, place);
    const isEmpty = value === '' || (Array.isArray(value) && value.length === 0);
    if (isEmpty) return null;
    const kind = place.field === 'keywords' ? 'keywords' : 'text';
    return { kind, element, label, request: { project: session.source.id, slide: slide.key, ...place }, value };
  }

  /**
   * 그림 아래 설명 글 → data 장면 visuals[]의 caption. 값의 주인은 data라(project-deck.js가 caption을 먼저 읽음) 장면 글과 같은 길(/__edit/deck)로 저장합니다.
   * 어느 그림인지 못 가리거나(같은 src가 둘) 캡션이 비었으면 null.
   */
  function captionSpot(session, slide, figcaption) {
    const figure = figcaption.parentElement;
    const ref = visualRefForFigure(session, slide, figure);
    if (ref === null) return null;
    const number = slide.visuals.indexOf(ref) + 1;
    const value = slideTextValue(slide, { field: 'caption', n: number });
    if (value === '') return null;
    return { kind: 'text', element: figcaption, label: '그림 설명', request: { project: session.source.id, slide: slide.key, field: 'caption', n: number }, value };
  }

  /** data 장면의 글 칸 값. 글은 { text, verbatim } 글 조각 또는 글자(schema.md 「글 조각」), 키워드는 글자 목록. 없으면 ''. */
  function slideTextValue(slide, place) {
    if (place.field === 'keywords') return Array.isArray(slide.keywords) ? slide.keywords : '';
    if (place.field === 'caption') {
      const caption = slide.visuals?.[place.n - 1]?.caption;
      return typeof caption === 'string' ? caption : caption?.text || '';
    }
    const piece = place.field === 'point' ? slide.points?.[place.n - 1] : slide[place.field];
    if (typeof piece === 'string') return piece;
    return piece?.text || '';
  }

  /**
   * 화면의 figure가 data 장면의 어느 visuals 항목(원문 블록)인지 찾습니다. 못 가리면 null(추측해 엉뚱한 md 줄을 고치지 않음).
   * 같은 종류 후보가 하나면 그것, 여럿이면 코드는 글자 포함·표는 머리 칸 글자·다이어그램은 그려진 순서로 가립니다.
   * 스크린샷 목록에서 온 항목(ref.direct)은 후보가 아닙니다. 그림(image)은 src로 가립니다(원문 블록이 아니라 캡션 글 자리를 찾는 데만 씀).
   */
  function visualRefForFigure(session, slide, figure) {
    const kind = figure.dataset.kind;
    if (kind === undefined) return null;
    const candidates = (slide.visuals || []).filter((ref) => ref.kind === kind && ref.direct !== true);
    if (kind === 'image') {
      const drawnSource = figure.querySelector('img')?.getAttribute('src');
      const sameSource = candidates.filter((ref) => PF.srcPath(session.source.id, ref.src) === drawnSource);
      return sameSource.length === 1 ? sameSource[0] : null;
    }
    if (candidates.length === 1) return candidates[0];
    if (candidates.length === 0) return null;

    if (kind === 'mermaid') {
      const scene = figure.closest('.pf-scene');
      const drawnFigures = [...scene.querySelectorAll('figure.vbox[data-kind="mermaid"]')];
      const isOrderUsable = drawnFigures.length === candidates.length;
      if (isOrderUsable === false) return null;
      return candidates[drawnFigures.indexOf(figure)];
    }

    const figureText = kind === 'code' ? normalizedText(figure.querySelector('pre')) : normalizedText(figure.querySelector('thead'));
    const matching = candidates.filter((ref) => {
      const picked = PF.md.pick(session.source.parsed, ref, slide.sectionPath || []);
      if (picked.el === undefined) return false;
      if (kind === 'code') return normalizedText(picked.el.querySelector('pre') || picked.el).includes(figureText);
      return normalizedText(picked.el.querySelector('thead')) === figureText;
    });
    return matching.length === 1 ? matching[0] : null;
  }

  /** 공백을 한 칸으로 줄인 글자(요소가 없으면 ''). 나뉜 코드 조각이 원문의 부분 글자인지 견줄 때 씀. */
  function normalizedText(element) {
    if (element === null) return '';
    return element.textContent.replace(/\s+/g, ' ').trim();
  }

  /**
   * 덱의 표·코드·다이어그램 → 그 원문 블록의 md 줄 범위. 덱과 같은 규칙(PF.md.pick)으로 원문 요소를 찾고, 그 요소를 담은 md 블록을 고릅니다.
   * 원문을 찾지 못한 그림(빨간 띠)·어느 블록인지 못 가린 그림은 null — 줄을 알 수 없으므로 상세 문서에서 고칩니다.
   */
  function visualSpot(session, slide, figure) {
    const ref = visualRefForFigure(session, slide, figure);
    if (ref === null) return null;
    const picked = PF.md.pick(session.source.parsed, ref, slide.sectionPath || []);
    if (picked.el === undefined) return null;
    const block = session.source.blocks.find((candidate) => candidate.elements.some((element) => element.contains(picked.el)));
    if (block === undefined) return null;
    const name = VISUAL_NAMES[ref.kind] || '원문';
    return {
      kind: 'md',
      element: figure,
      label: `${name} · ${session.source.mdPath} ${block.start}~${block.end}줄 (기술 문서 원문)`,
      block,
      note: `덱은 ${FINGERPRINT_PARTS[ref.kind] || '첫 줄을'} 보고 이 블록을 찾습니다. 그것을 바꾸면 /portfolio-build를 다시 할 때까지 이 장면에 띠가 뜹니다.`,
    };
  }

  // ── 화면 ──

  /** 오른쪽 아래 [편집 모드] 버튼. 켜면 고칠 수 있는 글·그림에 테두리가 보입니다(모양은 assets/doc-edit.css, 덱 자리는 deck-edit.css). */
  function addToggleButton(session) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'pf-edit-toggle';
    button.dataset.pfUi = 'edit';                     // 덱 키트 UI 표시: 미리보기 점검(덱 밖에 보이는 요소)·인쇄에서 뺌
    session.toggleButton = button;
    setEditing(session, false);
    button.addEventListener('click', () => {
      const isEditing = document.body.classList.contains('pf-editing');
      setEditing(session, isEditing === false);
    });
    document.body.appendChild(button);
  }

  function setEditing(session, isEditing) {
    document.body.classList.toggle('pf-editing', isEditing);
    session.toggleButton.textContent = isEditing ? '편집 끝내기' : '✎ 편집 모드';
    session.toggleButton.setAttribute('aria-pressed', String(isEditing));
    if (isEditing === false) session.openEditor?.close();
  }

  /**
   * 화면 아래에 그 칸의 입력창을 엽니다(장면을 가리지 않게 떠 있는 창). 한 번에 하나만 열립니다.
   * 원문 블록은 md 원문 그대로, 장면 글은 글만 보이고 저장 위치를 서버에 물어 보여 줍니다.
   */
  function openEditor(session, spot) {
    session.openEditor?.close();
    const form = document.createElement('form');
    form.className = 'pf-edit-form pf-edit-panel';
    form.dataset.pfUi = 'edit';
    form.innerHTML = `
      <p class="pf-edit-head"></p>
      <p class="pf-edit-where"></p>
      <textarea spellcheck="false"></textarea>
      <label class="pf-edit-only" hidden><input type="checkbox"> 기술 문서는 그대로 두고 발표 덱에만 저장 (deck-edits.json)</label>
      <p class="pf-edit-error" role="alert"></p>
      <div class="pf-edit-actions">
        <button type="submit" class="btn primary">저장 (Ctrl+Enter)</button>
        <button type="button" class="btn pf-edit-cancel">취소 (Esc)</button>
      </div>`;
    const textarea = form.querySelector('textarea');
    const deckOnly = form.querySelector('.pf-edit-only input');
    fillEditor(session, spot, form, textarea);

    const close = () => {
      form.remove();
      spot.element.classList.remove('pf-edit-current');
      session.openEditor = null;
    };
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      saveSpot(session, spot, textarea.value, deckOnly.checked, form);
    });
    form.querySelector('.pf-edit-cancel').addEventListener('click', close);
    deckOnly.addEventListener('change', () => showDestination(spot, deckOnly.checked, form));
    textarea.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') close();
      if (event.key === 'Enter' && event.ctrlKey) form.requestSubmit();
    });

    document.body.appendChild(form);
    spot.element.classList.add('pf-edit-current');
    session.openEditor = { close };
    if (spot.kind !== 'md') showDestination(spot, false, form);
    textarea.focus();
  }

  /** 입력창의 머리말·안내·처음 글을 칸 종류에 맞게 채웁니다. */
  function fillEditor(session, spot, form, textarea) {
    const head = form.querySelector('.pf-edit-head');
    const where = form.querySelector('.pf-edit-where');
    if (spot.kind === 'md') {
      head.textContent = spot.label;
      where.textContent = spot.note;
      textarea.value = originalMdText(session, spot.block);
      textarea.rows = Math.min(14, spot.block.end - spot.block.start + 2);
      textarea.wrap = 'off';                           // 표·코드 줄이 접히지 않게(md 한 줄 = 입력창 한 줄)
      return;
    }
    head.textContent = spot.kind === 'keywords' ? `${spot.label} 고치기 · 쉼표(,)로 나눕니다 (3개까지)` : `${spot.label} 고치기`;
    where.textContent = '저장 위치를 묻는 중…';
    textarea.value = spot.kind === 'keywords' ? spot.value.join(', ') : spot.value;
    textarea.rows = 3;
  }

  /** 서버에 장면 글의 저장 위치만 물어(dryRun) 보여 줍니다. 기술 문서에도 저장될 때만 [발표 덱에만 저장] 선택이 보입니다. */
  async function showDestination(spot, deckOnly, form) {
    const result = await postJson(TEXT_SAVE_URL, { ...spot.request, expected: spot.value, deckOnly, dryRun: true });
    const whereLine = form.querySelector('.pf-edit-where');
    if (result.ok === false) {
      whereLine.textContent = '';
      form.querySelector('.pf-edit-error').textContent = `고칠 수 없습니다: ${result.error}`;
      form.querySelector('button[type="submit"]').disabled = true;
      return;
    }
    whereLine.textContent = `저장 위치: ${result.where}`;
    const canChoose = result.canChooseOverride || deckOnly;
    form.querySelector('.pf-edit-only').hidden = canChoose === false;
  }

  /** 저장을 요청합니다. 원문 블록은 md 줄(/__edit/save), 장면 글은 /__edit/deck. 성공하면 편집 모드를 남기고 새로고침, 실패하면 서버의 이유를 보입니다. */
  async function saveSpot(session, spot, text, deckOnly, form) {
    const errorLine = form.querySelector('.pf-edit-error');
    errorLine.textContent = '저장하는 중…';
    let result;
    if (spot.kind === 'md') {
      const originalText = originalMdText(session, spot.block);
      if (text === originalText) { session.openEditor.close(); return; }
      result = await postJson(MD_SAVE_URL, { path: session.source.mdPath, start: spot.block.start, end: spot.block.end, expected: originalText, text });
    } else {
      const value = spot.kind === 'keywords' ? text.split(',').map((word) => word.trim()).filter((word) => word !== '') : text;
      result = await postJson(TEXT_SAVE_URL, { ...spot.request, expected: spot.value, value, deckOnly });
    }
    if (result.ok === false) { errorLine.textContent = `저장하지 못했습니다: ${result.error}`; return; }
    sessionStorage.setItem(RESTORE_KEY, '1');
    location.reload();
  }

  /** 원문 블록의 md 줄들(화면을 그릴 때 읽은 글). 서버는 파일의 지금 글이 이것과 다르면 저장하지 않습니다. */
  function originalMdText(session, block) {
    return session.source.lines.slice(block.start - 1, block.end).join('\n');
  }

  /** 요청 하나를 보내고 JSON 답을 돌려줍니다. 서버가 JSON이 아닌 답을 주거나 이 기능 전의 편집 서버면 { ok: false, error }. */
  async function postJson(url, body) {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    let result;
    try {
      result = await response.json();
    } catch {
      return { ok: false, error: `서버 오류(HTTP ${response.status}) — 편집 서버 창의 메시지를 확인해 주세요.` };
    }
    const isOldEditServer = response.status === 404 && result.error === '없는 주소입니다.';   // 이 기능 전의 edit-server.py가 아직 떠 있음
    if (isOldEditServer) {
      return { ok: false, error: '편집 서버가 이 기능 전의 것입니다. 서버를 껐다 켜 주세요(.\\preview.ps1 stop 뒤 .\\preview.ps1).' };
    }
    return result;
  }

  /** 저장 뒤 새로고침이면 편집 모드를 다시 켭니다(장면은 주소 #s=…로 돌아옴). */
  function restoreAfterSave(session) {
    const shouldRestore = sessionStorage.getItem(RESTORE_KEY) === '1';
    sessionStorage.removeItem(RESTORE_KEY);
    if (shouldRestore) setEditing(session, true);
  }
})();
