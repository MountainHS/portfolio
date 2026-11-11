#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""포트폴리오 문서 고치기 — 편집 서버(edit-server.py)의 저장과 /portfolio-build 마지막 반영이 함께 씁니다.

포트폴리오 문서(index.html)에서 사용자가 고친 글은 data/가 아니라 소스에 남깁니다(다시 빌드해도 남게).
  - 프로필(이름·영문 이름·한 줄 정체성·이메일·GitHub·자기소개)·기술 이름 → profile.md의 그 줄
  - 기술 문서에 같은 글자가 그대로 한 번만 있는 글 → 그 기술 문서 줄(제목 줄·코드 블록은 빼고)
  - 그 밖의 글(Claude가 줄인 한 줄 소개·기능 설명·기술 근거, 태그 등) → src/<폴더>/deck-edits.json의 "portfolio" 목록
저장할 때 data/도 같이 고쳐 새로고침하면 바로 보입니다. 다시 빌드한 뒤에는 `apply --all`이 "portfolio" 목록을 data/에 다시 덮습니다.

"portfolio" 목록 한 줄 = { "field", (자리 이름), "value": 고친 글, "from": 처음 고칠 때 빌드가 만든 글 }
  field: title · tagline · team.role · intent · tags · meta(+label) · highlight.title / highlight.what / highlight.caption(+section)
         · skill.evidence(+skill, n = 그 기술에서 이 프로젝트 근거의 몇 번째인지, 1부터)
  자리 이름(label·section·skill)은 다시 빌드해도 그대로인 값이라 번호가 바뀌어도 같은 칸을 찾습니다.
  value를 빌드 글(from)과 같게 고치면 그 줄은 지워집니다(= 되돌리기).

발표 덱(project.html) 편집 모드의 장면 글(제목·도입·요점·키워드)도 여기서 씁니다(save_deck_text).
  - 고친 글은 늘 src/<폴더>/deck-edits.json의 "deckText" 목록에 남기고, 기술 문서에 같은 글이 한 번만 있으면 그 줄도 고칩니다.
  - "deckText" 목록 한 줄 = { "slide": 장면 key, "field": title · lead · point · keywords, "n": 요점 번호(point만, 1부터), "value", "from" }
  - data에는 deck.slides와 deck.base(장면 고치기 전 빌드 장면, 있으면) 둘 다에 씁니다 → deck-edits.py apply가 base에서 장면을 다시 만들어도 남음.
    다만 /portfolio-edit로 고정한(pin) 장면은 고치기 파일의 글로 다시 만들어지므로, deck-edits.py apply 뒤에는 늘 이 스크립트의 apply를 실행합니다.

사용(레포 루트에서, Windows에서 python이 없으면 py):
  python .claude/skills/portfolio-build/portfolio-edits.py apply <폴더>… | --all     고친 글(포트폴리오 문서·발표 덱)을 data/에 반영
