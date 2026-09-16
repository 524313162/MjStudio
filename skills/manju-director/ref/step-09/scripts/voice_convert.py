#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
voice_convert.py —— 32号工作流「FL-CosyVoice3-音频声音转换」固定化封装脚本
=====================================================================
读取 32.FL-CosyVoice3-音频声音转换.json 模板，传入【目标音色声线】+【内容音频】，
自动把两个音频复制进 ComfyUI input、填节点、用内联提交引擎提交→轮询→从 output
源件取回换音色后的音频。以后凡是"把一段声音的音色换成角色声线"一律调用本脚本，
禁止临场手写工作流 JSON 或逐个填节点。

工作流节点映射（与 ref\step-09\配音.md 一致）：
  1 LoadAudio(声音A=目标音色，如 <角色>_种子.wav)
  2 LoadAudio(声音B=内容音频，要换音色的原声)
  3 FL_CosyVoice3_ModelLoader
  4 FL_CosyVoice3_VoiceConversion(source_audio=B, target_audio=A)
  5 PreviewAudio
  6 SaveAudio

用法（命令行）:
    python voice_convert.py --seed-audio "<种子wav路径>" --content-audio "<内容音频路径>" [选项]

参数:
    --seed-audio <path>    必填。目标音色声线 wav（如 06_音频\声线\小墨_种子.wav）。
    --content-audio <path> 必填。内容音频（要换音色的原声段）。
    --seed <int>           可选。随机种子。不传则随机生成（填节点4 seed）。
    --speed <float>        可选。语速（填节点4 speed），默认 1.0。
    --out-name <str>       可选。输出文件名（不含扩展名），默认取 content 名+_vc。
    --out-dir <path>       可选。取回音频落盘目录。不传则只打印源件路径。
    --comfy-input <path>   可选。ComfyUI input 根目录（默认 D:\\Comfy-Desktop\\ComfyUI-Shared\\input）。
    --comfy-output <path>  可选。ComfyUI output 根目录（默认 D:\\Comfy-Desktop\\ComfyUI-Shared\\output）。
    --temp-dir <path>      可选。临时 workflow JSON 写入目录（默认系统临时目录）。
    --dry-run              可选。只构造并打印 workflow 关键节点，不真实提交。
    --server <url>         可选。ComfyUI 地址（默认 http://127.0.0.1:8188）。
    --interval/--timeout/--max-retry  可选。内联提交引擎轮询/重试参数。

退出码:
    0 = 成功（换音色音频已取回）
    2 = 生成任务最终失败
    3 = 超时
    4 = 参数/模板/取回错误

标准输出（最后一行）:
    JSON 单行结果:
      {
        "status": "success|error|timeout|error_submit|error_arg",
        "seed_audio": "...", "content_audio": "...",
        "audio_outputs": [...], "audio_path": "<out-dir>/<out-name>.<ext>" | null,
        "error": "…", "elapsed_sec": n
      }
