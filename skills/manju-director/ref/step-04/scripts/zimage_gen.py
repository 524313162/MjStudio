#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
zimage_gen.py —— 漫剧 ZImage 文生图固定化封装脚本（图片环节统一入口）
=====================================================================
根据 01.文生图（ZImage）工作流模板，只传【正向提示词】（+资产名/类型）即可
自动填充节点并内联提交引擎（提交/轮询/重试/取回，自包含无全局依赖）。以后图片
环节出图一律调用本脚本，禁止临场手写工作流 JSON 或逐个填节点。

用法（命令行）:
    python zimage_gen.py --type <character|scene|prop> --name <资产名> --positive "<正向提示词EN>"

参数:
    --type <str>        必填。资产类型: character(角色) / scene(场景) / prop(道具)。
    --name <str>        必填。资产名（如 阿乐 / 池塘浅水区·孵化角 / 莲花），用于
                        输出前缀 manju/<type>/<name> 与取回命名 <name>.<ext>。
    --positive <str>    必填。正向提示词（英文提交版，填节点67）。
    --negative <str>    可选。反向提示词（英文提交版，填节点71）。不传则用模板内置反向词。
    --seed <int>        可选。随机种子（填节点70）。不传则随机生成。
    --width/--height    可选。覆盖画幅尺寸。不传则按类型默认:
                        character=1472x832(横16:9)、scene=1472x832、prop=832x1472。
    --out-dir <path>    可选。取回源件落盘目录，生成 <out-dir>/<name>.<ext>。
                        不传则只打印源件路径，不落盘。
    --comfy-input <path>可选。同时复制一份带前缀副本到 ComfyUI input
                        （角色 char_、场景 scene_、道具 prop_ 前缀）。
    --comfy-output <path> 可选。ComfyUI output 根目录（默认读环境变量
                        COMFY_OUTPUT_DIR，否则 D:\\Comfy-Desktop\\ComfyUI-Shared\\output）。
    --temp-dir <path>   可选。临时 workflow JSON 写入目录（默认系统临时目录）。
    --dry-run           可选。只构造并打印 workflow 关键节点，不真实提交。
    --server <url>      可选。ComfyUI 地址（默认 http://127.0.0.1:8188）。
    --interval/--timeout/--max-retry  可选。内联提交引擎轮询/重试参数。

退出码:
    0 = 成功（产物已取回/已打印源件路径）
    2 = 生成任务最终失败
    3 = 超时
    4 = 参数/模板/取回错误

标准输出（最后一行）:
    JSON 单行结果，字段:
      {
        "status": "success|error|timeout|error_submit|error_arg",
        "type": "...", "name": "...",
        "outputs": [ {"filename":"…","subfolder":"…","source_path":"…"} , …],
        "saved_path": "<out-dir>/<name>.<ext>" | null,
        "copied_input": "<comfy-input>/<前缀>_<name>.<ext>" | null,
        "error": "…", "elapsed_sec": n
      }