Python 3.8 이상, 표준 라이브러리만.
"""
import importlib.util
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PROFILE_PATH = ROOT / "profile.md"
PORTFOLIO_DATA_PATH = ROOT / "data" / "portfolio.json"
EDITS_FILE_NAME = "deck-edits.json"
DECODER = json.JSONDecoder()

# profile.md 「기본 정보」의 줄 이름(`- 이름: 홍산`) ↔ data/portfolio.json profile 칸
PROFILE_LABELS = {"name": "이름", "nameEn": "영문 이름", "headline": "한 줄 정체성", "email": "이메일", "github": "GitHub"}
PROJECT_TEXT_FIELDS = ("title", "tagline", "team.role", "intent", "tags", "meta", "highlight.title", "highlight.what", "highlight.caption")

for _stream in (sys.stdout, sys.stderr):                           # Claude가 읽는 파이프는 UTF-8로(한글 깨짐 방지)
    if _stream.isatty() == False:
        try:
            _stream.reconfigure(encoding="utf-8")
        except (AttributeError, ValueError):
            pass


class EditError(Exception):
    """저장 요청을 받아들일 수 없을 때. status는 편집 서버가 돌려줄 HTTP 상태 코드입니다."""

    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


# ════════════════════════════════════════════════════════════════
# 편집 서버 진입점
# ════════════════════════════════════════════════════════════════

def save(request):
    """편집 모드의 저장(또는 저장 위치 묻기) 요청 하나를 처리합니다. edit-server.py의 /__edit/portfolio가 부릅니다.

    request: { field, project?, skill?, evidence?, index?, expected, value, portfolioOnly?, dryRun? }
      field·project·skill·evidence·index = 화면의 어느 칸인지(assets/portfolio-edit.js가 data 순서 번호로 보냄)
      expected = 화면을 그릴 때의 값(지금 data와 다르면 저장하지 않음), value = 새 값(tags는 글자 목록)
      portfolioOnly = true면 기술 문서에 같은 글이 있어도 deck-edits.json에만 저장, dryRun = true면 저장 위치만 알려 줌
    반환: { ok, where: 저장 위치 설명, canChooseOverride: 기술 문서 대신 포트폴리오에만 저장할 수 있는지 }
    """
    target = find_target(request)
    is_screen_outdated = target["current"] != request.get("expected")
    if is_screen_outdated:
        raise EditError(409, "data가 화면을 그린 뒤에 바뀌었습니다. 새로고침한 뒤 다시 고쳐 주세요.")

    destination = choose_destination(target, request.get("portfolioOnly") == True)
    answer = {"ok": True, "where": destination["where"], "canChooseOverride": destination["kind"] == "doc"}
    if request.get("dryRun") == True:
        return answer

    new_value = clean_value(target, request.get("value"))
    if new_value == target["current"]:
        return answer
    write_destination(destination, target, new_value)
    write_data_value(target["dataPath"], target["jsonPath"], new_value)
    return answer


# ════════════════════════════════════════════════════════════════
# 고칠 칸 찾기: 화면의 칸 → data 파일·JSON 경로·지금 값·저장 자리 이름
# ════════════════════════════════════════════════════════════════

def find_target(request):
    """요청의 칸을 data에서 찾습니다.

    반환: { field, current: 지금 data 값, dataPath, jsonPath, projectId(프로필 칸이면 None), entryKey: deck-edits.json 자리 이름 }
    """
    field = request.get("field", "")
    portfolio_data = read_json(PORTFOLIO_DATA_PATH)

    if field.startswith("profile."):
        key = field[len("profile."):]
        is_known_profile_field = key in PROFILE_LABELS or key == "intro"
        if is_known_profile_field == False:
            raise EditError(400, f"모르는 프로필 칸입니다: {field}")
        return make_target(field, PORTFOLIO_DATA_PATH, portfolio_data, ["profile", key], None, None)

    if field == "skill.name":
        skill_index = int(request["skill"])
        return make_target(field, PORTFOLIO_DATA_PATH, portfolio_data, ["skills", skill_index, "name"], None, None)

    if field == "skill.evidence":
        skill = portfolio_data["skills"][int(request["skill"])]
        evidence_index = int(request["evidence"])
        project_id = skill["evidence"][evidence_index]["project"]
        same_project_indexes = [i for i, evidence in enumerate(skill["evidence"]) if evidence.get("project") == project_id]
        entry_key = {"field": field, "skill": skill["name"], "n": same_project_indexes.index(evidence_index) + 1}
        json_path = ["skills", int(request["skill"]), "evidence", evidence_index, "text"]
        return make_target(field, PORTFOLIO_DATA_PATH, portfolio_data, json_path, project_id, entry_key)

    if field in PROJECT_TEXT_FIELDS:
        project_id = request["project"]
        data_path = project_data_path(project_id)
        project_data = read_json(data_path)
        entry_key = project_entry_key(field, project_data, request.get("index"))
        json_path = project_json_path(entry_key, project_data)
        if json_path is None:
            raise EditError(404, f"{project_id}의 {field} 칸을 data에서 찾지 못했습니다. 새로고침해 주세요.")
        return make_target(field, data_path, project_data, json_path, project_id, entry_key)

    raise EditError(400, f"모르는 칸입니다: {field}")


def make_target(field, data_path, data, json_path, project_id, entry_key):
    """find_target의 결과 한 묶음을 만듭니다(지금 값은 json_path로 data에서 읽음)."""
    return {"field": field, "current": value_at(data, json_path), "dataPath": data_path, "jsonPath": json_path,
            "projectId": project_id, "entryKey": entry_key}


def project_entry_key(field, project_data, index):
    """프로젝트 칸의 deck-edits.json 자리 이름. 번호(index) 대신 다시 빌드해도 그대로인 값(meta 이름·기능 절 제목)을 씁니다."""
    if field == "meta":
        return {"field": field, "label": project_data["meta"][int(index)]["label"]}
    if field.startswith("highlight."):
        return {"field": field, "section": project_data["highlights"][int(index)]["section"]}
    return {"field": field}


def project_json_path(entry_key, project_data):
    """자리 이름 → data/projects/<폴더>.json 안의 JSON 경로. 다시 빌드로 그 자리가 없어졌으면 None."""
    field = entry_key["field"]
    simple_paths = {"title": ["title"], "tagline": ["tagline"], "team.role": ["team", "role"], "tags": ["tags"]}
    if field in simple_paths:
        return simple_paths[field]
    if field == "intent":
        has_intent = isinstance(project_data.get("intent"), dict)
        return ["intent", "text"] if has_intent else None
    if field == "meta":
        for index, meta in enumerate(project_data.get("meta") or []):
            if meta.get("label") == entry_key["label"]:
                return ["meta", index, "value"]
        return None
    for index, highlight in enumerate(project_data.get("highlights") or []):
        if highlight.get("section") != entry_key["section"]:
            continue
        if field == "highlight.title":
            return ["highlights", index, "title"]
        if field == "highlight.what":
            return ["highlights", index, "what"]
        return caption_json_path(highlight, index)
    return None


def caption_json_path(highlight, index):
    """기능 화면 설명의 JSON 경로. 설명은 글자 또는 { text, verbatim }(schema.md 「글 조각」), 그림·설명이 없으면 None."""
    image = highlight.get("image")
    if isinstance(image, dict) == False:
        return None
    caption = image.get("caption")
    if isinstance(caption, dict):
        return ["highlights", index, "image", "caption", "text"]
    if isinstance(caption, str) and caption != "":
        return ["highlights", index, "image", "caption"]
    return None


def skill_evidence_json_path(entry_key, project_id, portfolio_data):
    """기술 근거 자리 이름(기술 이름·이 프로젝트 근거의 n번째) → data/portfolio.json 안의 JSON 경로. 없으면 None."""
    for skill_index, skill in enumerate(portfolio_data.get("skills") or []):
        if skill.get("name") != entry_key["skill"]:
            continue
        same_project_indexes = [i for i, evidence in enumerate(skill.get("evidence") or []) if evidence.get("project") == project_id]
        if entry_key["n"] <= len(same_project_indexes):
            return ["skills", skill_index, "evidence", same_project_indexes[entry_key["n"] - 1], "text"]
    return None


def clean_value(target, value):
    """새 값을 칸 모양에 맞춥니다. tags는 빈 칸을 뺀 글자 목록, 자기소개는 여러 줄, 나머지는 한 줄 글자."""
    if target["field"] == "tags":
        return [str(tag).strip() for tag in value if str(tag).strip() != ""]
    text = str(value).replace("\r\n", "\n").strip()
    if target["field"] == "profile.intro":
        return text
    if "\n" in text:
        raise EditError(400, "이 칸은 한 줄 글입니다. 줄바꿈을 빼고 다시 저장해 주세요.")
    return text


# ════════════════════════════════════════════════════════════════
# 저장 위치 고르기: profile.md 줄 → 기술 문서 줄 → deck-edits.json
# ════════════════════════════════════════════════════════════════

def choose_destination(target, portfolio_only):
    """고친 글을 남길 소스를 고릅니다.

    반환: { kind: "profile" | "doc" | "override", where: 사람에게 보일 설명, path, line(1부터), ... }
    """
    field = target["field"]
    if field == "profile.intro":
        start, end = find_intro_lines(target["current"])
        return {"kind": "profile", "where": f"profile.md {start}~{end}줄 (자기소개)", "path": PROFILE_PATH, "start": start, "end": end}
    if field.startswith("profile."):
        line = find_profile_line(PROFILE_LABELS[field[len("profile."):]], target["current"])
        return {"kind": "profile", "where": f"profile.md {line}줄", "path": PROFILE_PATH, "start": line, "end": line}
    if field == "skill.name":
        line = find_skill_memo_line(target["current"])
        return {"kind": "profile", "where": f"profile.md {line}줄 (기술 스택 메모)", "path": PROFILE_PATH, "start": line, "end": line}

    edits_path = ROOT / "src" / target["projectId"] / EDITS_FILE_NAME
    override = {"kind": "override", "where": f"src/{target['projectId']}/{EDITS_FILE_NAME} (포트폴리오에만)", "path": edits_path}
    has_override_already = find_entry(read_portfolio_entries(edits_path), target["entryKey"]) is not None
    if portfolio_only or has_override_already or field == "tags":
        return override
    doc_path = project_doc_path(target["projectId"])
    line = find_doc_line(doc_path, target["current"])
    if line is None:
        return override
    return {"kind": "doc", "where": f"src/{target['projectId']}/{doc_path.name} {line}줄 (기술 문서)", "path": doc_path, "start": line, "end": line}


def find_profile_line(label, current):
    """profile.md에서 `- <label>: 값` 줄 번호를 찾습니다. 값이 화면 값과 다르면(빌드 뒤 profile.md를 고침) 저장하지 않습니다."""
    lines = read_lines(PROFILE_PATH)
    pattern = re.compile(r"^\s*-\s*" + re.escape(label) + r"\s*:(.*)$")
    for number, line in enumerate(lines, start=1):
        match = pattern.match(line)
        if match is None:
            continue
        if match.group(1).strip() != current:
            raise EditError(409, f"profile.md {number}줄의 값이 화면과 다릅니다. /portfolio-build로 다시 만든 뒤 고쳐 주세요.")
        return number
    raise EditError(404, f"profile.md에 '- {label}:' 줄이 없습니다.")


def find_intro_lines(current):
    """profile.md 「## 자기소개」 절에서 자기소개 글 줄 범위(안내 인용문 `>`·빈 줄·주석 제외)를 찾습니다. 반환: (시작 줄, 끝 줄)."""
    lines = read_lines(PROFILE_PATH)
    section_lines = section_line_numbers(lines, "자기소개")
    text_line_numbers = [number for number in section_lines if is_profile_text_line(lines[number - 1])]
    if text_line_numbers == []:
        raise EditError(404, "profile.md 「## 자기소개」에 글이 없습니다. profile.md에 먼저 쓰고 다시 빌드해 주세요.")
    start, end = text_line_numbers[0], text_line_numbers[-1]
    written = [line.strip() for line in lines[start - 1:end] if line.strip() != ""]
    shown = [line.strip() for line in current.split("\n") if line.strip() != ""]
    if written != shown:
        raise EditError(409, "profile.md의 자기소개가 화면과 다릅니다. /portfolio-build로 다시 만든 뒤 고쳐 주세요.")
    return start, end


