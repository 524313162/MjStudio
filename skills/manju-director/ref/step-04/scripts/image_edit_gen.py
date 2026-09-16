#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
image_edit_gen.py —— 漫剧图片编辑（同场景不同环境变体）固定化封装脚本（02.1.FLUX2-图片编辑.json，step_04）
=====================================================================
用途：在【同一张原图】基础上做"同场景不同环境/氛围"的变体编辑，例如：
    xx小屋.jpg        → xx小屋-大雨天.jpg（同一场景、同一构图，换天气/光线/氛围）
    xx小屋.jpg        → xx小屋-凌晨.jpg   （同一场景，换时间段）
只传【1 张参考图（原图）+ 编辑提示词 + 输出名】即可自动：
加载 02.1.FLUX2-图片编辑.json（单 LoadImage=76 参考图）→
复制原图进 ComfyUI input/manju_edit/ → 填节点76(参考图) / 节点113(正向 92:113) /
节点87(反向 92:87，工作流已写死默认不覆盖) / 节点105(种子 92:105) /
节点94(SaveImage 前缀) → 内联提交引擎提交、轮询 → 从 output 源件 Copy-Item 取回 → 改名落盘。
以后图片编辑（场景环境变体）一律调用本脚本，禁止临场手写工作流 JSON 或逐个填节点。

用法（命令行）:
    python image_edit_gen.py --image "xx小屋.jpg" --prompt "@编辑提示词" --name edit_home_rain
    python image_edit_gen.py --image "xx小屋.jpg" --prompt-file "xx小屋-大雨天_en.md" --out-name xx小屋-大雨天

参数:
    --image <path>      必填。原始参考图绝对路径（1 张，即要编辑的原图）。
    --prompt <str>      与 --prompt-file 二选一。编辑目标提示词英文全文（对应节点 92:113）。
    --prompt-file <path> 与 --prompt 二选一。指向 `<变体>_en.md`，自动提取
                        「## Positive Prompt」段全文作为正向提示词，并提取「## Negative
                        Prompt」段作为反向提示词（未显式传 --negative 时生效）。
    --negative <str>    可选。反向提示词（节点 92:87）。默认不覆盖（工作流已写死）。
    --name <str>        可选。输出前缀名（节点94 filename_prefix 尾部，成片 manju/edit/<name>）。
    --seed <int>        可选。随机种子（节点 92:105），不传随机生成。
    --width <int>       可选。画布宽度，默认不覆盖（工作流已固定 1024x576 16:9）。
    --height <int>      可选。画布高度，默认不覆盖。
    --out-name <str>    可选。取回图片文件名（不含扩展名），默认 <name>。
    --out-dir <path>    可选。取回图片落盘目录（建议 04_图片\场景\）。
    --jpg               可选。取回后转 jpg 落盘（资产图规范要求 jpg，用 ffmpeg 转换）。
    --workflow-dir <path> 可选。工作流目录（含 02.1.FLUX2-图片编辑.json，默认
                       ref\\step-04\\workflows）。
    --comfy-input <path>  可选。ComfyUI input 根目录（复制参考图，默认
                       D:\\Comfy-Desktop\\ComfyUI-Shared\\input）。
    --comfy-output <path> 可选。ComfyUI output 根目录（取回源件，默认
                       D:\\Comfy-Desktop\\ComfyUI-Shared\\output）。
    --temp-dir <path>   可选。临时 workflow JSON 写入目录。
    --dry-run           可选。只构造工作流 JSON 并打印关键信息，不真实提交。
    --server/--interval/--timeout/--max-retry 可选。内联提交引擎轮询/重试参数。

退出码:
    0 = 成功（图片已取回落盘）
    2 = 生成任务最终失败
    3 = 超时
    4 = 参数/模板/取回错误

标准输出（最后一行）:
    JSON 单行结果，字段:
      {
        "status": "success|error|timeout|error_submit|error_arg",
        "workflow": "02.1.FLUX2-图片编辑.json",
        "name": "...", "seed": 123,
        "image_outputs": [...], "image_path": "<out-dir>/<out-name>.jpg" | null,
        "error": "…", "elapsed_sec": n
      }
