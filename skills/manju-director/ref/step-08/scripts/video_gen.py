#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
video_gen.py —— 漫剧视频生成固定化封装脚本（15.MinimaxH3 多图参考01~20 按参考图数量 N 自动选档，step_08）
=====================================================================
视频环节按参考图数量 N 自动选择 15.MinimaxH3 多图参考{N:02d}.json（N=1~20）。
只传【参考图 + @xx模块式提示词 + 时长 + 画幅】即可自动：加载 15.MinimaxH3 多图参考{N:02d}.json →
复制参考图进 ComfyUI input/manju_vc/ → 填节点7(media_N/提示词/时长/画幅/种子) →
填 LoadImage(201~200+N 取前 N 个，N≤20) → 填节点93 输出前缀 → 处理节点102(SageAttention) →
用内联提交引擎提交、轮询、取回视频 → 改名落盘。以后视频环节一律调用本脚本，
禁止临场手写工作流 JSON 或逐个填节点（取回铁律：output 源件 Copy-Item，禁 8188/view 下载）。

用法（命令行）:
    python video_gen.py --images "图1,图2,..." --prompt "@xx模块式英文全文" --seconds 10 [选项]
    python video_gen.py --images "图1,图2" --prompt-file "镜头X_视频提示词.md" --seconds 6 --aspect-ratio 9:16

参数:
    --images <str>      必填。本镜参考图绝对路径，逗号分隔（1~6 张；media_1 为场景面图，
                       media_2~N 为角色/场景/道具；每角色独立单图，严禁合并角色图）。
    --prompt <str>      与 --prompt-file 二选一。@xx 模块式英文提示词全文
                       （subject_definitions / @ / summary / retention_analysis /
                       detailed_description / overall_soundscape / non_diegetic_music）。
                       支持中文占位「@图片N」，自动替换为 __MINIMAX_H3_REF_N__。
    --prompt-file <path> 与 --prompt 二选一。指向 `视频X提示词_en.md`（新 en 双文件），自动提取
                       「## Detailed Description:」段 @xx 模块式全文作为节点7 prompt；
                       兼容旧单文件「## 提示词 英文」段；找不到则取全文。
    --seconds <float>  必填。镜头时长秒数（节点7 seconds）。
    --aspect-ratio <str> 可选。16:9 / 9:16，默认 16:9。同步映射两套画幅取值体系：
                     节点7（MiniMaxH3Easy）填简写 9:16/16:9，节点75（ResolutionSelector）填完整枚举名
                     9:16 (Portrait Widescreen)/16:9 (Widescreen)。勿临场混用，详见 15-视频-MiniMaxH3-Easy.md 防坑表。
    --resolution <str>  可选。节点7 resolution，默认 360P（低分辨率版）。
    --lixiang <path>   可选。立项文档（00_项目\立项.md）显式路径。默认自动探测：从参考图/out-dir
                       向上定位项目「美术风格」字段；美术风格含 真人/写实/photoreal/realistic/
                       live-action 等关键词判定为真人风格。
    --megapixels <float> 可选。节点75 megapixels 显式覆盖；省略时自动取值：真人风格 0.7 / 其它 0.3。
    --name <str>       可选。输出前缀名（节点93 filename_prefix 尾部，成片 manju/vc/<name>）。
    --seed <int>       可选。随机种子（节点14），不传随机生成。
    --no-sage          可选。移除节点102 SageAttentionPatch，节点9/66/76 的 model 直连
                       节点7（本机缺 triton 环境报错时的标准处理）。默认保留。
    --out-name <str>   可选。取回视频文件名（不含扩展名），默认 <name>。
    --out-dir <path>   可选。取回视频落盘目录（建议 05_视频\第N集）。
    --workflow-dir <path> 可选。工作流目录（含 15.MinimaxH3 多图参考01~20.json，默认脚本同目录 ../workflows）。
    --comfy-input <path>  可选。ComfyUI input 根目录（复制参考图，默认
                       D:\\Comfy-Desktop\\ComfyUI-Shared\\input）。
    --comfy-output <path> 可选。ComfyUI output 根目录（取回源件，默认
                       D:\\Comfy-Desktop\\ComfyUI-Shared\\output）。
    --temp-dir <path>  可选。临时 workflow JSON 写入目录。
    --dry-run          可选。只构造工作流 JSON 并打印关键信息，不真实提交。
    --server/--interval/--timeout/--max-retry 可选。内联提交引擎轮询/重试参数。