def find_skill_memo_line(current):
    """profile.md 「## 기술 스택 메모」에서 `- <기술 이름>` 줄 번호를 찾습니다. 메모에 없는 이름(빌드가 고른 기술)은 고칠 수 없습니다."""
    lines = read_lines(PROFILE_PATH)
    for number in section_line_numbers(lines, "기술 스택 메모"):
        line = lines[number - 1]
        is_list_item = line.lstrip().startswith("-")
        is_same_name = re.sub(r"^\s*-\s*", "", line).strip() == current
        if is_list_item and is_same_name:
            return number
    raise EditError(404, f"profile.md 「기술 스택 메모」에 '- {current}' 줄이 없어 고칠 수 없습니다. 메모에 적은 이름만 여기서 고칩니다.")


def section_line_numbers(lines, title):
    """`## <title>`로 시작하는 절의 본문 줄 번호 목록(다음 `## ` 제목 전까지)."""
    numbers = []
    is_inside = False
    for number, line in enumerate(lines, start=1):
        if line.startswith("## "):
            is_inside = line[3:].strip().startswith(title)
            continue
        if is_inside:
            numbers.append(number)
    return numbers


def is_profile_text_line(line):
    """profile.md 절 안에서 사용자가 쓴 글 줄인지(안내 인용문·빈 줄·HTML 주석이 아닌지)."""
    stripped = line.strip()
    is_blank = stripped == ""
    is_guide_quote = stripped.startswith(">")
    is_comment = stripped.startswith("<!--")
    return is_blank == False and is_guide_quote == False and is_comment == False