"""
import argparse
import json
import os
import random
import re
import shutil
import subprocess
import sys
import tempfile
import time

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# 图片编辑工作流固定用单张（02.1.FLUX2-图片编辑.json），位于技能自包含 workflows 目录
DEFAULT_WORKFLOW_DIR = os.path.join(SCRIPT_DIR, "..", "workflows")
DEFAULT_COMFY_INPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\input"
DEFAULT_COMFY_OUTPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\output"

WORKFLOW_NAME = "02.1.FLUX2-图片编辑.json"

# 节点 id（工作流 '92:xxx' 重映射为 'xxx' 后）：
NODE_LOAD = "76"      # LoadImage 参考图（唯一原图）
NODE_POS = "113"      # CLIPTextEncode 正向（92:113）
NODE_NEG = "87"       # CLIPTextEncode 反向（92:87，工作流已写死）
NODE_OUT = "94"       # SaveImage
NODE_SEED = "105"     # RandomNoise（92:105）

INPUT_SUBDIR = "manju_edit"   # 参考图复制到 input\manju_edit\


def _extract_section(text, header):
    """从提示词文件提取指定 Markdown 二级段（如 ## Positive Prompt / ## Negative Prompt）的正文。
    兼容带 ```text 围栏与不带围栏两种写法；找不到返回 None。"""
    m = re.search(
        r"^##\s*" + re.escape(header) + r"[^\n]*\n\s*```(?:text|plain)?\s*\n(.*?)\n\s*```",
        text, re.S | re.M,
    )
    if m:
        return m.group(1).strip()
    m = re.search(
        r"^##\s*" + re.escape(header) + r"[^\n]*\n(.*?)(?=^##\s|\Z)",
        text, re.S | re.M,
    )
    if m:
        return m.group(1).strip()
    return None


def extract_prompt_from_file(prompt_file):
    """从 <变体>_en.md 提取「## Positive Prompt」段全文作为正向提示词；
    兼容旧版「## 提示词 英文」代码块与裸文本（去掉 ```text 围栏）。"""
    with open(prompt_file, encoding="utf-8") as fh:
        text = fh.read()
    sec = _extract_section(text, "Positive Prompt")
    if sec is not None:
        return sec
    m = re.search(r"##\s*提示词\s*英文[^\n]*\n\s*```(?:text|plain)?\s*\n(.*?)\n\s*```", text, re.S)
    if m:
        return m.group(1).strip()
    blocks = re.findall(r"```(?:text|plain)?\s*\n(.*?)\n```", text, re.S)
    if blocks:
        return blocks[-1].strip()
    m = re.search(r"##\s*提示词\s*英文[^\n]*\n(.*?)(?=\n##\s|\Z)", text, re.S)
    if m:
        return m.group(1).strip()
    return text.strip()


def extract_negative_from_file(prompt_file):
    """从 <变体>_en.md 提取「## Negative Prompt」段全文作为反向提示词；无该段返回 None。"""
    with open(prompt_file, encoding="utf-8") as fh:
        text = fh.read()
    return _extract_section(text, "Negative Prompt")


def load_workflow(wf_path):
    """读取工作流模板，把 '92:xxx' 节点 id 重映射为 'xxx' 并更新内部引用，返回 workflow dict。"""
    with open(wf_path, encoding="utf-8") as fh:
        workflow = json.load(fh)
    id_map = {}
    for k in list(workflow.keys()):
        if k.startswith("92:"):
            newk = k[3:]
            id_map[k] = newk
            workflow[newk] = workflow.pop(k)

    def remap(ref):
        if isinstance(ref, list) and len(ref) == 2:
            return [id_map.get(ref[0], ref[0]), ref[1]]
        return ref

    for node in workflow.values():
        for k, v in list(node["inputs"].items()):
            node["inputs"][k] = remap(v)

    # 校验引用完整
    for nid, node in workflow.items():
        for k, v in node["inputs"].items():
            if isinstance(v, list) and len(v) == 2:
                if v[0] not in workflow:
                    raise KeyError(f"工作流 {os.path.basename(wf_path)} 节点 {nid}.{k} 引用缺失节点 {v[0]}")
    return workflow


