#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
voice_seed_gen.py —— 漫剧配音取声固定化封装脚本（第一步：角色自我介绍短视频 + 提取种子声线）
=====================================================================
根据 13.MiniMaxH3 文生视频.json 工作流模板，只传【角色名】+【文生视频提示词】即可
自动填充节点并用内联提交引擎提交、轮询、取回短视频，再用 ffmpeg 提取音频作为
该角色专属种子声线。以后配音取声一律调用本脚本，禁止临场手写工作流 JSON 或逐个填节点。

用法（命令行）:
    python voice_seed_gen.py --name <角色名> --prompt "<文生视频提示词EN>" [选项]

参数:
    --name <str>        必填。角色名（如 甜甜），用于输出前缀与取回命名。
    --prompt <str>      必填。文生视频提示词（英文，填节点105:104，含角色声音特质+固定台词）。
    --duration <float>  可选。视频时长秒数（填节点105:111），默认 5.0（台词约4.5s+首尾留白）。
    --seed <int>        可选。随机种子（填节点105:15）。不传则随机生成。
    --extract           可选。提交成功后用 ffmpeg 提取音频种子声线（<name>_种子.wav）。
    --audio-dir <path>  可选。种子 wav 落盘目录（如 06_音频\声线），配合 --extract。
    --keep-mp4          可选。保留取声源视频（<name>_自我介绍.mp4），否则仅打印路径不落盘。
    --out-dir <path>    可选。取声源视频落盘目录。不传则只打印源件路径。
    --comfy-output <path> 可选。ComfyUI output 根目录（默认 D:\\Comfy-Desktop\\ComfyUI-Shared\\output）。
    --temp-dir <path>   可选。临时 workflow JSON 写入目录（默认系统临时目录）。
    --dry-run           可选。只构造并打印 workflow 关键节点，不真实提交。
    --server <url>      可选。ComfyUI 地址（默认 http://127.0.0.1:8188）。
    --interval/--timeout/--max-retry  可选。内联提交引擎轮询/重试参数。

退出码:
    0 = 成功（短视频已取回/或已提取种子声线）
    2 = 生成任务最终失败
    3 = 超时
    4 = 参数/模板/取回错误

标准输出（最后一行）:
    JSON 单行结果，字段:
      {
        "status": "success|error|timeout|error_submit|error_arg",
        "name": "...", "duration": n,
        "video_outputs": [...],
        "video_path": "<out-dir>/<name>_自我介绍.mp4" | null,
        "seed_audio": "<audio-dir>/<name>_种子.wav" | null,
        "error": "…", "elapsed_sec": n
      }