退出码:
    0 = 成功（视频已取回落盘）
    2 = 生成任务最终失败
    3 = 超时
    4 = 参数/模板/取回错误

标准输出（最后一行）:
    JSON 单行结果，字段:
      {
        "status": "success|error|timeout|error_submit|error_arg",
        "n_ref": 3, "workflow": "15.MinimaxH3 多图参考03.json",
        "name": "...", "seconds": 10, "seed": 123,
        "video_outputs": [...], "video_path": "<out-dir>/<out-name>.mp4" | null,
        "sage_patch_removed": true|false,
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
# 本脚本位于 ref\step-08\scripts\，工作流同目录上位 workflows\（ref\step-08\workflows\）
WORKFLOW_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "workflows"))
# 提交引擎已内联（自包含，无全局依赖）
DEFAULT_COMFY_INPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\input"
DEFAULT_COMFY_OUTPUT = r"D:\Comfy-Desktop\ComfyUI-Shared\output"

NODE_H3 = "7"          # MiniMaxH3Easy
NODE_SEED = "14"       # RandomNoise
NODE_RESOL = "75"      # ResolutionSelector
NODE_OUT = "93"        # VHS_VideoCombine 成片
NODE_SAGE = "102"      # MiniMaxH3MemoryEfficientSageAttentionPatch
SAGE_CONSUMERS = ["9", "66", "76"]   # model 来自 ['102',0]，--no-sage 时改直连 ['7',0]
# LoadImage 节点 = 201~200+N（第 i 张参考图填 str(200+i)），多图参考01~20 共用规则

INPUT_SUBDIR = "manju_vc"   # 参考图复制到 input\manju_vc\，LoadImage 填 manju_vc/<文件>
MAX_REFS = 20


def select_workflow(n_ref, workflow_dir):
    """按参考图数量 n_ref 自动选档：15.MinimaxH3 多图参考{n_ref:02d}.json（n_ref 1~20）。"""
    if not 1 <= n_ref <= MAX_REFS:
        raise ValueError(f"参考图数量 {n_ref} 超出 1~{MAX_REFS}（多图参考01~20 上限）")
    wf_name = f"15.MinimaxH3 多图参考{n_ref:02d}.json"
    wf_path = os.path.join(workflow_dir, wf_name)
    if not os.path.isfile(wf_path):
        raise FileNotFoundError(f"工作流模板不存在: {wf_path}")
    return wf_name, wf_path


def copy_refs_to_input(images, comfy_input_dir):
    """复制参考图进 input\\manju_vc\\，返回 [(manju_vc/文件名, 源路径)]。"""
    manju_dir = os.path.join(comfy_input_dir, INPUT_SUBDIR)
    os.makedirs(manju_dir, exist_ok=True)
    placed = []
    for i, src in enumerate(images, 1):
        if not os.path.isfile(src):
            raise FileNotFoundError(f"参考图不存在: {src}")
        ext = os.path.splitext(src)[1] or ".png"
        dst_name = f"REF_{i}{ext}"
        dst = os.path.join(manju_dir, dst_name)
        shutil.copy2(src, dst)
        if os.path.getsize(src) != os.path.getsize(dst):
            raise OSError(f"复制后字节数不一致: {src} -> {dst}")
        placed.append((f"{INPUT_SUBDIR}/{dst_name}", src))
    return placed


def convert_prompt(prompt):
    """把 @图片N 占位符替换为 __MINIMAX_H3_REF_N__（reference_mention_mode=index）。"""
    return re.sub(r"@图片(\d+)", lambda m: f"__MINIMAX_H3_REF_{m.group(1)}__", prompt)


