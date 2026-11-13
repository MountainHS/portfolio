"""편집 모드 줄 넣기 모듈 — 템플릿 파일(index.html·project.html·doc.html)을 보낼 때 메모리에서만 편집 모드 줄(</body> 위 로더 한 줄)을 넣습니다(디스크 파일은 원본 그대로).

- edit-server.py가 이 파일들을 보낼 때 patch_file을 부르고,
  켤 때 check_all로 기준 줄을 한 번 재서 [멈춤]만 서버 창에 찍습니다.
- install-lines.py check도 check_all로 기준 줄이 맞는지만 봅니다(템플릿 파일을 쓰지 않음).
- 묶음: 페이지(HTML 파일) 하나가 한 묶음입니다. 그 파일의 기준 줄 `</body>`을 찾지 못하거나 두 곳 이상이면 그 페이지만 원본 그대로 보냅니다(추측해서 넣지 않음).
- 이미 들어 있는 줄은 두 번 넣지 않습니다([있음]).
- 줄 끝(CRLF/LF)·UTF-8 BOM은 파일마다 원래대로 둡니다. 넣는 줄의 들여쓰기는 기준 줄의 들여쓰기를 따릅니다.
표준 라이브러리만 씁니다(Python 3.8 이상).
"""
from pathlib import Path


class Step:
    """한 파일에 넣을 줄 묶음 하나.

    where: 'after' = 기준 줄 다음 줄에 · 'before' = 기준 줄 바로 위에 · 'replace' = 기준 줄 안의 글자 anchor를 replacement로 바꿈
    keys: 이 줄들이 이미 들어 있는지 알아보는 글자(파일 전체에서 찾음). 모두 있으면 건너뜀, 일부만 있으면 멈춤
    exact: True면 기준 줄을 앞뒤 공백을 뺀 줄 전체와 비교(같은 글자가 다른 줄에도 들어 있을 때), False면 그 글자를 포함한 줄
    """

    def __init__(self, path, where, anchor, keys, lines=(), replacement='', exact=False):
        self.path = path
        self.where = where
        self.anchor = anchor
        self.keys = keys
        self.lines = list(lines)
        self.replacement = replacement
        self.exact = exact


# ── 넣을 줄 (MountainHS/portfolio main의 파일 그대로. 들여쓰기는 기준 줄을 따르므로 여기서는 기준 줄에 대한 상대 들여쓰기만) ──
# 세 페이지 모두 </body> 위에 로더 한 줄만 넣습니다. 로더(assets/edit-loader.js)가 편집 서버가 있을 때만 페이지 종류에 맞는 편집 CSS·JS를 싣습니다.
LOADER_KEY = 'assets/edit-loader.js'
LOADER_LINE = '<script src="assets/edit-loader.js"></script><!-- 편집 모드(이 포크 전용): 로컬 편집 서버가 있을 때만 켜짐 -->'


def loader_step(html_path):
    """html_path의 </body> 바로 위에 로더 한 줄을 넣는 Step."""
    return Step(html_path, 'before', '</body>', [LOADER_KEY], [LOADER_LINE], exact=True)


GROUPS = [
    ('상세 문서 (doc.html)', [loader_step('doc.html')]),
    ('포트폴리오 문서 (index.html)', [loader_step('index.html')]),
    ('발표 덱 (project.html)', [loader_step('project.html')]),
]


# ── 밖에서 부르는 함수 ──

def patch_file(root, relative_path):
    """편집 서버가 보낼 파일 하나에 편집 모드 줄을 넣은 글을 돌려줍니다(디스크는 쓰지 않음).

    edit-server.py의 do_GET이 부릅니다. 그 파일의 기준 줄(`</body>`)을 먼저 확인해,
    못 찾거나 두 곳 이상이면 원본 그대로 보내게 합니다(추측해서 넣지 않음).
    root: 레포 루트(Path 또는 글)
    relative_path: 레포 루트 기준 경로, '/' 구분(예: 'doc.html')
    돌려줌: (보낼 바이트, 멈춘 이유 목록) — 목록이 비면 줄을 넣은(또는 이미 있던) 글, 차 있으면 원본 바이트.
            묶음에 없는 파일이거나 그 파일이 디스크에 없으면 None(보통 정적 파일처럼 보내면 됨)
    """
    root = Path(root)
    steps = find_group_steps(relative_path)
    if steps is None:
        return None
    if (root / relative_path).is_file() == False:
        return None

    files, messages, problems = plan_group(root, steps)
    if problems:
        return (root / relative_path).read_bytes(), problems
    return encode_text_file(root / relative_path, files[relative_path]), []


def check_all(root):
    """템플릿 파일의 기준 줄을 묶음마다 재서 결과를 돌려줍니다(디스크는 쓰지 않음).

    edit-server.py의 main(켤 때 [멈춤]만 찍음)과 install-lines.py check(모두 찍음)가 부릅니다.
    root: 레포 루트(Path 또는 글)
    돌려줌: 묶음마다 {'name': 묶음 이름, 'messages': [넣음]·[있음] 줄 목록, 'problems': 멈춘 이유 목록} 사전의 목록(GROUPS 순서)
    """
    root = Path(root)
    results = []
    for name, steps in GROUPS:
        files, messages, problems = plan_group(root, steps)
        results.append({'name': name, 'messages': messages, 'problems': problems})
    return results


