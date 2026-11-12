"""편집 모드 줄 넣기 모듈 — 템플릿 파일 5개를 보낼 때 메모리에서만 편집 모드 줄을 넣습니다(디스크 파일은 원본 그대로).

- edit-server.py가 이 5개(index.html·project.html·doc.html·assets/doc.js·assets/project-deck.js)를 보낼 때 patch_file을 부르고,
  켤 때 check_all로 기준 줄을 한 번 재서 [멈춤]만 서버 창에 찍습니다.
- install-lines.py check도 check_all로 기준 줄이 맞는지만 봅니다(5개 파일을 쓰지 않음).
- 묶음: 함께 바뀌어야 하는 파일을 한 묶음으로 다룹니다(예: doc.js가 부르는 PFDocEdit는 doc.html이 싣는 doc-edit.js에 있음).
  한 파일을 보낼 때도 그 묶음 전체의 기준 줄을 확인하고, 하나라도 찾지 못하거나 두 곳 이상이면 그 묶음은 모두 원본 그대로 보냅니다(추측해서 넣지 않음).
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


# ── 넣을 줄 (MountainHS/portfolio-test main의 파일 그대로. 들여쓰기는 기준 줄을 따르므로 여기서는 기준 줄에 대한 상대 들여쓰기만) ──
MARK_FOR_DECK_EDIT = [
    '// 발표 덱 편집 모드(이 포크 전용, assets/deck-edit.js)가 화면의 글·그림이 data의 어느 장면(key)·몇 번째 시각 자료(visuals 번호)인지 알게 표시를 담',
    '//   기술 문서 원문 블록만 표시(data의 스크린샷 목록에서 온 그림 ref.direct는 md 줄이 없어 고칠 수 없음)',
    'function markForDeckEdit(s, sl, vis) {',
    '  s.dataset.pfSlide = sl.key;',
    "  s.querySelectorAll('.vis > figure.vbox').forEach((figure, i) => {",
    '    const ref = vis[i]?.ref;',
    '    const isSourceBlock = ref !== undefined && ref.direct !== true;',
    '    if (isSourceBlock) figure.dataset.pfVisual = String((sl.visuals || []).indexOf(ref));',
    '  });',
    '}',
]

# md를 try 블록 밖에서 선언해야 done(); 다음 줄의 PFDocEdit.attach가 읽음(새 템플릿은 `try { await renderDoc(); ready(); }` 한 줄).
# renderDoc 안에도 `let md;`가 있으므로, 이미 넣었는지는 뒤의 주석까지 포함한 이 줄 전체로 알아봄
DECLARE_MD = 'let md;                                                             // 편집 모드: renderDoc()이 돌려준 md 원문(try 밖에 두어야 아래 PFDocEdit.attach가 읽음)'

GROUPS = [
    ('상세 문서 (doc.html · assets/doc.js)', [
        Step('doc.html', 'after', '<link rel="stylesheet" href="assets/doc.css" />',
             ['assets/doc-edit.css'],
             ['<link rel="stylesheet" href="assets/doc-edit.css" />']),
        Step('doc.html', 'before', '<script src="assets/doc.js"></script>',
             ['assets/doc-edit.js'],
             ['<script src="assets/doc-edit.js"></script><!-- 편집 모드: 로컬 편집 서버(edit-server.py)가 있을 때만 켜짐. doc.js보다 먼저 -->']),
        Step('assets/doc.js', 'before', 'await renderDoc();',
             [DECLARE_MD],
             [DECLARE_MD]),
        Step('assets/doc.js', 'replace', 'await renderDoc();',
             ['md = await renderDoc();'],
             replacement='md = await renderDoc();'),
        Step('assets/doc.js', 'after', 'done();',
             ['PFDocEdit.attach('],
             ["if (md) PFDocEdit.attach({ id, docFile: p.doc, md, docElement: document.getElementById('doc') });   // 로컬 편집 서버(edit-server.py)가 있을 때만 [편집 모드] (assets/doc-edit.js)"],
             exact=True),
        Step('assets/doc.js', 'after', "addEventListener('resize', () => pres.forEach(fade));",
             ['return md;'],
             ['return md;                                                         // 편집 모드가 블록의 md 줄 범위를 찾을 때 씀']),
    ]),
    ('포트폴리오 문서 (index.html)', [
        Step('index.html', 'after', '<link rel="stylesheet" href="assets/portfolio.css" />',
             ['assets/doc-edit.css', 'assets/portfolio-edit.css'],
             ['<link rel="stylesheet" href="assets/doc-edit.css" /><!-- 편집 모드(이 포크 전용): 버튼·고칠 칸 테두리는 상세 문서와 같은 모양 -->',
              '<link rel="stylesheet" href="assets/portfolio-edit.css" />']),
        Step('index.html', 'after', '<script src="assets/portfolio.js"></script>',
             ['assets/portfolio-edit.js'],
             ['<script src="assets/portfolio-edit.js"></script><!-- 편집 모드: 로컬 편집 서버(edit-server.py)가 있을 때만 켜짐. portfolio.js 다음 -->']),
    ]),
    ('발표 덱 (project.html · assets/project-deck.js)', [
        Step('project.html', 'after', '<link rel="stylesheet" href="assets/deck-theme.css" />',
             ['assets/doc-edit.css', 'assets/portfolio-edit.css', 'assets/deck-edit.css'],
             ['<link rel="stylesheet" href="assets/doc-edit.css" /><!-- 편집 모드(이 포크 전용): 버튼·고칠 칸 테두리·입력창은 상세 문서·포트폴리오 문서와 같은 모양 -->',
              '<link rel="stylesheet" href="assets/portfolio-edit.css" />',
              '<link rel="stylesheet" href="assets/deck-edit.css" />']),
        Step('project.html', 'after', '<script src="assets/project-deck.js"></script>',
             ['assets/doc-edit.js', 'assets/deck-edit.js'],
             ['<script src="assets/doc-edit.js"></script><!-- 편집 모드(이 포크 전용): 덱의 표·그림·코드가 md 몇 줄인지 재는 데 씀 -->',
              '<script src="assets/deck-edit.js"></script><!-- 편집 모드: 로컬 편집 서버(edit-server.py)가 있을 때만 켜짐. project-deck.js 다음 -->']),
        Step('assets/project-deck.js', 'before', 'function fitSlide(',
             ['function markForDeckEdit('],
             MARK_FOR_DECK_EDIT),
        Step('assets/project-deck.js', 'after', 'PF.body(s).innerHTML = `<div class="pf-fill">${bodyHtml(sl, vs, v)}</div>`;',
             ['markForDeckEdit(s, sl, vis);'],
             ['markForDeckEdit(s, sl, vis);                                    // 발표 덱 편집 모드 표시(이 포크 전용, assets/deck-edit.js)']),
    ]),
]


# ── 밖에서 부르는 함수 ──

def patch_file(root, relative_path):
    """편집 서버가 보낼 파일 하나에 편집 모드 줄을 넣은 글을 돌려줍니다(디스크는 쓰지 않음).

    edit-server.py의 do_GET이 부릅니다. 그 파일이 든 묶음 전체의 기준 줄을 먼저 확인해,
    하나라도 못 찾으면 묶음의 파일을 모두 원본 그대로 보내게 합니다(반쪽만 줄이 들어가는 상태를 막음).
    root: 레포 루트(Path 또는 글)
    relative_path: 레포 루트 기준 경로, '/' 구분(예: 'assets/doc.js')
    돌려줌: (보낼 바이트, 멈춘 이유 목록) — 목록이 비면 줄을 넣은(또는 이미 있던) 글, 차 있으면 원본 바이트.
            5개 파일이 아니거나 그 파일이 디스크에 없으면 None(보통 정적 파일처럼 보내면 됨)
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
    """5개 파일의 기준 줄을 묶음마다 재서 결과를 돌려줍니다(디스크는 쓰지 않음).

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
    돌려줌: 그 묶음의 Step 목록. 5개 파일이 아니면 None
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
