"""프로젝트 소개 영상 렌더러.

사용법:  python video/render.py hive   (또는 wgo)
         python video/render.py hive-short   (쇼츠: 세로 1080×1920, 가운데 게임 화면 + 위 게임 이름 + 아래 자막)
읽는 것: video/<proj>-cuts.tsv (자르기·배속·루프 배지·강조 박스·mute=Y 소리 끔)
         video/<proj>-subtitles.tsv (자막 — 원본 영상 시각 기준, 사용자가 고침)
결과:    video/out/<proj>.mp4

자막 시각은 원본 영상 기준이므로, 배속을 바꿔도 자막 파일은 고칠 필요가 없습니다.
"""
import csv
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent
SOURCES = {
    "hive": {"main": REPO / "src/5-hive-purge/raw-video/Hive Purge raw-video.mp4"},
    "wgo": {
        "guest": REPO / "src/4-wgo/raw-video/게스트_홍산.mp4",
        "host": REPO / "src/4-wgo/raw-video/호스트_홍산.mp4",
    },
}
W, H, FPS = 1920, 1080, 30
# 원본에서 창 제목 표시줄·테두리를 빼고 남길 게임 화면 (ffmpeg crop=가로:세로:왼쪽:위). 없는 프로젝트는 자르지 않음
# hive: 원본 1922×1112 = 위 제목 표시줄 31줄 + 좌우·아래 테두리 1픽셀 (2026-11-21 측정, 영상 전체 같음)
CROPS = {"hive": "crop=1920:1080:1:31:exact=1"}  # exact=1: 홀수 좌표(1·31)를 짝수로 내리지 않게 (안 쓰면 제목 표시줄 1줄·왼쪽 선이 남음)
FONT = "Malgun Gothic"
FADE = 0.25  # 컷마다 앞뒤 페이드(초) — 배속 대신 컷 사이를 자연스럽게 잇기

# ── 쇼츠(세로) ──
# 프로젝트 이름 끝이 -short면 같은 원본(hive·wgo)을 세로 화면으로 렌더. 컷·자막 표는 <proj>-short-*.tsv
SHORT_SUFFIX = "-short"
SHORT_W, SHORT_H = 1080, 1920
SHORT_GAME_H = 608  # 가운데 게임 화면 높이 = 1080 × 9/16 (짝수로 올림)
SHORT_GAME_TOP = (SHORT_H - SHORT_GAME_H) // 2
SHORT_TITLES = {"hive": "Hive Purge", "wgo": "We Got One"}  # 위 띠에 영상 내내 보이는 게임 이름


def base_project(proj: str) -> str:
    """쇼츠 이름(wgo-short)에서 원본 프로젝트 이름(wgo)을 돌려줍니다. 쇼츠가 아니면 그대로."""
    return proj.removesuffix(SHORT_SUFFIX)


def is_short(proj: str) -> bool:
    """세로 쇼츠로 렌더할 프로젝트인지 돌려줍니다."""
    return proj.endswith(SHORT_SUFFIX)


def parse_time(text: str) -> float:
    parts = [float(p) for p in text.strip().split(":")]
    seconds = 0.0
    for part in parts:
        seconds = seconds * 60 + part
    return seconds


def read_tsv(path: Path) -> list[dict]:
    with path.open(encoding="utf-8-sig", newline="") as f:
        return [row for row in csv.DictReader(f, delimiter="\t") if row.get(next(iter(row)), "").strip()]


def ass_time(seconds: float) -> str:
    cs = int(round(seconds * 100))
    return f"{cs // 360000}:{cs // 6000 % 60:02d}:{cs // 100 % 60:02d}.{cs % 100:02d}"


def build_cuts(proj: str) -> list[dict]:
    cuts = []
    offset = 0.0
    for row in read_tsv(ROOT / f"{proj}-cuts.tsv"):
        start, end = parse_time(row["start"]), parse_time(row["end"])
        speed = float(row["speed"] or 1)
        duration = (end - start) / speed
        cuts.append({**row, "t0": start, "t1": end, "spd": speed, "out0": offset, "dur": duration})
        offset += duration
    return cuts