# ── 묶음 계산 ──

def find_group_steps(relative_path):
    """경로가 든 묶음의 Step 목록을 찾습니다.

    relative_path: 레포 루트 기준 경로, '/' 구분
    돌려줌: 그 묶음의 Step 목록. 묶음에 없는 파일이면 None
    """
    for name, steps in GROUPS:
        paths_in_group = [step.path for step in steps]
        if relative_path in paths_in_group:
            return steps
    return None


def plan_group(root, steps):
    """묶음 하나의 파일을 읽어, 모든 Step을 메모리에서 반영해 봅니다.

    root: 레포 루트(Path)
    steps: 이 묶음의 Step 목록(파일 여럿 가능)
    돌려줌: (경로 → read_text_file 사전, [넣음]·[있음] 줄 목록, 멈춘 이유 목록)
    """
    files = {}
    for step in steps:
        if step.path not in files:
            files[step.path] = read_text_file(root / step.path)

    messages = []
    problems = []
    for step in steps:
        problem = plan_step(files[step.path], step, messages)
        if problem:
            problems.append(problem)
    return files, messages, problems


def plan_step(text_file, step, messages):
    """Step 하나를 text_file의 줄 목록에 반영합니다(메모리에서만). 이미 있으면 [있음]만 남깁니다.

    text_file: read_text_file이 돌려준 사전. lines·changed를 고침. None이면 파일 없음
    step: 넣을 줄
    messages: [넣음]·[있음] 줄을 더할 목록
    돌려줌: 멈춰야 하면 그 이유 글, 아니면 None
    """
    if text_file is None:
        return '%s 파일이 없습니다' % step.path
    whole = '\n'.join(text_file['lines'])
    present = [key for key in step.keys if key in whole]
    if len(present) == len(step.keys):
        messages.append('[있음] %s: %s' % (step.path, ' · '.join(step.keys)))
        return None
    if present:
        return '%s: 넣을 줄 중 일부만 있습니다(있음: %s) — 직접 고친 흔적일 수 있어 건드리지 않습니다' % (step.path, ' · '.join(present))

    matches = [i for i, line in enumerate(text_file['lines']) if is_anchor(line, step)]
    if len(matches) != 1:
        return '%s: 기준 줄 `%s`이 %d곳입니다(1곳이어야 함)' % (step.path, step.anchor, len(matches))

    index = matches[0]
    anchor_line = text_file['lines'][index]
    indent = anchor_line[:len(anchor_line) - len(anchor_line.lstrip())]
    if step.where == 'replace':
        text_file['lines'][index] = anchor_line.replace(step.anchor, step.replacement, 1)
    else:
        new_lines = [indent + line for line in step.lines]
        at = index + 1 if step.where == 'after' else index
        text_file['lines'][at:at] = new_lines
    text_file['changed'] = True
    where_ko = {'after': '다음', 'before': '위', 'replace': '을 바꿈'}[step.where]
    messages.append('[넣음] %s: %s (기준 줄 `%s` %s)' % (step.path, ' · '.join(step.keys), step.anchor, where_ko))
    return None


def is_anchor(line, step):
    """줄이 Step의 기준 줄인지. exact면 앞뒤 공백을 뺀 줄 전체가 같아야 함."""
    if step.exact:
        return line.strip() == step.anchor
    return step.anchor in line


# ── 파일 읽기 (줄 끝·BOM 유지) ──

def read_text_file(path):
    """UTF-8 글 파일을 줄 목록으로 읽습니다.

    돌려줌: {'lines', 'newline'(\\r\\n 또는 \\n), 'bom', 'changed'} 사전. 파일이 없으면 None
    """
    if path.is_file() == False:
        return None
    raw = path.read_bytes()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    newline = '\r\n' if '\r\n' in text else '\n'
    return {'lines': text.replace('\r\n', '\n').split('\n'), 'newline': newline, 'bom': bom, 'changed': False}


def encode_text_file(path, text_file):
    """read_text_file의 사전을 원래 줄 끝·BOM의 바이트로 되돌립니다(예전 install-lines.py의 write_text_file이 쓰던 글과 같음).

    path: 그 파일의 경로. 줄을 넣지 않았으면(모두 [있음]) 디스크 바이트를 그대로 돌려줌
    text_file: read_text_file이 돌려준 사전(plan_step이 고친 것)
    돌려줌: 보낼 바이트
    """
    if text_file['changed'] == False:
        return path.read_bytes()
    text = text_file['newline'].join(text_file['lines'])
    data = text.encode('utf-8')
    if text_file['bom']:
        data = b'\xef\xbb\xbf' + data
    return data
