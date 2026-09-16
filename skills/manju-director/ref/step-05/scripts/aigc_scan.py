#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
aigc_scan.py —— 声线种子 AIGC 声明语音扫描（生成端硬拦截 + 评审端复核共用）
=====================================================================
用本地 faster-whisper 把种子 wav / mp4 转写为文本，检测是否含 AIGC 声明语音
（如「本音频由人工智能生成」「内容由AI生成，仅供参考」等平台声明）。
不落盘任何转写文件（区别于 transcribe_timestamps.py），结果以 JSON 输出。

用法（须用 faster_whisper 的 venv python 运行）:
    D:\\faster_whisper\\venv\\Scripts\\python.exe aigc_scan.py <音频或视频路径> [模型大小]

模型: tiny/base/small/medium/large-v3（默认 small，中文优先本地 small 模型）

标准输出（最后一行）JSON:
    {
      "ok": true/false,          # false = 命中 AIGC 声明
      "duration": <秒>,
      "language": "...",
      "segment_count": n,
      "hits": [ {"keyword":"...", "segment": {"start":..,"end":..,"text":".."}} ],
      "segments": [...]
    }

退出码: 0 = 无 AIGC 声明 / 2 = 命中声明 / 4 = 参数或转写错误
"""
import json
import os
import sys

MODEL_ROOT = r"D:\faster_whisper\models"

# 平台 AIGC 声明/水印关键词（命中任一即判不合格）
AIGC_KEYWORDS = [
    "人工智能生成", "AI 生成", "AI生成", "由AI生成", "智能生成",
    "仅供参考", "AI 创作", "AI创作", "人工智能创作",
]


def resolve_model(model_size):
    """本地模型目录优先（D:\\faster_whisper\\models\\<size>），否则回退 HF 在线下载。"""
    local_dir = os.path.join(MODEL_ROOT, model_size)
    if os.path.isdir(local_dir):
        return local_dir
    return model_size


def main():
    if len(sys.argv) < 2:
        print(json.dumps({"ok": False, "error": "用法: python aigc_scan.py <音频或视频路径> [模型]"}))
        return 4
    src = sys.argv[1]
    model_size = sys.argv[2] if len(sys.argv) > 2 else "small"
    if not os.path.isfile(src):
        print(json.dumps({"ok": False, "error": f"文件不存在: {src}"}))
        return 4

    try:
        from faster_whisper import WhisperModel
        model = WhisperModel(
            resolve_model(model_size), device="cpu",
            compute_type="int8", download_root=MODEL_ROOT,
        )
        # 注意: 必须 vad_filter=False 全量转写。
        # 此前用 vad_filter=True 会把声明语音段当作静音/非语音过滤掉, 造成漏检。
        segments, info = model.transcribe(
            src, language="zh", vad_filter=False, beam_size=5,
        )
        segs = [
            {"start": round(s.start, 3), "end": round(s.end, 3), "text": s.text.strip()}
            for s in segments
        ]
        hits = []
        for s in segs:
            for kw in AIGC_KEYWORDS:
                if kw in s["text"]:
                    hits.append({"keyword": kw, "segment": s})
                    break  # 一句命中一个关键词即可
        duration = getattr(info, "duration", None)
        if duration is None and segs:
            duration = segs[-1]["end"]
        result = {
            "ok": len(hits) == 0,
            "duration": round(duration, 3) if duration is not None else 0,
            "language": info.language,
            "language_probability": round(info.language_probability, 3),
            "segment_count": len(segs),
            "hits": hits,
            "segments": segs,
        }
        print(json.dumps(result, ensure_ascii=False))
        return 0 if result["ok"] else 2
    except Exception as e:
        print(json.dumps({"ok": False, "error": f"{type(e).__name__}: {e}"}))
        return 4


if __name__ == "__main__":
    sys.exit(main())