def render_segment(cut: dict, src: Path, out: Path, crop: str, short: bool) -> None:
    fade_out = max(cut["dur"] - FADE, 0)
    # 게임 화면 크기: 가로 영상은 화면 전체, 쇼츠는 가운데 띠 (강조 박스도 이 크기 기준)
    game_w, game_h = (SHORT_W, SHORT_GAME_H) if short else (W, H)
    filters = [crop] if crop else []
    filters += [
        f"setpts=(PTS-STARTPTS)/{cut['spd']}",
        f"scale={game_w}:{game_h}:force_original_aspect_ratio=decrease",
        f"pad={game_w}:{game_h}:(ow-iw)/2:(oh-ih)/2",
        f"fps={FPS}",
    ]
    filters += [f"fade=t=in:d={FADE}", f"fade=t=out:st={fade_out:.3f}:d={FADE}"]
    # 배속 소리: rubberband(음높이 유지 + 순간음 보존). atempo(WSOLA)는 0.5배에서 타격음이 두 번 들리고 떨림 (사용자 결정 11-21)
    stretch = "" if cut["spd"] == 1 else (
        f"rubberband=tempo={cut['spd']:g}:transients=crisp:detector=percussive:phase=independent:window=standard:pitchq=quality,"
    )
    # 소리 길이를 컷 길이에 딱 맞춰 영상과 어긋나지 않게
    audio = f"{stretch}apad,atrim=0:{cut['dur']:.3f},afade=t=in:d={FADE},afade=t=out:st={fade_out:.3f}:d={FADE}"
    if cut.get("mute", "").strip() == "Y":
        audio += ",volume=0"  # 타이틀(메인 메뉴) 슬로만 소리 끔. 게임 장면 슬로는 인게임 소리 유지 (사용자 결정 11-21)
    if cut.get("box", "").strip():
        x, y, w, h = (float(v) for v in cut["box"].split(","))
        thickness = 4 if short else 6
        filters.append(
            f"drawbox=x={x * game_w:.0f}:y={y * game_h:.0f}:w={w * game_w:.0f}:h={h * game_h:.0f}:color=yellow@0.9:t={thickness}"
        )
    if short:
        filters.append(f"pad={SHORT_W}:{SHORT_H}:0:{SHORT_GAME_TOP}:color=black")  # 게임 띠 위아래를 검은 바탕으로
    subprocess.run([
        "ffmpeg", "-y", "-v", "error", "-ss", str(cut["t0"]), "-to", str(cut["t1"]), "-i", str(src),
        "-vf", ",".join(filters), "-af", audio, "-c:a", "aac", "-b:a", "160k", "-ar", "48000", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
        "-pix_fmt", "yuv420p", str(out),
    ], check=True)


