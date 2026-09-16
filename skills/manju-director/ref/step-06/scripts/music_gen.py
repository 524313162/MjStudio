#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
music_gen.py —— 漫剧音乐生成固定化封装脚本（BGM 09号 / 歌曲片头尾曲 08号）
=====================================================================
根据 09.STABLE-BGM.json（BGM纯器乐）与 08.ACE-MUSIC-音乐生成.json（歌曲/片头尾曲）
工作流模板，只传【类型 + 名称 + 提示词 + 时长】即可自动填充节点并用内联提交引擎
提交、轮询、取回音频。以后音乐环节一律调用本脚本，禁止临场手写工作流 JSON 或逐个填节点。

用法（命令行）:
    python music_gen.py --type bgm --name <BGM名> --prompt "<短描述>" --duration <秒> [选项]
    python music_gen.py --type song --name <歌名> --prompt "<tags描述>" --lyrics "<歌词>" --duration <秒> [选项]

参数:
    --type <str>        必填。bgm=纯器乐(09号工作流) / song=歌曲·片头尾曲(08号工作流)。
    --name <str>        必填。曲名（BGM名/歌名），用于输出前缀与取回命名。
    --prompt <str>      必填。音乐描述：bgm=短描述(节点52:31)；song=tags 歌曲描述(节点94 tags)。
    --duration <float>  必填。时长秒数：bgm 填节点52:36；song 同时填 94.duration 与 98.seconds(必须一致)。
    --lyrics <str>      可选。仅 song 用，完整歌词(节点94 lyrics)；片头尾曲必填。
    --negative <str>    可选。反向词(仅 bgm，节点52:7)，默认空。
    --bpm <int>         可选。仅 song，节点94 bpm，默认 120。
    --timesignature <str> 可选。仅 song，节点94 timesignature，默认 '4'。
    --language <str>    可选。仅 song，节点94 language，默认 'zh'。
    --keyscale <str>    可选。仅 song，节点94 keyscale，默认 'D major'。
    --expand <bool>     可选。仅 bgm，LLM 扩写开关(节点52:35)，默认 true。
    --seed <int>        可选。随机种子：bgm 填52:3；song 填109(94/3同源)。不传则随机生成。
    --out-name <str>    可选。取回文件名（不含扩展名）。默认 <name>。
    --out-dir <path>    可选。取回音频落盘目录（bgm 建议 06_音频\BGM，song 建议 06_音频\音乐）。
    --comfy-output <path> 可选。ComfyUI output 根目录（默认 D:\\Comfy-Desktop\\ComfyUI-Shared\\output）。
    --temp-dir <path>   可选。临时 workflow JSON 写入目录（默认系统临时目录）。
    --dry-run           可选。只构造并打印 workflow 关键节点，不真实提交。
    --server <url>      可选。ComfyUI 地址（默认 http://127.0.0.1:8188）。
    --interval/--timeout/--max-retry  可选。内联提交引擎轮询/重试参数。

退出码:
    0 = 成功（音频已取回）
    2 = 生成任务最终失败
    3 = 超时
    4 = 参数/模板/取回错误

标准输出（最后一行）:
    JSON 单行结果，字段:
      {
        "status": "success|error|timeout|error_submit|error_arg",
        "type": "bgm|song", "name": "...", "duration": n,
        "audio_outputs": [...],
        "audio_path": "<out-dir>/<out-name>.mp3" | null,
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
# 本脚本已收拢至 ref\step-06\scripts\，工作流同目录上位 workflows\（ref\step-06\workflows\）
WORKFLOW_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "workflows"))
# 提交引擎已内联（自包含，无全局依赖）
DEFAULT_COMFY_OUTPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\output"

WF_BGM = "09.STABLE-BGM.json"
WF_SONG = "08.ACE-MUSIC-音乐生成.json"

