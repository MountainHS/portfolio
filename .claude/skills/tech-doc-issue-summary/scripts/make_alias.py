"""원천 별칭표 초안 만들기: 원천 폴더(또는 파일)를 받아 `별칭<TAB>절대경로` TSV 초안을 출력한다. 읽기만 한다.

왜: 분류 파일 대응표의 위치 표기(`R2:L86`)와 check_quotes.py는 별칭표가 있어야 동작한다.
지금까지는 손으로 써서 오타와 빠진 파일이 생겼다.

쓰는 법:
  python make_alias.py <원천 폴더 또는 파일> [...] [--td src/<폴더>/<문서>.md] [-o <접두어>-alias.tsv]

규칙:
  - .md·.txt → R1, R2, … (경로 순서)
  - .csv → CSV, CSV2, …   .pdf → PDF, PDF2, …   .json → JSON, …   그 밖의 글 파일은 건너뜀
  - --td 로 준 프로젝트 문서 → TD
  - 내용이 같은 파일(md5)은 첫 파일만 별칭을 받고, 나머지는 stderr에 "중복"으로 알린다(분류 파일 §8 단계 A에 적을 것).
  - `_`나 `.`으로 시작하는 폴더는 건너뛴다.
출력은 초안이다. 사용자가 고른 원천만 남기고, 별칭을 의미 있는 이름(PDF발표 등)으로 바꿔도 된다.
"""
import argparse
import hashlib
import sys
from pathlib import Path

KINDS = {".md": "R", ".txt": "R", ".csv": "CSV", ".pdf": "PDF", ".json": "JSON"}


def walk(root: Path):
    if root.is_file():
        yield root
        return
    for p in sorted(root.rglob("*")):
        if any(part.startswith(("_", ".")) for part in p.relative_to(root).parts[:-1]):
            continue
        if p.is_file():
            yield p


def md5(p: Path) -> str:
    h = hashlib.md5()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    sys.stderr.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sources", nargs="+")
    ap.add_argument("--td", help="현재 프로젝트 문서(대조 대상) 경로")
    ap.add_argument("-o", "--out", help="TSV 출력 파일(없으면 표준 출력)")
    a = ap.parse_args()

    counts, seen, rows = {}, {}, []
    for src in a.sources:
        root = Path(src).resolve()
        if not root.exists():
            print(f"[없음] {src}", file=sys.stderr)
            continue
        for p in walk(root):
            kind = KINDS.get(p.suffix.lower())
            if not kind:
                continue
            digest = md5(p)
            if digest in seen:
                print(f"[중복] {p} = {seen[digest]}", file=sys.stderr)
                continue
            counts[kind] = counts.get(kind, 0) + 1
            n = counts[kind]
            alias = f"R{n}" if kind == "R" else (kind if n == 1 else f"{kind}{n}")
            seen[digest] = alias
            rows.append((alias, p.as_posix()))
    if a.td:
        rows.append(("TD", Path(a.td).resolve().as_posix()))

    text = "".join(f"{al}\t{path}\n" for al, path in rows)
    if a.out:
        Path(a.out).write_text(text, encoding="utf-8")
        print(f"{len(rows)}개 별칭 → {a.out}", file=sys.stderr)
    else:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stdout.write(text)


if __name__ == "__main__":
    main()
