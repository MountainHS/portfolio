"""포트폴리오 미리보기 + 상세 문서·포트폴리오 문서 편집 저장 서버 (로컬 전용)

`python -m http.server 4600` 대신 이렇게 실행합니다.
    python edit-server.py          → http://localhost:4600
    python edit-server.py 4601     → 포트 바꾸기

- 정적 파일은 http.server와 똑같이 보냅니다(미리보기 세 페이지 그대로).
- 단, 템플릿 파일(index.html·project.html·doc.html)은 보낼 때 메모리에서만 </body> 위에 로더 한 줄(assets/edit-loader.js)을 넣습니다.
  넣는 줄·기준 줄은 .claude/skills/edit-mode-install/edit-mode-patch.py에 있고, 디스크의 파일은 원본 그대로 둡니다(/portfolio-update가 덮어도 그대로 동작).
- GET  /__edit/ping : 편집 서버가 떠 있다는 신호. assets/edit-loader.js가 이 응답을 받을 때만 편집 파일을 싣고 [편집 모드] 버튼을 보입니다.
- POST /__edit/save : src/ 아래 기술 문서 md의 줄 범위 하나를 새 글로 바꿔 씁니다.
- POST /__edit/portfolio : 포트폴리오 문서(index.html)의 글 한 칸을 소스(profile.md·기술 문서·deck-edits.json)와 data/에 씁니다.
  어디에 쓸지는 .claude/skills/portfolio-build/portfolio-edits.py가 정합니다(assets/portfolio-edit.js가 부름).
- POST /__edit/deck : 발표 덱(project.html) 장면 글(제목·도입·요점·키워드) 한 칸을 deck-edits.json(+ 기술 문서 줄)과 data/에 씁니다.
  규칙은 같은 portfolio-edits.py의 save_deck_text(assets/deck-edit.js가 부름). 덱의 표·그림·코드는 /__edit/save(md 줄)로 고칩니다.
127.0.0.1에만 열어 같은 PC에서만 저장할 수 있습니다. GitHub Pages에는 이 서버가 없으므로 편집 버튼이 나오지 않습니다.
"""
import codecs
import importlib.util
import json
import re
import sys
import urllib.parse
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SRC = ROOT / "src"
PORTFOLIO_EDITS_SCRIPT = ROOT / ".claude" / "skills" / "portfolio-build" / "portfolio-edits.py"
EDIT_MODE_PATCH_SCRIPT = ROOT / ".claude" / "skills" / "edit-mode-install" / "edit-mode-patch.py"
DEFAULT_PORT = 4600


class SaveError(Exception):
    """저장 요청을 받아들일 수 없을 때. status는 돌려줄 HTTP 상태 코드입니다."""

    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def resolve_md_path(relative_path):
    """요청의 경로(src/<폴더>/<기술 문서>.md)를 실제 파일 경로로 바꿉니다.

    src/ 밖의 파일이나 .md가 아닌 파일은 쓰지 않습니다(이 서버는 기술 문서만 고침).
    """
    md_path = (ROOT / relative_path).resolve()
    is_inside_src = SRC.resolve() in md_path.parents
    is_markdown = md_path.suffix.lower() == ".md"
    if is_inside_src == False or is_markdown == False:
        raise SaveError(400, f"src/ 아래의 .md 파일만 고칠 수 있습니다: {relative_path}")
    if md_path.is_file() == False:
        raise SaveError(404, f"파일이 없습니다: {relative_path}")
    return md_path


