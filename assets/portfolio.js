// 포트폴리오 페이지(index.html) — 아래로 읽는 한 페이지 + A4 인쇄(제출 PDF)
//   [포크] B안 순서: 머리(인사·자기소개·개발 분야) → 프로젝트 카드 3열 → 프로젝트 상세 패널(기능마다 그림·설명·[슬라이드 열기]) → 기술 스택 → 연락처
//   프로젝트는 data/portfolio.json의 projects 순서대로 표시합니다.
//   모든 글은 data/portfolio.json·data/projects/*.json 값 그대로입니다. 절 이름·버튼 같은 UI 문구는 문구 표(PF.t, assets/strings.js + theme/strings.tsv)에서 옵니다.
//   주소: #<절 id>(cover · projects · p-<폴더> · stack · contact, 예전 intro · glance · p-<폴더>-core도 받음). 예전 포트폴리오 슬라이드 주소(#s=<장면 id> · #<번호>)는 같은 절로 넘깁니다.
//   점검: PFPage.whenReady() → PFPage.report() · ?pf-audit=1이면 #pf-audit에 report()의 base64 JSON(headless용, 슬라이드와 같은 형식)
(() => {
  const root = document.getElementById('app');
  const html = document.documentElement;
  const { esc, inline, isUrl, shortUrl, link, t } = PF;
  const T = (k, v) => esc(t(k, v));                                    // HTML에 넣는 문구
  const pad2 = (n) => String(n).padStart(2, '0');
  const info = { errors: [], sections: [], projects: 0 };
  html.dataset.pfReady = 'no';
  // 처음 주소로 도착한 절, 또는 새로고침 전 스크롤 위치. 글꼴·그림이 늦게 와 위치가 밀리면 settle()에서 한 번 더 맞춤(그사이 사용자가 움직이지 않았을 때만)
  let arrived = '', restoreY = null, userMoved = false;
  ['wheel', 'touchstart', 'keydown', 'pointerdown'].forEach((t) => addEventListener(t, () => { userMoved = true; }, { once: true, passive: true }));
  const POS = `pf-portfolio-y:${location.pathname}`;                 // 새로고침해도 읽던 곳으로(내용을 늦게 그려 브라우저가 스스로 되돌리지 못함)
  try { history.scrollRestoration = 'manual'; } catch { /* 그대로 */ }
  addEventListener('pagehide', () => { try { sessionStorage.setItem(POS, String(Math.round(scrollY))); } catch { /* 저장소 막힘 */ } });
  let resolveReady;
  const ready = new Promise((r) => { resolveReady = r; });
  window.PFPage = { whenReady: () => ready, report };

  start().catch((err) => {
    info.errors.push(String(err && err.stack || err));
    PF.notice(root, '페이지를 그리지 못했습니다', `<p>${esc(err && err.message || err)}</p>`);
  }).finally(settle);

  async function start() {
    if (PF.guardFileProtocol(root)) { info.errors.push('file:// — 로컬 서버로 열어야 합니다'); return; }
    let pf;
    const strings = PF.loadStrings();                                   // 실패해도 기본 문구(던지지 않음)
    try { pf = await PF.loadJSON('data/portfolio.json'); }
    catch (err) { info.errors.push(`data/portfolio.json: ${err && err.message || err}`); PF.noData(root, err); return; }
    PF.applyTheme(pf.settings?.accent);
    const projects = (await Promise.all((pf.projects || []).map((id) => PF.loadJSON(`data/projects/${encodeURIComponent(id)}.json`)
      .catch((err) => {                                                 // 그 프로젝트만 빼고 그리되, 점검(report().errors)에는 남김 — 조용히 사라지지 않게
        console.warn('[portfolio] 프로젝트 데이터 없음:', err.message);
        info.errors.push(`data/projects/${id}.json: ${err && err.message || err}`);
        return null;
      })))).filter(Boolean);
    await strings;
    render(pf, projects);
    bind();
    arrived = go(projects);                                            // 내용을 그린 뒤라 브라우저가 처음 주소의 절로 스스로 가지 못함
    if (!arrived && performance.getEntriesByType?.('navigation')[0]?.type === 'reload') {
      try { restoreY = +sessionStorage.getItem(POS) || null; } catch { /* 저장소 막힘 */ }
      if (restoreY) scrollTo({ top: restoreY, behavior: 'instant' });
    }
    addEventListener('hashchange', () => go(projects));                // 같은 페이지에서 예전 주소(#s=…)를 붙여 넣은 경우
  }

  function go(projects) {
    const want = legacyTarget(projects);
    const el = want && document.getElementById(want);
    if (!el) return '';
    if (location.hash !== `#${encodeURIComponent(want)}`) try { history.replaceState(null, '', `${location.pathname}${location.search}#${encodeURIComponent(want)}`); } catch { /* 그대로 */ }
    el.scrollIntoView({ behavior: 'instant', block: 'start' });        // 부드러운 스크롤은 가려진 창에서 멈춤 → 바로 이동
    return want;
  }

  // 예전 포트폴리오 슬라이드의 주소(#s=<장면 id> · #<번호> · #s-<번호> · 옛 랜딩 #skills 등) → 이 페이지의 절 id. 지금 주소가 절 id 그대로여도 돌려줌(그리고 나서 그 절로 스크롤)
  //   번호는 실제로 그린 절의 차례(예전 슬라이드도 그린 장면만 셈. 나뉜 장면 stack-2·glance-2는 이제 한 절). #0·#-3 → 첫 절, #99 → 마지막 절(슬라이드 키트와 같은 규칙)
  function legacyTarget(projects) {
    let h = location.hash.slice(1);
    try { h = decodeURIComponent(h); } catch { /* 그대로 */ }
    if (!h) return '';
    const nth = (n) => info.sections[Math.max(1, Math.min(info.sections.length, n)) - 1] || '';
    if (/^-?\d+$/.test(h)) return nth(+h);
    const m = /^s=(.+)$/.exec(h);
    if (m) h = m[1].replace(/^(stack|glance)-\d+$/, '$1');
    const sn = /^s-(\d+)$/.exec(h);
    if (sn) return nth(+sn[1]);
    const core = /^p-(.+)-core$/.exec(h);                             // [포크] B안: 기능 소개 절은 상세 패널 안으로
    if (core) return `p-${core[1]}`;
    return { skills: 'stack', intro: 'cover', glance: 'projects', summary: 'projects' }[h] ?? h;
  }

  // [포크] B안 카드형 배치(2026-11-25): nav → 머리(#cover) → 프로젝트 카드(#projects) → 프로젝트 상세(#p-<폴더>) → 기술 스택(#stack) → 연락처(#contact)
  //   글은 모두 data 원문. 카드 메타(연도·개발 기간·엔진)만 data의 '기간'·태그 값에서 잘라 보여 줌
  function render(pf, projects) {
    const prof = pf.profile || {};
    const byId = Object.fromEntries(projects.map((p) => [p.id, p]));
    const nameT = prof.name || t('name.portfolio');
    document.title = `${prof.name ? prof.name + ' · ' : ''}${t('name.portfolio')}`;
    document.querySelector('.skip-link')?.replaceChildren(t('ui.skip'));
    const profLinks = (prof.links || []).filter((l) => isUrl(l.url));
    // 인쇄(PDF)에서는 버튼 대신 칠 수 있는 주소를 보여 줌. rows: [이름, 보이는 주소, 링크, 종류]
    const printUrls = (rows) => rows.length ? `<dl class="print-only purls">${rows.map(([k, u, href, kind]) => `<div><dt>${esc(k)}</dt><dd class="url">${href
      ? link({ href, label: esc(u), kind }) : esc(u)}</dd></div>`).join('')}</dl>` : '';
    const skills = pf.skills || [];
    const out = [];

    // ① 머리: 왼쪽 인사·한 줄 정체성·자기소개·연락 / 오른쪽 개발 분야 상자(기술 스택 이름·프로젝트 개수에서 계산)
    const teamCount = projects.filter((p) => p.team?.type === 'team').length;
    const focus = skills.length ? `<aside class="hero-side"><p class="eyebrow">${T('en.focus')}</p>
        <strong>${esc(skills.slice(0, 2).map((s) => s.name).join(' / '))}</strong>
        ${skills.length > 2 ? `<p>${esc(skills.slice(2).filter((s) => s.name !== '개발 워크플로').map((s) => s.name).join(' · '))}</p>` : ''}
        <p class="meta">${T('page.focus-count', { team: teamCount, solo: projects.length - teamCount })}</p></aside>` : '';
    out.push(`<section id="cover" class="hero"><div class="hero-l">
        <p class="eyebrow">${T('en.hero')}</p>
        <h1>${T('page.hello')}<br><span class="nm">${T('page.hello-name', { name: nameT })}</span></h1>
        ${prof.headline ? `<p class="headline">${inline(prof.headline)}</p>` : ''}
        ${prof.intro ? `<div class="about">${prof.intro.split(/\n+/).filter((l) => l.trim()).map((l) => `<p>${inline(l)}</p>`).join('')}</div>` : ''}
        <div class="links no-print">${[link({ href: '#contact', label: T('page.contact-cta'), kind: 'anchor' }),
          prof.email && link({ href: `mailto:${prof.email}`, label: esc(prof.email), kind: 'mail' }),
          isUrl(prof.github) && link({ href: prof.github, label: 'GitHub' }), ...profLinks.map((l) => link({ href: l.url, label: esc(l.label) }))].filter(Boolean).join('')}</div>
        ${printUrls([prof.email && [t('page.print-email'), prof.email, `mailto:${prof.email}`, 'mail'], isUrl(prof.github) && ['GitHub', shortUrl(prof.github), prof.github, 'external'],
          ...profLinks.map((l) => [l.label, shortUrl(l.url), l.url, 'external']), [t('page.print-web'), PF.visibleUrl('')]].filter(Boolean))}
      </div>${focus}</section>`);

    // ② 프로젝트 카드 3열
    if (projects.length) out.push(`<section id="projects" class="sec"><p class="eyebrow">${T('en.projects')}</p>
      <h2 class="sec-t">${T('page.games')} <small>${pad2(projects.length)}</small></h2>
      <div class="cards">${projects.map(card).join('')}</div></section>`);

    // ③ 프로젝트 상세: 카드와 같은 순서
    if (projects.length) out.push(`<section class="sec details" aria-labelledby="details-t"><p class="eyebrow">${T('en.details')}</p>
      <h2 class="sec-t" id="details-t">${T('page.details')}</h2>${projects.map((p) => detail(p, printUrls)).join('')}</section>`);

    // ④ 기술 역량: 키워드 목록과 중복 없는 근거 프로젝트 링크
    if (skills.length) out.push(`<section id="stack" class="sec"><p class="eyebrow">${T('en.capabilities')}</p><h2 class="sec-t">${T('page.stack')}</h2>
      <ul class="skills">${skills.map((s) => {
        const evidenceIds = new Set((s.evidence || []).map((e) => e.project));
        const related = projects.map((p) => p.id).filter((id) => evidenceIds.has(id));
        return `<li class="skill"><h3>${esc(s.name)}</h3><ul class="skill-keywords">${(s.evidence || []).map((e) => `<li><span>${esc(e.text)}</span></li>`).join('')}</ul>
          <div class="skill-projects"><span>관련 프로젝트</span>${related.map((id) => link({ href: `#p-${id}`, label: esc(byId[id].title), kind: 'anchor' })).join('')}</div></li>`;
      }).join('')}</ul></section>`);

    // ⑤ 연락처
    out.push(`<footer id="contact" class="contact"><b class="ct-name">${esc(nameT)}${prof.nameEn ? ` / <span lang="en">${esc(prof.nameEn)}</span>` : ''}</b>
      ${prof.email ? `<span class="em">${link({ href: `mailto:${prof.email}`, label: esc(prof.email), kind: 'mail' })}</span>` : ''}
      ${isUrl(prof.github) ? link({ href: prof.github, label: 'GitHub' }) : ''}
      ${profLinks.map((l) => link({ href: l.url, label: esc(l.label) })).join('')}
      <span class="print-only url">${esc(PF.visibleUrl(''))}</span></footer>`);

    const nav = [['projects', t('page.nav-projects'), projects.length], ['stack', t('page.nav-stack'), skills.length], ['contact', t('page.nav-contact'), true]].filter((x) => x[2]);
    root.innerHTML = `<div class="wrap"><header class="top no-print">
        <a class="top-name" href="#cover">${T('page.brand', { name: (prof.nameEn || nameT).toUpperCase() })}</a>
        <nav aria-label="절 이동"><ul>${nav.map(([id, label]) => `<li><a href="#${esc(id)}">${esc(label)}</a></li>`).join('')}</ul>
        <button type="button" class="pdf" data-pdf>${T('ui.pdf')}</button></nav></header>
      <main id="main">${out.join('')}</main></div>`;
    root.querySelectorAll('img').forEach((i) => { if (!i.hasAttribute('loading')) i.loading = 'lazy'; i.decoding = 'async'; });
    info.sections = [...root.querySelectorAll('main > section[id], main > footer[id], .details > article[id]')].map((s) => s.id);
    info.projects = projects.length;
  }

  // 카드 메타 칸 값: '기간'(2026.10.15 — 11.03 …) → 연도 2026 · 개발 기간 10.15–11.03, 엔진 = 태그 중 Unity로 시작하는 것
  function cardFacts(p) {
    const period = PF.metaValue(p, '기간');
    const m = /(\d{4})\.(\d{2}\.\d{2})\s*[—–-]\s*(?:\d{4}\.)?(\d{2}\.\d{2})/.exec(period);
    return [[t('page.card-year'), m ? m[1] : ''], [t('page.card-size'), p.team?.size || PF.metaValue(p, '인원')],
      [t('page.card-period'), m ? `${m[2]}–${m[3]}` : period], [t('page.card-engine'), (p.tags || []).find((x) => /^unity/i.test(x)) || engineFromMeta(p)]].filter(([, v]) => v);
  }
  // [포크] 태그에서 Unity를 뺀 프로젝트(2026-11-27)는 '환경' 메타에서 엔진을 읽음: 'Unity 6000.3.15f1' → 'Unity 6.3', 'Unity 2022.3.10f1' → 'Unity 2022.3'
  function engineFromMeta(p) {
    const m = /Unity\s*(\d+)\.(\d+)/i.exec(PF.metaValue(p, '환경') || '');
    if (m === null) return '';
    const major = m[1] === '6000' ? '6' : m[1];
    return `Unity ${major}.${m[2]}`;
  }

  // 프로젝트 카드: 대표 화면 · 메타 2×2 · 제목 · 역할 · 장르 · 개발 의도 · 태그 4개 · View Project(상세 패널로)
  function card(p) {
    const role = p.team?.role || PF.teamLabel(p.team);
    const genre = PF.metaValue(p, '장르');
    return `<article class="card">${p.cover ? `<img class="cover" src="${esc(PF.srcPath(p.id, p.cover))}" alt="${esc(p.title)} 대표 화면">` : ''}
      <div class="body"><dl class="facts">${cardFacts(p).map(([k, v]) => `<div><dt>${esc(k)}</dt><dd>${esc(v)}</dd></div>`).join('')}</dl>
        <h3 class="title">${esc(p.title)}</h3><p class="role">${inline(role)}</p>
        ${genre ? `<p class="genre">${inline(genre)}</p>` : ''}
        ${p.intent?.text ? `<p class="intent">${inline(p.intent.text)}</p>` : ''}
        ${(p.tags || []).length ? `<ul class="tags">${p.tags.slice(0, 4).map((x) => `<li>${esc(x)}</li>`).join('')}</ul>` : ''}
        ${link({ href: `#p-${p.id}`, label: T('page.view'), kind: 'anchor', cls: 'more no-print' })}</div></article>`;
  }

  // 상세 패널: 팀·인원·기간 → 제목 → 역할 → 한 줄 소개 → 메타 원문 → 태그 전체 → 링크 행 → 기능(그림·번호·제목·설명·슬라이드 딥링크)
  function detail(p, printUrls) {
    const urls = linkRows(p);
    const highlights = p.highlights || [];
    const feats = highlights.map((h, i) => {
      const cap = typeof h.image?.caption === 'string' ? h.image.caption : h.image?.caption?.text || '';
      const size = h.image?.w && h.image?.h ? ` width="${h.image.w}" height="${h.image.h}"` : '';
      const img = h.image?.src ? `<img src="${esc(PF.srcPath(p.id, h.image.src))}" alt="${esc(cap || h.title)}"${size}>` : '';
      return `<li class="feat" data-fi="${i}">${img}<p class="eyebrow">${pad2(i + 1)}</p><h4>${inline(h.title)}</h4>${h.what ? `<p class="what">${inline(h.what)}</p>` : ''}
        ${link({ href: PF.projectUrl(p.id, PF.deckSceneFor(p, h.section)), label: T('page.see-in-deck'), kind: 'page', cls: 'pill no-print',
          attrs: `aria-label="${esc(PF.normTitle(h.title))} — ${T('page.see-in-deck')} (새 탭)"` })}</li>`;
    }).join('');
    const meta = (p.meta || []).map((m) => `<div><dt>${esc(m.label)}</dt><dd>${inline(m.value)}</dd></div>`).join('');
    const period = PF.metaValue(p, '기간');
    return `<article class="detail" id="p-${esc(p.id)}">
      <p class="meta-line">${esc(PF.teamLabel(p.team))}${period ? ` · ${esc(period)}` : ''}</p>
      <h3>${esc(p.title)}</h3>
      ${p.team?.role ? `<p class="d-role">${inline(p.team.role)}</p>` : ''}
      ${p.tagline ? `<p class="tagline">${inline(p.tagline)}</p>` : ''}
      ${meta ? `<dl class="meta">${meta}</dl>` : ''}
      ${(p.tags || []).length ? `<ul class="tags">${p.tags.map((x) => `<li>${esc(x)}</li>`).join('')}</ul>` : ''}
      <div class="links no-print">${urls.map(([label, href, kind, featured]) => link({ href, label: esc(label), kind, cls: featured ? 'featured' : '' })).join('')}</div>
      ${printUrls(urls.map(([label, href, kind]) => [label, kind === 'page' ? PF.visibleUrl(href) : shortUrl(href), href, kind]))}
      ${feats ? `<ol class="features" style="--feature-cols:${Math.min(4, highlights.length)}">${feats}</ol>` : ''}</article>`;
  }

  // 링크 행 [이름, 주소, 종류, 강조]: 쇼츠 · 시연 영상 · 슬라이드 · 상세 문서 · GitHub(여럿이면 클라이언트·서버)
  const ghExtra = (p) => (p.links?.extra || []).filter((l) => isUrl(l.url) && /github\.com/i.test(l.url));
  const shortsExtra = (p) => (p.links?.extra || []).filter((l) => isUrl(l.url) && (l.label === '쇼츠' || /youtube\.com\/shorts\//i.test(l.url)));
  const otherExtra = (p) => (p.links?.extra || []).filter((l) => isUrl(l.url) && !ghExtra(p).includes(l) && !shortsExtra(p).includes(l));
  function linkRows(p) {
    return [...shortsExtra(p).map((l) => [l.label, l.url, 'external', true]),
      isUrl(p.links?.video) && [t('name.video'), p.links.video, 'external', true],
      [t('name.deck'), PF.projectUrl(p.id), 'page', true], [t('name.doc-short'), PF.docUrl(p.id), 'page'],
      isUrl(p.links?.github) && [ghExtra(p).length ? 'GitHub(클라이언트)' : 'GitHub', p.links.github, 'external'],
      ...ghExtra(p).map((l) => [l.label, l.url, 'external']),
      ...otherExtra(p).map((l) => [l.label, l.url, 'external'])].filter(Boolean);
  }

  // 화면과 설명 짝 강조(마우스·키보드 포커스) + [PDF로 저장]
  function bind() {
    const pair = (el, on) => {
      const item = el.closest('[data-fi]');
      const box = item?.closest('.fe');
      if (!box) return;
      box.querySelectorAll(`[data-fi="${item.dataset.fi}"]`).forEach((x) => x.classList.toggle('on', on));
    };
    root.addEventListener('mouseover', (e) => pair(e.target, true));
    root.addEventListener('mouseout', (e) => pair(e.target, false));
    root.addEventListener('focusin', (e) => pair(e.target, true));
    root.addEventListener('focusout', (e) => pair(e.target, false));
    root.querySelector('[data-pdf]')?.addEventListener('click', async (e) => {
      const b = e.currentTarget, label = b.textContent;
      b.disabled = true; b.textContent = t('ui.preparing');
      await loadAll(15000);                                            // 아래쪽 그림까지 받은 뒤 인쇄(늦게 받는 그림은 PDF에서 빠짐)
      b.disabled = false; b.textContent = label;
      window.print();
    });
    addEventListener('beforeprint', () => eager());                    // Ctrl+P: 아직 받지 않은 그림을 바로 받기 시작(할 수 있는 만큼)
  }

  const eager = () => document.querySelectorAll('img[loading="lazy"]').forEach((i) => { i.loading = 'eager'; });
  const loaded = (i) => (i.complete ? null : new Promise((r) => {     // decode()·rAF는 쓰지 않음: 가려진 창에서 끝나지 않음(슬라이드 키트와 같은 이유)
    i.addEventListener('load', r, { once: true }); i.addEventListener('error', r, { once: true }); }));
  async function loadAll(ms) {
    eager();
    await Promise.race([Promise.all([...document.images].map(loaded)), new Promise((r) => setTimeout(r, ms))]);
  }

  // 준비: 데스크톱 기기면 그림을 미리 다 받아 Ctrl+P도 빠짐없이 인쇄(휴대폰·데이터 절약 모드는 화면에 올 때 받음)
  async function settle() {
    const desk = matchMedia('(pointer: fine)').matches && !(navigator.connection && navigator.connection.saveData);
    if (desk) eager();
    await Promise.race([Promise.all([...document.querySelectorAll('img:not([loading="lazy"])')].map(loaded)), new Promise((r) => setTimeout(r, 20000))]);
    if (document.fonts) await Promise.race([document.fonts.ready, new Promise((r) => setTimeout(r, 3000))]);
    if (!userMoved) {                                                  // 글꼴이 바뀌며 절 높이가 달라져 도착 위치가 밀린 만큼 다시 맞춤
      const el = arrived && document.getElementById(arrived);
      if (el) el.scrollIntoView({ behavior: 'instant', block: 'start' });
      else if (restoreY) scrollTo({ top: restoreY, behavior: 'instant' });
    }
    html.dataset.pfReady = 'yes';
    resolveReady(report());
    if (new URLSearchParams(location.search).get('pf-audit') === '1') writeAudit();
  }

  function report() {
    const imgs = [...document.images];
    return {
      page: 'portfolio', ready: html.dataset.pfReady, sections: info.sections, projects: info.projects,
      images: { total: imgs.length, broken: imgs.filter((i) => i.complete && i.naturalWidth === 0).map((i) => i.getAttribute('src')),
                pending: imgs.filter((i) => !i.complete).map((i) => i.getAttribute('src')) },
      deckLinks: [...document.querySelectorAll('.features a.pill')].map((a) => a.getAttribute('href')),
      overflowX: Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth),
      cardTags: cardTagsReport(),
      errors: info.errors, strings: PF.stringsReport(),
    };
  }
  // [포크] 카드 태그 칸 정렬 점검(2026-11-27): 같은 행 카드끼리 태그 칸 첫 줄 높이가 2px 넘게 다르면 misaligned에 남김. rows = 카드별 태그 줄 수(3 이상이면 칸 높이를 넘김)
  function cardTagsReport() {
    const lists = [...document.querySelectorAll('.cards .card .tags')].map((ul) => {
      const card = ul.closest('.card'), lis = [...ul.children];
      return { title: card.querySelector('.title')?.textContent || '', cardTop: Math.round(card.getBoundingClientRect().top),
               top: Math.round(lis[0]?.getBoundingClientRect().top ?? 0), rows: new Set(lis.map((li) => Math.round(li.getBoundingClientRect().top))).size };
    });
    const misaligned = [];
    for (const row of new Set(lists.map((x) => x.cardTop))) {
      const same = lists.filter((x) => x.cardTop === row);
      const tops = same.map((x) => x.top);
      if (Math.max(...tops) - Math.min(...tops) > 2) misaligned.push(same.map((x) => `${x.title}:${x.top}`).join(' · '));
    }
    return { rows: Object.fromEntries(lists.map((x) => [x.title, x.rows])), misaligned };
  }
  function writeAudit() {
    let pre = document.getElementById('pf-audit');
    if (!pre) { pre = document.createElement('pre'); pre.id = 'pf-audit'; pre.hidden = true; document.body.appendChild(pre); }
    const bytes = new TextEncoder().encode(JSON.stringify(report()));
    let bin = '';
    for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    pre.textContent = btoa(bin);
  }
})();
