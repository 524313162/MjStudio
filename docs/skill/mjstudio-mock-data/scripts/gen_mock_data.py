# -*- coding: utf-8 -*-
"""
模拟假数据生成脚本：通过 MjStudio 内置 API 生成「项目 + 剧本」数据。

用法：
    python scripts/gen_mock_data.py [--base http://127.0.0.1:6066] [--project 测试漫剧]

说明：
    - 默认在「测试漫剧」项目下生成 3 集剧本（第1~3集）。
    - 若指定 --project 为已存在项目，则直接使用；否则先创建该项目。
    - 幂等：已存在的集数会跳过（不重复创建）。
"""
import argparse
import json
import sys
import urllib.request
import urllib.error
import urllib.parse

BASE = "http://127.0.0.1:6066"


# ==================== HTTP 工具 ====================
def api(method, path, body=None, timeout=15, base=None):
    url = (base or BASE) + path
    data = None
    headers = {"Content-Type": "application/json"}
    if body is not None:
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read().decode("utf-8")
            return resp.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", errors="replace")
        print(f"  [HTTP {e.code}] {method} {path} -> {raw[:300]}")
        return e.code, None


def get(path, base=None):
    return api("GET", path, base=base)


def post(path, body, base=None):
    return api("POST", path, body, base=base)


def put(path, body, base=None):
    return api("PUT", path, body, base=base)


# ==================== 项目数据 ====================
def create_project(name, story_name, worldview, description, total_episodes,
                   episode_duration, aspect_ratio, target_platform, genre,
                   audience, art_style, voice_language, original_or_adapted,
                   bgm_style, has_opening_ending, deliverables, status, base=None):
    """创建项目（若已存在则跳过）"""
    code, names = get("/api/projects", base=base)
    if names and name in names:
        print(f"[项目] 已存在，跳过创建：{name}")
        return True
    body = {
        "name": name,
        "storyName": story_name,
        "worldview": worldview,
        "description": description,
        "totalEpisodes": total_episodes,
        "episodeDuration": episode_duration,
        "aspectRatio": aspect_ratio,
        "targetPlatform": target_platform,
        "genre": genre,
        "audience": audience,
        "artStyle": art_style,
        "voiceLanguage": voice_language,
        "originalOrAdapted": original_or_adapted,
        "bgmStyle": bgm_style,
        "hasOpeningEnding": has_opening_ending,
        "deliverables": deliverables,
        "status": status,
    }
    code, data = post("/api/projects", body, base=base)
    if code == 200 and data:
        print(f"[项目] 创建成功：{name} (id={data.get('id')}, status={data.get('status')})")
        return True
    print(f"[项目] 创建失败：{name}")
    return False


def load_project(name, base=None):
    """加载（切换）当前项目"""
    quoted = urllib.parse.quote(name)
    code, data = post(f"/api/projects/load/{quoted}", {}, base=base)
    if code == 200 and data:
        print(f"[项目] 已加载：{name}")
        return True
    print(f"[项目] 加载失败：{name}")
    return False


# ==================== 剧本数据 ====================
def list_stories(base=None):
    code, data = get("/api/stories", base=base)
    return data if code == 200 and data else []


def upsert_story(episode_no, title, character_list, content, duration, base=None):
    """创建/更新剧本（按集号幂等）"""
    body = {
        "episodeNo": episode_no,
        "title": title,
        "characterList": character_list,
        "content": content,
        "duration": duration,
    }
    code, data = post("/api/stories", body, base=base)
    if code == 200 and data:
        print(f"[剧本] 第{episode_no}集《{title}》OK (id={data.get('id')})")
        return True
    print(f"[剧本] 第{episode_no}集《{title}》失败")
    return False