"""
import argparse
import json
import os
import random
import shutil
import subprocess
import sys
import tempfile
import time

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# 本脚本已收拢至 ref\step-05\scripts\，工作流同目录上位 workflows\（ref\step-05\workflows\）
WORKFLOW_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "workflows"))
# 提交引擎已内联（自包含，无全局依赖）
DEFAULT_COMFY_OUTPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\output"

# AIGC 声明语音校验（生成端硬拦截）——本地 faster-whisper，可与评审端复用
FASTWHISPER_PY = os.environ.get("FASTWHISPER_PY", r"D:\faster_whisper\venv\Scripts\python.exe")
AIGC_SCAN_SCRIPT = os.path.join(SCRIPT_DIR, "aigc_scan.py")

# 固定节点号（与 ref\step-05\配音.md 13号文生视频取声节点映射一致）
WF_NAME = "13.MiniMaxH3 文生视频.json"
NODE_PROMPT = "105:104"   # MiniMaxH3ImageToVideo prompt
NODE_DURATION = "105:111" # Float (duration) value
NODE_SEED = "105:15"      # RandomNoise noise_seed
NODE_PREFIX = "92"        # SaveVideo filename_prefix


def build_workflow(name, prompt, duration, seed, dry_run=False):
    """读取 13 号模板并填充节点，返回构造好的 workflow dict。"""
    wf_path = os.path.join(WORKFLOW_DIR, WF_NAME)
    if not os.path.isfile(wf_path):
        raise FileNotFoundError(f"工作流模板不存在: {wf_path}")
    with open(wf_path, "r", encoding="utf-8") as fh:
        workflow = json.load(fh)

    for nid in (NODE_PROMPT, NODE_DURATION, NODE_SEED, NODE_PREFIX):
        if nid not in workflow:
            raise KeyError(f"工作流 {WF_NAME} 缺少节点 {nid}")

    # 节点105:104 文生视频提示词
    workflow[NODE_PROMPT]["inputs"]["prompt"] = prompt
    # 节点105:111 时长秒数
    workflow[NODE_DURATION]["inputs"]["value"] = float(duration)
    # 节点105:15 seed
    workflow[NODE_SEED]["inputs"]["noise_seed"] = seed
    # 节点92 输出前缀 video/manju_voice/<name>_自我介绍
    workflow[NODE_PREFIX]["inputs"]["filename_prefix"] = f"video/manju_voice/{name}_自我介绍"

    return workflow


def _http_json(method, url, payload=None, timeout=30):
    """发送 HTTP JSON 请求, 返回解析后的 dict, 失败抛异常。"""
    import urllib.request
    import urllib.error
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        body = resp.read().decode("utf-8")
        if not body:
            return {}
        return json.loads(body)


def _find_output_files(outputs):
    """从 ComfyUI /history/{id} 的 outputs 字典中收集产物文件条目。"""
    files = []
    for node_id, node_out in (outputs or {}).items():
        if not isinstance(node_out, dict):
            continue
        for kind, entries in node_out.items():
            if kind in ("images", "audio", "gifs", "video"):
                for ent in (entries or []):
                    if isinstance(ent, dict) and "filename" in ent:
                        files.append({
                            "node_id": node_id,
                            "type": "video" if kind == "video" else ("image" if kind == "images" else "audio"),
                            "filename": ent.get("filename", ""),
                            "subfolder": ent.get("subfolder", ""),
                            "type_field": ent.get("type", "output"),
                        })
            elif kind == "video" and isinstance(entries, dict) and "filename" in entries:
                files.append({
                    "node_id": node_id,
                    "type": "video",
                    "filename": entries.get("filename", ""),
                    "subfolder": entries.get("subfolder", ""),
                    "type_field": entries.get("type", "output"),
                })
    return files


def submit(workflow, args):
    """写临时 workflow JSON 并内联提交引擎提交（提交→轮询→重试→超时），返回 (exit_code, result_dict)。
    退出码: 0=成功 / 2=最终失败 / 3=超时 / 4=提交/参数错误。
    """
    fd, tmp_path = tempfile.mkstemp(
        prefix=f"voice_seed_{args.name}_", suffix=".json",
        dir=args.temp_dir or None,
    )
    os.close(fd)
    with open(tmp_path, "w", encoding="utf-8") as fh:
        json.dump(workflow, fh, ensure_ascii=False)

    import uuid
    client_id = f"manju-{uuid.uuid4().hex[:12]}"
    server = args.server.rstrip("/")
    start = time.time()
    attempts = 0
    last_error = ""
    rc = 4

    while True:
        attempts += 1
        # 1) 提交
        try:
            sub = _http_json("POST", f"{server}/prompt", {"prompt": workflow, "client_id": client_id}, timeout=60)
        except Exception as e:
            last_error = f"提交失败: {e}"
            if attempts <= args.max_retry:
                print(f"[retry {attempts}/{args.max_retry}] {last_error}", file=sys.stderr)
                time.sleep(args.interval)
                continue
            rc = 4
            break

        prompt_id = sub.get("prompt_id", "")
        # 2) 轮询
        poll_start = time.time()
        while True:
            time.sleep(args.interval)
            try:
                history = _http_json("GET", f"{server}/history/{prompt_id}", timeout=30)
            except Exception as e:
                last_error = f"轮询请求失败: {e}"
                break

            rec = history.get(prompt_id, {})
            status = rec.get("status", {})
            status_str = status.get("status_str", "")
            completed = status.get("completed", False)

            if status_str == "success" and completed:
                outputs = _find_output_files(rec.get("outputs", {}))
                result = {
                    "status": "success",
                    "prompt_id": prompt_id,
                    "outputs": outputs,
                    "attempts": attempts,
                    "elapsed_sec": int(time.time() - start),
                }
                result["_tmp_workflow"] = tmp_path
                return 0, result

            if status_str == "error":
                last_error = json.dumps(rec.get("status", {}), ensure_ascii=False)
                break  # 需要重试或报错

            # completed==false 且非 error → 继续轮询
            if time.time() - poll_start > args.timeout:
                last_error = f"任务超时(>{args.timeout}s)"
                rc = 3
                break

        # 3) 失败处理
        if rc == 0:
            continue
        if attempts <= args.max_retry:
            print(f"[retry {attempts}/{args.max_retry}] status=error: {last_error}", file=sys.stderr)
            time.sleep(args.interval)
            continue
        if rc == 4:
            break
        rc = 2
        break

    result = {
        "status": "error" if rc == 2 else ("timeout" if rc == 3 else "error_submit"),
        "prompt_id": None,
        "error": last_error,
        "attempts": attempts,
        "elapsed_sec": int(time.time() - start),
    }
    result["_tmp_workflow"] = tmp_path
    return rc, result


def fetch_outputs(result, comfy_output_dir):
    """把 outputs 的 filename/subfolder 映射为 output 目录源件绝对路径（取回铁律：源件 Copy-Item）。"""
    fetched = []
    for o in result.get("outputs", []) or []:
        subfolder = o.get("subfolder", "") or ""
        filename = o.get("filename", "")
        if not filename:
            continue
        src = os.path.join(comfy_output_dir, subfolder, filename) if subfolder \
            else os.path.join(comfy_output_dir, filename)
        fetched.append({
            "filename": filename,
            "subfolder": subfolder,
            "source_path": src,
            "exists": os.path.isfile(src),
        })
    return fetched


def copyfile(src, dst):
    """复制源件，核对字节数一致（取回铁律）。"""
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)
    if os.path.getsize(src) != os.path.getsize(dst):
        raise OSError(f"复制后字节数不一致: {src} -> {dst}")


def extract_audio(mp4_path, wav_path):
    """ffmpeg 从短视频提取音频种子声线。"""
    if not shutil.which("ffmpeg"):
        raise RuntimeError("ffmpeg 未在 PATH 中")
    os.makedirs(os.path.dirname(wav_path), exist_ok=True)
    cmd = [
        "ffmpeg", "-y",
        "-i", mp4_path,
        "-vn", "-ac", "1", "-ar", "16000",
        wav_path,
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError(f"ffmpeg 提取音频失败: {proc.stderr[-500:]}")
    if not os.path.isfile(wav_path) or os.path.getsize(wav_path) == 0:
        raise RuntimeError(f"提取音频结果为空: {wav_path}")


def verify_no_aigc(wav_path):
    """生成端硬拦截：ASR 转写种子 wav，若含 AIGC 声明语音 → 删除不合格文件并抛错，要求重新取声。
    用 faster-whisper 的 venv python 跑 aigc_scan.py（内存转写、不落盘），解析其 JSON 结果。"""
    if not os.path.isfile(AIGC_SCAN_SCRIPT):
        raise RuntimeError(f"AIGC 扫描脚本不存在: {AIGC_SCAN_SCRIPT}")
    if not os.path.isfile(FASTWHISPER_PY):
        raise RuntimeError(f"faster-whisper python 不存在: {FASTWHISPER_PY}")
    cmd = [FASTWHISPER_PY, AIGC_SCAN_SCRIPT, wav_path]
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8")
    out = (proc.stdout or "").strip()
    last_line = out.splitlines()[-1] if out else ""
    try:
        scan = json.loads(last_line)
    except Exception:
        raise RuntimeError(
            f"AIGC 校验失败(无法解析扫描结果): 退出码={proc.returncode} stdout={proc.stdout[-300:]} stderr={proc.stderr[-300:]}"
        )
    if not scan.get("ok", False):
        # 命中 AIGC 声明 → 拒绝落盘，删除不合格种子
        if os.path.isfile(wav_path):
            os.remove(wav_path)
        hits = scan.get("hits", [])
        detail = "；".join(
            f"「{h['segment']['text']}」[{h['segment']['start']:.1f}s-{h['segment']['end']:.1f}s]"
            for h in hits[:5]
        )
        raise RuntimeError(
            f"种子 wav 含 AIGC 声明语音，已删除不合格文件，请重新取声生成。命中: {detail}"
        )
    return scan


def main():
    ap = argparse.ArgumentParser(description="漫剧配音取声封装脚本（13号文生视频+ffmpeg提取种子声线）")
    ap.add_argument("--name", required=True, help="角色名")
    ap.add_argument("--prompt", required=True, help="文生视频提示词（英文，节点105:104）")
    ap.add_argument("--duration", type=float, default=5.0, help="视频时长秒数（节点105:111），默认5")
    ap.add_argument("--seed", type=int, default=None, help="随机种子，不传随机生成")
    ap.add_argument("--extract", action="store_true", help="提交成功后用 ffmpeg 提取音频种子声线")
    ap.add_argument("--skip-asr-check", action="store_true", help="跳过 AIGC 声明语音校验（不推荐，仅调试用）")
    ap.add_argument("--audio-dir", default=None, help="种子 wav 落盘目录（配合 --extract）")
    ap.add_argument("--keep-mp4", action="store_true", help="保留取声源视频到 --out-dir")
    ap.add_argument("--out-dir", default=None, help="取声源视频落盘目录")
    ap.add_argument("--comfy-output", default=os.environ.get("COMFY_OUTPUT_DIR", DEFAULT_COMFY_OUTPUT),
                    help="ComfyUI output 根目录（取回源件）")
    ap.add_argument("--temp-dir", default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--server", default="http://127.0.0.1:8188")
    ap.add_argument("--interval", type=int, default=10)
    ap.add_argument("--timeout", type=int, default=1800)
    ap.add_argument("--max-retry", type=int, default=2)
    args = ap.parse_args()

    start = time.time()
    try:
        seed = args.seed if args.seed is not None else random.randint(1, 2**31 - 1)
        workflow = build_workflow(args.name, args.prompt, args.duration, seed)

        if args.dry_run:
            print(json.dumps({
                "status": "success",
                "dry_run": True,
                "name": args.name, "duration": args.duration, "seed": seed,
                "filename_prefix": workflow[NODE_PREFIX]["inputs"]["filename_prefix"],
                "prompt_preview": str(workflow[NODE_PROMPT]["inputs"]["prompt"])[:200],
                "elapsed_sec": 0,
            }, ensure_ascii=False))
            sys.exit(0)

        rc, result = submit(workflow, args)
        if rc != 0:
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(rc)

        fetched = fetch_outputs(result, args.comfy_output)
        result["video_outputs"] = fetched
        result["name"] = args.name
        result["duration"] = args.duration

        video_path = None
        seed_audio = None
        if fetched:
            # 13号单输出：优先取第一有效源件（视频）
            src = next((f["source_path"] for f in fetched if f["exists"]), None)
            if src is None:
                result["status"] = "error"
                result["error"] = f"取回源件不存在于 output 目录: {[f['source_path'] for f in fetched]}"
                print(json.dumps(result, ensure_ascii=False))
                sys.exit(4)
            ext = os.path.splitext(fetched[0]["filename"])[1] or ".mp4"
            # 保留取声源视频
            if args.out_dir:
                video_path = os.path.join(args.out_dir, f"{args.name}_自我介绍{ext}")
                copyfile(src, video_path)
            # 提取种子声线
            if args.extract:
                if not args.audio_dir:
                    raise ValueError("--extract 需配合 --audio-dir 指定种子 wav 落盘目录")
                if video_path is None:
                    # 未保留视频时用临时副本提取
                    fd, tmp_mp4 = tempfile.mkstemp(prefix=f"{args.name}_", suffix=".mp4", dir=args.temp_dir or None)
                    os.close(fd)
                    copyfile(src, tmp_mp4)
                    video_path = tmp_mp4
                seed_audio = os.path.join(args.audio_dir, f"{args.name}_种子.wav")
                extract_audio(video_path, seed_audio)
                # 生成端硬拦截：提取后 ASR 校验无 AIGC 声明（默认开启，--skip-asr-check 可跳过）
                if not args.skip_asr_check:
                    verify_no_aigc(seed_audio)
                    result["asr_verified"] = True
                else:
                    result["asr_verified"] = False

        result["video_path"] = video_path
        result["seed_audio"] = seed_audio
        result["elapsed_sec"] = int(time.time() - start)
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)

    except Exception as e:
        print(json.dumps({
            "status": "error_arg",
            "name": args.name,
            "error": f"{type(e).__name__}: {e}",
            "elapsed_sec": int(time.time() - start),
        }, ensure_ascii=False))
        sys.exit(4)


if __name__ == "__main__":
    main()