def find_doc_line(doc_path, text):
    """기술 문서에서 text가 글자 그대로 들어 있는 줄 번호를 찾습니다.

    문서 전체에 정확히 한 번 있고, 그 줄이 제목 줄(#)·코드 블록 안이 아닐 때만 줄 번호. 아니면 None(→ deck-edits.json에 저장).
    제목 줄을 빼는 이유: 절 제목은 발표 덱 장면·[발표 덱에서 보기]가 절을 찾는 열쇠라 바뀌면 연결이 끊김.
    """
    if text == "" or doc_path.is_file() == False:
        return None
    lines = read_lines(doc_path)
    if "\n".join(lines).count(text) != 1:
        return None
    is_in_code = False
    for number, line in enumerate(lines, start=1):
        if re.match(r"^ {0,3}(```|~~~)", line):
            is_in_code = is_in_code == False
            continue
        if text not in line:
            continue
        is_heading = re.match(r"^ {0,3}#{1,6}\s", line) is not None
        if is_in_code or is_heading:
            return None
        return number
    return None


# ════════════════════════════════════════════════════════════════
# 쓰기
# ════════════════════════════════════════════════════════════════

def write_destination(destination, target, new_value):
    """choose_destination이 고른 소스에 새 값을 씁니다."""
    kind = destination["kind"]
    if kind == "override":
        write_override(destination["path"], target, new_value)
        return
    lines = read_lines(destination["path"])
    if target["field"] == "profile.intro":
        new_lines = new_value.split("\n")
    else:
        old_line = lines[destination["start"] - 1]
        if kind == "profile" and target["field"] == "skill.name":
            new_lines = [old_line[:len(old_line) - len(old_line.lstrip())] + "- " + new_value]
        elif kind == "profile":
            new_lines = [old_line[:old_line.index(":") + 1] + " " + new_value]
        else:
            new_lines = [old_line.replace(target["current"], new_value, 1)]
    replace_lines(destination["path"], destination["start"], destination["end"], new_lines)
    if target["field"] == "skill.name":
        rename_skill_in_overrides(target["current"], new_value)


def rename_skill_in_overrides(old_name, new_name):
    """기술 이름을 바꾸면 모든 프로젝트 deck-edits.json의 그 기술 근거 줄(skill.evidence)도 새 이름을 가리키게 합니다(자리 이름이 기술 이름이라)."""
    for edits_path in sorted((ROOT / "src").glob(f"*/{EDITS_FILE_NAME}")):
        edits = read_json(edits_path)
        entries = edits.get("portfolio") if isinstance(edits.get("portfolio"), list) else []
        renamed = [entry for entry in entries if entry.get("field") == "skill.evidence" and entry.get("skill") == old_name]
        for entry in renamed:
            entry["skill"] = new_name
        if renamed != []:
            write_text(edits_path, deck_edits_module().dump(edits) + "\n")


def write_override(edits_path, target, new_value):
    """deck-edits.json의 "portfolio" 목록에 고친 글을 넣습니다. 같은 자리 줄이 있으면 값만 바꾸고, 빌드 글로 되돌렸으면 그 줄을 지웁니다."""
    edits = read_json(edits_path) if edits_path.exists() else {"schemaVersion": 1, "pin": [], "hide": []}
    entries = edits.get("portfolio") if isinstance(edits.get("portfolio"), list) else []
    entry = find_entry(entries, target["entryKey"])
    if entry is None:
        entry = dict(target["entryKey"], value=new_value, **{"from": target["current"]})
        entries.append(entry)
    else:
        entry["value"] = new_value
    if entry["value"] == entry["from"]:
        entries.remove(entry)
    edits["portfolio"] = entries if entries != [] else None
    write_text(edits_path, deck_edits_module().dump(edits) + "\n")


