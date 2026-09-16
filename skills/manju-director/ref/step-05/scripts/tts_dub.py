#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
tts_dub.py —— 漫剧克隆配音固定化封装脚本（第二步：31号 CosyVoice3 克隆TTS 逐句配音）
=====================================================================
根据 31.FL-CosyVoice3-克隆配音TTS.json 工作流模板，传入【角色种子声线】+【台词正文】+
【可选语气指令】即可自动填充节点、把种子声复制到 ComfyUI input、用内联提交引擎
提交轮询取回配音音频。以后配音一律调用本脚本，禁止临场手写工作流 JSON 或逐个填节点。

用法（命令行）:
    python tts_dub.py --character <角色名> --seed-audio "<种子wav路径>" --text "<台词正文>" [选项]

参数:
    --character <str>   必填。角色名（如 甜甜）。
    --seed-audio <path> 必填。该角色专属种子声线 wav 路径（第一步 voice_seed_gen 产出）。
    --text <str>        必填。纯台词正文（普通话，禁止混入指令词，填节点3 text）。
    --instruct <str>    可选。语气/情绪/方言指令（填节点3 instruct_text；普通话+正常情绪则留空）。
    --speed <float>     可选。语速（填节点3 speed），默认 1.0。
    --seed <int>        可选。随机种子。不传则随机生成。
    --out-name <str>    可选。输出文件名（不含扩展名）。默认 <character>_<text前8字>。
    --out-dir <path>    可选。取回音频落盘目录（如 06_音频\第N集）。不传则只打印源件路径。
    --comfy-input <path> 可选。ComfyUI input 根目录（默认 D:\\Comfy-Desktop\\ComfyUI-Shared\\input）。
    --comfy-output <path> 可选。ComfyUI output 根目录（默认 D:\\Comfy-Desktop\\ComfyUI-Shared\\output）。
    --temp-dir <path>   可选。临时 workflow JSON 写入目录（默认系统临时目录）。
    --dry-run           可选。只构造并打印 workflow 关键节点，不真实提交。
    --server <url>      可选。ComfyUI 地址（默认 http://127.0.0.1:8188）。
    --interval/--timeout/--max-retry  可选。内联提交引擎轮询/重试参数。

退出码:
    0 = 成功（配音音频已取回）
    2 = 生成任务最终失败
    3 = 超时
    4 = 参数/模板/取回错误