def write_ass(proj: str, cuts: list[dict], path: Path) -> None:
    short = is_short(proj)
    play_w, play_h = (SHORT_W, SHORT_H) if short else (W, H)
    if short:
        # 쇼츠: 자막은 게임 띠 바로 아래(위 정렬 8), 게임 이름은 게임 띠 위 검은 바탕 가운데
        sub_style = f"Style: Sub,{FONT},60,&H00FFFFFF,&H00000000,&H00000000,1,8,50,50,{SHORT_GAME_TOP + SHORT_GAME_H + 70},1,4,0"
        title_style = f"Style: Title,{FONT},96,&H0000E6FF,&H00000000,&H00000000,1,8,40,40,{SHORT_GAME_TOP - 230},1,5,0"
    else:
        sub_style = f"Style: Sub,{FONT},52,&H00FFFFFF,&H00000000,&H99000000,1,2,120,120,70,3,8,0"
        title_style = ""
    lines = [
        "[Script Info]", "ScriptType: v4.00+", f"PlayResX: {play_w}", f"PlayResY: {play_h}", "",
        "[V4+ Styles]",
        "Format: Name, Fontname, Fontsize, PrimaryColour, OutlineColour, BackColour, Bold, Alignment, "
        "MarginL, MarginR, MarginV, BorderStyle, Outline, Shadow",
        sub_style,
        f"Style: Loop,{FONT},38,&H0000E6FF,&H00000000,&H99000000,1,7,40,40,36,3,6,0",
        f"Style: Speed,{FONT},34,&H00FFFFFF,&H00000000,&H99000000,1,9,40,40,36,3,6,0",
    ]
    if title_style:
        lines.append(title_style)
    lines += ["", "[Events]", "Format: Layer, Start, End, Style, Text"]
    if short:
        total = sum(cut["dur"] for cut in cuts)
        lines.append(f"Dialogue: 0,{ass_time(0)},{ass_time(total)},Title,{SHORT_TITLES[base_project(proj)]}")
    # 왼쪽 위 루프 배지(라운드 전투 등)·오른쪽 위 배속 표시는 그리지 않음 (사용자 결정 11-21). cuts 표 loop 칸은 메모로만 남음
    by_id = {cut["id"]: cut for cut in cuts}
    for sub in read_tsv(ROOT / f"{proj}-subtitles.tsv"):
        if sub["text"].strip() == "":
            continue  # 자막 생략 줄
        cut = by_id.get(sub["cut"].strip())
        if not cut:
            print(f"[경고] 자막의 cut '{sub['cut']}'가 cuts 표에 없음 — 건너뜀")
            continue
        s = max(parse_time(sub["start"]), cut["t0"])
        e = min(parse_time(sub["end"]), cut["t1"])
        if e <= s:
            print(f"[경고] {sub['cut']} 자막 시각이 구간 밖 — 건너뜀")
            continue
        out_s = cut["out0"] + (s - cut["t0"]) / cut["spd"]
        out_e = cut["out0"] + (e - cut["t0"]) / cut["spd"]
        lines.append(f"Dialogue: 1,{ass_time(out_s)},{ass_time(out_e)},Sub,{sub['text'].strip()}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8-sig")


def main() -> None:
    proj = sys.argv[1] if len(sys.argv) > 1 else ""
    base = base_project(proj)
    if base not in SOURCES:
        sys.exit(f"사용법: python video/render.py [{'|'.join(SOURCES)}] (쇼츠는 이름 뒤에 {SHORT_SUFFIX})")
    from check_subtitles import check_project  # 서로 import하므로 여기서 불러옴
    if check_project(proj, ROOT / f"{proj}-subtitles.tsv") > 0:
        sys.exit("자막 검사 오류가 있어 렌더를 멈춥니다. 위 [오류]를 고친 뒤 다시 실행하세요.")
    out_dir = ROOT / "out"
    seg_dir = out_dir / f"_seg_{proj}"
    seg_dir.mkdir(parents=True, exist_ok=True)
    cuts = build_cuts(proj)
    for cut in cuts:
        seg = seg_dir / f"{cut['id']}.mp4"
        print(f"{cut['id']}  {cut['start']}~{cut['end']}  x{cut['spd']:g}  -> {cut['dur']:.1f}s")
        render_segment(cut, SOURCES[base][cut["src"].strip()], seg, CROPS.get(base, ""), is_short(proj))
    (seg_dir / "list.txt").write_text("".join(f"file '{c['id']}.mp4'\n" for c in cuts), encoding="utf-8")
    write_ass(proj, cuts, out_dir / f"{proj}.ass")
    subprocess.run([
        "ffmpeg", "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", f"_seg_{proj}/list.txt",
        "-vf", f"subtitles={proj}.ass", "-c:v", "libx264", "-preset", "medium", "-crf", "20",
        "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", f"{proj}.mp4",
    ], check=True, cwd=out_dir)
    total = sum(c["dur"] for c in cuts)
    print(f"완료: video/out/{proj}.mp4  ({total // 60:.0f}분 {total % 60:.0f}초)")


if __name__ == "__main__":
    main()