def extract_prompt_from_file(prompt_file):
    """从 视频X提示词_en.md 提取提交用英文提示词。优先取「## Detailed Description:」标题之后、下一个 ## 标题之前的 @xx 模块式正文；兼容旧单文件结构（「## 提示词 英文」段）；找不到取全文。"""
    with open(prompt_file, encoding="utf-8") as fh:
        text = fh.read()
    # 新 en 双文件结构：## Detailed Description: 之后到下一个 ## 标题前的 @xx 模块式正文（提交全文）
    m = re.search(r"##\s*Detailed\s*Description\s*[:：]?[^\n]*\n(.*?)(?=\n##\s|\Z)", text, re.S)
    if m:
        return m.group(1).strip()
    # 旧单文件结构：## 提示词 英文（提交用…）之后到下一个 ## 标题前的英文提示词段
    m = re.search(r"##\s*提示词\s*英文[^\n]*\n(.*?)(?=\n##\s|\Z)", text, re.S)
    if m:
        return m.group(1).strip()
    # 兜底：取最后一个 ```text 代码块
    blocks = re.findall(r"```(?:text|plain)?\s*\n(.*?)\n```", text, re.S)
    if blocks:
        return blocks[-1].strip()
    return text.strip()


def find_lixiang(images, out_dir):
    """从参考图目录 / out-dir 向上定位项目立项文档（00_项目\\立项.md 或 立项.md）。
    返回立项文档绝对路径；找不到返回 None。"""
    bases = []
    if out_dir:
        bases.append(out_dir)
    for img in images or []:
        bases.append(os.path.dirname(img))
    for base in bases:
        d = os.path.abspath(base)
        for _ in range(10):  # 向上最多 10 层
            for name in (os.path.join(d, "00_项目", "立项.md"), os.path.join(d, "立项.md")):
                if os.path.isfile(name):
                    return name
            parent = os.path.dirname(d)
            if parent == d:
                break
            d = parent
    return None


REALISTIC_KEYWORDS = ("真人", "写实", "photoreal", "realistic", "live-action", "photo", "实拍")


def is_realistic_style(lixiang_path):
    """读立项.md 的「美术风格」字段，含真人风格关键词 → True，否则 False。
    兜底：无「美术风格」字段时扫全文档关键词。"""
    try:
        with open(lixiang_path, encoding="utf-8") as fh:
            text = fh.read()
    except OSError:
        return False
    m = re.search(r"(?im)^\s*美术风格\s*[:：]\s*(.+)$", text)
    scope = m.group(1) if m else text
    low = scope.lower()
    return any(k in low for k in REALISTIC_KEYWORDS)


