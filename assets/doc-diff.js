// 상세 문서 [± 바뀐 점] — assets/edit-loader.js가 상세 문서(doc.html)에 doc-edit.js 뒤로 싣습니다(이 포크 전용, 편집 서버가 있을 때만).
//   하는 일: 프로젝트 문서의 git 기준판(기본 HEAD = 커밋 안 한 변경)과 지금 글을 맨 위 블록(문단·제목·목록·표·코드) 단위로 비교해 화면에 색칠합니다.
//     새 블록 = 초록 배경 · 지운 블록 = 그 자리에 빨간 배경으로 끼워 넣음 · 고친 문단·제목·목록 = 블록 안 낱말 단위로 <del> 빨강 / <ins> 초록.
//     표·코드·다이어그램·인용은 낱말 비교를 하지 않고 블록째 지움 + 추가로 보입니다(구조가 깨지지 않게).
//   기준 바꾸기: 주소에 &diff-base=HEAD~1 (커밋한 뒤에도 직전 변경 보기). &pf-diff=1이면 열자마자 켭니다.
//   끄면 새로고침 없이 원래 화면으로 되돌립니다. 켜는 동안 편집 모드는 끕니다(색칠한 글을 원문으로 착각하지 않게).
(() => {
  const BASE_URL = '/__edit/base';
  const PAIR_SIMILARITY = 0.5;                                     // 지움·추가 블록을 "고침"으로 짝지을 최소 글자 유사도(0~1)
  const WORD_DIFF_TYPES = ['paragraph', 'heading', 'list'];        // 낱말 비교를 하는 블록 종류(marked 토큰 type)

  start();

  /** doc.js가 다 그린 뒤 기준판을 받아 버튼을 답니다. 기준판을 못 받으면 버튼을 달지 않습니다. */
  async function start() {
    if (window.PFDoc === undefined) return;
    await PFDoc.whenReady();
    const params = new URLSearchParams(location.search);
    const id = params.get('p');
    const docElement = document.getElementById('doc');
    if (id === null || docElement === null) return;

    const project = await PF.loadJSON(`data/projects/${encodeURIComponent(id)}.json`);
    const mdPath = `src/${id}/${project.doc}`;
    const currentText = await PF.loadText(PF.srcPath(id, project.doc));
    const baseRef = params.get('diff-base') || 'HEAD';
    const response = await fetch(`${BASE_URL}?path=${encodeURIComponent(mdPath)}&base=${encodeURIComponent(baseRef)}`, { cache: 'no-store' });
    const answer = await response.json();
    if (answer.ok === false) { console.warn('[doc-diff]', answer.error); return; }

    const session = { docElement, baseRef, currentText, baseText: answer.text ?? '', isNewFile: answer.text === null, undo: [], marks: [], cursor: -1, bar: null, button: null };
    addToggleButton(session);
    if (params.get('pf-diff') === '1') setDiffing(session, true);
  }

  // ── 켜고 끄기 ──

  function addToggleButton(session) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'pf-diff-toggle';
    button.textContent = '± 바뀐 점';
    button.addEventListener('click', () => setDiffing(session, session.undo.length === 0 && session.bar === null));
    document.body.appendChild(button);
    session.button = button;
  }

  function setDiffing(session, isOn) {
    if (isOn) {
      turnOffEditMode();
      const counts = paintDiff(session);
      showBar(session, counts);
    } else {
      session.undo.reverse().forEach((restore) => restore());
      session.undo = [];
      session.marks = [];
      session.bar?.remove();
      session.bar = null;
    }
    document.body.classList.toggle('pf-diffing', isOn);
    session.button.setAttribute('aria-pressed', String(isOn));
  }

  /** 편집 모드가 켜져 있으면 [편집 끝내기]를 눌러 끕니다(doc-edit.js의 버튼을 그대로 씀). */
  function turnOffEditMode() {
    const isEditing = document.body.classList.contains('pf-editing');
    if (isEditing) document.querySelector('.pf-edit-toggle')?.click();
  }

  /** 위쪽 띠: 기준·개수·[다음 변경 ▼]. */
  function showBar(session, counts) {
    const bar = document.createElement('div');
    bar.className = 'pf-diff-bar';
    const baseLabel = session.isNewFile ? `${session.baseRef}에 없던 새 파일` : `기준: ${session.baseRef}`;
    bar.innerHTML = `<span>${PF.esc(baseLabel)}</span>
      <span class="pf-diff-count add">추가 ${counts.added}</span>
      <span class="pf-diff-count del">삭제 ${counts.deleted}</span>
      <span class="pf-diff-count mod">고침 ${counts.modified}</span>
      <button type="button" class="pf-diff-next">다음 변경 ▼</button>`;
    bar.querySelector('.pf-diff-next').addEventListener('click', () => scrollToNext(session));
    document.body.appendChild(bar);
    session.bar = bar;
  }

  function scrollToNext(session) {
    if (session.marks.length === 0) return;
    session.cursor = (session.cursor + 1) % session.marks.length;
    session.marks[session.cursor].scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  // ── 비교해서 색칠 ──

  /**
   * 기준판과 지금 글을 블록 단위로 비교해 화면에 색칠하고, 되돌리기 함수를 session.undo에 쌓습니다.
   * 반환: { added, deleted, modified } 블록 개수
   */
  function paintDiff(session) {
    const currentBlocks = splitBlocks(session.currentText);
    const baseBlocks = splitBlocks(session.baseText);
    const elementsByStart = new Map(PFDocEdit.mapBlocksToLines(session.currentText, session.docElement).map((block) => [block.start, block.elements]));
    currentBlocks.forEach((block) => { block.elements = elementsByStart.get(block.start) || []; });

    const steps = pairModified(diffSequence(baseBlocks, currentBlocks, (block) => block.key));
    const counts = { added: 0, deleted: 0, modified: 0 };
    let anchor = null;                                             // 지운 블록을 끼울 자리: 바로 앞 지금 블록의 마지막 요소
    for (const step of steps) {
      if (step.kind === 'same') { anchor = lastOf(step.current.elements) || anchor; continue; }
      if (step.kind === 'add') { markAdded(session, step.current); counts.added++; anchor = lastOf(step.current.elements) || anchor; continue; }
      if (step.kind === 'del') { insertDeleted(session, step.base, anchor); counts.deleted++; continue; }
      markModified(session, step.base, step.current);
      counts.modified++;
      anchor = lastOf(step.current.elements) || anchor;
    }
    session.marks.sort((a, b) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1));
    session.cursor = -1;
    return counts;
  }

  function markAdded(session, block) {
    block.elements.forEach((element) => {
      element.classList.add('pf-diff-add');
      session.undo.push(() => element.classList.remove('pf-diff-add'));
    });
    if (block.elements[0]) session.marks.push(block.elements[0]);
  }

  /** 지운 블록을 md 그대로 그려 anchor 뒤(없으면 문서 맨 앞)에 끼웁니다. */
  function insertDeleted(session, block, anchor) {
    const box = document.createElement('div');
    box.className = 'pf-diff-del';
    box.innerHTML = PF.mdHtml(block.raw);
    if (anchor) anchor.after(box);
    else session.docElement.prepend(box);
    session.undo.push(() => box.remove());
    session.marks.push(box);
  }

  /** 고친 블록: 화면 요소 하나의 안쪽을 낱말 비교 결과로 바꿉니다. 그려 보니 모양이 다르면 지움 + 추가로 보입니다. */
  function markModified(session, baseBlock, currentBlock) {
    const element = currentBlock.elements.length === 1 ? currentBlock.elements[0] : null;
    const template = document.createElement('template');
    template.innerHTML = PF.mdHtml(wordDiffMarkdown(baseBlock.raw, currentBlock.raw));
    const rendered = template.content.firstElementChild;
    const isSameShape = element !== null && rendered !== null && template.content.childElementCount === 1 && rendered.tagName === element.tagName;
    if (isSameShape === false) {
      markAdded(session, currentBlock);
      insertDeleted(session, baseBlock, currentBlock.elements[0]?.previousElementSibling || null);
      return;
    }
    const originalHtml = element.innerHTML;
    element.innerHTML = rendered.innerHTML;
    element.classList.add('pf-diff-mod');
    session.undo.push(() => { element.innerHTML = originalHtml; element.classList.remove('pf-diff-mod'); });
    session.marks.push(element);
  }

  // ── 블록 나누기 ──

  /**
   * md를 맨 위 블록으로 나눕니다(doc-edit.js의 topLevelTokenRanges와 같은 방법 — 시작 줄로 화면 요소와 잇기 위해).
   * 반환: [{ start, type, raw, key }] start는 1부터인 줄, key는 비교용(줄 끝 공백·CRLF 정리)
   */
  function splitBlocks(text) {
    const normalized = text.replace(/\r\n?/g, '\n');
    const expanded = normalized.replace(/^( *)(\t+)/gm, (_, leading, tabs) => leading + '    '.repeat(tabs.length));
    const blocks = [];
    let cursor = 0;
    for (const token of marked.lexer(expanded, { gfm: true })) {
      if (token.type === 'space') continue;
      const at = expanded.indexOf(token.raw, cursor);
      if (at < 0) continue;
      const start = (expanded.slice(0, at).match(/\n/g) || []).length + 1;
      const lineCount = (token.raw.replace(/\n+$/, '').match(/\n/g) || []).length + 1;
      const raw = normalized.split('\n').slice(start - 1, start - 1 + lineCount).join('\n');
      blocks.push({ start, type: token.type, raw, key: raw.replace(/[ \t]+$/gm, '') });
      cursor = at + token.raw.length;
    }
    return blocks;
  }

  // ── 비교 알고리즘 ──

  /**
   * 두 목록의 최장 공통 부분열(LCS)로 같음·지움·추가 순서를 만듭니다.
   * keyOf: 원소를 비교할 글을 돌려주는 함수
   * 반환: [{ kind: 'same'|'del'|'add', base?, current? }] 문서 순서
   */
  function diffSequence(baseItems, currentItems, keyOf) {
    const rows = baseItems.length;
    const cols = currentItems.length;
    const lengths = Array.from({ length: rows + 1 }, () => new Uint32Array(cols + 1));
    for (let i = rows - 1; i >= 0; i--) {
      for (let j = cols - 1; j >= 0; j--) {
        const isSame = keyOf(baseItems[i]) === keyOf(currentItems[j]);
        lengths[i][j] = isSame ? lengths[i + 1][j + 1] + 1 : Math.max(lengths[i + 1][j], lengths[i][j + 1]);
      }
    }
    const steps = [];
    let i = 0;
    let j = 0;
    while (i < rows && j < cols) {
      const isSame = keyOf(baseItems[i]) === keyOf(currentItems[j]);
      if (isSame) { steps.push({ kind: 'same', base: baseItems[i++], current: currentItems[j++] }); continue; }
      if (lengths[i + 1][j] >= lengths[i][j + 1]) steps.push({ kind: 'del', base: baseItems[i++] });
      else steps.push({ kind: 'add', current: currentItems[j++] });
    }
    while (i < rows) steps.push({ kind: 'del', base: baseItems[i++] });
    while (j < cols) steps.push({ kind: 'add', current: currentItems[j++] });
    return steps;
  }

  /**
   * 붙어 있는 지움·추가 묶음에서 같은 종류이고 닮은 블록을 차례로 짝지어 'mod'로 바꿉니다.
   * 짝이 안 된 것은 지움·추가 그대로 둡니다.
   */
  function pairModified(steps) {
    const result = [];
    let index = 0;
    while (index < steps.length) {
      if (steps[index].kind === 'same') { result.push(steps[index++]); continue; }
      const deleted = [];
      const added = [];
      while (index < steps.length && steps[index].kind !== 'same') {
        if (steps[index].kind === 'del') deleted.push(steps[index].base);
        else added.push(steps[index].current);
        index++;
      }
      result.push(...pairRun(deleted, added));
    }
    return result;
  }

  /** 한 묶음 안에서 추가 블록마다 아직 안 쓴 닮은 지움 블록을 찾아 짝짓습니다. 지움 블록은 짝의 앞자리에 둡니다. */
  function pairRun(deleted, added) {
    const used = new Set();
    const result = [];
    for (const current of added) {
      const partnerIndex = deleted.findIndex((base, k) => used.has(k) === false && canPair(base, current));
      if (partnerIndex < 0) { result.push({ kind: 'add', current }); continue; }
      deleted.slice(0, partnerIndex).forEach((base, k) => {
        if (used.has(k)) return;
        used.add(k);
        result.push({ kind: 'del', base });
      });
      used.add(partnerIndex);
      result.push({ kind: 'mod', base: deleted[partnerIndex], current });
    }
    deleted.forEach((base, k) => { if (used.has(k) === false) result.push({ kind: 'del', base }); });
    return result;
  }

  function canPair(base, current) {
    const isSameType = base.type === current.type;
    const isWordDiffType = WORD_DIFF_TYPES.includes(current.type);
    const isSimilar = similarity(base.key, current.key) >= PAIR_SIMILARITY;
    return isSameType && isWordDiffType && isSimilar;
  }

  /** 두 글의 낱말 LCS 비율(0~1). 긴 쪽 낱말 수로 나눕니다. */
  function similarity(a, b) {
    const wordsA = a.split(/\s+/).filter(Boolean);
    const wordsB = b.split(/\s+/).filter(Boolean);
    const longer = Math.max(wordsA.length, wordsB.length);
    if (longer === 0) return 1;
    const same = diffSequence(wordsA, wordsB, (word) => word).filter((step) => step.kind === 'same').length;
    return same / longer;
  }

  /**
   * 두 md 블록을 낱말 단위로 비교해, 지운 낱말은 <del>, 넣은 낱말은 <ins>로 감싼 md 하나로 합칩니다.
   * 공백·줄바꿈은 지금 글 것을 씁니다. 줄 머리 기호(목록 -, 1., 제목 #)는 감싸지 않습니다(감싸면 목록·제목이 깨짐).
   */
  function wordDiffMarkdown(baseRaw, currentRaw) {
    const baseTokens = baseRaw.match(/\s+|\S+/g) || [];
    const currentTokens = currentRaw.match(/\s+|\S+/g) || [];
    const baseWords = baseTokens.filter((token) => /\S/.test(token));
    const currentWords = currentTokens.map((token, k) => ({ token, k })).filter((item) => /\S/.test(item.token));
    const steps = diffSequence(baseWords.map((word) => ({ word })), currentWords.map((item) => ({ word: item.token, k: item.k })), (item) => item.word);

    // 지금 글의 낱말 자리마다 그 앞에 붙일 지운 낱말과 자신의 표시(넣음/같음)를 모읍니다
    const deletedBefore = new Map();
    const insertedAt = new Set();
    let pendingDeleted = [];
    for (const step of steps) {
      if (step.kind === 'del') { pendingDeleted.push(step.base.word); continue; }
      if (pendingDeleted.length > 0) { deletedBefore.set(step.current.k, pendingDeleted); pendingDeleted = []; }
      if (step.kind === 'add') insertedAt.add(step.current.k);
    }

    const pieces = currentTokens.map((token, k) => {
      const isMarker = isLineMarker(currentTokens, k);
      const deleted = deletedBefore.get(k);
      const deletedHtml = deleted ? `<del>${deleted.join(' ')}</del> ` : '';
      if (isMarker) return token + (deletedHtml ? ' ' + deletedHtml.trimEnd() : '');
      const body = insertedAt.has(k) ? `<ins>${token}</ins>` : token;
      return deletedHtml + body;
    });
    const trailing = pendingDeleted.length > 0 ? ` <del>${pendingDeleted.join(' ')}</del>` : '';
    return pieces.join('') + trailing;
  }

  /** k번째 낱말이 줄 머리 md 기호(목록 -·*·+·1.·제목 #·인용 >)인지. */
  function isLineMarker(tokens, k) {
    const isMarkerShape = /^([-*+>]|\d+[.)]|#{1,6})$/.test(tokens[k]);
    const previous = k === 0 ? '\n' : tokens[k - 1];
    const isAtLineStart = k === 0 || /\n[ \t]*$/.test(previous) || (k === 1 && /^[ \t]*$/.test(previous));
    return isMarkerShape && isAtLineStart;
  }

  function lastOf(items) {
    return items.length > 0 ? items[items.length - 1] : null;
  }
})();
