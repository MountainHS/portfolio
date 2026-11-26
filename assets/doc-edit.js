// 상세 문서 편집 모드 — assets/edit-loader.js가 상세 문서·슬라이드에 싣습니다(슬라이드는 mapBlocksToLines만 빌려 씀).
//   상세 문서에서는 파일 끝의 startOnDocPage()가 doc.js가 문서를 다 그리길 기다린 뒤 PFDocEdit.attach(...)를 부릅니다.
//   켜지는 조건: 주소가 localhost·127.0.0.1이고, 로컬 편집 서버(edit-server.py)의 /__edit/ping이 응답할 때만.
//   GitHub Pages나 python -m http.server에서는 아무것도 하지 않습니다(편집 버튼 없음).
//   흐름: md 블록마다 원문 줄 범위 찾기 → [편집 모드] 버튼 → 블록 누르기 → 그 줄들의 md 원문 입력창 → [저장] → 서버가 그 줄만 바꿔 씀 → 새로고침
//   블록 = 맨 위 수준의 md 덩어리(문단·제목·목록·표·코드·인용). 목록 안 한 줄만 고치는 단위는 없습니다(목록 전체가 한 블록).
//   상세 문서에서 누르는 단위 = 글 묶음: 같은 제목 아래 이어진 문단·목록·인용을 한 입력창으로 엽니다(groupTextRuns).
//     제목·표·코드·다이어그램·그림만 있는 문단·html 덩어리는 지금처럼 하나씩 따로 엽니다.
(() => {
  const PING_URL = '/__edit/ping';
  const SAVE_URL = '/__edit/save';
  const MARKER_PREFIX = 'pf-md:';                  // 줄 범위 표시 주석 <!--pf-md:시작-끝-->
  const RESTORE_KEY = 'pf-doc-edit-restore';       // 저장 뒤 새로고침에서 편집 모드·스크롤 위치를 되살리는 sessionStorage 키

  /**
   * 편집 모드 진입점. startOnDocPage가 문서를 다 그린 뒤 부릅니다.
   * id: src 폴더 이름, docFile: 그 폴더 안 프로젝트 문서 파일 이름, md: 화면을 그린 md 원문, docElement: 문서 본문(#doc)
   */
  async function attach({ id, docFile, md, docElement }) {
    const isLocalAddress = ['localhost', '127.0.0.1'].includes(location.hostname);
    if (isLocalAddress === false) return;
    const hasEditServer = await pingEditServer();
    if (hasEditServer === false) return;

    const blocks = groupTextRuns(mapBlocksToLines(md, docElement));
    const originalLines = md.replace(/\r\n?/g, '\n').split('\n');
    const session = { mdPath: `src/${id}/${docFile}`, originalLines, blocks, docElement, toggleButton: null, openEditor: null };
    markEditableBlocks(session);
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

  // ── 블록 ↔ md 줄 범위 ──

  /**
   * 화면의 블록 요소마다 md 원문 줄 범위를 찾습니다.
   * 방법: md의 맨 위 수준 토큰(marked.lexer)마다 줄 범위를 재고, 그 토큰 앞에 <!--pf-md:시작-끝--> 주석 줄을 끼운 md를 한 번 더 그립니다.
   *   주석은 요소가 아니므로 그 그림의 요소 배치는 화면과 같습니다. 그래서 주석 바로 뒤 요소의 위치(자식 순서 경로)로 화면 요소를 찾습니다.
   * 반환: [{ start, end, type, elements }] (줄은 1부터, 끝 포함. type은 topLevelTokenRanges와 같음). 화면과 배치가 어긋난 블록은 넣지 않고 콘솔에 남깁니다.
   */
  function mapBlocksToLines(md, docElement) {
    const text = md.replace(/\r\n?/g, '\n');
    const ranges = topLevelTokenRanges(text);
    const template = document.createElement('template');
    template.innerHTML = renderMarkdown(insertLineMarkers(text, ranges));

    const typeByStart = new Map(ranges.map((range) => [range.start, range.type]));
    const blocks = [];
    for (const marker of markerComments(template.content)) {
      const [start, end] = marker.data.slice(MARKER_PREFIX.length).split('-').map(Number);
      const elements = elementsAfterMarker(marker)
        .filter((element) => containsMarker(element) === false)        // <details> 같은 HTML 덩어리 안에 다른 블록이 들어 있으면 그 겉은 고치지 않음
        .map((element) => findSameElement(element, template.content, docElement));
      const isMatched = elements.length > 0 && elements.every(Boolean);
      if (isMatched) blocks.push({ start, end, type: typeByStart.get(start), elements });
      else if (elements.length > 0) console.warn(`[doc-edit] ${start}~${end}줄 블록을 화면에서 찾지 못해 편집에서 뺍니다.`);
    }
    return blocks;
  }

  /**
   * 줄 범위 표시를 끼운 md를 화면과 같은 규칙으로 그립니다. mapBlocksToLines가 화면 요소와 맞대 보는 그림입니다.
   * 원본에 PF.mdHtml(한국어 굵은 글씨 보완이 들어간 공용 md 변환)이 있으면 그것을 쓰고, 예전 원본이면 marked를 그대로 부릅니다.
   * @param {string} md 줄 범위 주석을 끼운 md
   * @returns {string} HTML 글
   */
  function renderMarkdown(md) {
    const hasSharedRenderer = typeof window.PF?.mdHtml === 'function';
    if (hasSharedRenderer) return PF.mdHtml(md);
    return marked.parse(md, { gfm: true });
  }

  /**
   * md의 맨 위 수준 토큰(빈 줄 제외)마다 줄 범위를 잽니다.
   * marked가 줄 앞 탭을 공백으로 바꾸므로 같은 규칙으로 바꾼 글에서 토큰 원문(raw)의 위치를 찾습니다(줄 수는 그대로).
   * 반환: [{ start, end, type }] (1부터, 끝 포함). type은 marked 토큰 종류(paragraph·list·blockquote·heading·table·code·html…),
   *   단 그림만 있는 문단은 'image'(글 묶음에서 그림을 따로 두려고)
   */
  function topLevelTokenRanges(text) {
    const expanded = text.replace(/^( *)(\t+)/gm, (_, leading, tabs) => leading + '    '.repeat(tabs.length));
    const ranges = [];
    let cursor = 0;
    for (const token of marked.lexer(expanded, { gfm: true })) {
      if (token.type === 'space') continue;
      const at = expanded.indexOf(token.raw, cursor);
      if (at < 0) { console.warn('[doc-edit] 토큰 위치를 찾지 못해 편집을 끕니다:', token); return []; }
      const start = countNewlines(expanded.slice(0, at)) + 1;
      const end = start + countNewlines(token.raw.replace(/\n+$/, ''));
      ranges.push({ start, end, type: isImageOnlyParagraph(token) ? 'image' : token.type });
      cursor = at + token.raw.length;
    }
    return ranges;
  }

  /** 그림(![](…))만 있고 다른 글이 없는 문단인지. 그림 사이 공백·줄바꿈은 글로 치지 않습니다. */
  function isImageOnlyParagraph(token) {
    if (token.type !== 'paragraph') return false;
    const children = token.tokens || [];
    const hasImage = children.some((child) => child.type === 'image');
    const hasOtherText = children.some((child) => child.type !== 'image' && /\S/.test(child.raw));
    return hasImage && hasOtherText === false;
  }

  const TEXT_BLOCK_TYPES = ['paragraph', 'list', 'blockquote'];   // 글 묶음으로 이어 붙이는 블록 종류

  /**
   * 이웃한 글 블록(문단·목록·인용)을 한 묶음으로 합칩니다. 상세 문서에서 누르는 단위입니다.
   * 제목·표·코드·다이어그램·그림 문단·html 블록은 하나씩 그대로 두고, 그 자리에서 글 묶음이 끊깁니다.
   * 묶음의 줄 범위는 첫 블록 시작 ~ 마지막 블록 끝이라 사이의 빈 줄·주석 줄(<!-- 촬영 -->)도 함께 고칩니다.
   * blocks: mapBlocksToLines 결과(문서 순서). 반환: [{ start, end, type, elements }] — 묶음의 type은 'text'
   */
  function groupTextRuns(blocks) {
    const groups = [];
    for (const block of blocks) {
      const previous = groups[groups.length - 1];
      const isTextBlock = TEXT_BLOCK_TYPES.includes(block.type);
      const canJoinPrevious = previous !== undefined && previous.type === 'text' && isTextBlock;
      if (canJoinPrevious) {
        previous.end = block.end;
        previous.elements.push(...block.elements);
        continue;
      }
      groups.push({ start: block.start, end: block.end, type: isTextBlock ? 'text' : block.type, elements: [...block.elements] });
    }
    return groups;
  }

  /** 각 범위의 시작 줄 앞에 <!--pf-md:시작-끝--> 줄을 끼운 md를 돌려줍니다(HTML 주석 줄은 문단을 끊고, 그 자체는 요소를 만들지 않음). */
  function insertLineMarkers(text, ranges) {
    const markerByLine = new Map(ranges.map((range) => [range.start, `<!--${MARKER_PREFIX}${range.start}-${range.end}-->`]));
    return text.split('\n').map((line, index) => {
      const marker = markerByLine.get(index + 1);
      if (marker === undefined) return line;
      return `${marker}\n${line}`;
    }).join('\n');
  }

  /** root 아래의 줄 범위 표시 주석을 문서 순서대로 모읍니다. */
  function markerComments(root) {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_COMMENT);
    const comments = [];
    while (walker.nextNode()) {
      if (walker.currentNode.data.startsWith(MARKER_PREFIX)) comments.push(walker.currentNode);
    }
    return comments;
  }

  /** 표시 주석 뒤의 형제 요소들(다음 표시 주석 전까지) = 그 토큰이 만든 요소들 */
  function elementsAfterMarker(marker) {
    const elements = [];
    for (let node = marker.nextSibling; node; node = node.nextSibling) {
      const isNextMarker = node.nodeType === Node.COMMENT_NODE && node.data.startsWith(MARKER_PREFIX);
      if (isNextMarker) break;
      if (node.nodeType === Node.ELEMENT_NODE) elements.push(node);
    }
    return elements;
  }

  function containsMarker(element) {
    return markerComments(element).length > 0;
  }

  /**
   * 표시를 끼운 그림의 요소와 같은 자리(자식 요소 순서 경로)에 있는 화면 요소를 찾습니다.
   * doc.js가 바꾼 자리도 같은 블록으로 봅니다: 표 → .table-wrap, mermaid 코드 → figure.mmd.
   * 반환: 화면 요소, 자리나 종류가 다르면 null
   */
  function findSameElement(element, templateRoot, docElement) {
    const path = [];
    for (let node = element; node !== templateRoot; node = node.parentNode) {
      path.unshift([...node.parentNode.children].indexOf(node));
    }
    let shown = docElement;
    for (const index of path) {
      shown = shown?.children[index];
    }
    if (shown === undefined || shown === null) return null;

    const isSameTag = shown.tagName === element.tagName;
    const isWrappedTable = element.tagName === 'TABLE' && shown.classList.contains('table-wrap');
    const isMermaidFigure = element.tagName === 'PRE' && shown.classList.contains('mmd');
    if (isSameTag || isWrappedTable || isMermaidFigure) return shown;
    return null;
  }

  function countNewlines(text) {
    return (text.match(/\n/g) || []).length;
  }

  // ── 화면 ──

  /** 고칠 수 있는 블록 요소에 표시를 달고, 편집 모드에서 누르면 입력창을 엽니다(링크·그림 확대보다 먼저 받음). */
  function markEditableBlocks(session) {
    session.blocks.forEach((block, index) => {
      block.elements.forEach((element) => {
        element.classList.add('pf-editable');
        element.dataset.pfBlock = String(index);
        element.title = `${block.start}~${block.end}줄 고치기`;
      });
    });
    session.docElement.addEventListener('click', (event) => {
      const isEditing = document.body.classList.contains('pf-editing');
      if (isEditing === false) return;
      const element = event.target.closest('.pf-editable');
      if (element === null) return;
      event.preventDefault();
      event.stopPropagation();
      openEditor(session, session.blocks[Number(element.dataset.pfBlock)]);
    }, true);
  }

  /** 오른쪽 아래 [편집 모드] 버튼. 켜면 고칠 수 있는 블록에 테두리가 보입니다. */
  function addToggleButton(session) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'pf-edit-toggle';
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
   * 블록 자리에 그 줄들의 md 원문 입력창을 엽니다. 한 번에 하나만 열립니다.
   * [저장](Ctrl+Enter)은 서버에 줄 범위·원래 글·새 글을 보내고, 성공하면 새로고침합니다. [취소](Esc)는 블록을 다시 보입니다.
   */
  function openEditor(session, block) {
    session.openEditor?.close();
    const originalText = session.originalLines.slice(block.start - 1, block.end).join('\n');

    const form = document.createElement('form');
    form.className = 'pf-edit-form';
    form.innerHTML = `
      <p class="pf-edit-head">${block.start}~${block.end}줄 · md 원문</p>
      <textarea spellcheck="false"></textarea>
      <p class="pf-edit-error" role="alert"></p>
      <div class="pf-edit-actions">
        <button type="submit" class="btn primary">저장 (Ctrl+Enter)</button>
        <button type="button" class="btn pf-edit-cancel">취소 (Esc)</button>
      </div>`;
    const textarea = form.querySelector('textarea');
    textarea.value = originalText;
    textarea.rows = Math.min(30, block.end - block.start + 3);

    const close = () => {
      form.remove();
      block.elements.forEach((element) => element.classList.remove('pf-edit-hidden'));
      session.openEditor = null;
    };
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      saveBlock(session, block, originalText, textarea.value, form);
    });
    form.querySelector('.pf-edit-cancel').addEventListener('click', close);
    textarea.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') close();
      if (event.key === 'Enter' && event.ctrlKey) form.requestSubmit();
    });

    block.elements[0].before(form);
    block.elements.forEach((element) => element.classList.add('pf-edit-hidden'));
    session.openEditor = { close };
    textarea.focus();
  }

  /** 서버에 저장을 요청합니다. 성공하면 편집 모드·스크롤 위치를 남기고 새로고침, 실패하면 입력창 아래에 서버의 이유를 보입니다. */
  async function saveBlock(session, block, originalText, newText, form) {
    const errorLine = form.querySelector('.pf-edit-error');
    if (newText === originalText) { session.openEditor.close(); return; }
    errorLine.textContent = '저장하는 중…';
    const response = await fetch(SAVE_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: session.mdPath, start: block.start, end: block.end, expected: originalText, text: newText }),
    });
    const result = await response.json();
    if (result.ok === false) { errorLine.textContent = `저장하지 못했습니다: ${result.error}`; return; }
    sessionStorage.setItem(RESTORE_KEY, JSON.stringify({ path: session.mdPath, scrollY: window.scrollY }));
    location.reload();
  }

  /** 저장 뒤 새로고침이면 편집 모드를 다시 켜고 보던 자리로 돌아갑니다. */
  function restoreAfterSave(session) {
    const saved = JSON.parse(sessionStorage.getItem(RESTORE_KEY) || 'null');
    sessionStorage.removeItem(RESTORE_KEY);
    if (saved === null || saved.path !== session.mdPath) return;
    setEditing(session, true);
    window.scrollTo(0, saved.scrollY);
  }

  // mapBlocksToLines는 슬라이드 편집 모드(assets/deck-edit.js)도 씀: 덱의 표·그림·코드가 md 몇 줄인지 같은 방법으로 잼
  window.PFDocEdit = { attach, mapBlocksToLines, groupTextRuns };

  /**
   * 상세 문서(doc.html)에서만 스스로 켭니다(window.PFDoc가 있을 때. 슬라이드에는 없어 건너뜀).
   * doc.js가 문서를 다 그린 뒤, doc.js와 같은 값(?p=, data/projects/<id>.json의 doc, 그 프로젝트 문서 md)을 다시 읽어 attach를 부릅니다.
   * 주소에 ?p=가 없거나 data·md를 읽지 못하면 doc.js가 이미 오류 화면을 그린 것이므로 아무것도 하지 않습니다.
   */
  async function startOnDocPage() {
    if (window.PFDoc === undefined) return;
    await PFDoc.whenReady();
    const id = new URLSearchParams(location.search).get('p');
    const docElement = document.getElementById('doc');
    if (id === null || docElement === null) return;
    let docFile;
    let md;
    try {
      const project = await PF.loadJSON(`data/projects/${encodeURIComponent(id)}.json`);
      docFile = project.doc;
      md = await PF.loadText(PF.srcPath(id, docFile));
    } catch {
      return;
    }
    await attach({ id, docFile, md, docElement });
  }
  startOnDocPage();
})();