def build_workflow(n_ref, prompt, seconds, seed, name, args, placed):
    """读取 15.MinimaxH3 多图参考{n_ref:02d}.json 模板并填充节点，返回 workflow dict。"""
    wf_name, wf_path = select_workflow(n_ref, args.workflow_dir)
    with open(wf_path, encoding="utf-8") as fh:
        workflow = json.load(fh)

    # 节点7：模式/分辨率/画幅/时长/提示词/媒体槽
    node7 = workflow[NODE_H3]["inputs"]
    node7["mode"] = "reference"
    node7["prompt"] = convert_prompt(prompt)
    node7["seconds"] = float(seconds)
    node7["resolution"] = args.resolution
    node7["fps"] = 24
    if args.aspect_ratio == "9:16":
        node7["aspect_ratio"] = "9:16"
        node7["width"] = 768
        node7["height"] = 1344
    else:
        node7["aspect_ratio"] = "16:9"
        node7["width"] = 1344
        node7["height"] = 768

    # 节点7：media_1~N 槽 → LoadImage 201~200+N；清理多余槽
    for i in range(1, MAX_REFS + 1):
        k_media = f"media_{i}"
        k_type = f"media_type_{i}"
        if i <= n_ref:
            node7[k_media] = [str(200 + i), 0]
            node7[k_type] = "image"
        else:
            node7.pop(k_media, None)
            node7.pop(k_type, None)

    # LoadImage 201~200+N（取前 N 个；多图参考系列无音频槽）
    for i, (manju_path, _src) in enumerate(placed, 1):
        nid = str(200 + i)
        if nid not in workflow:
            raise KeyError(f"工作流 {wf_name} 缺少 LoadImage 节点 {nid}")
        workflow[nid]["inputs"]["image"] = manju_path

    # 节点14 种子
    workflow[NODE_SEED]["inputs"]["noise_seed"] = seed
    # 节点75 画幅/分辨率
    # ⚠️ 防坑（2026-09-01）：节点75 ResolutionSelector 必须填完整枚举名（含括号后缀），
    # 与节点7 的简写（9:16/16:9）是两套取值体系。曾填 "9:16 (Portrait)" 导致校验失败、
    # 成片节点93被忽略、output 无成片。合法值含 '9:16 (Portrait Widescreen)'，无 '9:16 (Portrait)'。
    resol = workflow[NODE_RESOL]["inputs"]
    resol["aspect_ratio"] = "9:16 (Portrait Widescreen)" if args.aspect_ratio == "9:16" else "16:9 (Widescreen)"
    resol["megapixels"] = float(args.megapixels)
    # 节点93 输出前缀
    workflow[NODE_OUT]["inputs"]["filename_prefix"] = f"manju/vc/{name}"

    # 节点102 SageAttention：--no-sage 删除并改 9/66/76 直连节点7
    sage_removed = False
    if args.no_sage:
        workflow.pop(NODE_SAGE, None)
        for nid in SAGE_CONSUMERS:
            if nid in workflow and workflow[nid]["inputs"].get("model") == [NODE_SAGE, 0]:
                workflow[nid]["inputs"]["model"] = [NODE_H7 := "7", 0]
        sage_removed = True

    return wf_name, workflow, sage_removed


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
        prefix=f"video_{args.name}_", suffix=".json",
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
    ap = argparse.ArgumentParser(description="漫剧视频生成封装脚本（15.MinimaxH3 多图参考01~20 按N自动选档，step_08）")
    ap.add_argument("--images", required=True, help="本镜参考图绝对路径，逗号分隔（1~20张；media_1 本镜朝向匹配的场景面图，media_2~N 角色/场景/道具）")
    ap.add_argument("--prompt", default=None, help="@xx模块式英文提示词全文（与 --prompt-file 二选一）")
    ap.add_argument("--prompt-file", default=None, help="指向 镜头X_视频提示词.md（与 --prompt 二选一）")
    ap.add_argument("--seconds", required=True, type=float, help="镜头时长秒数")
    ap.add_argument("--aspect-ratio", default="16:9", choices=["16:9", "9:16"], help="画幅，默认16:9")
    ap.add_argument("--resolution", default="360P", help="节点7 resolution，默认360P（低分辨率版）")
    ap.add_argument("--lixiang", default=None, help="立项文档路径（00_项目\\立项.md），默认自动探测并按美术风格判定真人档")
    ap.add_argument("--megapixels", type=float, default=None, help="节点75 megapixels 显式覆盖；省略时按风格自动取值（真人0.7/默认0.3）")
    ap.add_argument("--name", default="YZ-H3", help="输出前缀名（成片 manju/vc/<name>）")
    ap.add_argument("--seed", type=int, default=None, help="随机种子，不传随机生成")
    ap.add_argument("--no-sage", action="store_true", help="移除节点102 SageAttention，9/66/76 直连节点7")
    ap.add_argument("--out-name", default=None, help="取回视频文件名（不含扩展名）")
    ap.add_argument("--out-dir", default=None, help="取回视频落盘目录（建议 05_视频\\第N集）")
    ap.add_argument("--workflow-dir", default=WORKFLOW_DIR, help="工作流目录（含 15.MinimaxH3 多图参考01~20.json）")
    ap.add_argument("--comfy-input", default=os.environ.get("COMFY_INPUT_DIR", DEFAULT_COMFY_INPUT),
                    help="ComfyUI input 根目录（复制参考图）")
    ap.add_argument("--comfy-output", default=os.environ.get("COMFY_OUTPUT_DIR", DEFAULT_COMFY_OUTPUT),
                    help="ComfyUI output 根目录（取回源件）")
    ap.add_argument("--temp-dir", default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--server", default="http://127.0.0.1:8188")
    ap.add_argument("--interval", type=int, default=10)
    ap.add_argument("--timeout", type=int, default=1800)
    ap.add_argument("--max-retry", type=int, default=2)
    args = ap.parse_args()

    if bool(args.prompt) == bool(args.prompt_file):
        ap.error("必须且只能提供 --prompt 或 --prompt-file 之一")
    prompt = args.prompt if args.prompt is not None else extract_prompt_from_file(args.prompt_file)
    if not prompt.strip():
        ap.error("提示词为空")

    images = [p.strip() for p in args.images.split(",") if p.strip()]
    n_ref = len(images)
    if not 1 <= n_ref <= MAX_REFS:
        ap.error(f"参考图数量 {n_ref} 超出 1~{MAX_REFS}")

    # 节点75 megapixels 自动取值：定位立项文档读「美术风格」，真人风格 → 0.7，其它 → 0.3；
    # 显式传了 --megapixels 则以其为准（覆盖自动判定）。
    realistic = False
    lixiang = None
    if args.megapixels is None:
        lixiang = args.lixiang or find_lixiang(images, args.out_dir)
        realistic = bool(lixiang) and is_realistic_style(lixiang)
        args.megapixels = 0.7 if realistic else 0.3

    start = time.time()
    try:
        seed = args.seed if args.seed is not None else random.randint(1, 2**31 - 1)
        placed = copy_refs_to_input(images, args.comfy_input)
        wf_name, workflow, sage_removed = build_workflow(
            n_ref, prompt, args.seconds, seed, args.name, args, placed
        )

        if args.dry_run:
            print(json.dumps({
                "status": "success",
                "dry_run": True,
                "n_ref": n_ref,
                "workflow": wf_name,
                "name": args.name, "seconds": args.seconds,
                "aspect_ratio": args.aspect_ratio, "seed": seed,
                "realistic": realistic, "lixiang": lixiang, "megapixels": args.megapixels,
                "sage_patch_removed": sage_removed,
                "refs": [p for p, _ in placed],
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
        result["video_outputs"] = fetched
        result["n_ref"] = n_ref
        result["workflow"] = wf_name
        result["name"] = args.name
        result["seconds"] = args.seconds
        result["seed"] = seed
        result["realistic"] = realistic
        result["lixiang"] = lixiang
        result["megapixels"] = args.megapixels
        result["sage_patch_removed"] = sage_removed

        video_path = None
        if fetched:
            # 优先取成片（节点93 前缀 manju/vc/<name> 对应源件），兜底取第一个存在源件
            prefix = f"manju/vc/{args.name}"
            src = next((f["source_path"] for f in fetched
                        if f["exists"] and prefix in f["source_path"].replace("\\", "/")), None)
            if src is None:
                src = next((f["source_path"] for f in fetched if f["exists"]), None)
            if src is None:
                result["status"] = "error"
                result["error"] = f"取回源件不存在于 output 目录: {[f['source_path'] for f in fetched]}"
                print(json.dumps(result, ensure_ascii=False))
                sys.exit(4)
            ext = os.path.splitext(fetched[0]["filename"])[1] or ".mp4"
            if args.out_dir:
                out_name = args.out_name or args.name
                video_path = os.path.join(args.out_dir, f"{out_name}{ext}")
                copyfile(src, video_path)

        result["video_path"] = video_path
        result["elapsed_sec"] = int(time.time() - start)
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)

    except Exception as e:
        print(json.dumps({
            "status": "error_arg",
            "n_ref": len(images),
            "name": args.name,
            "error": f"{type(e).__name__}: {e}",
            "elapsed_sec": int(time.time() - start),
        }, ensure_ascii=False))
        sys.exit(4)


if __name__ == "__main__":
    main()
