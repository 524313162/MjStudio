#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
MjStudio 内置 API 命令行客户端 —— 仅依赖 Python 3 标准库，可随技能迁移到任何 Agent。

用法：
    python mj.py <command> [options]

环境类：
    ensure                就绪保障：API 不通则按 config.json 的 exePath 拉起 MjStudio，轮询到 healthy
    health                健康检查
    settings-get          读取软件设置（dbRoot / resourcesRoot / apiPort 等）

项目（第一步 立项）：
    project-list                      列出全部项目名
    project-current                   当前项目
    ensure-project --name X [...]     幂等创建 + 加载为当前项目（已存在则复用）
    project-load --name X             加载（切换）当前项目
    project-get  --name X             查询项目详情
    project-delete --name X           删除项目

剧本（第二步）：
    story-list                        列当前项目全部剧本
    story-get --episode-no 1          按集号取剧本
    story-upsert ...                  创建/更新剧本（按 episodeNo 幂等）
    story-delete --id 1001

分镜（第三步）：
    episode-list                      列当前项目全部集
    episode-upsert ...                创建/更新集（按 episodeNo 幂等）
    shot-list --episode-id 1001       列某集全部镜头
    shot-upsert --data-file s.json    单个镜头（按 shotNo 幂等）
    shot-upsert-many --episode-no 1 --data-file shots.json   批量镜头（按 shotNo 幂等）
    shot-delete --id 3001

评审：
    review-get --stage 剧本
    review-put --stage 剧本 --decision 通过 [--content-file a.md] [--issues-file b.md]
    review-revision --stage 剧本 --issues-file b.md      记一次打回（计数+1，>5 自动升级）

通用约定：
    * 中文一律通过「UTF-8 文件」传入（--xxx-file），不要拼在命令行里（Windows 下易乱码）。
    * --data-file 指向完整 JSON payload 文件（对象或数组），优先级高于其它单字段参数。
    * 所有输出为 UTF-8 JSON；失败时进程非 0 退出并打印 [HTTP xxx] 原因。