def build_workflow(image_placed, positive, negative_override, seed, name, width, height, args):
    """读取 02.1.FLUX2-图片编辑.json 模板并填节点，返回 (wf_name, workflow)。
    image_placed=(input相对路径, 源路径)。"""
    wf_path = os.path.join(args.workflow_dir, WORKFLOW_NAME)
    if not os.path.isfile(wf_path):
        raise FileNotFoundError(f"工作流模板不存在: {wf_path}")
    workflow = load_workflow(wf_path)

    # 唯一参考图 → 节点76 LoadImage
    workflow[NODE_LOAD]["inputs"]["image"] = image_placed[0]

    # 正向提示词
    workflow[NODE_POS]["inputs"]["text"] = positive
    # 反向提示词：默认保留工作流写死值（92:87 已固定），仅显式 --negative 时覆盖
    if negative_override is not None:
        workflow[NODE_NEG]["inputs"]["text"] = negative_override
    # 种子
    workflow[NODE_SEED]["inputs"]["noise_seed"] = seed
    # 输出前缀
    workflow[NODE_OUT]["inputs"]["filename_prefix"] = f"manju/edit/{name}"

    # 画幅：工作流已固定 1024x576（16:9），仅当显式传 --width/--height 时覆盖
    if width is not None or height is not None:
        w = width if width is not None else 1024
        h = height if height is not None else 576
        for nid in ("109", "115"):
            if nid in workflow:
                workflow[nid]["inputs"]["width"] = w
                workflow[nid]["inputs"]["height"] = h

    return WORKFLOW_NAME, workflow


def _http_json(method, url, payload=None, timeout=30):
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


def _find_output_images(outputs):
    files = []
    for node_id, node_out in (outputs or {}).items():
        if not isinstance(node_out, dict):
            continue
        for kind, entries in node_out.items():
            if kind == "images":
                for ent in (entries or []):
                    if isinstance(ent, dict) and "filename" in ent:
                        files.append({
                            "node_id": node_id,
                            "filename": ent.get("filename", ""),
                            "subfolder": ent.get("subfolder", ""),
                            "type_field": ent.get("type", "output"),
                        })
    return files


def submit(workflow, args):
    fd, tmp_path = tempfile.mkstemp(
        prefix=f"edit_{args.name}_", suffix=".json",
        dir=args.temp_dir or None,
    )
    os.close(fd)
    with open(tmp_path, "w", encoding="utf-8") as fh:
        json.dump(workflow, fh, ensure_ascii=False)

    import uuid
    client_id = f"manju-edit-{uuid.uuid4().hex[:12]}"
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
                images = _find_output_images(rec.get("outputs", {}))
                result = {
                    "status": "success",
                    "prompt_id": prompt_id,
                    "image_outputs": images,
                    "attempts": attempts,
                    "elapsed_sec": int(time.time() - start),
                }
                result["_tmp_workflow"] = tmp_path
                return 0, result

            if status_str == "error":
                last_error = json.dumps(rec.get("status", {}), ensure_ascii=False)
                break

            if time.time() - poll_start > args.timeout:
                last_error = f"任务超时(>{args.timeout}s)"
                rc = 3
                break

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
    fetched = []
    for o in result.get("image_outputs", []) or []:
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
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)
    if os.path.getsize(src) != os.path.getsize(dst):
        raise OSError(f"复制后字节数不一致: {src} -> {dst}")


