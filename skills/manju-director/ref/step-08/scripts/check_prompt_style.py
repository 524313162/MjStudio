#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_prompt_style.py — 提示词风格锚点自动校验（防"风格串跑偏"再犯）
=====================================================================
用途：
  批量生成 视频/资产 提示词后，出图/出片前跑一遍，校验每个文件的正向提示词
  风格行是否锚定立项美术设定，并拦截命中反向词（水彩/国漫/动漫/卡通/厚涂等）的措辞。

用法：
  python check_prompt_style.py --dir <提示词目录> [--anchor <立项.md 路径>] [--pattern "视频*提示词_cn.md"]
  缺省 --dir 时使用当前目录；--anchor 缺省时自动在 --dir 向上 3 层内找 立项.md。

校验项：
  [FAIL] 风格行命中立项反向锚点词（如 动漫/卡通/水彩/国漫/厚涂/赛璐璐/3D/CGI/anime/cartoon/watercolor/cel shading...）
  [FAIL] 风格行未写"根据项目设定一致"/"consistent with the project setting"（疑似自由自定义措辞）
  [WARN] 同批多份文件风格行未逐字一致（公共风格串防跑偏）
  [PASS] 全部通过
退出码：0=全部通过，1=存在 FAIL，2=存在 WARN（无 FAIL）
"""
import argparse, glob, os, re, sys

# 通用硬性反向词（无论立项怎么写都要拦的动漫/水彩类措辞）
HARD_NEG = ["动漫", "卡通", "水彩", "国漫", "厚涂", "赛璐璐", "3D渲染", "3d渲染", "CGI",
            "anime", "cartoon", "watercolor", "cel shading", "thick paint", "3D render", "3d render", "manga"]
# 正向强制约束（防自定义措辞）
CN_REQ = ["根据项目设定一致"]
EN_REQ = ["consistent with the project setting"]

def find_anchor(start_dir):
    d = os.path.abspath(start_dir)
    for _ in range(4):
        cand = os.path.join(d, "00_项目", "立项.md")
        if os.path.exists(cand):
            return cand
        cand2 = os.path.join(d, "立项.md")
        if os.path.exists(cand2):
            return cand2
        d = os.path.dirname(d)
    return None

def load_anchor(path):
    """从立项.md 提取美术风格名、正/反向锚点词"""
    pos, neg, name = [], [], ""
    try:
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                m = re.match(r"美术风格[：:]\s*(.+)", line)
                if m:
                    name = m.group(1).strip()
                for kw in ("反向锚点", "反向："):
                    if kw in line and ":" in line or "：" in line:
                        if "反向" in line:
                            val = re.split(r"[：:]", line, 1)[-1].strip()
                            neg.extend(re.split(r"[，,、]", val))
                for kw in ("正向锚点", "正向："):
                    if "正向" in line and (":" in line or "：" in line):
                        val = re.split(r"[：:]", line, 1)[-1].strip()
                        pos.extend(re.split(r"[，,、]", val))
    except Exception as e:
        print(f"[warn] 读取立项锚点失败: {e}")
    neg = [w for w in neg if w and len(w) >= 2]
    pos = [w for w in pos if w and len(w) >= 2]
    return name, pos, neg

def collect_style_lines(files):
    """返回 {文件名: (风格行原文, 语言类型)} 仅取正向提示词部分的首个风格行"""
    res = {}
    for f in files:
        style = None
        in_neg = False
        try:
            with open(f, encoding="utf-8") as fh:
                for line in fh:
                    s = line.strip()
                    if not s:
                        continue
                    if s.startswith("# 反向提示词") or s.startswith("## Negative") or s.startswith("# Negative"):
                        in_neg = True
                        continue
                    if in_neg:
                        continue
                    if s.startswith("风格：") or s.startswith("风格:"):
                        style = ("cn", s)
                        break
                    if s.startswith("Style:"):
                        style = ("en", s)
                        break
        except Exception as e:
            res[f] = (None, f"[读取失败:{e}]")
            continue
        res[f] = style if style else (None, "(未找到风格行)")
    return res

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=".")
    ap.add_argument("--anchor", default=None)
    ap.add_argument("--pattern", default="*提示词_cn.md")
    args = ap.parse_args()

    files = sorted(glob.glob(os.path.join(args.dir, args.pattern)))
    # 若 cn 模式命中 0 份，尝试也扫 en
    if not files and "*_cn.md" in args.pattern:
        files = sorted(glob.glob(os.path.join(args.dir, args.pattern.replace("_cn.md", "*_en.md"))))
    if not files:
        print(f"[!] {args.dir} 下未匹配到 {args.pattern}")
        sys.exit(1)

    anchor = args.anchor or find_anchor(args.dir)
    name, pos, neg = ("", [], [])
    if anchor:
        name, pos, neg = load_anchor(anchor)
        print(f"立项锚点: {os.path.basename(anchor)}  美术风格='{name}'  反向词数={len(neg)}")

    neg_pool = set(HARD_NEG) | set(w.lower() for w in neg)
    styles = collect_style_lines(files)

    fail, warn = [], []
    seen = {}
    for f in files:
        lang, st = styles[f]
        base = os.path.basename(f)
        if lang is None:
            fail.append((base, "未找到正向风格行"))
            continue
        low = st.lower()
        hit_neg = [w for w in neg_pool if w and w in low]
        if hit_neg:
            fail.append((base, f"命中反向词 {hit_neg}"))
        if lang == "cn" and not any(r in st for r in CN_REQ):
            fail.append((base, "未写'根据项目设定一致'（疑似自定义措辞）"))
        if lang == "en" and not any(r in low for r in EN_REQ):
            fail.append((base, "未写 'consistent with the project setting'"))
        seen.setdefault(st, []).append(base)

    if len(seen) > 1:
        warn.append((f"共{len(files)}份风格行非逐字一致，出现{len(seen)}种写法", [b for v in seen.values() for b in v][:3]))

    print("=" * 60)
    for base, msg in fail:
        print(f"[FAIL] {base}: {msg}")
    for base, msg in warn:
        print(f"[WARN] {msg}")
    if not fail and not warn:
        print(f"[PASS] {len(files)} 份提示词风格行全部锚定立项设定且逐字一致")
    print("=" * 60)
    sys.exit(0 if not fail else (1 if not warn else 2))

if __name__ == "__main__":
    main()
