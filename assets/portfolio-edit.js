// 포트폴리오 페이지 편집 모드 — index.html이 portfolio.js 다음에 싣습니다(이 포크 전용).
//   켜지는 조건: 주소가 localhost·127.0.0.1이고, 로컬 편집 서버(edit-server.py)의 /__edit/ping이 응답할 때만.
//   GitHub Pages나 python -m http.server에서는 아무것도 하지 않습니다(편집 버튼 없음).
//   흐름: 페이지를 다 그린 뒤 data를 다시 읽어 화면의 글 칸마다 data 칸을 짝짓기 → [편집 모드] 버튼 → 글 누르기
//         → 아래 입력창(저장 위치를 서버에 물어 보여 줌) → [저장] → 서버가 소스(profile.md·프로젝트 문서·deck-edits.json)와 data를 씀 → 새로고침
//   어디에 쓸지는 서버(.claude/skills/portfolio-build/portfolio-edits.py)가 정합니다. 이 파일은 "화면의 어느 칸이 data의 어느 칸인지"만 압니다.
(() => {
  const PING_URL = '/__edit/ping';
  const SAVE_URL = '/__edit/portfolio';
  const RESTORE_KEY = 'pf-portfolio-edit-restore';  // 저장 뒤 새로고침에서 편집 모드를 다시 켜는 sessionStorage 키(스크롤 위치는 portfolio.js가 되살림)

  start();

  /** 편집 모드 진입점. 편집 서버가 있을 때만 페이지가 다 그려지길 기다렸다가 고칠 칸에 표시를 답니다. */
  async function start() {
    const isLocalAddress = ['localhost', '127.0.0.1'].includes(location.hostname);
    if (isLocalAddress === false) return;
    const hasEditServer = await pingEditServer();
    if (hasEditServer === false) return;

    await PFPage.whenReady();
    const data = await loadData();
    const spots = findEditableSpots(data);
    if (spots.length === 0) return;
    const session = { spots, toggleButton: null, openEditor: null };
    markEditableSpots(session);
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

  /** 화면을 그린 것과 같은 data를 읽습니다(portfolio.js와 같은 순서, 읽지 못한 프로젝트는 뺌 — 화면에도 없음). */
  async function loadData() {
    const portfolio = await (await fetch('data/portfolio.json', { cache: 'no-store' })).json();
    const projects = await Promise.all((portfolio.projects || []).map(async (id) => {
      const response = await fetch(`data/projects/${encodeURIComponent(id)}.json`, { cache: 'no-store' });
      return response.ok ? response.json() : null;
    }));
    return { portfolio, projects: projects.filter(Boolean) };
  }

  // ── 화면의 글 칸 ↔ data 칸 ──

  /**
   * 화면에서 고칠 수 있는 글 칸을 모읍니다. 칸의 자리는 portfolio.js가 그리는 HTML 구조(절 id·클래스)로 찾습니다.
   * 반환: [{ element, label, request: 서버에 보낼 칸 이름·번호, value: 지금 data 값, kind: 'line' | 'lines' | 'tags' }]
   * 빈 칸(화면에 안 나오는 값)은 넣지 않습니다.
   */
  function findEditableSpots({ portfolio, projects }) {
    const spots = [];
    const add = (element, label, request, value, kind = 'line') => {
      const isEmpty = value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0);
      if (element === null || isEmpty) return;
      spots.push({ element, label, request, value, kind });
    };
    const profile = portfolio.profile || {};
    const cover = document.getElementById('cover');
    const contact = document.getElementById('contact');

    // ① 머리 · 자기소개 · 연락처 → profile.md  ([포크] B안 배치: 인사 문구는 문구 표라 이름만 .nm 칸)
    add(contact?.querySelector('.ct-name [lang="en"]') ?? null, '영문 이름', { field: 'profile.nameEn' }, profile.nameEn);
    add(cover?.querySelector('h1 .nm') ?? null, '이름', { field: 'profile.name' }, profile.name);
    add(cover?.querySelector('.headline') ?? null, '한 줄 정체성', { field: 'profile.headline' }, profile.headline);
    document.querySelectorAll('#cover a[href^="mailto:"], #contact .em a').forEach((element) => add(element, '이메일', { field: 'profile.email' }, profile.email));
    document.querySelectorAll('#cover .links a, #contact > a').forEach((element) => {
      if (element.getAttribute('href') === profile.github) add(element, 'GitHub 주소', { field: 'profile.github' }, profile.github);
    });
    add(document.querySelector('#cover .about'), '자기소개', { field: 'profile.intro' }, profile.intro, 'lines');

    // ② 기술 스택: 기술 이름(profile.md 메모) · 근거 문장
    document.querySelectorAll('#stack .skill').forEach((skillElement, skillIndex) => {
      const skill = portfolio.skills[skillIndex];
      add(skillElement.querySelector('h3'), '기술 이름', { field: 'skill.name', skill: skillIndex }, skill.name);
      skillElement.querySelectorAll(':scope > ul > li').forEach((item, evidenceIndex) => {
        add(item.querySelector('span'), `${skill.name} 근거`, { field: 'skill.evidence', skill: skillIndex, evidence: evidenceIndex }, skill.evidence[evidenceIndex].text);
      });
    });

    // ③ 프로젝트마다 카드 · 상세 패널(카드는 상세 패널의 View Project 링크로 짝지음)
    projects.forEach((project) => {
      const card = document.querySelector(`#projects .card a.more[href="#p-${CSS.escape(project.id)}"]`)?.closest('.card') ?? null;
      addProjectSpots(add, project, card, document.getElementById(`p-${project.id}`));
    });
    return spots;
  }

  /** 프로젝트 카드(.card)와 상세 패널(#p-<폴더>)의 글 칸. 카드 메타(연도·개발 기간·엔진)는 잘라 보인 값이라 편집 대상이 아님 */
  function addProjectSpots(add, project, card, detail) {
    const ask = (field, extra = {}) => ({ field, project: project.id, ...extra });
    if (card !== null) {
      add(card.querySelector('.title'), '프로젝트 이름', ask('title'), project.title);
      add(card.querySelector('.role'), '내 역할', ask('team.role'), project.team?.role);
      add(card.querySelector('.intent'), '개발 의도', ask('intent'), project.intent?.text);
    }
    if (detail === null) return;
    add(detail.querySelector('h3'), '프로젝트 이름', ask('title'), project.title);
    add(detail.querySelector('.d-role'), '내 역할', ask('team.role'), project.team?.role);
    add(detail.querySelector('.tagline'), '한 줄 소개', ask('tagline'), project.tagline);
    detail.querySelectorAll('.meta > div').forEach((row, index) => {
      add(row.querySelector('dd'), project.meta[index].label, ask('meta', { index }), project.meta[index].value);
    });
    add(detail.querySelector('.tags'), '태그', ask('tags'), project.tags, 'tags');
    detail.querySelectorAll('.features > li[data-fi]').forEach((row) => {
      const index = Number(row.dataset.fi);
      const highlight = project.highlights[index];
      add(row.querySelector('h4'), `기능 ${index + 1} 제목`, ask('highlight.title', { index }), highlight.title);
      add(row.querySelector('.what'), `기능 ${index + 1} 설명`, ask('highlight.what', { index }), highlight.what);
    });
  }
  // ── 화면 ──

  /** 고칠 칸에 표시를 달고, 편집 모드에서 누르면 입력창을 엽니다(링크 이동보다 먼저 받음). */
  function markEditableSpots(session) {
    session.spots.forEach((spot, index) => {
      spot.element.classList.add('pf-editable');
      spot.element.dataset.pfSpot = String(index);
    });
    document.addEventListener('click', (event) => {
      const isEditing = document.body.classList.contains('pf-editing');
      if (isEditing === false) return;
      const element = event.target.closest('.pf-editable');
      if (element === null) return;
      event.preventDefault();
      event.stopPropagation();
      openEditor(session, session.spots[Number(element.dataset.pfSpot)]);
    }, true);
  }

  /** 오른쪽 아래 [편집 모드] 버튼. 켜면 고칠 수 있는 글에 테두리가 보입니다(상세 문서와 같은 버튼 모양, assets/doc-edit.css). */
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
   * 화면 아래에 그 칸의 입력창을 엽니다(글 자리를 밀지 않게 떠 있는 창). 한 번에 하나만 열립니다.
   * 열 때 서버에 저장 위치를 물어 보여 주고, 프로젝트 문서에 저장될 글이면 [포트폴리오에만 저장] 선택을 보입니다.
   */
  function openEditor(session, spot) {
    session.openEditor?.close();
    const form = document.createElement('form');
    form.className = 'pf-edit-form pf-edit-panel';
    form.innerHTML = `
      <p class="pf-edit-head"></p>
      <p class="pf-edit-where">저장 위치를 묻는 중…</p>
      <textarea spellcheck="false"></textarea>
      <label class="pf-edit-only" hidden><input type="checkbox"> 프로젝트 문서는 그대로 두고 포트폴리오에만 저장 (deck-edits.json)</label>
      <p class="pf-edit-error" role="alert"></p>
      <div class="pf-edit-actions">
        <button type="submit" class="btn primary">저장 (Ctrl+Enter)</button>
        <button type="button" class="btn pf-edit-cancel">취소 (Esc)</button>
      </div>`;
    form.querySelector('.pf-edit-head').textContent = spot.kind === 'tags' ? `${spot.label} 고치기 · 쉼표(,)로 나눕니다` : `${spot.label} 고치기`;
    const textarea = form.querySelector('textarea');
    textarea.value = spot.kind === 'tags' ? spot.value.join(', ') : spot.value;
    textarea.rows = spot.kind === 'lines' ? 6 : 2;
    const onlyPortfolio = form.querySelector('.pf-edit-only input');

    const close = () => {
      form.remove();
      spot.element.classList.remove('pf-edit-current');
      session.openEditor = null;
    };
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      saveSpot(spot, textarea.value, onlyPortfolio.checked, form);
    });
    form.querySelector('.pf-edit-cancel').addEventListener('click', close);
    onlyPortfolio.addEventListener('change', () => showDestination(spot, onlyPortfolio.checked, form));
    textarea.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') close();
      if (event.key === 'Enter' && event.ctrlKey) form.requestSubmit();
    });

    document.body.appendChild(form);
    spot.element.classList.add('pf-edit-current');
    session.openEditor = { close };
    showDestination(spot, false, form);
    textarea.focus();
  }

  /** 서버에 저장 위치만 물어(dryRun) 입력창 위에 보입니다. 프로젝트 문서에 저장될 때만 [포트폴리오에만 저장] 선택이 보입니다. */
  async function showDestination(spot, portfolioOnly, form) {
    const result = await postEdit({ ...spot.request, expected: spot.value, portfolioOnly, dryRun: true });
    const whereLine = form.querySelector('.pf-edit-where');
    if (result.ok === false) {
      whereLine.textContent = '';
      form.querySelector('.pf-edit-error').textContent = `고칠 수 없습니다: ${result.error}`;
      form.querySelector('button[type="submit"]').disabled = true;
      return;
    }
    whereLine.textContent = `저장 위치: ${result.where}`;
    const canChoose = result.canChooseOverride || portfolioOnly;
    form.querySelector('.pf-edit-only').hidden = canChoose === false;
  }

  /** 서버에 저장을 요청합니다. 성공하면 편집 모드를 남기고 새로고침, 실패하면 입력창 아래에 서버의 이유를 보입니다. */
  async function saveSpot(spot, text, portfolioOnly, form) {
    const errorLine = form.querySelector('.pf-edit-error');
    const value = spot.kind === 'tags' ? text.split(',').map((tag) => tag.trim()).filter((tag) => tag !== '') : text;
    errorLine.textContent = '저장하는 중…';
    const result = await postEdit({ ...spot.request, expected: spot.value, value, portfolioOnly });
    if (result.ok === false) { errorLine.textContent = `저장하지 못했습니다: ${result.error}`; return; }
    sessionStorage.setItem(RESTORE_KEY, '1');
    location.reload();
  }

  /** /__edit/portfolio에 요청 하나를 보내고 JSON 답을 돌려줍니다. 서버가 JSON이 아닌 답(처리 중 오류)을 주면 { ok: false, error }. */
  async function postEdit(body) {
    const response = await fetch(SAVE_URL, {
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

  /** 저장 뒤 새로고침이면 편집 모드를 다시 켭니다. */
  function restoreAfterSave(session) {
    const shouldRestore = sessionStorage.getItem(RESTORE_KEY) === '1';
    sessionStorage.removeItem(RESTORE_KEY);
    if (shouldRestore) setEditing(session, true);
  }
})();
