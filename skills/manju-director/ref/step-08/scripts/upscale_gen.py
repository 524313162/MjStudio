#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
upscale_gen.py —— 漫剧视频放大固定化封装脚本（17.视频放大1K.json，step_08 · 08-3 放大）
=====================================================================
视频放大环节只使用 G:\\01-ManJu-Workflow\\17.视频放大1K.json 工作流：
  VHS_LoadVideo(1) → UpscaleModelLoader(2, RealESRGAN_x4plus_anime_6B)
  → ImageUpscaleWithModel(3) → ImageScale(4, 1920x1080 lanczos)
  → VHS_VideoCombine(5, upscaled_1K, 保留音频链路)。
只传【待放大视频 + 输出名】即可自动：复制视频进 ComfyUI input/manju_up/ →
填节点1 video → 填节点4 缩放目标 → 填节点5 输出前缀 →
用内联提交引擎提交、轮询、取回放大件 → 改名落盘。
以后放大环节一律调用本脚本，禁止临场手写工作流 JSON 或逐个填节点
（取回铁律：output 源件 Copy-Item，禁 8188/view 下载）。

用法（命令行）:
    python upscale_gen.py --video "05_视频\\第N集\\video1.mp4" --out-name video1_1K [选项]

参数:
    --video <path>     必填。待放大视频绝对路径（一次一个，逐片调用；正片 videoX.mp4 走本脚本）。
    --out-name <str>   必填。放大件取回文件名（不含扩展名，约定带 _1K 后缀，如 video1_1K）。
    --width <int>      可选。缩放目标宽，默认 1920（横屏 16:9 1K）。
    --height <int>     可选。缩放目标高，默认 1080（横屏 16:9 1K；竖屏项目改 1080x1920）。
    --out-dir <path>   可选。取回放大件落盘目录（建议 05_视频\\第N集）。
    --prefix <str>     可选。input 复制前缀，默认 manju_up。
    --workflow <path>  可选。放大工作流路径，默认 ref\\step-08\\workflows\\17.视频放大1K.json。
    --comfy-input <path>  可选。ComfyUI input 根目录（复制输入视频，默认
                       D:\\Comfy-Desktop\\ComfyUI-Shared\\input）。
    --comfy-output <path> 可选。ComfyUI output 根目录（取回源件，默认
                       D:\\Comfy-Desktop\\ComfyUI-Shared\\output）。
    --temp-dir <path>  可选。临时 workflow JSON 写入目录。
    --dry-run          可选。只构造工作流 JSON 并打印关键信息，不真实提交。
    --server/--interval/--timeout/--max-retry 可选。内联提交引擎轮询/重试参数。

退出码:
    0 = 成功（放大件已取回落盘）
    2 = 生成任务最终失败
    3 = 超时
    4 = 参数/模板/取回错误

标准输出（最后一行）:
    JSON 单行结果，字段:
      {
        "status": "success|error|timeout|error_submit|error_arg",
        "workflow": "17.视频放大1K.json",
        "name": "...", "width": 1920, "height": 1080,
        "video_outputs": [...], "video_path": "<out-dir>/<out-name>.mp4" | null,
        "error": "…", "elapsed_sec": n
      }
