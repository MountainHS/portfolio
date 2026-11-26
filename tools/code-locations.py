"""슬라이드 코드 칸 아래 캡션에 쓸 실제 코드 위치를 찾는다 (이 포크 전용).

프로젝트 문서 md의 코드 블록마다 src/<폴더>/code/ 아래 원본 소스와 줄을 대조해
"클래스.메서드() · 파일:줄"을 만들고 data/code-locations/<폴더>.json에 쓴다.
슬라이드 렌더러(assets/project-deck.js)가 이 파일을 읽어 캡션을 그린다.
원본에서 못 찾은 블록은 첫 줄 주석의 이름을 쓰고, 그것도 없으면 캡션을 비운다.

사용: python tools/code-locations.py <프로젝트 폴더>|--all
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE_SUFFIXES = {'.cs'}
CONTROL_WORDS = {'var', 'throw', 'yield', 'if', 'for', 'foreach', 'while', 'switch', 'catch', 'using', 'lock', 'return', 'new', 'else', 'do', 'try', 'fixed', 'nameof', 'typeof', 'await'}
TYPE_DECLARATION = re.compile(r'\b(class|struct|interface|record|enum)\s+(?!(?:class|struct)\b)([A-Za-z_]\w*)')
METHOD_DECLARATION = re.compile(r'^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|abstract|async|sealed|extern|unsafe|partial|readonly)\s+)*[\w<>\[\],.?]+(?:\s*<[^>]*>)?\s+([A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\(')
CONSTRUCTOR_DECLARATION = re.compile(r'^\s*(?:public|private|protected|internal|static)\s+([A-Za-z_]\w*)\s*\(')
COMMENT_NAME = re.compile(r'^(?:code/)?[A-Za-z_][\w.]*(?:\(\))?(?:\.cs)?(?::[\d\-, ]+)?$')


def main():
    """명령줄 인자로 고른 프로젝트마다 위치 파일을 쓰고 요약을 출력한다."""
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    if sys.argv[1] == '--all':
        projects = sorted(path.stem for path in (ROOT / 'data' / 'projects').glob('*.json'))
    else:
        projects = [sys.argv[1].strip('/\\').split('/')[-1].split('\\')[-1]]
    for project in projects:
        write_project_locations(project)


def write_project_locations(project):
    """한 프로젝트의 코드 블록 위치를 찾아 data/code-locations/<폴더>.json에 쓴다."""
    data = json.loads((ROOT / 'data' / 'projects' / f'{project}.json').read_text(encoding='utf-8'))
    project_folder = ROOT / 'src' / project
    markdown_lines = (project_folder / data['doc']).read_text(encoding='utf-8').splitlines()
    source_files = load_source_files(project_folder / 'code')

    entries = []
    missing_lines = []
    found_count = 0
    comment_count = 0
    for start_line, code_lines in code_blocks(markdown_lines):
        parts = []
        for segment_start, segment_end in segments(code_lines):
            part = find_part(code_lines, segment_start, segment_end, source_files)
            is_comment_only = all(normalize(line) == '' for line in code_lines[segment_start:segment_end])
            if is_comment_only and len(parts) + 1 < len(segments(code_lines)):
                continue
            if part['file']:
                found_count += 1
            elif part['label']:
                comment_count += 1
            else:
                missing_lines.append(start_line + segment_start)
            parts.append(part)
        entries.append({'key': block_key(code_lines), 'parts': parts})

    output_folder = ROOT / 'data' / 'code-locations'
    output_folder.mkdir(exist_ok=True)
    (output_folder / f'{project}.json').write_text(json.dumps(entries, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print(f'{project}: 찾음 {found_count} / 주석 대체 {comment_count} / 없음 {len(missing_lines)}'
          + (f' (md 줄 {", ".join(map(str, missing_lines))})' if missing_lines else ''))


def load_source_files(code_folder):
    """code/ 아래 원본 소스를 {상대 경로: 줄 목록}으로 읽는다. 폴더가 없으면 빈 dict."""
    if code_folder.is_dir() == False:
        return {}
    files = {}
    for path in sorted(code_folder.rglob('*')):
        if path.suffix in SOURCE_SUFFIXES:
            files[path.relative_to(code_folder).as_posix()] = path.read_text(encoding='utf-8-sig', errors='replace').splitlines()
    return files


def code_blocks(markdown_lines):
    """md의 ``` 코드 블록을 (여는 줄 번호, 블록 안 줄 목록)으로 하나씩 돌려준다. mermaid는 뺀다."""
    index = 0
    while index < len(markdown_lines):
        opening = re.match(r'^\s*(`{3,}|~{3,})\s*([\w+#-]*)', markdown_lines[index])
        if opening is None:
            index += 1
            continue
        fence, language = opening.group(1), opening.group(2).lower()
        end = index + 1
        while end < len(markdown_lines) and markdown_lines[end].strip().startswith(fence) == False:
            end += 1
        if language != 'mermaid':
            yield index + 1, markdown_lines[index + 1:end]
        index = end + 1


def block_key(code_lines):
    """렌더러와 같은 방식(줄마다 앞뒤 공백 제거 후 \\n으로 이음)으로 블록을 가리키는 열쇠를 만든다."""
    return '\n'.join(line.strip() for line in code_lines).strip('\n')


def normalize(line):
    """대조용 줄: 끝 // 주석과 앞뒤 공백을 뗀다. 비교할 내용이 없으면 ''."""
    text = re.sub(r'\s*//.*$', '', line).strip()
    if text in ('', '...', '…', '{', '}', '};', ')', ');'):
        return ''
    return re.sub(r'\s+', ' ', text)


def segments(code_lines):
    """블록을 위치 주석 줄('// 클래스.메서드()' 꼴)마다 나눠 (시작, 끝) 줄 번호(0부터, 끝 제외)로 돌려준다."""
    starts = [0]
    for index, line in enumerate(code_lines):
        if index > 0 and comment_label([line]):
            starts.append(index)
    ends = starts[1:] + [len(code_lines)]
    return list(zip(starts, ends))


def find_part(code_lines, segment_start, segment_end, source_files):
    """블록 한 구간을 원본 파일과 대조해 {label, file, start, end, lineMap, className, methodMap}을 만든다.

    구간 첫 줄 주석이 원본 파일 이름(클래스)을 가리키면 그 파일만, 아니면 모든 파일에서
    가장 많은 줄이 맞는 하나를 고른다(비교 줄의 절반 이상, 동점 없음). lineMap은 구간 줄마다
    원본 줄 번호(1부터, 못 맞추면 None), methodMap은 그 줄을 감싸는 메서드 이름(렌더러가 나뉜 장면마다
    보이는 줄의 메서드만 고르는 데 씀). 못 찾으면 file = ''과 첫 줄 주석 이름을 쓴다.
    """
    segment_lines = code_lines[segment_start:segment_end]
    comment_name = comment_label(segment_lines)
    declared_type = next((found.group(2) for found in map(TYPE_DECLARATION.search, segment_lines) if found), '')
    part = {'label': comment_name or declared_type, 'file': '', 'start': segment_start + 1, 'end': segment_end, 'lineMap': []}
    compared = [normalize(line) for line in segment_lines]
    compared_count = sum(1 for text in compared if text)
    if compared_count == 0:
        return part

    candidates = named_files(comment_name, source_files) or source_files
    scores = []
    for path, source_lines in candidates.items():
        line_map = match_lines(compared, [normalize(line) for line in source_lines])
        scores.append((sum(1 for number in line_map if number), path, line_map))
    scores.sort(key=lambda score: -score[0])
    if len(scores) == 0:
        return part
    best_count, best_path, best_map = scores[0]
    is_tied = len(scores) > 1 and scores[1][0] == best_count
    is_enough = best_count * 2 >= compared_count
    if is_tied or is_enough == False:
        return part

    matched_numbers = [number for number in best_map if number]
    part['label'] = member_label(candidates[best_path], matched_numbers)
    part['file'] = best_path.split('/')[-1]
    part['lineMap'] = best_map
    part['className'] = enclosing_type(candidates[best_path], matched_numbers[0])
    part['methodMap'] = [enclosing_method(candidates[best_path], number) if number else '' for number in best_map]
    return part


def named_files(comment_name, source_files):
    """주석 이름의 첫 칸(클래스·파일 이름)과 같은 이름의 원본 파일만 고른다. 없으면 빈 dict."""
    if comment_name == '':
        return {}
    stem = re.split(r'[.:(]', comment_name.removesuffix('.cs').split('.cs:')[0])[0]
    return {path: lines for path, lines in source_files.items() if Path(path).stem == stem}


def match_lines(compared, source_normalized):
    """md 줄마다 원본 줄 번호를 고른다. 앞 매칭 줄 뒤에서 먼저 찾고, 없으면 파일 처음부터 찾는다."""
    line_map = []
    previous = 0
    for text in compared:
        if text == '':
            line_map.append(None)
            continue
        number = find_after(source_normalized, text, previous)
        if number is None:
            number = find_after(source_normalized, text, 0)
        line_map.append(number)
        if number:
            previous = number
    return line_map


def find_after(source_normalized, text, after):
    """after 줄(1부터) 뒤에서 text와 같은 원본 줄 번호. 없으면 None."""
    for index in range(after, len(source_normalized)):
        if source_normalized[index] == text:
            return index + 1
    return None


def member_label(source_lines, matched_numbers):
    """매칭된 원본 줄을 감싸는 클래스·메서드 이름으로 'Class.Method()' 꼴 라벨을 만든다."""
    class_name = enclosing_type(source_lines, matched_numbers[0])
    method_names = []
    for number in matched_numbers:
        name = enclosing_method(source_lines, number)
        if name and name not in method_names:
            method_names.append(name)
    if len(method_names) == 0:
        return class_name
    methods = ' / '.join(f'{name}()' for name in method_names)
    return f'{class_name}.{methods}' if class_name else methods


def enclosing_type(source_lines, number):
    """number 줄(1부터)을 감싸는 class·struct 선언 이름(그 줄보다 얕게 들여쓴 가장 가까운 선언). 없으면 ''."""
    target_indent = indent_of(source_lines[number - 1])
    for index in range(number - 2, -1, -1):
        line = source_lines[index]
        found = TYPE_DECLARATION.search(re.sub(r'//.*$', '', line))
        if found and indent_of(line) < target_indent:
            return found.group(2)
    return ''


def enclosing_method(source_lines, number):
    """number 줄을 감싸는 메서드 선언 이름. 그 줄이 선언 자체여도 된다. 클래스 본문 줄이면 ''."""
    target_indent = indent_of(source_lines[number - 1])
    for index in range(number - 1, -1, -1):
        line = source_lines[index]
        if line.strip() == '':
            continue
        is_outer_type = TYPE_DECLARATION.search(re.sub(r'//.*$', '', line)) and indent_of(line) < target_indent
        if is_outer_type:
            return ''
        is_declaration_line = index == number - 1
        if is_declaration_line == False and indent_of(line) >= target_indent:
            continue
        name = method_name(line)
        if name:
            return name
    return ''


def method_name(line):
    """줄이 메서드·생성자 선언이면 이름, 아니면 ''."""
    for pattern in (METHOD_DECLARATION, CONSTRUCTOR_DECLARATION):
        found = pattern.match(line)
        if found is None:
            continue
        first_word = line.split()[0]
        is_statement = line.rstrip().endswith(';') or first_word in CONTROL_WORDS or '=' in line.split('(')[0]
        if is_statement == False and found.group(1) not in CONTROL_WORDS:
            return found.group(1)
    return ''


def indent_of(line):
    """줄 앞 공백 폭(탭 = 4칸)."""
    expanded = line.replace('\t', '    ')
    return len(expanded) - len(expanded.lstrip(' '))


def comment_label(code_lines):
    """첫 줄 주석 '// 이름 — 설명'에서 이름만 꺼낸다. 이름 꼴이 아니면 ''."""
    first = next((line.strip() for line in code_lines if line.strip()), '')
    if first.startswith('//') == False:
        return ''
    name = re.split(r'\s+[—–-]\s+|\s+\(', first[2:].strip())[0].strip()
    if COMMENT_NAME.match(name) is None:
        return ''
    return name.removeprefix('code/')


if __name__ == '__main__':
    main()
