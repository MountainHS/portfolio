"""edit-mode-install 보조 스크립트 — 템플릿 파일의 기준 줄을 확인하고, .claude/CLAUDE.md 절과 .claude/launch.json 구성을 맞춥니다.

    python install-lines.py check <레포 루트>   # 무엇을 바꿀지 보여 주기만 합니다(파일을 쓰지 않음)
    python install-lines.py apply <레포 루트>   # .claude/CLAUDE.md 절·.claude/launch.json 구성을 실제로 씁니다

- 템플릿 파일(index.html·project.html·doc.html)은 쓰지 않습니다.
  편집 서버(edit-server.py)가 보낼 때 edit-mode-patch.py로 메모리에서만 줄을 넣습니다. 여기서는 그 기준 줄이 맞는지만 봅니다.
  묶음 안의 기준 줄을 하나라도 찾지 못하거나 두 곳 이상이면 [멈춤] — 그 페이지는 편집 버튼 없이 원본 그대로 열립니다.
- 멱등: 넣을 절·구성이 이미 있으면 건너뜁니다. 두 번 실행해도 두 번째는 아무것도 바꾸지 않습니다.
- 줄 끝(CRLF/LF)·UTF-8 BOM은 파일마다 원래대로 둡니다.
- 종료 코드: 0 = 모두 맞춤(이미 있음 포함) · 2 = 멈춘 항목이 있음 · 1 = 사용법 오류
표준 라이브러리만 씁니다(Python 3.8 이상).
"""
import importlib.util
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
EDIT_MODE_PATCH_FILE = HERE / 'edit-mode-patch.py'


def load_edit_mode_patch():
    """줄 넣기 모듈(edit-mode-patch.py)을 읽습니다. 편집 서버와 같은 파일을 써서 넣는 줄·기준 줄이 한 곳에만 있게 합니다."""
    spec = importlib.util.spec_from_file_location('edit_mode_patch', str(EDIT_MODE_PATCH_FILE))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


edit_mode_patch = load_edit_mode_patch()


def main():
    """명령줄을 읽어 세 가지(템플릿 기준 줄 확인 · CLAUDE.md 절 · launch.json 구성)를 차례로 맞추고, 멈춘 것이 있으면 2를 돌려줍니다."""
    is_usage_ok = len(sys.argv) == 3 and sys.argv[1] in ('check', 'apply')
    if is_usage_ok == False:
        print('사용법: python install-lines.py check|apply <레포 루트>')
        return 1
    root = Path(sys.argv[2]).resolve()
    write = sys.argv[1] == 'apply'

    stopped = 0
    stopped += report_template_lines(root)
    stopped += install_claude_md_block(root, write)
    stopped += install_launch_config(root, write)

    print()
    if stopped:
        print('결과: 멈춘 항목 %d개 — 위 [멈춤] 줄을 확인하세요(템플릿 묶음이면 그 페이지는 편집 버튼 없이 열립니다)' % stopped)
        return 2
    print('결과: 모두 맞춤' if write else '결과: 멈출 항목 없음')
    return 0


# ── 템플릿 파일 기준 줄 확인 (쓰지 않음) ──

def report_template_lines(root):
    """edit-mode-patch.py의 check_all로 템플릿 파일의 기준 줄을 재서 묶음마다 찍습니다. 파일은 쓰지 않습니다.

    root: 레포 루트
    돌려줌: 멈춘 묶음 수
    """
    stopped = 0
    for result in edit_mode_patch.check_all(root):
        print('■ %s — 편집 서버가 보낼 때 넣음(디스크는 그대로)' % result['name'])
        for line in result['messages']:
            print('  ' + line)
        for problem in result['problems']:
            print('  [멈춤] %s' % problem)
        if result['problems']:
            stopped += 1
    return stopped


# ── .claude/CLAUDE.md 절 ──

def install_claude_md_block(root, write):
    """표지 주석 사이에 claude-md-block.md 내용을 넣습니다. 파일이 없으면 만들고, 표지가 없으면 끝에 더하고, 있으면 그 사이만 바꿉니다.

    돌려줌: 멈췄으면 1, 아니면 0
    """
    print('■ .claude/CLAUDE.md')
    path = root / '.claude' / 'CLAUDE.md'
    body = CLAUDE_MD_BLOCK_FILE.read_text(encoding='utf-8-sig').strip('\r\n').replace('\r\n', '\n').split('\n')
    block = [BLOCK_START] + body + [BLOCK_END]

    text_file = edit_mode_patch.read_text_file(path)
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
    text_file = edit_mode_patch.read_text_file(path)
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


# ── 파일 쓰기 (줄 끝·BOM 유지, 읽기는 edit-mode-patch.py의 read_text_file) ──

def write_text_file(path, text_file):
    """read_text_file의 사전을 원래 줄 끝·BOM으로 씁니다."""
    text = text_file['newline'].join(text_file['lines'])
    data = text.encode('utf-8')
    if text_file['bom']:
        data = b'\xef\xbb\xbf' + data
    path.write_bytes(data)


if __name__ == '__main__':
    sys.exit(main())
