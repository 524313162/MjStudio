#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
配音.py —— 第9步「配音」环节一键脚本（配音吧）
=====================================================================
对当前集的每一个分镜视频（05_视频\第N集\视频X.mp4）：
  1. 提取音轨，用 faster-whisper 离线转写，实测该镜台词在视频内的【真实起止时间】
     （以实测为准，与台词清单 start_sec 估算值不同没关系）；
  2. 按真实时间把该镜原声台词段裁剪出来；
  3. 调 32 号工作流（FL-CosyVoice3-音频声音转换，见 voice_convert.py 封装）
     把裁剪出的原声台词段音色换成对应角色的声线（06_音频\声线\<角色>_种子.wav）；
  4. 生成「只有台词声音」的配音版分镜视频：整段原声全部去掉（静音），
     把换好音色的台词声贴回原真实时间位置，得到 视频X_配音.mp4；
  5. 每句换音色后的台词音频另存 06_音频\第N集\<file>（台词清单 file 字段）。
衔接视频（videoN-(N+1).mp4）已取消（2026-09-07 用户决策），不再处理衔接片段。

以后第9步配音一律调用本脚本，禁止临场手搓 ffmpeg / 逐个填 32 号工作流。

用法（命令行）:
    python 配音.py --project "<项目根>" --episode N [选项]

参数:
    --project <path>     必填。项目根目录（含 00_项目 / 03_分镜 / 05_视频 / 06_音频）。
    --episode N          必填。第N集。
    --voice-dir <path>   可选。声线目录（默认 <项目>\06_音频\声线）。
    --asr-model <name>   可选。faster-whisper 模型，默认 small。
    --asr-python <path>  可选。faster-whisper venv 的 python（默认 D:\\faster_whisper\\venv\\Scripts\\python.exe）。
    --asr-script <path>  可选。转写脚本（默认 D:\\faster_whisper\\transcribe_timestamps.py）。
    --out-suffix <str>   可选。配音版视频后缀，默认 _配音。
    --padding <sec>      可选。裁剪台词段前后各多留秒数，默认 0.2。
    --keep-temp          可选。保留 temp 下中间音频不清理。
    --temp-dir <path>    可选。中间产物目录（默认 <项目>\05_视频\第N集\temp\配音）。
    --dry-run            可选。只打印处理计划与 ASR 定位结果，不裁剪/不调工作流。
    --comfy-input/--comfy-output/--server  可选。ComfyUI 参数（透传给 voice_convert.py）。

退出码: 0 = 全部完成；1 = 部分镜头失败；2 = 致命错误（无有效镜头可处理）。

标准输出（最后一行）:
    JSON 单行结果: {"status":"success|partial|error","episode":N,
                    "shots_ok":n,"shots_failed":[...],"outputs":[...]}