"""

import argparse
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEFAULT_CONFIG = os.path.join(ROOT, "config.json")

STAGES = {"立项": 0, "剧本": 1, "分镜": 2, "资产": 3, "声线": 4,
          "音乐": 5, "音效": 6, "视频": 7, "配音": 8, "合成": 9}
DECISIONS = {"待评审": 0, "通过": 1, "打回": 2, "升级": 3}

# 项目里必须按 int 提交的字段（枚举/数值），传字符串会被服务端判 400
PROJECT_INT_FIELDS = {"totalEpisodes", "episodeDuration", "aspectRatio", "status"}


# ---------------------------------------------------------------- 基础

def load_config(path=None):
    p = path or os.environ.get("MJSTUDIO_CONFIG") or DEFAULT_CONFIG
    cfg = {"baseUrl": "http://127.0.0.1:6066", "exePath": "", "launchTimeoutSec": 90}
    if os.path.exists(p):
        with open(p, "r", encoding="utf-8") as f:
            cfg.update(json.load(f) or {})
    return cfg


def call(cfg, method, path, body=None, timeout=30):
    url = cfg["baseUrl"].rstrip("/") + path
    data = None
    if body is not None:
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(url, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json; charset=utf-8")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode("utf-8")
            return json.loads(raw) if raw.strip() else None
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", "replace")
        try:
            msg = json.loads(raw).get("message", raw)
        except Exception:
            msg = raw
        raise SystemExit("[HTTP %s] %s %s -> %s" % (e.code, method, path, msg))
    except urllib.error.URLError as e:
        raise SystemExit("[网络错误] %s %s -> %s（MjStudio 未启动？先跑 mj.py ensure）"
                         % (method, path, e.reason))


def out(obj):
    print(json.dumps(obj, ensure_ascii=False, indent=2) if not isinstance(obj, str) else obj)


def q(name):
    """URL 路径中的中文项目名需编码"""
    return urllib.parse.quote(str(name), safe="")


def read_text_file(p, field):
    if not p:
        return None
    if not os.path.exists(p):
        raise SystemExit("[文件不存在] %s (%s)" % (p, field))
    with open(p, "r", encoding="utf-8") as f:
        return f.read()


def read_json_file(p, field):
    txt = read_text_file(p, field)
    try:
        return json.loads(txt)
    except Exception as e:
        raise SystemExit("[JSON 解析失败] %s (%s): %s" % (p, field, e))


def stage_value(s):
    if s is None:
        raise SystemExit("缺少 --stage")
    if isinstance(s, int) or str(s).isdigit():
        return int(s)
    if s in STAGES:
        return STAGES[s]
    raise SystemExit("未知 stage: %s（可选：%s）" % (s, " / ".join(STAGES)))


def decision_value(d):
    if d is None:
        raise SystemExit("缺少 --decision")
    if isinstance(d, int) or str(d).isdigit():
        return int(d)
    if d in DECISIONS:
        return DECISIONS[d]
    raise SystemExit("未知 decision: %s（可选：%s）" % (d, " / ".join(DECISIONS)))


def _snake(key):
    """characterList -> character_list（argparse 的 dest 是 snake_case）"""
    return re.sub(r"(?<!^)(?=[A-Z])", "_", key).lower()


def pick_file_or_text(args, key):
    """优先 --xxx-file，其次 --xxx；key 用 camelCase 字段名即可"""
    base = _snake(key)
    f = getattr(args, base + "_file", None)
    if f:
        return read_text_file(f, key)
    return getattr(args, base, None)


# ---------------------------------------------------------------- 环境

def cmd_health(args, cfg):
    out(call(cfg, "GET", "/health"))


def health_ok(cfg):
    try:
        call(cfg, "GET", "/health", timeout=5)
        return True
    except SystemExit:
        return False
    except Exception:
        return False


def cmd_ensure(args, cfg):
    if health_ok(cfg):
        out({"status": "healthy", "action": "none", "baseUrl": cfg["baseUrl"]})
        return
    exe = args.exe or cfg.get("exePath")
    if not exe or not os.path.exists(exe):
        raise SystemExit(
            "MjStudio 未运行，且无法自动拉起：exePath 未配置或不存在（%r）。\n"
            "请手动启动 MjStudio，或修改 config.json 的 exePath / 用 --exe 指定。" % exe)
    flags = 0
    if os.name == "nt":
        flags = 0x00000008 | 0x00000200  # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
    subprocess.Popen([exe], cwd=os.path.dirname(exe) or None, creationflags=flags)
    deadline = time.time() + int(args.timeout or cfg.get("launchTimeoutSec") or 90)
    while time.time() < deadline:
        time.sleep(2)
        if health_ok(cfg):
            out({"status": "healthy", "action": "launched", "exe": exe, "baseUrl": cfg["baseUrl"]})
            return
    raise SystemExit("已尝试拉起 %s，但 %ss 内 API 仍未就绪，请手动检查 MjStudio 窗口/端口设置。" % (exe, args.timeout))


def cmd_settings_get(args, cfg):
    out(call(cfg, "GET", "/api/settings"))


# ---------------------------------------------------------------- 项目

def cmd_project_list(args, cfg):
    out(call(cfg, "GET", "/api/projects"))


def cmd_project_current(args, cfg):
    out(call(cfg, "GET", "/api/projects/current"))


def cmd_project_load(args, cfg):
    out(call(cfg, "POST", "/api/projects/load/" + q(args.name)))


def cmd_project_get(args, cfg):
    out(call(cfg, "POST", "/api/projects/load/" + q(args.name)))


def cmd_project_delete(args, cfg):
    out(call(cfg, "DELETE", "/api/projects/" + q(args.name)))


def cmd_ensure_project(args, cfg):
    payload = None
    if args.data_file:
        payload = read_json_file(args.data_file, "data-file")
        payload.setdefault("name", args.name)
    else:
        payload = {"name": args.name}
        for k, v in [("storyName", args.story_name), ("worldview", args.worldview),
                     ("description", args.description), ("totalEpisodes", args.total_episodes),
                     ("episodeDuration", args.episode_duration), ("aspectRatio", args.aspect_ratio),
                     ("targetPlatform", args.target_platform), ("genre", args.genre),
                     ("audience", args.audience), ("artStyle", args.art_style),
                     ("voiceLanguage", args.voice_language),
                     ("originalOrAdapted", args.original_or_adapted),
                     ("bgmStyle", args.bgm_style), ("deliverables", args.deliverables)]:
            if v is not None:
                # 枚举/数值字段必须是 int，传字符串会导致服务端 400
                payload[k] = int(v) if k in PROJECT_INT_FIELDS else v
        for k in ("worldview", "description", "deliverables"):
            f = getattr(args, k + "_file", None)
            if f:
                payload[k] = read_text_file(f, k)
        if args.has_opening_ending is not None:
            payload["hasOpeningEnding"] = bool(args.has_opening_ending)
        if args.status is not None:
            payload["status"] = int(args.status)

    # 兜底：--data-file 里数字写成字符串时也转成 int，避免服务端枚举解析 400
    for k in PROJECT_INT_FIELDS:
        v = payload.get(k)
        if isinstance(v, str) and v.strip().lstrip("-").isdigit():
            payload[k] = int(v)

    name = payload.get("name")
    if not name:
        raise SystemExit("缺少项目名（--name 或 data-file 里的 name）")

    names = call(cfg, "GET", "/api/projects") or []
    if name in names:
        proj = call(cfg, "POST", "/api/projects/load/" + q(name))
        changed = False
        if args.update_existing:
            patch = {k: v for k, v in payload.items() if k != "name" and v is not None}
            if patch:
                full = dict(proj)
                full.update(patch)
                full["name"] = name
                proj = call(cfg, "PUT", "/api/projects/%s" % proj["id"], full)
                changed = True
        out({"action": "updated" if changed else "reused", "project": proj})
    else:
        proj = call(cfg, "POST", "/api/projects", payload)
        proj = call(cfg, "POST", "/api/projects/load/" + q(name))
        out({"action": "created", "project": proj})


# ---------------------------------------------------------------- 剧本

def cmd_story_list(args, cfg):
    out(call(cfg, "GET", "/api/stories"))


def cmd_story_get(args, cfg):
    out(call(cfg, "GET", "/api/stories/episode/%s" % int(args.episode_no)))


def cmd_story_delete(args, cfg):
    out(call(cfg, "DELETE", "/api/stories/%s" % int(args.id)))


def cmd_story_upsert(args, cfg):
    if args.data_file:
        payload = read_json_file(args.data_file, "data-file")
    else:
        payload = {}
        for k, v in [("episodeNo", args.episode_no), ("title", args.title),
                     ("duration", args.duration)]:
            if v is not None:
                payload[k] = int(v) if k in ("episodeNo", "duration") else v
        for k in ("characterList", "content"):
            val = pick_file_or_text(args, k)
            if val is not None:
                payload[k] = val
    if "episodeNo" not in payload:
        raise SystemExit("缺少 episodeNo")

    existing = {}
    for s in (call(cfg, "GET", "/api/stories") or []):
        existing[int(s["episodeNo"])] = s["id"]
    eid = existing.get(int(payload["episodeNo"]))
    if eid:
        payload["id"] = eid
    res = call(cfg, "POST", "/api/stories", payload)
    out({"action": "updated" if eid else "created", "story": res})


# ---------------------------------------------------------------- 分镜

def cmd_episode_list(args, cfg):
    out(call(cfg, "GET", "/api/episodes"))


def cmd_episode_upsert(args, cfg):
    if args.data_file:
        payload = read_json_file(args.data_file, "data-file")
    else:
        payload = {}
        if args.episode_no is not None:
            payload["episodeNo"] = int(args.episode_no)
        if args.name is not None:
            payload["name"] = args.name
        if args.duration is not None:
            payload["duration"] = int(args.duration)
        for k in ("assetWhitelist", "cameraSwitchTable", "audioCueTable", "dialogueList"):
            val = pick_file_or_text(args, k)
            if val is not None:
                payload[k] = val
    if "episodeNo" not in payload:
        raise SystemExit("缺少 episodeNo")

    existing = {}
    for e in (call(cfg, "GET", "/api/episodes") or []):
        existing[int(e["episodeNo"])] = e["id"]
    eid = existing.get(int(payload["episodeNo"]))
    if eid:
        payload["id"] = eid
    res = call(cfg, "POST", "/api/episodes", payload)
    out({"action": "updated" if eid else "created", "episodeId": res["id"], "episode": res})


def cmd_shot_list(args, cfg):
    out(call(cfg, "GET", "/api/episodes/%s/shots" % int(args.episode_id)))


def cmd_shot_delete(args, cfg):
    out(call(cfg, "DELETE", "/api/shots/%s" % int(args.id)))


def _resolve_episode_id(cfg, args):
    if args.episode_id:
        return int(args.episode_id)
    if args.episode_no is None:
        raise SystemExit("需要 --episode-id 或 --episode-no")
    for e in (call(cfg, "GET", "/api/episodes") or []):
        if int(e["episodeNo"]) == int(args.episode_no):
            return e["id"]
    raise SystemExit("当前项目没有第 %s 集，请先 episode-upsert" % args.episode_no)


def _upsert_shots(cfg, episode_id, shots):
    if isinstance(shots, dict):
        shots = [shots]
    if not isinstance(shots, list):
        raise SystemExit("镜头数据必须是对象或数组")
    existing = {}
    for s in (call(cfg, "GET", "/api/episodes/%s/shots" % episode_id) or []):
        existing[int(s["shotNo"])] = s["id"]
    results = []
    for sh in shots:
        body = dict(sh)
        body["episodeId"] = int(episode_id)
        if "shotNo" not in body:
            raise SystemExit("镜头缺少 shotNo：%s" % json.dumps(sh, ensure_ascii=False)[:120])
        sid = existing.get(int(body["shotNo"]))
        if sid:
            body["id"] = sid
        res = call(cfg, "POST", "/api/shots", body)
        results.append({"shotNo": res["shotNo"], "id": res["id"],
                        "action": "updated" if sid else "created"})
    out({"episodeId": int(episode_id), "count": len(results), "results": results})


def cmd_shot_upsert(args, cfg):
    payload = read_json_file(args.data_file, "data-file")
    _upsert_shots(cfg, _resolve_episode_id(cfg, args), payload)


def cmd_shot_upsert_many(args, cfg):
    payload = read_json_file(args.data_file, "data-file")
    _upsert_shots(cfg, _resolve_episode_id(cfg, args), payload)


# ---------------------------------------------------------------- 评审

def cmd_review_get(args, cfg):
    if args.all:
        out(call(cfg, "GET", "/api/reviews"))
        return
    out(call(cfg, "GET", "/api/reviews/%s" % stage_value(args.stage)))


def cmd_review_put(args, cfg):
    st = stage_value(args.stage)
    body = {"decision": decision_value(args.decision)}
    c = read_text_file(args.content_file, "content") if args.content_file else args.content
    if c is not None:
        body["content"] = c
    i = read_text_file(args.issues_file, "issues") if args.issues_file else args.issues
    if i is not None:
        body["issues"] = i
    out(call(cfg, "PUT", "/api/reviews/%s" % st, body))


def cmd_review_revision(args, cfg):
    st = stage_value(args.stage)
    body = {"decision": 2}
    i = read_text_file(args.issues_file, "issues") if args.issues_file else args.issues
    if i is not None:
        body["issues"] = i
    out(call(cfg, "POST", "/api/reviews/%s/revision" % st, body))


# ---------------------------------------------------------------- CLI

def build_parser():
    p = argparse.ArgumentParser(prog="mj.py", description="MjStudio 内置 API 客户端（stdlib only）")
    p.add_argument("--config", help="config.json 路径（默认技能根目录）")
    p.add_argument("--base-url", help="覆盖 baseUrl，如 http://127.0.0.1:6066")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("ensure", help="就绪保障：不通则拉起 exe 并轮询")
    s.add_argument("--exe")
    s.add_argument("--timeout", type=int)
    s.set_defaults(func=cmd_ensure)

    s = sub.add_parser("health")
    s.set_defaults(func=cmd_health)

    s = sub.add_parser("settings-get")
    s.set_defaults(func=cmd_settings_get)

    s = sub.add_parser("project-list")
    s.set_defaults(func=cmd_project_list)

    s = sub.add_parser("project-current")
    s.set_defaults(func=cmd_project_current)

    s = sub.add_parser("project-load")
    s.add_argument("--name", required=True)
    s.set_defaults(func=cmd_project_load)

    s = sub.add_parser("project-get")
    s.add_argument("--name", required=True)
    s.set_defaults(func=cmd_project_get)

    s = sub.add_parser("project-delete")
    s.add_argument("--name", required=True)
    s.set_defaults(func=cmd_project_delete)

    s = sub.add_parser("ensure-project", help="幂等创建并加载当前项目")
    s.add_argument("--name", required=True)
    s.add_argument("--data-file")
    s.add_argument("--update-existing", action="store_true", help="已存在时用这些字段覆盖")
    for f, h in [("story-name", "故事名"), ("worldview", "世界观"), ("description", "项目描述"),
                 ("total-episodes", "总集数"), ("episode-duration", "每集时长秒"),
                 ("aspect-ratio", "画幅 1=9:16 2=16:9 3=1:1"),
                 ("target-platform", "目标平台"), ("genre", "题材"), ("audience", "受众"),
                 ("art-style", "美术风格（含风格锚点）"), ("voice-language", "配音语言"),
                 ("original-or-adapted", "原创/改编"), ("bgm-style", "BGM风格"),
                 ("deliverables", "交付物"), ("status", "0进行中 1已完结")]:
        s.add_argument("--" + f, help=h)
    s.add_argument("--has-opening-ending", dest="has_opening_ending", choices=["0", "1", "true", "false"])
    for f in ("worldview", "description", "deliverables"):
        s.add_argument("--%s-file" % f, help="从 UTF-8 文件读入 %s" % f)
    s.set_defaults(func=cmd_ensure_project)

    s = sub.add_parser("story-list")
    s.set_defaults(func=cmd_story_list)

    s = sub.add_parser("story-get")
    s.add_argument("--episode-no", required=True)
    s.set_defaults(func=cmd_story_get)

    s = sub.add_parser("story-delete")
    s.add_argument("--id", required=True)
    s.set_defaults(func=cmd_story_delete)

    s = sub.add_parser("story-upsert")
    s.add_argument("--episode-no")
    s.add_argument("--title")
    s.add_argument("--character-list")
    s.add_argument("--character-list-file")
    s.add_argument("--content")
    s.add_argument("--content-file", help="剧本正文 Markdown 文件（UTF-8）")
    s.add_argument("--duration")
    s.add_argument("--data-file", help="完整 JSON payload 文件")
    s.set_defaults(func=cmd_story_upsert)

    s = sub.add_parser("episode-list")
    s.set_defaults(func=cmd_episode_list)

    s = sub.add_parser("episode-upsert")
    s.add_argument("--episode-no")
    s.add_argument("--name")
    s.add_argument("--duration")
    s.add_argument("--asset-whitelist")
    s.add_argument("--asset-whitelist-file")
    s.add_argument("--camera-switch-table")
    s.add_argument("--camera-switch-table-file")
    s.add_argument("--audio-cue-table")
    s.add_argument("--audio-cue-table-file")
    s.add_argument("--dialogue-list")
    s.add_argument("--dialogue-list-file")
    s.add_argument("--data-file")
    s.set_defaults(func=cmd_episode_upsert)

    s = sub.add_parser("shot-list")
    s.add_argument("--episode-id", required=True)
    s.set_defaults(func=cmd_shot_list)

    s = sub.add_parser("shot-delete")
    s.add_argument("--id", required=True)
    s.set_defaults(func=cmd_shot_delete)

    s = sub.add_parser("shot-upsert")
    s.add_argument("--episode-id")
    s.add_argument("--episode-no")
    s.add_argument("--data-file", required=True)
    s.set_defaults(func=cmd_shot_upsert)

    s = sub.add_parser("shot-upsert-many")
    s.add_argument("--episode-id")
    s.add_argument("--episode-no")
    s.add_argument("--data-file", required=True)
    s.set_defaults(func=cmd_shot_upsert_many)

    s = sub.add_parser("review-get")
    s.add_argument("--stage")
    s.add_argument("--all", action="store_true", help="列出全部环节评审")
    s.set_defaults(func=cmd_review_get)

    s = sub.add_parser("review-put")
    s.add_argument("--stage", required=True)
    s.add_argument("--decision", required=True, help="待评审/通过/打回/升级 或 0/1/2/3")
    s.add_argument("--content")
    s.add_argument("--content-file")
    s.add_argument("--issues")
    s.add_argument("--issues-file")
    s.set_defaults(func=cmd_review_put)

    s = sub.add_parser("review-revision", help="记一次打回（计数+1，>5 自动升级）")
    s.add_argument("--stage", required=True)
    s.add_argument("--issues")
    s.add_argument("--issues-file")
    s.set_defaults(func=cmd_review_revision)

    return p


def main():
    args = build_parser().parse_args()
    cfg = load_config(args.config)
    if args.base_url:
        cfg["baseUrl"] = args.base_url
    if getattr(args, "has_opening_ending", None) in ("1", "true"):
        args.has_opening_ending = True
    elif getattr(args, "has_opening_ending", None) in ("0", "false"):
        args.has_opening_ending = False
    args.func(args, cfg)


if __name__ == "__main__":
    main()