def replace_lines(md_path, start_line, end_line, expected_text, new_text):
    """md 파일의 start_line~end_line(1부터, 끝 포함) 줄을 new_text로 바꿔 씁니다.

    expected_text는 화면을 그릴 때의 그 줄들입니다. 파일의 지금 내용과 다르면(편집기에서 따로 고침 등) 쓰지 않습니다.
    파일의 줄바꿈(CRLF/LF)과 UTF-8 BOM은 그대로 둡니다. 그래서 git diff에는 고친 줄만 나옵니다.
    """
    raw = md_path.read_bytes()
    has_bom = raw.startswith(codecs.BOM_UTF8)
    text = raw.decode("utf-8-sig")
    eol = "\r\n" if "\r\n" in text else "\n"

    # 줄 끝 문자를 붙인 채로 나눔 → 바꾸지 않는 줄은 바이트 그대로 남음
    lines = re.findall(r"[^\n]*\n|[^\n]+$", text)
    is_range_valid = 1 <= start_line <= end_line <= len(lines)
    if is_range_valid == False:
        raise SaveError(409, f"줄 범위 {start_line}~{end_line}가 파일({len(lines)}줄)과 맞지 않습니다. 새로고침해 주세요.")

    old_lines = lines[start_line - 1:end_line]
    current_text = "\n".join(line.rstrip("\r\n") for line in old_lines)
    if current_text != expected_text:
        raise SaveError(409, "파일이 화면을 그린 뒤에 바뀌었습니다. 새로고침한 뒤 다시 고쳐 주세요.")

    # 새 글: 줄바꿈을 파일 방식으로 맞추고, 끝의 빈 줄은 뺌(블록 사이 빈 줄은 원래 줄에 그대로 있음)
    new_text = new_text.replace("\r\n", "\n").rstrip("\n")
    new_lines = [line + eol for line in new_text.split("\n")] if new_text else []
    last_old_line_has_eol = old_lines[-1].endswith("\n")
    if new_lines and last_old_line_has_eol == False:
        new_lines[-1] = new_lines[-1][:-len(eol)]

    lines[start_line - 1:end_line] = new_lines
    result = "".join(lines).encode("utf-8")
    if has_bom:
        result = codecs.BOM_UTF8 + result
    md_path.write_bytes(result)


def load_portfolio_edits():
    """포트폴리오 문서 고치기 모듈(portfolio-edits.py)을 읽습니다. 빌드 스킬과 같은 파일을 써서 저장 규칙이 한 곳에만 있게 합니다."""
    spec = importlib.util.spec_from_file_location("portfolio_edits", str(PORTFOLIO_EDITS_SCRIPT))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_edit_mode_patch():
    """편집 모드 줄 넣기 모듈(edit-mode-patch.py)을 읽습니다. 설치 스킬의 check와 같은 파일을 써서 넣는 줄·기준 줄이 한 곳에만 있게 합니다."""
    spec = importlib.util.spec_from_file_location("edit_mode_patch", str(EDIT_MODE_PATCH_SCRIPT))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def requested_file(request_path):
    """요청 주소를 레포 루트 기준 파일 경로로 바꿉니다(쿼리·# 뺌). 루트 주소(/)는 http.server처럼 index.html입니다.

    request_path: 요청 줄의 경로(예: '/project.html?p=5-hive-purge')
    돌려줌: '/' 구분 상대 경로(예: 'project.html')
    """
    path = urllib.parse.unquote(urllib.parse.urlsplit(request_path).path).lstrip("/")
    if path == "":
        return "index.html"
    return path


