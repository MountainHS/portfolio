// 편집 모드 로더 — doc.html·index.html·project.html이 </body> 위에 이 파일 한 줄만 싣습니다(이 포크 전용. 줄은 편집 서버가 보낼 때만 넣음).
//   하는 일: 로컬 편집 서버가 있을 때만, 페이지 종류에 맞는 편집 CSS·JS를 붙입니다. GitHub Pages나 python -m http.server에서는 아무것도 받지 않습니다.
//   페이지 종류는 전역으로 압니다(파일 이름은 루트 `/`로 열면 알 수 없음): PFDoc = 상세 문서 · PFPage = 포트폴리오 문서 · PFScene.info.page === 'project' = 발표 덱.
//   CSS는 <head> 끝에 이 순서로 붙이고(deck-edit.css가 portfolio-edit.css의 규칙을 덮음), 다 받은 뒤 JS를 차례로 싣습니다(버튼이 모양 없이 보이는 것을 막음).
//   세 전역이 하나도 없으면 아무것도 하지 않습니다(편집 버튼 없음, 에러 없음).
(() => {
  const PING_URL = '/__edit/ping';
  const FILES_BY_PAGE = {
    doc: { css: ['doc-edit.css'], js: ['doc-edit.js'] },
    portfolio: { css: ['doc-edit.css', 'portfolio-edit.css'], js: ['portfolio-edit.js'] },
    deck: { css: ['doc-edit.css', 'portfolio-edit.css', 'deck-edit.css'], js: ['doc-edit.js', 'deck-edit.js'] },
  };

  start();

  /** 편집 서버가 있을 때만 페이지 종류를 가려 CSS → JS 순서로 싣습니다. */
  async function start() {
    const isLocalAddress = ['localhost', '127.0.0.1'].includes(location.hostname);
    if (isLocalAddress === false) return;
    const hasEditServer = await pingEditServer();
    if (hasEditServer === false) return;

    const pageKind = findPageKind();
    if (pageKind === null) return;
    const files = FILES_BY_PAGE[pageKind];
    await Promise.all(files.css.map(loadStylesheet));
    files.js.forEach(addScript);
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

  /** 이 페이지가 어느 렌더러가 그린 것인지. 모르면 null. */
  function findPageKind() {
    if (window.PFDoc) return 'doc';
    if (window.PFPage) return 'portfolio';
    if (window.PFScene?.info?.page === 'project') return 'deck';
    return null;
  }

  /** <head> 끝에 CSS를 붙이고 다 받으면(실패해도) 풀립니다. 붙인 순서가 DOM 순서가 됩니다. */
  function loadStylesheet(fileName) {
    return new Promise((resolve) => {
      const link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = `assets/${fileName}`;
      link.addEventListener('load', resolve);
      link.addEventListener('error', resolve);
      document.head.appendChild(link);
    });
  }

  /** 동적 스크립트는 기본이 async라 순서가 섞이므로 async=false로 붙인 순서대로 돌게 합니다. */
  function addScript(fileName) {
    const script = document.createElement('script');
    script.src = `assets/${fileName}`;
    script.async = false;
    document.body.appendChild(script);
  }
})();