def write_data_value(data_path, json_path, new_value):
    """data JSON 파일에서 json_path의 값 글자만 바꿔 씁니다(나머지 글자·줄 끝·BOM은 그대로라 git diff에 그 값만 나옴)."""
    raw_bytes = data_path.read_bytes()
    has_bom = raw_bytes.startswith(b"\xef\xbb\xbf")
    raw = raw_bytes.decode("utf-8-sig")
    start, end = find_value_span(raw, json_path)
    new_raw = raw[:start] + json_literal_like(raw, start, end, new_value) + raw[end:]

    expected_data = json.loads(raw)
    set_value_at(expected_data, json_path, new_value)
    if json.loads(new_raw) != expected_data:
        raise EditError(500, f"{data_path.name}에 값을 끼워 넣은 결과가 맞지 않아 쓰지 않았습니다.")
    write_text(data_path, new_raw, has_bom)


def json_literal_like(raw, start, end, new_value):
    """새 값의 JSON 글자. 원래 값이 여러 줄(예: 태그 목록)이었으면 같은 들여쓰기·줄 끝(CRLF/LF)으로 여러 줄로 씁니다."""
    was_multiline = "\n" in raw[start:end]
    if was_multiline == False:
        return json.dumps(new_value, ensure_ascii=False)
    line_end = "\r\n" if "\r\n" in raw else "\n"
    line_start = raw.rfind("\n", 0, start) + 1
    indent = re.match(r"[ \t]*", raw[line_start:start]).group(0)
    return json.dumps(new_value, ensure_ascii=False, indent=2).replace("\n", line_end + indent)


def find_value_span(raw, json_path):
    """JSON 글자 raw에서 json_path(키·번호 목록)가 가리키는 값의 [시작, 끝) 글자 위치를 돌려줍니다."""
    position = skip_spaces(raw, 0)
    for step in json_path:
        position = find_child_start(raw, position, step)
    _, end = DECODER.raw_decode(raw, position)
    return position, end


def find_child_start(raw, position, step):
    """position에서 시작하는 객체(키) 또는 배열(번호)에서 step 자식 값이 시작하는 위치. 없으면 KeyError."""
    is_object = raw[position] == "{"
    position = skip_spaces(raw, position + 1)
    index = 0
    while raw[position] not in "}]":
        key = index
        if is_object:
            key, position = DECODER.raw_decode(raw, position)
            position = skip_spaces(raw, skip_spaces(raw, position) + 1)    # ':' 건너뛰기
        if key == step:
            return position
        _, position = DECODER.raw_decode(raw, position)
        position = skip_spaces(raw, position)
        if raw[position] == ",":
            position = skip_spaces(raw, position + 1)
        index += 1
    raise KeyError(step)


def skip_spaces(raw, position):
    while raw[position] in " \t\r\n":
        position += 1
    return position


def replace_lines(path, start, end, new_lines):
    """파일의 start~end 줄(1부터, 끝 포함)을 new_lines로 바꿉니다. 줄바꿈 방식(CRLF/LF)과 BOM은 그대로 둡니다."""
    raw_bytes = path.read_bytes()
    has_bom = raw_bytes.startswith(b"\xef\xbb\xbf")
    text = raw_bytes.decode("utf-8-sig")
    line_end = "\r\n" if "\r\n" in text else "\n"
    lines_with_end = re.findall(r"[^\n]*\n|[^\n]+$", text)
    last_old_line_has_end = lines_with_end[end - 1].endswith("\n")
    replaced = [line + line_end for line in new_lines]
    if last_old_line_has_end == False:
        replaced[-1] = replaced[-1][:-len(line_end)]
    lines_with_end[start - 1:end] = replaced
    write_text(path, "".join(lines_with_end), has_bom)


# ════════════════════════════════════════════════════════════════
# 다시 빌드 뒤 반영: deck-edits.json "portfolio" → data/
# ════════════════════════════════════════════════════════════════

def apply_project(project_id):
    """한 프로젝트의 "portfolio" 목록을 data/projects/<폴더>.json·data/portfolio.json에 덮습니다(멱등).

    반환: 보고 줄 목록([참고]·[권장]). 빌드 글이 처음 고칠 때(from)와 달라졌거나 자리를 잃은 줄을 알려 줍니다.
    """
    entries = read_portfolio_entries(ROOT / "src" / project_id / EDITS_FILE_NAME)
    messages = []
    for entry in entries:
        data_path, json_path = entry_location(entry, project_id)
        if json_path is None:
            messages.append(f"[권장] 고친 글 자리({describe_entry(entry)})를 data에서 찾지 못했습니다(다시 빌드로 그 칸이 없어졌거나 절 제목이 바뀜) → 반영하지 않았습니다. "
                            f"편집 모드에서 다시 고치거나 src/{project_id}/{EDITS_FILE_NAME}의 그 줄을 지우세요")
            continue
        current = value_at(read_json(data_path), json_path)
        if current == entry["value"]:
            continue
        is_build_text_changed = current != entry.get("from")
        write_data_value(data_path, json_path, entry["value"])
        if is_build_text_changed:
            messages.append(f"[참고] {describe_entry(entry)}: 빌드가 새 글({current})을 만들었지만 고친 글로 덮었습니다. 빌드 글을 쓰려면 src/{project_id}/{EDITS_FILE_NAME}의 그 줄을 지우세요")
        else:
            messages.append(f"[참고] {describe_entry(entry)}: 고친 글을 반영했습니다")
    return messages


