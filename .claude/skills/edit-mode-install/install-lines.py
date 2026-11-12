"""edit-mode-install 보조 스크립트 — 템플릿 파일 5개에 편집 모드 줄을 넣고, .claude/CLAUDE.md 절과 .claude/launch.json 구성을 맞춥니다.

    python install-lines.py check <레포 루트>   # 무엇을 바꿀지 보여 주기만 합니다(파일을 쓰지 않음)
    python install-lines.py apply <레포 루트>   # 실제로 씁니다

- 멱등: 넣을 줄이 이미 있으면 건너뜁니다. 두 번 실행해도 두 번째는 아무것도 바꾸지 않습니다.
- 묶음: 함께 바뀌어야 하는 파일을 한 묶음으로 다룹니다(예: doc.js가 부르는 PFDocEdit는 doc.html이 싣는 doc-edit.js에 있음).
  묶음 안의 기준 줄을 하나라도 찾지 못하거나 두 곳 이상이면, 그 묶음은 아무것도 쓰지 않고 [멈춤]으로 알립니다(추측해서 넣지 않음).
- 줄 끝(CRLF/LF)·UTF-8 BOM은 파일마다 원래대로 둡니다. 넣는 줄의 들여쓰기는 기준 줄의 들여쓰기를 따릅니다.
- 종료 코드: 0 = 모두 맞춤(이미 있음 포함) · 2 = 멈춘 항목이 있음 · 1 = 사용법 오류
표준 라이브러리만 씁니다(Python 3.8 이상).
"""
import json
import re
import sys
from pathlib import Path

for _stream in (sys.stdout, sys.stderr):                          # Claude가 읽는 파이프는 UTF-8로(한글 깨짐 방지)
    try:
        _stream.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass

HERE = Path(__file__).resolve().parent
CLAUDE_MD_BLOCK_FILE = HERE / 'claude-md-block.md'
BLOCK_START = '<!-- edit-mode-install:start -->'
BLOCK_END = '<!-- edit-mode-install:end -->'
LAUNCH_NAME = 'portfolio-edit'


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