class EditHandler(SimpleHTTPRequestHandler):
    """정적 파일 + /__edit/ping · /__edit/save · /__edit/portfolio · /__edit/deck 주소만 더한 http.server 처리기입니다."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(ROOT), **kwargs)

    def end_headers(self):
        """모든 응답(정적 파일·JSON·ping)에 캐시 금지를 붙입니다.

        같은 주소(127.0.0.1:4600)를 다른 레포가 번갈아 쓰므로, 브라우저가 이전 레포의 index.html·JS를 휴리스틱으로 재사용하지 않게 합니다.
        """
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def do_GET(self):
        if self.path == "/__edit/ping":
            self.send_json(200, {"ok": True, "root": str(ROOT)})   # preview.ps1이 다른 폴더의 서버를 재사용하지 않도록 폴더를 알려 줌
            return
        relative_path = requested_file(self.path)
        patched = load_edit_mode_patch().patch_file(ROOT, relative_path)   # 요청마다 다시 읽음 → 넣는 줄을 고쳐도 서버를 다시 켜지 않아도 됨
        if patched is None:
            super().do_GET()
            return
        data = patched[0]                                          # [1]은 멈춘 이유 목록 — 서버를 켤 때 main이 [멈춤]으로 찍음
        self.send_patched_file(relative_path, data)

    def send_patched_file(self, relative_path, data):
        """편집 모드 줄을 넣은 템플릿 파일 하나를 보냅니다(멈춘 묶음이면 원본 그대로의 바이트).

        relative_path: 레포 루트 기준 경로(Content-Type을 정할 때 씀)
        data: 보낼 바이트(edit-mode-patch.py의 patch_file이 돌려준 것)
        캐시 금지 헤더는 end_headers가 모든 응답에 붙입니다(http.server는 파일 날짜만 보고 304를 주므로, 넣는 줄을 고쳐도 예전 글이 남지 않게).
        """
        self.send_response(200)
        self.send_header("Content-Type", self.guess_type(relative_path))
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        request = json.loads(self.rfile.read(length).decode("utf-8"))
        if self.path == "/__edit/portfolio":
            self.save_portfolio_field(request)
            return
        if self.path == "/__edit/deck":
            self.save_deck_text(request)
            return
        if self.path != "/__edit/save":
            self.send_json(404, {"ok": False, "error": "없는 주소입니다."})
            return
        try:
            md_path = resolve_md_path(request["path"])
            replace_lines(md_path, int(request["start"]), int(request["end"]), request["expected"], request["text"])
        except SaveError as error:
            self.send_json(error.status, {"ok": False, "error": str(error)})
            return
        print(f"[edit] 저장: {request['path']} {request['start']}~{request['end']}줄")
        self.send_json(200, {"ok": True})

    def save_portfolio_field(self, request):
        """포트폴리오 문서의 글 한 칸 저장(또는 dryRun이면 저장 위치만 묻기). 규칙은 portfolio-edits.py의 save."""
        portfolio_edits = load_portfolio_edits()                  # 요청마다 다시 읽음 → 규칙을 고쳐도 서버를 다시 켜지 않아도 됨
        try:
            answer = portfolio_edits.save(request)
        except portfolio_edits.EditError as error:
            self.send_json(error.status, {"ok": False, "error": str(error)})
            return
        if request.get("dryRun") != True:
            print(f"[edit] 포트폴리오 저장: {request.get('field')} → {answer['where']}")
        self.send_json(200, answer)

    def save_deck_text(self, request):
        """발표 덱 장면 글 한 칸 저장(또는 dryRun이면 저장 위치만 묻기). 규칙은 portfolio-edits.py의 save_deck_text."""
        portfolio_edits = load_portfolio_edits()                  # 요청마다 다시 읽음 → 규칙을 고쳐도 서버를 다시 켜지 않아도 됨
        try:
            answer = portfolio_edits.save_deck_text(request)
        except portfolio_edits.EditError as error:
            self.send_json(error.status, {"ok": False, "error": str(error)})
            return
        if request.get("dryRun") != True:
            print(f"[edit] 발표 덱 저장: {request.get('slide')} {request.get('field')} → {answer['where']}")
        self.send_json(200, answer)

    def send_json(self, status, body):
        """JSON 응답 하나를 보냅니다(ping·save 공용)."""
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def main():
    """포트를 정해 127.0.0.1에서 서버를 엽니다. Ctrl+C로 끕니다."""
    port = int(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_PORT
    server = ThreadingHTTPServer(("127.0.0.1", port), EditHandler)
    # localhost로 열면 브라우저가 IPv6(::1)를 먼저 찾아, 같은 포트의 다른 서버(python -m http.server 등)로 갈 수 있음 → 127.0.0.1 주소로 안내
    print(f"미리보기 + 편집 서버: http://127.0.0.1:{port}  (포트폴리오 문서·발표 덱·상세 문서 오른쪽 아래 [편집 모드])  끄기: Ctrl+C")
    print_edit_mode_stops()
    server.serve_forever()


def print_edit_mode_stops():
    """템플릿 파일의 기준 줄을 한 번 재서, 줄을 넣지 못하는 묶음만 [멈춤]으로 찍습니다(그 페이지는 편집 버튼 없이 원본 그대로 열림)."""
    for result in load_edit_mode_patch().check_all(ROOT):
        for problem in result["problems"]:
            print(f"[멈춤] {result['name']}: {problem} — 이 페이지는 편집 버튼 없이 열립니다")


if __name__ == "__main__":
    main()