"""
import argparse
import json
import os
import shutil
import sys
import tempfile
import time
import uuid

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WORKFLOW = os.path.join(SCRIPT_DIR, "..", "workflows", "17.视频放大1K.json")
DEFAULT_COMFY_INPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\input"
DEFAULT_COMFY_OUTPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\output"

NODE_LOAD = "1"      # VHS_LoadVideo
NODE_MODEL = "2"     # UpscaleModelLoader (RealESRGAN_x4plus_anime_6B)
NODE_UP = "3"        # ImageUpscaleWithModel
NODE_SCALE = "4"     # ImageScale (1920x1080 lanczos)
NODE_OUT = "5"       # VHS_VideoCombine (upscaled_1K)


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


def submit(workflow, args, name):
    """写临时 workflow JSON 并内联提交引擎提交（提交→轮询→重试→超时），返回 (exit_code, result_dict)。"""
    fd, tmp_path = tempfile.mkstemp(
        prefix=f"up_{name}_", suffix=".json",
        dir=args.temp_dir or None,
    )
    os.close(fd)
    with open(tmp_path, "w", encoding="utf-8") as fh:
        json.dump(workflow, fh, ensure_ascii=False)

    client_id = f"manju-up-{uuid.uuid4().hex[:12]}"
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


def build_workflow(args, input_rel):
    """读取 17.视频放大1K.json 模板并填充节点，返回 workflow dict。"""
    if not os.path.isfile(args.workflow):
        raise FileNotFoundError(f"放大工作流不存在: {args.workflow}")
    with open(args.workflow, encoding="utf-8") as fh:
        workflow = json.load(fh)

    # 节点1 VHS_LoadVideo：填输入视频相对 input 的路径
    if NODE_LOAD not in workflow:
        raise KeyError(f"工作流 {os.path.basename(args.workflow)} 缺少 VHS_LoadVideo 节点 {NODE_LOAD}")
    workflow[NODE_LOAD]["inputs"]["video"] = input_rel

    # 节点4 ImageScale：缩放目标（默认 1920x1080）
    if NODE_SCALE not in workflow:
        raise KeyError(f"工作流 {os.path.basename(args.workflow)} 缺少 ImageScale 节点 {NODE_SCALE}")
    workflow[NODE_SCALE]["inputs"]["width"] = int(args.width)
    workflow[NODE_SCALE]["inputs"]["height"] = int(args.height)

    # 节点5 VHS_VideoCombine：输出前缀
    if NODE_OUT not in workflow:
        raise KeyError(f"工作流 {os.path.basename(args.workflow)} 缺少 VHS_VideoCombine 节点 {NODE_OUT}")
    workflow[NODE_OUT]["inputs"]["filename_prefix"] = f"manju/up/{args.out_name}"

    return workflow


def main():
    ap = argparse.ArgumentParser(description="漫剧视频放大封装脚本（17.视频放大1K.json，step_08·08-3放大）")
    ap.add_argument("--video", required=True, help="待放大视频绝对路径（一次一个；正片 videoX.mp4 走本脚本）")
    ap.add_argument("--out-name", required=True, help="放大件取回文件名（不含扩展名，约定带 _1K 后缀）")
    ap.add_argument("--width", type=int, default=1920, help="缩放目标宽，默认1920（16:9 1K）")
    ap.add_argument("--height", type=int, default=1080, help="缩放目标高，默认1080（16:9 1K；竖屏改1080x1920）")
    ap.add_argument("--out-dir", default=None, help="取回放大件落盘目录（建议 05_视频\\第N集）")
    ap.add_argument("--prefix", default="manju_up", help="input 复制前缀，默认 manju_up")
    ap.add_argument("--workflow", default=DEFAULT_WORKFLOW, help=f"放大工作流路径，默认 {DEFAULT_WORKFLOW}")
    ap.add_argument("--comfy-input", default=os.environ.get("COMFY_INPUT_DIR", DEFAULT_COMFY_INPUT),
                    help="ComfyUI input 根目录（复制输入视频）")
    ap.add_argument("--comfy-output", default=os.environ.get("COMFY_OUTPUT_DIR", DEFAULT_COMFY_OUTPUT),
                    help="ComfyUI output 根目录（取回源件）")
    ap.add_argument("--temp-dir", default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--server", default="http://127.0.0.1:8188")
    ap.add_argument("--interval", type=int, default=10)
    ap.add_argument("--timeout", type=int, default=1800)
    ap.add_argument("--max-retry", type=int, default=2)
    args = ap.parse_args()

    if not os.path.isfile(args.video):
        ap.error(f"待放大视频不存在: {args.video}")

    start = time.time()
    try:
        # 复制输入视频进 input\manju_up\
        manju_dir = os.path.join(args.comfy_input, args.prefix)
        os.makedirs(manju_dir, exist_ok=True)
        ext = os.path.splitext(args.video)[1] or ".mp4"
        dst_name = f"IN{ext}"
        dst = os.path.join(manju_dir, dst_name)
        shutil.copy2(args.video, dst)
        if os.path.getsize(args.video) != os.path.getsize(dst):
            raise OSError(f"复制后字节数不一致: {args.video} -> {dst}")
        input_rel = f"{args.prefix}/{dst_name}"

        workflow = build_workflow(args, input_rel)

        if args.dry_run:
            print(json.dumps({
                "status": "success",
                "dry_run": True,
                "workflow": os.path.basename(args.workflow),
                "video": args.video,
                "input_rel": input_rel,
                "width": args.width, "height": args.height,
                "filename_prefix": workflow[NODE_OUT]["inputs"]["filename_prefix"],
                "elapsed_sec": 0,
            }, ensure_ascii=False))
            sys.exit(0)

        rc, result = submit(workflow, args, args.out_name)
        if rc != 0:
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(rc)

        fetched = fetch_outputs(result, args.comfy_output)
        result["video_outputs"] = fetched
        result["workflow"] = os.path.basename(args.workflow)
        result["name"] = args.out_name
        result["width"] = args.width
        result["height"] = args.height

        video_path = None
        if fetched:
            # 优先取成片（前缀 manju/up/<out-name> 对应源件），兜底取第一个存在源件
            prefix = f"manju/up/{args.out_name}"
            src = next((f["source_path"] for f in fetched
                        if f["exists"] and prefix in f["source_path"].replace("\\", "/")), None)
            if src is None:
                src = next((f["source_path"] for f in fetched if f["exists"]), None)
            if src is None:
                result["status"] = "error"
                result["error"] = f"取回源件不存在于 output 目录: {[f['source_path'] for f in fetched]}"
                print(json.dumps(result, ensure_ascii=False))
                sys.exit(4)
            ext2 = os.path.splitext(fetched[0]["filename"])[1] or ".mp4"
            if args.out_dir:
                video_path = os.path.join(args.out_dir, f"{args.out_name}{ext2}")
                copyfile(src, video_path)

        result["video_path"] = video_path
        result["elapsed_sec"] = int(time.time() - start)
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)

    except Exception as e:
        print(json.dumps({
            "status": "error_arg",
            "name": args.out_name,
            "error": f"{type(e).__name__}: {e}",
            "elapsed_sec": int(time.time() - start),
        }, ensure_ascii=False))
        sys.exit(4)


if __name__ == "__main__":
    main()