def entry_location(entry, project_id):
    """"portfolio" 줄 하나 → (data 파일 경로, JSON 경로). 자리를 잃었으면 JSON 경로가 None."""
    if entry.get("field") == "skill.evidence":
        return PORTFOLIO_DATA_PATH, skill_evidence_json_path(entry, project_id, read_json(PORTFOLIO_DATA_PATH))
    data_path = project_data_path(project_id)
    return data_path, project_json_path(entry, read_json(data_path))


def describe_entry(entry):
    """보고 줄에 쓸 자리 이름(예: `highlight.what §3.3 …`)."""
    place = entry.get("label") or entry.get("section") or (f"{entry.get('skill')} {entry.get('n')}번째" if entry.get("skill") else "")
    return f"{entry.get('field')} {place}".strip()


# ════════════════════════════════════════════════════════════════
# 발표 덱 장면 글: 제목 · 도입 · 요점 · 키워드 → deck-edits.json "deckText" (+ 기술 문서 줄)
# ════════════════════════════════════════════════════════════════

DECK_TEXT_LIST = "deckText"
DECK_TEXT_FIELDS = ("title", "lead", "point", "keywords")
DECK_KEYWORDS_MAX = 3                                              # deck-edits.py 검사와 같은 개수 상한


def save_deck_text(request):
    """발표 덱 편집 모드의 장면 글 한 칸 저장(또는 저장 위치 묻기). edit-server.py의 /__edit/deck이 부릅니다.

    request: { project, slide: 장면 key, field: title | lead | point | keywords, n: 요점 번호(point만, 1부터),
               expected: 화면을 그릴 때의 값, value: 새 값(keywords는 글자 목록), deckOnly?, dryRun? }
      deckOnly = true면 기술 문서에 같은 글이 있어도 deck-edits.json에만 저장, dryRun = true면 저장 위치만 알려 줌
    반환: { ok, where: 저장 위치 설명, canChooseOverride: 기술 문서를 빼고 발표 덱에만 저장할 수 있는지 }
    """
    project_id = request["project"]
    entry_key = deck_text_entry_key(request)
    data_path = project_data_path(project_id)
    current = deck_text_value(read_json(data_path), entry_key)
    if current != request.get("expected"):
        raise EditError(409, "data가 화면을 그린 뒤에 바뀌었습니다. 새로고침한 뒤 다시 고쳐 주세요.")

    doc_line = choose_deck_doc_line(project_id, entry_key["field"], current, request.get("deckOnly") == True)
    edits_where = f"src/{project_id}/{EDITS_FILE_NAME}"
    if doc_line is None:
        where = f"{edits_where} (발표 덱에만)"
    else:
        where = f"src/{project_id}/{project_doc_path(project_id).name} {doc_line}줄 (기술 문서) + {edits_where}"
    answer = {"ok": True, "where": where, "canChooseOverride": doc_line is not None}
    if request.get("dryRun") == True:
        return answer

    new_value = clean_deck_text_value(entry_key["field"], request.get("value"))
    if new_value == current:
        return answer
    if doc_line is not None:
        doc_path = project_doc_path(project_id)
        old_line = read_lines(doc_path)[doc_line - 1]
        replace_lines(doc_path, doc_line, doc_line, [old_line.replace(current, new_value, 1)])
    write_deck_text_entry(ROOT / "src" / project_id / EDITS_FILE_NAME, entry_key, current, new_value)
    write_deck_text_to_data(data_path, entry_key, new_value)
    return answer


def deck_text_entry_key(request):
    """요청 → "deckText" 자리 이름 { slide, field, n? }. 장면 key는 다시 빌드해도 대개 그대로라 자리 이름으로 씁니다."""
    field = request.get("field")
    if field not in DECK_TEXT_FIELDS:
        raise EditError(400, f"모르는 장면 글 칸입니다: {field}")
    entry_key = {"slide": str(request.get("slide", "")), "field": field}
    if field == "point":
        entry_key["n"] = int(request["n"])
    return entry_key


def deck_text_value(data, entry_key):
    """장면 글의 지금 값(deck.slides 쪽). 장면이나 칸이 없으면 404."""
    slides_path, _ = deck_text_json_paths(data, entry_key)
    if slides_path is None:
        raise EditError(404, f"발표 덱에서 장면 '{entry_key['slide']}'의 {entry_key['field']} 칸을 찾지 못했습니다. 새로고침해 주세요.")
    return value_at(data, slides_path)


def deck_text_json_paths(data, entry_key):
    """장면 글 자리 → (deck.slides 안 JSON 경로, deck.base 안 JSON 경로). 없으면 그 쪽이 None.

    base(장면 고치기 전 빌드 장면)에도 쓰는 이유: deck-edits.py apply가 base에서 장면을 다시 만들므로, base에 없으면 /portfolio-edit 뒤 고친 글이 사라짐.
    """
    deck = data.get("deck") if isinstance(data.get("deck"), dict) else {}
    found = {"slides": None, "base": None}
    for list_name in found:
        for index, slide in enumerate(deck.get(list_name) or []):
            if slide.get("key") != entry_key["slide"]:
                continue
            path_in_slide = slide_text_path(slide, entry_key)
            if path_in_slide is not None:
                found[list_name] = ["deck", list_name, index] + path_in_slide
            break
    return found["slides"], found["base"]