# BGM 09号节点
BGM_DESC = "52:31"     # PrimitiveStringMultiline value
BGM_DUR = "52:36"      # PrimitiveFloat value
BGM_EXPAND = "52:35"   # PrimitiveBoolean value
BGM_NEG = "52:7"       # CLIPTextEncode text
BGM_SEED = "52:3"      # KSampler seed
BGM_PREFIX = "19"      # SaveAudioMP3 filename_prefix

# 歌曲 08号节点
SONG_TAGS = "94"       # TextEncodeAceStepAudio1.5 tags
SONG_LYRICS = "94"     # TextEncodeAceStepAudio1.5 lyrics
SONG_DUR = "94"        # TextEncodeAceStepAudio1.5 duration
SONG_BPM = "94"        # TextEncodeAceStepAudio1.5 bpm
SONG_TSIG = "94"       # TextEncodeAceStepAudio1.5 timesignature
SONG_LANG = "94"       # TextEncodeAceStepAudio1.5 language
SONG_KEY = "94"        # TextEncodeAceStepAudio1.5 keyscale
SONG_SECONDS = "98"    # EmptyAceStep1.5LatentAudio seconds
SONG_SEED = "109"      # PrimitiveInt value（94/3 引用）
SONG_PREFIX = "107"    # SaveAudioMP3 filename_prefix