"""
import argparse
import difflib
import json
import os
import re
import shutil
import subprocess
import sys
import time

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
VOICE_CONVERT = os.path.join(SCRIPT_DIR, "voice_convert.py")
DEFAULT_ASR_PYTHON = r"D:\faster_whisper\venv\Scripts\python.exe"
DEFAULT_ASR_SCRIPT = r"D:\faster_whisper\transcribe_timestamps.py"
DEFAULT_ASR_MODEL_ROOT = r"D:\faster_whisper\models"
_ASR_MODEL_CACHE = {}

CN = re.compile(r"[\s，。！？、,!?!：:；;\"'“”‘’()（）\-—…~·]")


def norm_text(s):
    """去除标点空白，仅保留中文/字母/数字，用于 ASR 匹配。"""
    return re.sub(CN, "", str(s or ""))


def run(cmd, timeout=7200):
    """执行命令，返回 (returncode, stdout, stderr)。"""
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    return p.returncode, p.stdout or "", p.stderr or ""


def ffmpeg_bin():
    w = shutil.which("ffmpeg")
    if w:
        return w
    return "ffmpeg"


def probe_duration(video, ffmpeg):
    """用 ffprobe 取视频时长（秒）。"""
    ffprobe = ffmpeg.replace("ffmpeg", "ffprobe")
    if not shutil.which(ffprobe):
        ffprobe = "ffprobe"
    try:
        out = subprocess.run(
            [ffprobe, "-v", "error", "-show_entries", "format=duration",
             "-of", "default=noprint_wrappers=1:nokey=1", video],
            capture_output=True, text=True, timeout=60)
        return float(out.stdout.strip())
    except Exception:
        return None


def match_line_span(segs, text):
    """
    在 ASR segments 中定位一句台词的真实时间跨度。
    台词可能被 ASR 切分成多段（如 [0.66~2.66][2.66~4.66]），
    这里收集所有文本命中该台词的相邻段，合并取最小 start ~ 最大 end。
    返回 {"start":..,"end":..} 或 None（无法定位）。
    """
    tgt = norm_text(text)
    if not tgt:
        return None
    hits = []
    for s in segs:
        src = norm_text(s.get("text", ""))
        if not src:
            continue
        r = difflib.SequenceMatcher(None, tgt, src).ratio()
        if tgt in src or src in tgt:
            r = max(r, 0.85)
        if r >= 0.3:
            hits.append(s)
    if not hits:
        return None
    # 段之间小间隙（<=1.5s）视为连续，取最小 start ~ 最大 end
    ordered = sorted(hits, key=lambda s: s["start"])
    start = ordered[0]["start"]
    end = ordered[-1]["end"]
    for i in range(1, len(ordered)):
        if ordered[i]["start"] - ordered[i - 1]["end"] > 1.5:
            # 出现明显间隙：仅保留主段之前的连续部分，避免误并不相关段
            end = ordered[i - 1]["end"]
            break
    return {"start": float(start), "end": float(end)}


def extract_track(video, out_wav, ffmpeg, mono16k=False):
    """提取视频音轨到 wav。mono16k=True 用于 ASR；否则 44.1k 立体声用于裁剪。"""
    cmd = [ffmpeg, "-y", "-i", video, "-vn"]
    if mono16k:
        cmd += ["-ac", "1", "-ar", "16000"]
    else:
        cmd += ["-ac", "2", "-ar", "44100"]
    cmd += ["-c:a", "pcm_s16le", out_wav]
    rc, _, err = run(cmd)
    if rc != 0:
        raise RuntimeError(f"提取音轨失败: {err[-500:]}")
    return out_wav


def get_asr_model(model):
    """加载 faster-whisper 模型（模块级缓存，避免逐镜重复加载）。"""
    if model not in _ASR_MODEL_CACHE:
        from faster_whisper import WhisperModel
        local_dir = os.path.join(DEFAULT_ASR_MODEL_ROOT, model)
        path = local_dir if os.path.isdir(local_dir) else model
        _ASR_MODEL_CACHE[model] = WhisperModel(path, device="cpu", compute_type="int8")
    return _ASR_MODEL_CACHE[model]


def asr_segments(audio_wav, model, vad=False):
    """
    faster-whisper 直接转写，返回 segments 列表 [{start,end,text}]。
    默认关闭 VAD：个别镜头台词音量偏低时，Silero VAD 会把整段误滤为静音
    （导致 0 句、无法定位），关闭 VAD 可稳定召回全部台词。
    """
    m = get_asr_model(model)
    segs, info = m.transcribe(audio_wav, language="zh", vad_filter=vad, beam_size=5)
    return [{"start": s.start, "end": s.end, "text": s.text.strip()} for s in segs]


def voice_convert(seed_wav, content_wav, out_dir, out_name, args, seed=None):
    """调 voice_convert.py（32 号工作流封装）换音色，返回输出音频路径。"""
    cmd = [sys.executable, VOICE_CONVERT,
           "--seed-audio", seed_wav,
           "--content-audio", content_wav,
           "--out-dir", out_dir,
           "--out-name", out_name,
           "--comfy-input", args.comfy_input,
           "--comfy-output", args.comfy_output,
           "--server", args.server,
           "--interval", str(args.interval), "--timeout", str(args.timeout),
           "--max-retry", str(args.max_retry)]
    if seed is not None:
        cmd += ["--seed", str(seed)]
    rc, out, err = run(cmd, timeout=args.timeout + 120)
    last = ""
    for ln in reversed((out or "").strip().splitlines()):
        ln = ln.strip()
        if ln.startswith("{"):
            last = ln
            break
    if rc != 0 or not last:
        raise RuntimeError(f"voice_convert 失败 rc={rc}: {(err or out)[-500:]}")
    res = json.loads(last)
    if res.get("status") != "success" or not res.get("audio_path"):
        raise RuntimeError(f"voice_convert 未成功: {last[:400]}")
    return res["audio_path"]


def build_dubbed_video(video, placed, out, ffmpeg):
    """
    placed: list of (start_sec, wav_path)。把视频音轨全静音，台词贴回 start_sec。
    无 placed → 全静音输出。
    """
    os.makedirs(os.path.dirname(out), exist_ok=True)
    if not placed:
        cmd = [ffmpeg, "-y", "-i", video, "-c:v", "copy",
               "-af", "volume=0", "-c:a", "aac", "-b:a", "192k", out]
        rc, _, err = run(cmd)
        if rc != 0:
            raise RuntimeError(f"静音合成失败: {err[-500:]}")
        return out

    cmd = [ffmpeg, "-y", "-i", video]
    fc = ["[0:a]aformat=sample_rates=44100:channel_layouts=stereo,volume=0[a0]"]
    for i, (t, w) in enumerate(placed, start=1):
        cmd += ["-i", w]
        ms = int(round(t * 1000))
        fc.append(f"[{i}:a]aformat=sample_rates=44100:channel_layouts=stereo,adelay={ms}|{ms}[a{i}]")
    mix = "".join(f"[a{j}]" for j in range(len(placed) + 1))
    fc.append(f"{mix}amix=inputs={len(placed) + 1}:duration=first:dropout_transition=0:normalize=0[aout]")
    cmd += ["-filter_complex", ";".join(fc), "-map", "0:v", "-map", "[aout]",
            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", out]
    rc, _, err = run(cmd)
    if rc != 0:
        raise RuntimeError(f"配音版合成失败: {err[-500:]}")
    return out


def main():
    ap = argparse.ArgumentParser(description="第9步配音环节一键脚本（配音吧）")
    ap.add_argument("--project", required=True, help="项目根目录")
    ap.add_argument("--episode", required=True, type=int, help="第N集")
    ap.add_argument("--voice-dir", default=None, help="声线目录（默认 06_音频\\声线）")
    ap.add_argument("--asr-model", default="small", help="faster-whisper 模型，默认 small")
    ap.add_argument("--asr-python", default=os.environ.get("ASR_PYTHON", DEFAULT_ASR_PYTHON))
    ap.add_argument("--asr-script", default=os.environ.get("ASR_SCRIPT", DEFAULT_ASR_SCRIPT))
    ap.add_argument("--asr-vad", action="store_true", help="开启 faster-whisper VAD 过滤（默认关闭，避免轻音量台词被误滤）")
    ap.add_argument("--out-suffix", default="_配音", help="配音版视频后缀，默认 _配音")
    ap.add_argument("--padding", type=float, default=0.2, help="裁剪台词段前后多留秒数，默认0.2")
    ap.add_argument("--keep-temp", action="store_true")
    ap.add_argument("--temp-dir", default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--comfy-input", default=r"D:\Comfy-Desktop\ComfyUI-Shared\input")
    ap.add_argument("--comfy-output", default=r"D:\Comfy-Desktop\ComfyUI-Shared\output")
    ap.add_argument("--server", default="http://127.0.0.1:8188")
    ap.add_argument("--interval", type=int, default=10)
    ap.add_argument("--timeout", type=int, default=1800)
    ap.add_argument("--max-retry", type=int, default=2)
    args = ap.parse_args()

    ffmpeg = ffmpeg_bin()
    start = time.time()
    proj = os.path.abspath(args.project)
    ep = args.episode

    try:
        # ---- 定位台词清单（03_分镜\台词清单.json 优先，其次 第N集台词.json）----
        cl_1 = os.path.join(proj, "03_分镜", "台词清单.json")
        cl_2 = os.path.join(proj, "03_分镜", f"第{ep}集台词.json")
        cl_path = cl_1 if os.path.isfile(cl_1) else (cl_2 if os.path.isfile(cl_2) else None)
        if not cl_path:
            raise FileNotFoundError(f"未找到台词清单: {cl_1} / {cl_2}")
        cl = json.load(open(cl_path, encoding="utf-8"))
        lines = cl.get("lines", [])

        vdir = os.path.join(proj, "05_视频", f"第{ep}集")
        if not os.path.isdir(vdir):
            raise FileNotFoundError(f"视频目录不存在: {vdir}")
        voice_dir = args.voice_dir or os.path.join(proj, "06_音频", "声线")
        out_audio_dir = os.path.join(proj, "06_音频", f"第{ep}集")
        os.makedirs(out_audio_dir, exist_ok=True)
        temp_dir = args.temp_dir or os.path.join(vdir, "temp", "配音")
        os.makedirs(temp_dir, exist_ok=True)

        # ---- 按 shot 归组台词，收集镜头号 ----
        by_shot = {}
        for ln in lines:
            by_shot.setdefault(int(ln.get("shot")), []).append(ln)
        shot_nums = sorted(by_shot)
        # 补充无台词镜头：扫描目录 video*.mp4（衔接片段已取消，无需排除）
        vids = sorted(
            int(f[5:-4]) for f in os.listdir(vdir)
            if re.match(r"^video\d+\.mp4$", f))
        all_shots = sorted(set(shot_nums) | set(vids))

        ok_shots, failed, outputs = [], [], []
        report_lines = []

        # ---- 逐镜处理 ----
        for shot in all_shots:
            video = os.path.join(vdir, f"video{shot}.mp4")
            if not os.path.isfile(video):
                failed.append(f"shot{shot}: 视频不存在")
                continue
            shot_lines = by_shot.get(shot, [])
            out_video = os.path.join(vdir, f"video{shot}{args.out_suffix}.mp4")

            try:
                if not shot_lines:
                    # 无台词镜头：原声全部去掉（静音）
                    if not args.dry_run:
                        build_dubbed_video(video, [], out_video, ffmpeg)
                        outputs.append(out_video)
                    report_lines.append(f"shot{shot}: 无台词 → 静音 {os.path.basename(out_video)}")
                    continue

                # 1) 提取音轨（ASR 用 16k mono；裁剪用 44.1k stereo）
                asr_wav = os.path.join(temp_dir, f"shot{shot}_asr.wav")
                full_wav = os.path.join(temp_dir, f"shot{shot}_full.wav")
                extract_track(video, asr_wav, ffmpeg, mono16k=True)
                extract_track(video, full_wav, ffmpeg, mono16k=False)

                # 2) ASR 实测（默认关闭 VAD，避免轻音量台词被误滤）
                segs = asr_segments(asr_wav, args.asr_model, vad=args.asr_vad)
                if args.dry_run:
                    report_lines.append(f"shot{shot} ASR 段:")
                    for s in segs:
                        report_lines.append(f"    [{s['start']:.2f}~{s['end']:.2f}] {s['text'].strip()}")
                    continue

                # 3) 逐句匹配真实时间 → 裁剪 → 换音色
                placed = []
                for ln in shot_lines:
                    span = match_line_span(segs, ln.get("text", ""))
                    if span is None:
                        raise RuntimeError(f"shot{shot} 台词「{ln.get('text')}」未在 ASR 结果中定位，跳过该镜")
                    t0 = max(0.0, span["start"] - args.padding)
                    t1 = span["end"] + args.padding
                    dur = probe_duration(video, ffmpeg)
                    if dur:
                        t1 = min(t1, dur)

                    content_wav = os.path.join(temp_dir, f"shot{shot}_seq{ln.get('seq')}_src.wav")
                    rc, _, err = run([ffmpeg, "-y", "-ss", f"{t0:.3f}", "-to", f"{t1:.3f}",
                                      "-i", full_wav, "-c:a", "pcm_s16le",
                                      "-ar", "44100", "-ac", "2", content_wav])
                    if rc != 0:
                        raise RuntimeError(f"裁剪台词段失败: {err[-300:]}")

                    # 换音色：声线 = 06_音频\声线\<角色>_种子.wav
                    character = ln.get("character", "")
                    seed_wav = os.path.join(voice_dir, f"{character}_种子.wav")
                    if not os.path.isfile(seed_wav):
                        raise RuntimeError(f"shot{shot} 角色「{character}」声线缺失: {seed_wav}")
                    vc_wav = voice_convert(seed_wav, content_wav, temp_dir,
                                           f"shot{shot}_vc", args)
                    placed.append((span["start"], vc_wav))

                    # 换音色台词 wav 另存 06_音频\第N集\<file>
                    fname = ln.get("file") or f"镜头{shot}_{character}_台词{ln.get('seq')}.wav"
                    dst = os.path.join(out_audio_dir, fname)
                    shutil.copy2(vc_wav, dst)
                    outputs.append(dst)
                    report_lines.append(
                        f"shot{shot} [{span['start']:.2f}~{span['end']:.2f}] {character}: 已换音色 → {fname}")

                # 4) 合成配音版视频（全静音 + 台词贴回）
                build_dubbed_video(video, placed, out_video, ffmpeg)
                outputs.append(out_video)
                ok_shots.append(shot)

            except Exception as e:
                failed.append(f"shot{shot}: {e}")
                report_lines.append(f"shot{shot}: 失败 → {e}")

        # ---- 衔接视频已取消（2026-09-07 用户决策），无衔接片段处理 ----

        for ln in report_lines:
            print(ln)

        status = "success" if not failed else ("partial" if ok_shots else "error")
        result = {"status": status, "episode": ep, "shots_ok": len(ok_shots),
                  "shots_failed": failed, "outputs": outputs,
                  "elapsed_sec": int(time.time() - start)}
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0 if status == "success" else (1 if status == "partial" else 2))

    except Exception as e:
        print(json.dumps({"status": "error", "episode": ep,
                          "error": f"{type(e).__name__}: {e}",
                          "elapsed_sec": int(time.time() - start)}, ensure_ascii=False))
        sys.exit(2)


if __name__ == "__main__":
    main()