def slide_text_path(slide, entry_key):
    """장면 하나 안에서 글 칸의 JSON 경로. 글은 { text, verbatim } 글 조각 또는 글자(schema.md 「글 조각」). 빈 칸(화면에 안 나옴)이면 None."""
    field = entry_key["field"]
    if field == "keywords":
        return ["keywords"] if isinstance(slide.get("keywords"), list) else None
    if field == "point":
        points = slide.get("points") or []
        number = entry_key["n"]
        is_known_point = 1 <= number <= len(points) and isinstance(points[number - 1], dict)
        return ["points", number - 1, "text"] if is_known_point else None
    piece = slide.get(field)
    if isinstance(piece, dict) and isinstance(piece.get("text"), str) and piece["text"] != "":
        return [field, "text"]
    if isinstance(piece, str) and piece != "":
        return [field]
    return None


def choose_deck_doc_line(project_id, field, current, deck_only):
    """장면 글을 기술 문서에도 쓸 줄 번호. 키워드·발표 덱에만 저장·문서에 정확히 한 번 없는 글(제목 줄·코드 블록 제외)이면 None."""
    is_doc_candidate = field != "keywords" and deck_only == False
    if is_doc_candidate == False:
        return None
    return find_doc_line(project_doc_path(project_id), current)


def clean_deck_text_value(field, value):
    """새 값을 칸 모양에 맞춥니다. 키워드는 빈 칸을 뺀 낱말 0~3개, 나머지는 빈 글이 아닌 한 줄 글자."""
    if field == "keywords":
        keywords = [str(word).strip() for word in value if str(word).strip() != ""]
        if len(keywords) > DECK_KEYWORDS_MAX:
            raise EditError(400, f"키워드는 {DECK_KEYWORDS_MAX}개까지입니다.")
        return keywords
    text = str(value).replace("\r\n", "\n").strip()
    if text == "":
        raise EditError(400, "빈 글은 저장할 수 없습니다. 장면을 숨기려면 /portfolio-edit를 쓰세요.")
    if "\n" in text:
        raise EditError(400, "이 칸은 한 줄 글입니다. 줄바꿈을 빼고 다시 저장해 주세요.")
    return text


def write_deck_text_entry(edits_path, entry_key, current, new_value):
    """deck-edits.json의 "deckText" 목록에 고친 글을 넣습니다. 같은 자리 줄이 있으면 값만 바꾸고, 빌드 글(from)로 되돌렸으면 그 줄을 지웁니다."""
    edits = read_json(edits_path) if edits_path.exists() else {"schemaVersion": 1, "pin": [], "hide": []}
    entries = edits.get(DECK_TEXT_LIST) if isinstance(edits.get(DECK_TEXT_LIST), list) else []
    entry = find_entry(entries, entry_key)
    if entry is None:
        entry = dict(entry_key, value=new_value, **{"from": current})
        entries.append(entry)
    else:
        entry["value"] = new_value
    if entry["value"] == entry["from"]:
        entries.remove(entry)
    edits[DECK_TEXT_LIST] = entries if entries != [] else None
    write_text(edits_path, deck_edits_module().dump(edits) + "\n")


def write_deck_text_to_data(data_path, entry_key, new_value):
    """data의 deck.slides·deck.base 두 곳에 새 글을 쓰고, 장면 고치기 표시(deck.applied)를 새 장면 목록에 맞춥니다.

    글 조각의 verbatim(원문 그대로)은 false로 바꿉니다. 사용자가 고친 글은 원문 그 절에 그대로 있다고 장담할 수 없고,
    true로 남기면 나중에 /portfolio-edit로 그 장면을 고정할 때 deck-edits.py 검사("원문 그 절에 그대로 없음")에 걸립니다.
    """
    slides_path, base_path = deck_text_json_paths(read_json(data_path), entry_key)
    for json_path in (slides_path, base_path):
        if json_path is None:
            continue
        if value_at(read_json(data_path), json_path) != new_value:
            write_data_value(data_path, json_path, new_value)
        mark_not_verbatim(data_path, json_path)
    refresh_applied_digest(data_path)


def mark_not_verbatim(data_path, text_path):
    """text_path가 글 조각의 text(…, "text")이고 그 조각이 verbatim: true면 false로 바꿉니다. 키워드·글자 칸은 그대로."""
    is_piece_text = text_path[-1] == "text"
    if is_piece_text == False:
        return
    verbatim_path = text_path[:-1] + ["verbatim"]
    piece = value_at(read_json(data_path), text_path[:-1])
    if piece.get("verbatim") == True:
        write_data_value(data_path, verbatim_path, False)