def build_workflow(mtype, name, prompt, duration, seed, args):
    """读取对应模板并填充节点，返回构造好的 workflow dict。"""
    wf_name = WF_BGM if mtype == "bgm" else WF_SONG
    wf_path = os.path.join(WORKFLOW_DIR, wf_name)
    if not os.path.isfile(wf_path):
        raise FileNotFoundError(f"工作流模板不存在: {wf_path}")
    with open(wf_path, "r", encoding="utf-8") as fh:
        workflow = json.load(fh)

    if mtype == "bgm":
        for nid in (BGM_DESC, BGM_DUR, BGM_EXPAND, BGM_NEG, BGM_SEED, BGM_PREFIX):
            if nid not in workflow:
                raise KeyError(f"工作流 {wf_name} 缺少节点 {nid}")
        workflow[BGM_DESC]["inputs"]["value"] = prompt
        workflow[BGM_DUR]["inputs"]["value"] = float(duration)
        workflow[BGM_EXPAND]["inputs"]["value"] = bool(args.expand)
        workflow[BGM_NEG]["inputs"]["text"] = args.negative or ""
        workflow[BGM_SEED]["inputs"]["seed"] = seed
        workflow[BGM_PREFIX]["inputs"]["filename_prefix"] = f"manju/bgm/{name}"
    else:
        for nid in (SONG_TAGS, SONG_SECONDS, SONG_SEED, SONG_PREFIX):
            if nid not in workflow:
                raise KeyError(f"工作流 {wf_name} 缺少节点 {nid}")
        workflow["94"]["inputs"]["tags"] = prompt
        workflow["94"]["inputs"]["lyrics"] = args.lyrics or ""
        workflow["94"]["inputs"]["duration"] = int(duration)
        workflow["94"]["inputs"]["bpm"] = int(args.bpm)
        workflow["94"]["inputs"]["timesignature"] = str(args.timesignature)
        workflow["94"]["inputs"]["language"] = str(args.language)
        workflow["94"]["inputs"]["keyscale"] = str(args.keyscale)
        workflow["98"]["inputs"]["seconds"] = int(duration)
        workflow["109"]["inputs"]["value"] = seed
        workflow["107"]["inputs"]["filename_prefix"] = f"manju/music/{name}"

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
        prefix=f"music_{args.type}_{args.name}_", suffix=".json",
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
    ap = argparse.ArgumentParser(description="漫剧音乐生成封装脚本（BGM 09号 / 歌曲片头尾曲 08号）")
    ap.add_argument("--type", required=True, choices=["bgm", "song"], help="bgm=纯器乐(09号) / song=歌曲·片头尾曲(08号)")
    ap.add_argument("--name", required=True, help="曲名（BGM名/歌名）")
    ap.add_argument("--prompt", required=True, help="音乐描述（bgm=短描述 52:31；song=tags 歌曲描述 94.tags）")
    ap.add_argument("--duration", required=True, type=float, help="时长秒数")
    ap.add_argument("--lyrics", default=None, help="完整歌词（song 必填，94.lyrics）")
    ap.add_argument("--negative", default=None, help="反向词（bgm，52:7），默认空")
    ap.add_argument("--bpm", type=int, default=120, help="song bpm，默认120")
    ap.add_argument("--timesignature", default="4", help="song timesignature，默认'4'")
    ap.add_argument("--language", default="zh", help="song language，默认'zh'")
    ap.add_argument("--keyscale", default="D major", help="song keyscale，默认'D major'")
    ap.add_argument("--expand", default=True, type=lambda x: str(x).lower() == "true", help="bgm LLM扩写开关，默认true")
    ap.add_argument("--seed", type=int, default=None, help="随机种子，不传随机生成")
    ap.add_argument("--out-name", default=None, help="取回文件名（不含扩展名）")
    ap.add_argument("--out-dir", default=None, help="取回音频落盘目录")
    ap.add_argument("--comfy-output", default=os.environ.get("COMFY_OUTPUT_DIR", DEFAULT_COMFY_OUTPUT),
                    help="ComfyUI output 根目录（取回源件）")
    ap.add_argument("--temp-dir", default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--server", default="http://127.0.0.1:8188")
    ap.add_argument("--interval", type=int, default=10)
    ap.add_argument("--timeout", type=int, default=1800)
    ap.add_argument("--max-retry", type=int, default=2)
    args = ap.parse_args()

    if args.type == "song" and not args.lyrics:
        ap.error("song 类型需提供 --lyrics 完整歌词")

    start = time.time()
    try:
        seed = args.seed if args.seed is not None else random.randint(1, 2**31 - 1)
        workflow = build_workflow(args.type, args.name, args.prompt, args.duration, seed, args)

        if args.dry_run:
            wf_name = WF_BGM if args.type == "bgm" else WF_SONG
            print(json.dumps({
                "status": "success",
                "dry_run": True,
                "type": args.type, "name": args.name, "duration": args.duration, "seed": seed,
                "workflow": wf_name,
                "filename_prefix": (workflow[BGM_PREFIX] if args.type == "bgm" else workflow[SONG_PREFIX])["inputs"]["filename_prefix"],
                "prompt_preview": str(args.prompt)[:200],
                "elapsed_sec": 0,
            }, ensure_ascii=False))
            sys.exit(0)

        rc, result = submit(workflow, args)
        if rc != 0:
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(rc)

        fetched = fetch_outputs(result, args.comfy_output)
        result["audio_outputs"] = fetched
        result["type"] = args.type
        result["name"] = args.name
        result["duration"] = args.duration

        audio_path = None
        if fetched:
            # 单输出：优先取第一有效源件（音频）
            src = next((f["source_path"] for f in fetched if f["exists"]), None)
            if src is None:
                result["status"] = "error"
                result["error"] = f"取回源件不存在于 output 目录: {[f['source_path'] for f in fetched]}"
                print(json.dumps(result, ensure_ascii=False))
                sys.exit(4)
            ext = os.path.splitext(fetched[0]["filename"])[1] or ".mp3"
            if args.out_dir:
                out_name = args.out_name or args.name
                audio_path = os.path.join(args.out_dir, f"{out_name}{ext}")
                copyfile(src, audio_path)

        result["audio_path"] = audio_path
        result["elapsed_sec"] = int(time.time() - start)
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)

    except Exception as e:
        print(json.dumps({
            "status": "error_arg",
            "type": args.type,
            "name": args.name,
            "error": f"{type(e).__name__}: {e}",
            "elapsed_sec": int(time.time() - start),
        }, ensure_ascii=False))
        sys.exit(4)


if __name__ == "__main__":
    main()