"""
import argparse
import json
import os
import random
import shutil
import sys
import tempfile
import time

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# 工作流已收拢至 ref\step-09\workflows\
WORKFLOW_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "workflows"))
DEFAULT_COMFY_INPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\input"
DEFAULT_COMFY_OUTPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\output"

WF_NAME = "32.FL-CosyVoice3-音频声音转换.json"
NODE_TARGET = "1"   # LoadAudio 声音A（目标音色种子）
NODE_SOURCE = "2"   # LoadAudio 声音B（内容音频）
NODE_CONVERT = "4"  # FL_CosyVoice3_VoiceConversion
NODE_SAVE = "6"     # SaveAudio


def build_workflow(seed_filename, content_filename, speed, seed):
    wf_path = os.path.join(WORKFLOW_DIR, WF_NAME)
    if not os.path.isfile(wf_path):
        raise FileNotFoundError(f"工作流模板不存在: {wf_path}")
    with open(wf_path, "r", encoding="utf-8") as fh:
        workflow = json.load(fh)

    for nid in (NODE_TARGET, NODE_SOURCE, NODE_CONVERT, NODE_SAVE):
        if nid not in workflow:
            raise KeyError(f"工作流 {WF_NAME} 缺少节点 {nid}")

    # 节点1 LoadAudio -> 目标音色种子文件名（ComfyUI input 下）
    workflow[NODE_TARGET]["inputs"]["audio"] = seed_filename
    # 节点2 LoadAudio -> 内容音频文件名
    workflow[NODE_SOURCE]["inputs"]["audio"] = content_filename
    # 节点4 VoiceConversion -> speed / seed
    workflow[NODE_CONVERT]["inputs"]["speed"] = float(speed)
    workflow[NODE_CONVERT]["inputs"]["seed"] = seed

    return workflow


def _http_json(method, url, payload=None, timeout=30):
    import urllib.request
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        body = resp.read().decode("utf-8")
        return json.loads(body) if body else {}


def _find_output_files(outputs):
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
                            "type": "audio" if kind == "audio" else kind,
                            "filename": ent.get("filename", ""),
                            "subfolder": ent.get("subfolder", ""),
                        })
    return files


def submit(workflow, args):
    fd, tmp_path = tempfile.mkstemp(prefix="voice_convert_", suffix=".json", dir=args.temp_dir or None)
    os.close(fd)
    with open(tmp_path, "w", encoding="utf-8") as fh:
        json.dump(workflow, fh, ensure_ascii=False)

    import uuid
    client_id = f"manju-vc-{uuid.uuid4().hex[:12]}"
    server = args.server.rstrip("/")
    start = time.time()
    attempts = 0
    last_error = ""
    rc = 4

    while True:
        attempts += 1
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
                return 0, {"status": "success", "prompt_id": prompt_id,
                           "outputs": outputs, "attempts": attempts,
                           "elapsed_sec": int(time.time() - start), "_tmp_workflow": tmp_path}

            if status_str == "error":
                last_error = json.dumps(rec.get("status", {}), ensure_ascii=False)
                break

            if time.time() - poll_start > args.timeout:
                last_error = f"任务超时(>{args.timeout}s)"
                rc = 3
                break

        if attempts <= args.max_retry:
            print(f"[retry {attempts}/{args.max_retry}] status=error: {last_error}", file=sys.stderr)
            time.sleep(args.interval)
            continue
        rc = 2
        break

    result = {"status": "error" if rc == 2 else ("timeout" if rc == 3 else "error_submit"),
              "prompt_id": None, "error": last_error, "attempts": attempts,
              "elapsed_sec": int(time.time() - start), "_tmp_workflow": tmp_path}
    return rc, result


def fetch_outputs(result, comfy_output_dir):
    fetched = []
    for o in result.get("outputs", []) or []:
        subfolder = o.get("subfolder", "") or ""
        filename = o.get("filename", "")
        if not filename:
            continue
        src = os.path.join(comfy_output_dir, subfolder, filename) if subfolder \
            else os.path.join(comfy_output_dir, filename)
        fetched.append({"filename": filename, "subfolder": subfolder,
                        "source_path": src, "exists": os.path.isfile(src)})
    return fetched


def copyfile(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)
    if os.path.getsize(src) != os.path.getsize(dst):
        raise OSError(f"复制后字节数不一致: {src} -> {dst}")


def main():
    ap = argparse.ArgumentParser(description="32号音频声音转换封装脚本（CosyVoice3 换音色）")
    ap.add_argument("--seed-audio", required=True, help="目标音色声线 wav 路径")
    ap.add_argument("--content-audio", required=True, help="内容音频（要换音色的原声）路径")
    ap.add_argument("--seed", type=int, default=None, help="随机种子，不传随机生成")
    ap.add_argument("--speed", type=float, default=1.0, help="语速（节点4 speed），默认1.0")
    ap.add_argument("--out-name", default=None, help="输出文件名（不含扩展名）")
    ap.add_argument("--out-dir", default=None, help="取回音频落盘目录")
    ap.add_argument("--comfy-input", default=os.environ.get("COMFY_INPUT_DIR", DEFAULT_COMFY_INPUT))
    ap.add_argument("--comfy-output", default=os.environ.get("COMFY_OUTPUT_DIR", DEFAULT_COMFY_OUTPUT))
    ap.add_argument("--temp-dir", default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--server", default="http://127.0.0.1:8188")
    ap.add_argument("--interval", type=int, default=10)
    ap.add_argument("--timeout", type=int, default=1800)
    ap.add_argument("--max-retry", type=int, default=2)
    args = ap.parse_args()

    start = time.time()
    try:
        for p in (args.seed_audio, args.content_audio):
            if not os.path.isfile(p):
                raise FileNotFoundError(f"音频不存在: {p}")

        # 1) 两个音频复制进 ComfyUI input（LoadAudio 只读 input 下文件名）
        os.makedirs(args.comfy_input, exist_ok=True)
        seed_ext = os.path.splitext(args.seed_audio)[1] or ".wav"
        seed_name = f"{os.path.splitext(os.path.basename(args.seed_audio))[0]}_vc{seed_ext}"
        seed_copy = os.path.join(args.comfy_input, seed_name)
        copyfile(args.seed_audio, seed_copy)

        content_ext = os.path.splitext(args.content_audio)[1] or ".wav"
        content_name = f"{os.path.splitext(os.path.basename(args.content_audio))[0]}{content_ext}"
        content_copy = os.path.join(args.comfy_input, content_name)
        copyfile(args.content_audio, content_copy)

        seed = args.seed if args.seed is not None else random.randint(1, 2**31 - 1)
        workflow = build_workflow(os.path.basename(seed_copy), os.path.basename(content_copy), args.speed, seed)

        if args.dry_run:
            print(json.dumps({
                "status": "success", "dry_run": True,
                "seed_audio_input": os.path.basename(seed_copy),
                "content_audio_input": os.path.basename(content_copy),
                "speed": args.speed, "seed": seed, "elapsed_sec": 0,
            }, ensure_ascii=False))
            sys.exit(0)

        rc, result = submit(workflow, args)
        if rc != 0:
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(rc)

        fetched = fetch_outputs(result, args.comfy_output)
        result["audio_outputs"] = fetched
        result["seed_audio"] = args.seed_audio
        result["content_audio"] = args.content_audio

        audio_path = None
        if fetched:
            src = next((f["source_path"] for f in fetched if f["exists"]), None)
            if src is None:
                result["status"] = "error"
                result["error"] = f"取回源件不存在于 output 目录: {[f['source_path'] for f in fetched]}"
                print(json.dumps(result, ensure_ascii=False))
                sys.exit(4)
            ext = os.path.splitext(fetched[0]["filename"])[1] or ".wav"
            if args.out_dir:
                out_name = args.out_name or f"{os.path.splitext(os.path.basename(args.content_audio))[0]}_vc"
                audio_path = os.path.join(args.out_dir, f"{out_name}{ext}")
                copyfile(src, audio_path)

        result["audio_path"] = audio_path
        result["elapsed_sec"] = int(time.time() - start)
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)

    except Exception as e:
        print(json.dumps({
            "status": "error_arg",
            "seed_audio": args.seed_audio,
            "content_audio": args.content_audio,
            "error": f"{type(e).__name__}: {e}",
            "elapsed_sec": int(time.time() - start),
        }, ensure_ascii=False))
        sys.exit(4)


if __name__ == "__main__":
    main()