def main():
    ap = argparse.ArgumentParser(description="漫剧图片编辑（同场景不同环境变体）封装脚本（02.1.FLUX2-图片编辑.json，step_04）")
    ap.add_argument("--image", required=True, help="原始参考图绝对路径（1 张，要编辑的原图，如 xx小屋.jpg）")
    ap.add_argument("--prompt", default=None, help="编辑目标提示词英文全文（与 --prompt-file 二选一）")
    ap.add_argument("--prompt-file", default=None, help="指向 <变体>_en.md（与 --prompt 二选一；自动提取 Positive/Negative Prompt）")
    ap.add_argument("--negative", default=None, help="反向提示词英文，默认不覆盖（工作流已写死）")
    ap.add_argument("--name", default="EDIT-H3", help="输出前缀名（成片 manju/edit/<name>）")
    ap.add_argument("--seed", type=int, default=None, help="随机种子，不传随机生成")
    ap.add_argument("--width", type=int, default=None, help="画布宽度，默认不覆盖（工作流固定 1024x576 16:9）")
    ap.add_argument("--height", type=int, default=None, help="画布高度，默认不覆盖")
    ap.add_argument("--out-name", default=None, help="取回图片文件名（不含扩展名）")
    ap.add_argument("--out-dir", default=None, help="取回图片落盘目录（建议 04_图片\\场景\\）")
    ap.add_argument("--jpg", action="store_true", help="取回后转 jpg 落盘（资产图规范要求 jpg，用 ffmpeg 转换）")
    ap.add_argument("--workflow-dir", default=os.environ.get("MANJU_WF_DIR", DEFAULT_WORKFLOW_DIR),
                    help="工作流目录（含 02.1.FLUX2-图片编辑.json）")
    ap.add_argument("--comfy-input", default=os.environ.get("COMFY_INPUT_DIR", DEFAULT_COMFY_INPUT),
                    help="ComfyUI input 根目录（复制参考图）")
    ap.add_argument("--comfy-output", default=os.environ.get("COMFY_OUTPUT_DIR", DEFAULT_COMFY_OUTPUT),
                    help="ComfyUI output 根目录（取回源件）")
    ap.add_argument("--temp-dir", default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--server", default="http://127.0.0.1:8188")
    ap.add_argument("--interval", type=int, default=5)
    ap.add_argument("--timeout", type=int, default=900)
    ap.add_argument("--max-retry", type=int, default=2)
    args = ap.parse_args()

    if bool(args.prompt) == bool(args.prompt_file):
        ap.error("必须且只能提供 --prompt 或 --prompt-file 之一")
    prompt = args.prompt if args.prompt is not None else extract_prompt_from_file(args.prompt_file)
    if not prompt.strip():
        ap.error("提示词为空")
    # 反向提示词：--negative 未显式给出且 --prompt-file 指向的 en 文件含「## Negative Prompt」段时自动提取
    if args.negative is None and args.prompt_file is not None:
        neg = extract_negative_from_file(args.prompt_file)
        if neg:
            args.negative = neg

    image = args.image.strip()
    if not os.path.isfile(image):
        ap.error(f"参考图不存在: {image}")

    start = time.time()
    try:
        seed = args.seed if args.seed is not None else random.randint(1, 2**31 - 1)

        # 复制参考图进 input\manju_edit\，固定命名为 image1
        manju_dir = os.path.join(args.comfy_input, INPUT_SUBDIR)
        os.makedirs(manju_dir, exist_ok=True)
        ext = os.path.splitext(image)[1] or ".png"
        dst_name = f"image1{ext}"
        dst = os.path.join(manju_dir, dst_name)
        shutil.copy2(image, dst)
        if os.path.getsize(image) != os.path.getsize(dst):
            raise OSError(f"复制后字节数不一致: {image} -> {dst}")
        placed = (f"{INPUT_SUBDIR}/{dst_name}", image)

        wf_name, workflow = build_workflow(
            placed, prompt, args.negative, seed, args.name, args.width, args.height, args
        )

        if args.dry_run:
            print(json.dumps({
                "status": "success",
                "dry_run": True,
                "workflow": wf_name,
                "name": args.name, "seed": seed,
                "source": os.path.basename(image),
                "filename_prefix": workflow[NODE_OUT]["inputs"]["filename_prefix"],
                "prompt_preview": str(prompt)[:200],
                "elapsed_sec": 0,
            }, ensure_ascii=False))
            sys.exit(0)

        rc, result = submit(workflow, args)
        if rc != 0:
            print(json.dumps(result, ensure_ascii=False))
            sys.exit(rc)

        fetched = fetch_outputs(result, args.comfy_output)
        result["image_outputs"] = fetched
        result["workflow"] = wf_name
        result["name"] = args.name
        result["seed"] = seed

        image_path = None
        if fetched:
            prefix = f"manju/edit/{args.name}"
            src = next((f["source_path"] for f in fetched
                        if f["exists"] and prefix in f["source_path"].replace("\\", "/")), None)
            if src is None:
                src = next((f["source_path"] for f in fetched if f["exists"]), None)
            if src is None:
                result["status"] = "error"
                result["error"] = f"取回源件不存在于 output 目录: {[f['source_path'] for f in fetched]}"
                print(json.dumps(result, ensure_ascii=False))
                sys.exit(4)
            ext = os.path.splitext(fetched[0]["filename"])[1] or ".png"
            if args.out_dir:
                out_name = args.out_name or args.name
                if args.jpg and ext.lower() in (".png", ".webp"):
                    image_path = os.path.join(args.out_dir, f"{out_name}.jpg")
                    _run = subprocess.run(
                        ["ffmpeg", "-y", "-i", src, "-q:v", "2", image_path],
                        capture_output=True, text=True)
                    if _run.returncode != 0:
                        result["status"] = "error"
                        result["error"] = f"jpg 转换失败: {_run.stderr[-500:]}"
                        print(json.dumps(result, ensure_ascii=False))
                        sys.exit(4)
                else:
                    image_path = os.path.join(args.out_dir, f"{out_name}{ext}")
                    copyfile(src, image_path)

        result["image_path"] = image_path
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