GROUPS = [
    ('상세 문서 (doc.html · assets/doc.js)', [
        Step('doc.html', 'after', '<link rel="stylesheet" href="assets/doc.css" />',
             ['assets/doc-edit.css'],
             ['<link rel="stylesheet" href="assets/doc-edit.css" />']),
        Step('doc.html', 'before', '<script src="assets/doc.js"></script>',
             ['assets/doc-edit.js'],
             ['<script src="assets/doc-edit.js"></script><!-- 편집 모드: 로컬 편집 서버(edit-server.py)가 있을 때만 켜짐. doc.js보다 먼저 -->']),
        Step('assets/doc.js', 'replace', 'await renderDoc();',
             ['const md = await renderDoc();'],
             replacement='const md = await renderDoc();'),
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


def main():
    """명령줄을 읽어 세 가지(템플릿 줄 · CLAUDE.md 절 · launch.json 구성)를 차례로 맞추고, 멈춘 것이 있으면 2를 돌려줍니다."""
    is_usage_ok = len(sys.argv) == 3 and sys.argv[1] in ('check', 'apply')
    if is_usage_ok == False:
        print('사용법: python install-lines.py check|apply <레포 루트>')
        return 1
    root = Path(sys.argv[2]).resolve()
    write = sys.argv[1] == 'apply'

    stopped = 0
    for name, steps in GROUPS:
        stopped += install_group(root, name, steps, write)
    stopped += install_claude_md_block(root, write)
    stopped += install_launch_config(root, write)

    print()
    if stopped:
        print('결과: 멈춘 항목 %d개 — 위 [멈춤] 줄을 확인하세요(그 묶음의 파일은 쓰지 않았습니다)' % stopped)
        return 2
    print('결과: 모두 맞춤' if write else '결과: 멈출 항목 없음')
    return 0


# ── 템플릿 파일에 줄 넣기 ──

def install_group(root, name, steps, write):
    """묶음 하나의 줄을 계산해, 모든 기준 줄을 찾았을 때만 씁니다.

    root: 레포 루트
    name: 묶음 이름(출력용)
    steps: 이 묶음의 Step 목록(파일 여럿 가능)
    write: True면 파일에 씀, False면 보여 주기만
    돌려줌: 멈춘 묶음이면 1, 아니면 0
    """
    print('■ %s' % name)
    files = {}
    for step in steps:
        if step.path not in files:
            files[step.path] = read_text_file(root / step.path)

    messages, errors = [], []
    for step in steps:
        problem = plan_step(files[step.path], step, messages)
        if problem:
            errors.append(problem)

    if errors:
        for problem in errors:
            print('  [멈춤] %s' % problem)
        print('  → 이 묶음(%s)은 아무것도 쓰지 않았습니다.' % ', '.join(files))
        return 1

    for line in messages:
        print('  ' + line)
    if write:
        for path, text_file in files.items():
            if text_file['changed']:
                write_text_file(root / path, text_file)
        for problem in count_keys(files, steps):
            print('  [확인 필요] %s' % problem)
    return 0


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


def count_keys(files, steps):
    """쓴 뒤 확인: 넣은 줄의 글자가 파일마다 정확히 한 번씩 있는지. 어긋난 것의 설명 목록을 돌려줍니다(비면 정상)."""
    problems = []
    for step in steps:
        whole = '\n'.join(files[step.path]['lines'])
        for key in step.keys:
            count = whole.count(key)
            if count != 1:
                problems.append('%s: `%s`가 %d번 있습니다' % (step.path, key, count))
    return problems


# ── .claude/CLAUDE.md 절 ──

def install_claude_md_block(root, write):
    """표지 주석 사이에 claude-md-block.md 내용을 넣습니다. 파일이 없으면 만들고, 표지가 없으면 끝에 더하고, 있으면 그 사이만 바꿉니다.

    돌려줌: 멈췄으면 1, 아니면 0
    """
    print('■ .claude/CLAUDE.md')
    path = root / '.claude' / 'CLAUDE.md'
    body = CLAUDE_MD_BLOCK_FILE.read_text(encoding='utf-8-sig').strip('\r\n').replace('\r\n', '\n').split('\n')
    block = [BLOCK_START] + body + [BLOCK_END]

    text_file = read_text_file(path)
    if text_file is None:
        text_file = {'lines': block + [''], 'newline': '\n', 'bom': False, 'changed': True}
        message = '[넣음] 새 파일을 만들고 편집 모드 절을 넣음'
    else:
        lines = text_file['lines']
        starts = [i for i, line in enumerate(lines) if line.strip() == BLOCK_START]
        ends = [i for i, line in enumerate(lines) if line.strip() == BLOCK_END]
        has_no_markers = len(starts) == 0 and len(ends) == 0
        has_one_pair = len(starts) == 1 and len(ends) == 1 and starts[0] < ends[0]
        if has_no_markers:
            while lines and lines[-1].strip() == '':
                lines.pop()
            text_file['lines'] = lines + ([''] if lines else []) + block + ['']
            text_file['changed'] = True
            message = '[넣음] 파일 끝에 편집 모드 절을 더함(기존 내용은 그대로)'
        elif has_one_pair:
            if lines[starts[0]:ends[0] + 1] == block:
                print('  [있음] 편집 모드 절이 최신입니다')
                return 0
            text_file['lines'] = lines[:starts[0]] + block + lines[ends[0] + 1:]
            text_file['changed'] = True
            message = '[넣음] 표지 주석 사이의 편집 모드 절을 새 내용으로 바꿈(절 밖은 그대로)'
        else:
            print('  [멈춤] 표지 주석(%s · %s)이 짝이 맞지 않습니다(시작 %d개, 끝 %d개) — 직접 정리한 뒤 다시 실행하세요' % (BLOCK_START, BLOCK_END, len(starts), len(ends)))
            return 1

    print('  ' + message)
    if write:
        path.parent.mkdir(parents=True, exist_ok=True)
        write_text_file(path, text_file)
    return 0


# ── .claude/launch.json 구성 ──

LAUNCH_CONFIG_LINES = [
    '    {',
    '      "name": "%s",' % LAUNCH_NAME,
    '      "runtimeExecutable": "python",',
    '      "runtimeArgs": ["edit-server.py", "4600"],',
    '      "port": 4600',
    '    }',
]


def install_launch_config(root, write):
    """edit-server.py를 띄우는 구성이 없을 때만 이름 portfolio-edit으로 하나 더합니다. 기존 구성은 바꾸지 않습니다.

    돌려줌: 멈췄으면 1, 아니면 0
    """
    print('■ .claude/launch.json')
    path = root / '.claude' / 'launch.json'
    text_file = read_text_file(path)
    if text_file is None:
        lines = ['{', '  "version": "0.0.1",', '  "configurations": ['] + LAUNCH_CONFIG_LINES + ['  ]', '}', '']
        text_file = {'lines': lines, 'newline': '\n', 'bom': False, 'changed': True}
        print('  [넣음] 새 파일을 만들고 구성 "%s"(python edit-server.py 4600)를 넣음' % LAUNCH_NAME)
        if write:
            write_text_file(path, text_file)
        return 0

    text = '\n'.join(text_file['lines'])
    try:
        configurations = json.loads(text).get('configurations')
    except (ValueError, AttributeError) as error:
        print('  [멈춤] launch.json을 읽을 수 없습니다(%s) — 이 파일은 건드리지 않았습니다' % error)
        return 1
    if isinstance(configurations, list) == False:
        print('  [멈춤] launch.json에 configurations 배열이 없습니다 — 이 파일은 건드리지 않았습니다')
        return 1

    runs_edit_server = [c.get('name') for c in configurations if isinstance(c, dict) and 'edit-server.py' in json.dumps(c)]
    if runs_edit_server:
        print('  [있음] edit-server.py를 띄우는 구성이 있습니다(%s)' % ', '.join(str(n) for n in runs_edit_server))
        return 0
    is_name_taken = any(isinstance(c, dict) and c.get('name') == LAUNCH_NAME for c in configurations)
    if is_name_taken:
        print('  [멈춤] 이름 "%s"인 다른 구성이 이미 있습니다 — 기존 구성을 바꾸지 않으려고 건너뜁니다' % LAUNCH_NAME)
        return 1

    new_text = insert_last_configuration(text)
    if new_text is None:
        print('  [멈춤] configurations 배열의 끝을 찾지 못했습니다 — 구성 "%s"(python edit-server.py 4600)를 직접 더해 주세요' % LAUNCH_NAME)
        return 1
    text_file['lines'] = new_text.split('\n')
    text_file['changed'] = True
    print('  [넣음] 구성 "%s"(python edit-server.py 4600)를 configurations 끝에 더함(기존 구성은 그대로)' % LAUNCH_NAME)
    if write:
        write_text_file(path, text_file)
    return 0


def insert_last_configuration(text):
    """configurations 배열의 마지막 구성 `}` 다음에 새 구성을 글자로 끼웁니다(기존 줄의 모양을 바꾸지 않으려고 JSON을 다시 쓰지 않음).

    text: launch.json 전체(줄 끝은 \n)
    돌려줌: 새 글. configurations가 맨 끝 키가 아니거나 비어 있어 자리를 못 찾으면 None
    """
    match = re.search(r'\}(\s*\]\s*\}\s*)$', text)
    if match is None:
        return None
    new_text =text[:match.start() + 1] + ',\n' + '\n'.join(LAUNCH_CONFIG_LINES) + match.group(1)
    try:
        names = [c.get('name') for c in json.loads(new_text)['configurations']]
    except (ValueError, KeyError, AttributeError):
        return None
    if LAUNCH_NAME not in names:
        return None
    return new_text


# ── 파일 읽고 쓰기 (줄 끝·BOM 유지) ──

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


def write_text_file(path, text_file):
    """read_text_file의 사전을 원래 줄 끝·BOM으로 씁니다."""
    text = text_file['newline'].join(text_file['lines'])
    data = text.encode('utf-8')
    if text_file['bom']:
        data = b'\xef\xbb\xbf' + data
    path.write_bytes(data)


if __name__ == '__main__':
    sys.exit(main())