# ==================== 剧本内容模板 ====================
def story_content(ep_no, title, characters, scenes, plot_points, dialogue):
    """按 manju-director 剧本格式生成 Markdown 正文"""
    lines = []
    lines.append(f"# 第{ep_no}集：{title}")
    lines.append("")
    lines.append("## 本集概要")
    lines.append(f"（{title}，本集围绕{characters[0]}展开，{plot_points[0]}）")
    lines.append("")
    lines.append("## 本集角色列表")
    for c in characters:
        lines.append(f"- {c}")
    lines.append("")
    lines.append("## 剧情热点")
    for i, p in enumerate(plot_points, 1):
        lines.append(f"- 热点{i}：{p}")
    lines.append("")
    lines.append("## 时长估算")
    lines.append("台词秒 = Σ(每条台词字数÷4) + 0.4×台词条数 = ?s")
    lines.append("旁白秒 = ?s")
    lines.append("动作秒 = 动作条数×2.5 = ?s")
    lines.append("环境音效 = 0")
    lines.append("切换秒 = (场景数-1)×1.5 = ?s")
    lines.append("片头尾 = 0（默认无）")
    lines.append("合计 = ?s  （必须 ≥ 120s）")
    lines.append("")
    lines.append("## 片头/片尾")
    lines.append("- 片头：无（默认）")
    lines.append("- 片尾：无（默认）")
    lines.append("")
    for i, scene in enumerate(scenes, 1):
        lines.append(f"## 场景{i}：{scene}")
        lines.append("叙事内容描述...")
        for d in dialogue:
            lines.append(f"> {d}")
        lines.append("")
    return "\n".join(lines)


# ==================== 主流程 ====================
def main():
    parser = argparse.ArgumentParser(description="生成 MjStudio 模拟假数据（项目+剧本）")
    parser.add_argument("--base", default=BASE, help="API 地址")
    parser.add_argument("--project", default="测试漫剧", help="目标项目名")
    parser.add_argument("--episodes", type=int, default=3, help="生成剧本集数")
    args = parser.parse_args()

    base = args.base

    # 1. 创建项目（若不存在）
    create_project(
        name=args.project,
        story_name="星海旅人",
        worldview="在浩瀚星海中，一位旅人寻找失落的故乡星球。",
        description="治愈系科幻漫剧，讲述旅人与伙伴在星际间的温暖旅程。",
        total_episodes=12,
        episode_duration=120,
        aspect_ratio=1,  # 竖屏 9:16
        target_platform="抖音",
        genre="科幻治愈",
        audience="全年龄",
        art_style="手绘水彩",
        voice_language="中文普通话",
        original_or_adapted="原创",
        bgm_style="治愈系钢琴",
        has_opening_ending=False,
        deliverables="第N集_原生版.mp4 + 第N集_配音版.mp4",
        status=0,  # 进行中
        base=base,
    )

    # 2. 加载项目
    if not load_project(args.project, base=base):
        sys.exit(1)

    # 3. 生成剧本
    existing = {s.get("episodeNo") for s in list_stories(base=base)}
    for ep in range(1, args.episodes + 1):
        if ep in existing:
            print(f"[剧本] 第{ep}集已存在，跳过")
            continue
        title = f"启程·第{ep}话"
        characters = [
            "主角：星野，星际旅人",
            "配角：小机器人Momo",
            "一次性角色：空间站站长",
            "旁白：无画面，仅声音",
        ]
        scenes = ["星港码头（夜晚）", "飞船驾驶舱（航行中）", "未知星球（清晨）"]
        plot_points = [
            "星野收到故乡星球的信号",
            "Momo 发现信号来自被封锁的星域",
            "星野决定启程，踏上归途",
        ]
        dialogue = [
            "星野（望向星空）\"故乡的星星，还在那里吗……\"",
            "Momo（电子音）\"检测到微弱信号，来源：被封锁星域。\"",
            "星野（坚定）\"那就出发吧，Momo。\"",
        ]
        content = story_content(ep, title, characters, scenes, plot_points, dialogue)
        upsert_story(ep, title, "\n".join(characters), content, 120, base=base)

    print("\nDONE")


if __name__ == "__main__":
    main()