"""
import argparse
import copy
import json
import os
import random
import subprocess
import sys
import tempfile
import time

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# 本脚本已收拢至 ref\step-04\scripts\，工作流同目录上位 workflows\（ref\step-04\workflows\）
WORKFLOW_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "workflows"))
# 提交引擎已内联（自包含，无全局依赖）；工作流同目录 ref\step-04\workflows\
DEFAULT_COMFY_OUTPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\output"

# 各类型固定配置：模板文件名、前缀目录、规范画幅（2026-08-31 用户确认尺寸规则）
TYPE_CONFIG = {
    "character": {
        "workflow": "01.文生图（ZImage）-角色视图.json",
        "prefix_dir": "character",
        "width": 1472,
        "height": 832,
    },
    "scene": {
        "workflow": "01.文生图（ZImage）-场景俯视图.json",
        "prefix_dir": "scene",
        "width": 1472,
        "height": 832,
    },
    "prop": {
        "workflow": "01.文生图（ZImage）-道具双视图.json",
        "prefix_dir": "prop",
        "width": 832,
        "height": 1472,
    },
}

# 固定节点号（与 ref\\01-图片-ZImage.md 节点参数映射一致）
NODE_POSITIVE = "67"   # CLIPTextEncode text -> 正向提示词
NODE_NEGATIVE = "71"   # CLIPTextEncode text -> 反向提示词
NODE_SIZE = "68"       # EmptySD3LatentImage width/height
NODE_SEED = "70"       # KSampler seed
NODE_PREFIX = "9"      # SaveImage filename_prefix


def build_workflow(cfg, positive, negative, seed, width, height, name, dry_run=False):
    """读取模板并填充节点，返回构造好的 workflow dict。"""
    wf_path = os.path.join(WORKFLOW_DIR, cfg["workflow"])
    if not os.path.isfile(wf_path):
        raise FileNotFoundError(f"工作流模板不存在: {wf_path}")
    with open(wf_path, "r", encoding="utf-8") as fh:
        workflow = json.load(fh)

    # 校验关键节点存在
    for nid in (NODE_POSITIVE, NODE_SIZE, NODE_SEED, NODE_PREFIX):
        if nid not in workflow:
            raise KeyError(f"工作流 {cfg['workflow']} 缺少节点 {nid}")

    # 节点67 正向
    workflow[NODE_POSITIVE]["inputs"]["text"] = positive
    # 节点71 反向（未提供则保持模板内置）
    if negative is not None:
        workflow[NODE_NEGATIVE]["inputs"]["text"] = negative
    # 节点68 画幅
    workflow[NODE_SIZE]["inputs"]["width"] = width
    workflow[NODE_SIZE]["inputs"]["height"] = height
    # 节点70 seed
    workflow[NODE_SEED]["inputs"]["seed"] = seed
    # 节点9 输出前缀 manju/<type>/<name>
    workflow[NODE_PREFIX]["inputs"]["filename_prefix"] = f"manju/{cfg['prefix_dir']}/{name}"

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
        prefix=f"zimage_{args.type}_{args.name}_", suffix=".json",
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


def main():
    ap = argparse.ArgumentParser(description="漫剧 ZImage 文生图固定化封装脚本")
    ap.add_argument("--type", required=True, choices=list(TYPE_CONFIG.keys()),
                    help="资产类型: character/scene/prop")
    ap.add_argument("--name", required=True, help="资产名")
    ap.add_argument("--positive", required=True, help="正向提示词（英文提交版，节点67）")
    ap.add_argument("--negative", default=None, help="反向提示词（英文提交版，节点71），不传用模板内置")
    ap.add_argument("--seed", type=int, default=None, help="随机种子，不传随机生成")
    ap.add_argument("--width", type=int, default=None)
    ap.add_argument("--height", type=int, default=None)
    ap.add_argument("--out-dir", default=None, help="取回源件落盘目录")
    ap.add_argument("--comfy-input", default=None, help="ComfyUI input 目录，复制带前缀副本")
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
        cfg = TYPE_CONFIG[args.type]
        seed = args.seed if args.seed is not None else random.randint(1, 2**31 - 1)
        width = args.width or cfg["width"]
        height = args.height or cfg["height"]

        workflow = build_workflow(cfg, args.positive, args.negative, seed, width, height, args.name)

        if args.dry_run:
            print(json.dumps({
                "status": "success",
                "dry_run": True,
                "type": args.type, "name": args.name,
                "seed": seed, "width": width, "height": height,
                "filename_prefix": workflow[NODE_PREFIX]["inputs"]["filename_prefix"],
                "positive_preview": str(workflow[NODE_POSITIVE]["inputs"]["text"])[:200],
                "elapsed_sec": 0,
            }, ensure_ascii=False))
            sys.exit(0)

        rc, result = submit(workflow, args)
        if rc != 0:
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(rc)

        # 取回：从 output 目录源件 Copy-Item
        fetched = fetch_outputs(result, args.comfy_output)
        result["outputs"] = fetched
        result["type"] = args.type
        result["name"] = args.name

        saved = None
        copied = None
        if fetched:
            # 优先取第一张有效源件（单图工作流只有一张）
            src = next((f["source_path"] for f in fetched if f["exists"]), None)
            if src is None:
                result["status"] = "error"
                result["error"] = f"取回源件不存在于 output 目录: {[f['source_path'] for f in fetched]}"
                print(json.dumps(result, ensure_ascii=False))
                sys.exit(4)
            ext = os.path.splitext(fetched[0]["filename"])[1]
            # 落盘 <name>.<ext>
            if args.out_dir:
                os.makedirs(args.out_dir, exist_ok=True)
                saved = os.path.join(args.out_dir, f"{args.name}{ext}")
                _copyfile(src, saved)
            # 复制到 ComfyUI input（带前缀副本）
            if args.comfy_input:
                os.makedirs(args.comfy_input, exist_ok=True)
                prefix_map = {"character": "char_", "scene": "scene_", "prop": "prop_"}
                copied = os.path.join(args.comfy_input, f"{prefix_map[args.type]}{args.name}{ext}")
                _copyfile(src, copied)

        result["saved_path"] = saved
        result["copied_input"] = copied
        result["elapsed_sec"] = int(time.time() - start)
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)

    except Exception as e:
        print(json.dumps({
            "status": "error_arg",
            "type": args.type, "name": args.name,
            "error": f"{type(e).__name__}: {e}",
            "elapsed_sec": int(time.time() - start),
        }, ensure_ascii=False))
        sys.exit(4)


def _copyfile(src, dst):
    """复制源件，核对字节数一致（取回铁律）。"""
    import shutil
    shutil.copy2(src, dst)
    if os.path.getsize(src) != os.path.getsize(dst):
        raise OSError(f"复制后字节数不一致: {src} -> {dst}")


if __name__ == "__main__":
    main()