def refresh_applied_digest(data_path):
    """deck.applied(= deck-edits.py가 마지막으로 만든 장면 목록의 해시)를 지금 deck.slides에 맞춥니다.

    맞추지 않으면 다음 deck-edits.py apply가 "slides를 누가 손으로 고쳤다"고 보고 고친 장면까지 빌드 장면(base)으로 삼습니다.
    """
    deck = read_json(data_path).get("deck") or {}
    if "applied" not in deck:
        return
    new_digest = deck_edits_module().digest(deck.get("slides") or [])
    if deck["applied"] != new_digest:
        write_data_value(data_path, ["deck", "applied"], new_digest)


def apply_deck_text(project_id):
    """한 프로젝트의 "deckText" 목록을 data/projects/<폴더>.json의 발표 덱에 덮습니다(멱등). deck-edits.py apply 뒤에 실행합니다.

    반환: 보고 줄 목록([참고]·[권장]).
    """
    edits_path = ROOT / "src" / project_id / EDITS_FILE_NAME
    entries = read_json(edits_path).get(DECK_TEXT_LIST) if edits_path.exists() else None
    if isinstance(entries, list) == False:
        return []
    data_path = project_data_path(project_id)
    messages = []
    for entry in entries:
        place = f"장면 {entry.get('slide')} {entry.get('field')}{' ' + str(entry.get('n')) if entry.get('n') else ''}"
        slides_path, base_path = deck_text_json_paths(read_json(data_path), entry)
        if slides_path is None:
            messages.append(f"[권장] 발표 덱 고친 글({place})의 장면을 찾지 못했습니다(다시 빌드로 장면 key가 바뀌었거나 숨김) → 반영하지 않았습니다. "
                            f"편집 모드에서 다시 고치거나 src/{project_id}/{EDITS_FILE_NAME}의 그 줄을 지우세요")
            continue
        data = read_json(data_path)
        current = value_at(data, slides_path)
        is_slides_done = current == entry["value"]
        is_base_done = base_path is None or value_at(data, base_path) == entry["value"]
        if is_slides_done and is_base_done:
            continue
        write_deck_text_to_data(data_path, entry, entry["value"])
        if current != entry.get("from"):
            messages.append(f"[참고] {place}: 빌드가 새 글({current})을 만들었지만 고친 글로 덮었습니다. 빌드 글을 쓰려면 src/{project_id}/{EDITS_FILE_NAME}의 그 줄을 지우세요")
        else:
            messages.append(f"[참고] {place}: 고친 글을 반영했습니다")
    return messages


# ════════════════════════════════════════════════════════════════
# 작은 도구
# ════════════════════════════════════════════════════════════════

def read_portfolio_entries(edits_path):
    """deck-edits.json의 "portfolio" 목록(없거나 null이면 빈 목록)."""
    if edits_path.exists() == False:
        return []
    entries = read_json(edits_path).get("portfolio")
    return entries if isinstance(entries, list) else []


def find_entry(entries, entry_key):
    """같은 자리(field·label·section·skill·n이 모두 같은) 줄. 없으면 None."""
    for entry in entries:
        is_same_place = all(entry.get(name) == value for name, value in entry_key.items())
        if is_same_place:
            return entry
    return None


def project_data_path(project_id):
    data_path = ROOT / "data" / "projects" / f"{project_id}.json"
    is_inside_data = (ROOT / "data" / "projects").resolve() == data_path.resolve().parent
    if is_inside_data == False or data_path.is_file() == False:
        raise EditError(404, f"data/projects/{project_id}.json이 없습니다.")
    return data_path


def project_doc_path(project_id):
    return ROOT / "src" / project_id / read_json(project_data_path(project_id)).get("doc", "")


def value_at(data, json_path):
    value = data
    for step in json_path:
        value = value[step]
    return value


def set_value_at(data, json_path, new_value):
    value_at(data, json_path[:-1])[json_path[-1]] = new_value


def read_json(path):
    return json.loads(path.read_bytes().decode("utf-8-sig"))


def read_lines(path):
    return path.read_bytes().decode("utf-8-sig").replace("\r\n", "\n").split("\n")


def write_text(path, text, has_bom=False):
    with open(str(path), "w", encoding="utf-8-sig" if has_bom else "utf-8", newline="") as file:
        file.write(text)


def deck_edits_module():
    """deck-edits.json을 /portfolio-edit와 같은 줄 모양으로 쓰려고(dump), 장면 목록 해시를 같은 방식으로 재려고(digest) deck-edits.py를 빌려 씁니다."""
    spec = importlib.util.spec_from_file_location("deck_edits", str(Path(__file__).resolve().parent / "deck-edits.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    """`apply <폴더>… | --all`: 프로젝트마다 고친 글(포트폴리오 문서 → 발표 덱 순)을 data/에 반영하고 보고 줄을 찍습니다."""
    arguments = sys.argv[1:]
    if arguments[:1] != ["apply"] or len(arguments) < 2:
        print(__doc__)
        sys.exit(2)
    project_ids = arguments[1:]
    if project_ids == ["--all"]:
        project_ids = sorted(path.stem for path in (ROOT / "data" / "projects").glob("*.json"))
    for project_id in project_ids:
        messages = apply_project(project_id) + apply_deck_text(project_id)
        if messages == []:
            continue
        print(f"── {project_id} (포트폴리오 문서·발표 덱 고친 글) ──")
        for message in messages:
            print("  " + message)


if __name__ == "__main__":
    main()