标准输出（最后一行）:
    JSON 单行结果，字段:
      {
        "status": "success|error|timeout|error_submit|error_arg",
        "character": "...", "text": "...", "instruct": "...",
        "audio_outputs": [...],
        "audio_path": "<out-dir>/<out-name>.wav" | null,
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
DEFAULT_COMFY_INPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\input"
DEFAULT_COMFY_OUTPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\output"

# 固定节点号（与 ref\step-05\配音.md 31号克隆TTS节点映射一致）
WF_NAME = "31.FL-CosyVoice3-克隆配音TTS.json"
NODE_LOAD_AUDIO = "1"   # LoadAudio audio（种子文件名，需在 ComfyUI input）
NODE_INSTRUCT2 = "3"    # FL_CosyVoice3_Instruct2 text/instruct_text/speed/seed


def build_workflow(seed_filename, text, instruct, speed, seed, dry_run=False):
    """读取 31 号模板并填充节点，返回构造好的 workflow dict。"""
    wf_path = os.path.join(WORKFLOW_DIR, WF_NAME)
    if not os.path.isfile(wf_path):
        raise FileNotFoundError(f"工作流模板不存在: {wf_path}")
    with open(wf_path, "r", encoding="utf-8") as fh:
        workflow = json.load(fh)

    for nid in (NODE_LOAD_AUDIO, NODE_INSTRUCT2):
        if nid not in workflow:
            raise KeyError(f"工作流 {WF_NAME} 缺少节点 {nid}")

    # 节点1 LoadAudio -> 种子文件名（ComfyUI input 下）
    workflow[NODE_LOAD_AUDIO]["inputs"]["audio"] = seed_filename
    # 节点3 Instruct2 -> text 纯台词 / instruct_text 语气 / speed / seed
    workflow[NODE_INSTRUCT2]["inputs"]["text"] = text
    workflow[NODE_INSTRUCT2]["inputs"]["instruct_text"] = instruct or ""
    workflow[NODE_INSTRUCT2]["inputs"]["speed"] = float(speed)
    workflow[NODE_INSTRUCT2]["inputs"]["seed"] = seed

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
        prefix=f"tts_dub_{args.character}_", suffix=".json",
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


def main():
    ap = argparse.ArgumentParser(description="漫剧克隆配音封装脚本（31号 CosyVoice3 逐句配音）")
    ap.add_argument("--character", required=True, help="角色名")
    ap.add_argument("--seed-audio", required=True, help="角色专属种子声线 wav 路径")
    ap.add_argument("--text", required=True, help="纯台词正文（普通话，节点3 text）")
    ap.add_argument("--instruct", default=None, help="语气/情绪/方言指令（节点3 instruct_text），可留空")
    ap.add_argument("--speed", type=float, default=1.0, help="语速（节点3 speed），默认1.0")
    ap.add_argument("--seed", type=int, default=None, help="随机种子，不传随机生成")
    ap.add_argument("--out-name", default=None, help="输出文件名（不含扩展名）")
    ap.add_argument("--out-dir", default=None, help="取回音频落盘目录")
    ap.add_argument("--comfy-input", default=os.environ.get("COMFY_INPUT_DIR", DEFAULT_COMFY_INPUT),
                    help="ComfyUI input 根目录（种子声复制到此处）")
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
        if not os.path.isfile(args.seed_audio):
            raise FileNotFoundError(f"种子声线不存在: {args.seed_audio}")

        # 1) 种子声复制到 ComfyUI input（LoadAudio 只读 input 下文件名）
        os.makedirs(args.comfy_input, exist_ok=True)
        seed_ext = os.path.splitext(args.seed_audio)[1] or ".wav"
        seed_copy = os.path.join(args.comfy_input, f"{args.character}_种子{seed_ext}")
        copyfile(args.seed_audio, seed_copy)

        seed = args.seed if args.seed is not None else random.randint(1, 2**31 - 1)
        workflow = build_workflow(os.path.basename(seed_copy), args.text, args.instruct, args.speed, seed)

        if args.dry_run:
            print(json.dumps({
                "status": "success",
                "dry_run": True,
                "character": args.character,
                "seed_audio_input": os.path.basename(seed_copy),
                "text": args.text,
                "instruct": args.instruct or "",
                "speed": args.speed,
                "seed": seed,
                "elapsed_sec": 0,
            }, ensure_ascii=False))
            sys.exit(0)

        rc, result = submit(workflow, args)
        if rc != 0:
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(rc)

        fetched = fetch_outputs(result, args.comfy_output)
        result["audio_outputs"] = fetched
        result["character"] = args.character
        result["text"] = args.text
        result["instruct"] = args.instruct or ""

        audio_path = None
        if fetched:
            # 31号单输出：优先取第一有效源件（音频）
            src = next((f["source_path"] for f in fetched if f["exists"]), None)
            if src is None:
                result["status"] = "error"
                result["error"] = f"取回源件不存在于 output 目录: {[f['source_path'] for f in fetched]}"
                print(json.dumps(result, ensure_ascii=False))
                sys.exit(4)
            ext = os.path.splitext(fetched[0]["filename"])[1] or ".wav"
            if args.out_dir:
                out_name = args.out_name or f"{args.character}_{args.text[:8]}"
                audio_path = os.path.join(args.out_dir, f"{out_name}{ext}")
                copyfile(src, audio_path)

        result["audio_path"] = audio_path
        result["elapsed_sec"] = int(time.time() - start)
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)

    except Exception as e:
        print(json.dumps({
            "status": "error_arg",
            "character": args.character,
            "text": args.text,
            "error": f"{type(e).__name__}: {e}",
            "elapsed_sec": int(time.time() - start),
        }, ensure_ascii=False))
        sys.exit(4)


if __name__ == "__main__":
    main()
