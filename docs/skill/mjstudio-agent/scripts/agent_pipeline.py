# -*- coding: utf-8 -*-
"""
MjStudio Agent 接入 · 一键流水线：项目 → 剧本 → 资产 → 分镜。

用法：
    python agent_pipeline.py [--base http://127.0.0.1:6066] [--project 星海旅人]
                             [--episodes 3] [--shots 4] [--steps all]

说明：
    - 全环节幂等：项目按名 / 剧本按集号 / 资产按名 / 集按集号 / 镜头按集内镜号查重。
    - 纯 Python 标准库（urllib），无第三方依赖。
    - 写入顺序：创建项目 → load 当前项目 → 剧本 → 资产 → 分镜（集+镜头）。
"""
import argparse
import json
import sys
import urllib.request
import urllib.error
import urllib.parse

BASE = "http://127.0.0.1:6066"


# ==================== HTTP 工具 ====================
def api(method, path, body=None, timeout=20, base=None):
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
    except urllib.error.URLError as e:
        print(f"  [连接失败] {method} {path} -> {e.reason}")
        return 0, None


def get(path, base=None):
    return api("GET", path, base=base)


def post(path, body, base=None):
    return api("POST", path, body, base=base)


# ==================== 环节 1：项目 ====================
def step_project(args):
    name = args.project
    code, names = get("/api/projects", base=args.base)
    if code != 200:
        print("[项目] 无法连接 API，请先启动 MjStudio")
        return False
    if name in (names or []):
        print(f"[项目] 已存在，跳过创建：{name}")
    else:
        body = {
            "name": name,
            "storyName": "星海旅人",
            "worldview": "在浩瀚星海中，一位旅人寻找失落的故乡星球。",
            "description": "治愈系科幻漫剧，讲述旅人与伙伴在星际间的温暖旅程。",
            "totalEpisodes": max(args.episodes, 12),
            "episodeDuration": 120,
            "aspectRatio": 1,  # 竖屏 9:16
            "targetPlatform": "抖音",
            "genre": "科幻治愈",
            "audience": "全年龄",
            "artStyle": "手绘水彩",
            "voiceLanguage": "中文普通话",
            "originalOrAdapted": "原创",
            "bgmStyle": "治愈系钢琴",
            "hasOpeningEnding": False,
            "deliverables": "第N集_原生版.mp4 + 第N集_配音版.mp4",
            "status": 0,
        }
        code, data = post("/api/projects", body, base=args.base)
        if code != 200 or not data:
            print(f"[项目] 创建失败：{name}")
            return False
        print(f"[项目] 创建成功：{name} (id={data.get('id')})")

    # 加载为当前项目（后续环节依赖）
    quoted = urllib.parse.quote(name)
    code, data = post(f"/api/projects/load/{quoted}", {}, base=args.base)
    if code != 200 or not data:
        print(f"[项目] 加载失败：{name}")
        return False
    print(f"[项目] 已加载为当前项目：{name}")
    return True


# ==================== 环节 2：剧本 ====================
def story_content(ep_no, title, characters, scenes, plot_points, dialogue):
    lines = [f"# 第{ep_no}集：{title}", "", "## 本集概要",
             f"（{title}，本集围绕{characters[0]}展开，{plot_points[0]}）", "",
             "## 本集角色列表"]
    lines += [f"- {c}" for c in characters]
    lines += ["", "## 剧情热点"]
    lines += [f"- 热点{i}：{p}" for i, p in enumerate(plot_points, 1)]
    lines += ["", "## 时长估算",
              "台词秒 = Σ(每条台词字数÷4) + 0.4×台词条数 = ?s",
              "旁白秒 = ?s",
              "动作秒 = 动作条数×2.5 = ?s",
              "环境音效 = 0",
              "切换秒 = (场景数-1)×1.5 = ?s",
              "片头尾 = 0（默认无）",
              "合计 = ?s  （必须 ≥ 120s）", "",
              "## 片头/片尾", "- 片头：无（默认）", "- 片尾：无（默认）", ""]
    for i, scene in enumerate(scenes, 1):
        lines.append(f"## 场景{i}：{scene}")
        lines.append("叙事内容描述...")
        lines += [f"> {d}" for d in dialogue]
        lines.append("")
    return "\n".join(lines)


def step_story(args):
    code, stories = get("/api/stories", base=args.base)
    existing = {s.get("episodeNo") for s in (stories or [])} if code == 200 else set()
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
    ok = True
    for ep in range(1, args.episodes + 1):
        if ep in existing:
            print(f"[剧本] 第{ep}集已存在，跳过")
            continue
        title = f"启程·第{ep}话"
        content = story_content(ep, title, characters, scenes, plot_points, dialogue)
        body = {
            "episodeNo": ep,
            "title": title,
            "characterList": "\n".join(characters),
            "content": content,
            "duration": 120,
        }
        code, data = post("/api/stories", body, base=args.base)
        if code == 200 and data:
            print(f"[剧本] 第{ep}集《{title}》OK (id={data.get('id')})")
        else:
            print(f"[剧本] 第{ep}集《{title}》失败")
            ok = False
    return ok


# ==================== 环节 3：资产 ====================
# (类型枚举, 名称, 描述, 正向提示词, 反向提示词)
ASSETS = [
    (1, "星野", "主角，星际旅人，20多岁青年，银灰色短发，蓝色旅行外套",
     "a young interstellar traveler, silver-grey short hair, blue travel jacket, hand-drawn watercolor style, full body portrait, front view",
     "photo, realistic, 3d render, deformed, extra limbs, watermark, text"),
    (1, "Momo", "配角，小机器人，圆球形白色机身，蓝色发光眼睛",
     "a small round white robot with glowing blue eyes, cute design, hand-drawn watercolor style, full body portrait, front view",
     "photo, realistic, 3d render, deformed, extra limbs, watermark, text"),
    (2, "星港码头", "场景，夜晚的星际港口，霓虹灯与停泊的飞船",
     "a night interstellar spaceport dock, neon lights, parked spaceships, stars in sky, hand-drawn watercolor style, wide panoramic view",
     "photo, realistic, 3d render, deformed, watermark, text"),
    (2, "飞船驾驶舱", "场景，飞船内部驾驶舱，弧形舷窗可见星空",
     "a spaceship cockpit interior, curved window showing starry sky, control panels, hand-drawn watercolor style, wide view",
     "photo, realistic, 3d render, deformed, watermark, text"),
    (2, "未知星球", "场景，清晨的未知星球地表，紫色天空与奇异植物",
     "an alien planet surface at dawn, purple sky, strange plants, hand-drawn watercolor style, wide panoramic view",
     "photo, realistic, 3d render, deformed, watermark, text"),
    (3, "信号接收器", "道具，星野随身携带的旧式信号接收器，金属外壳带天线",
     "an old handheld signal receiver device, metal shell with antenna, sci-fi prop, hand-drawn watercolor style, two views",
     "photo, realistic, 3d render, deformed, watermark, text"),
    (4, "治愈系钢琴主题曲", "BGM，治愈系钢琴曲，舒缓温暖，带弦乐铺底",
     "healing piano theme music, warm and gentle, soft strings pad, cinematic, 2 minutes",
     "loud, aggressive, distorted, noise"),
    (6, "引擎轰鸣", "音效，飞船引擎启动的低沉轰鸣声",
     "spaceship engine startup rumble, low frequency, cinematic sound effect, 3 seconds",
     "distorted, clipping, noise"),
    (6, "信号提示音", "音效，信号接收器发出的清脆提示音",
     "crisp signal beep sound, sci-fi interface notification, 1 second",
     "distorted, clipping, noise"),
]


def step_assets(args):
    code, assets = get("/api/assets", base=args.base)
    existing = {a.get("name") for a in (assets or [])} if code == 200 else set()
    ok = True
    for i, (atype, name, desc, prompt, neg) in enumerate(ASSETS, 1):
        if name in existing:
            print(f"[资产] 已存在，跳过：{name}")
            continue
        body = {
            "assetType": atype,
            "name": name,
            "description": desc,
            "prompt": prompt,
            "negativePrompt": neg,
            "order": i,
        }
        code, data = post("/api/assets", body, base=args.base)
        if code == 200 and data:
            print(f"[资产] 创建成功：{name} (type={atype}, id={data.get('id')})")
        else:
            print(f"[资产] 创建失败：{name}")
            ok = False
    return ok


# ==================== 环节 4：分镜（集 + 镜头） ====================
# 每集 4 镜模板（每镜 30s，合计 120s）
SHOTS_TEMPLATE = [
    {
        "title": "星港码头·夜",
        "videoContent": "星野独自站在星港码头，望着星空，Momo 从身后飞来",
        "firstFrameDesc": "夜晚星港码头，霓虹灯闪烁，星野背影立于前景，星空背景",
        "camera": "固定机位", "shotSize": "全景", "cameraFacing": "正面",
        "spatialPosition": "星野画面中央偏左，Momo 从右侧入画",
        "bgm": "治愈系钢琴主题曲", "bgmRange": "0-30s",
        "timecode": "00:00-00:30", "timeline": "0-5s 空镜码头 / 5-20s 星野望星空 / 20-30s Momo 入画",
        "voiceConstraint": "星野台词1句（5-15s）",
        "ambientSfx": "港口环境音、远处引擎声", "sfxRange": "0-30s",
        "duration": 30.0, "transition": "淡入",
        "videoPromptCn": "手绘水彩风格，夜晚星港码头，霓虹灯与停泊飞船，银灰短发青年星野背影立于前景望向星空，圆球白色小机器人Momo从右侧飞入画面，固定机位全景，氛围安静治愈",
        "videoPromptEn": "hand-drawn watercolor style, night interstellar spaceport dock with neon lights and parked spaceships, a young traveler with silver-grey short hair standing in foreground gazing at starry sky, a small round white robot flying in from the right, fixed camera wide shot, quiet healing atmosphere",
        "videoNegativePromptEn": "photo, realistic, 3d render, deformed, extra limbs, watermark, text, scene change, camera cut",
    },
    {
        "title": "驾驶舱·信号",
        "videoContent": "飞船驾驶舱内，Momo 向星野展示信号接收器上的微弱信号",
        "firstFrameDesc": "飞船驾驶舱，弧形舷窗外是星空，星野与Momo 在控制台前",
        "camera": "缓慢推近", "shotSize": "中景", "cameraFacing": "侧面",
        "spatialPosition": "星野左侧，Momo 悬浮控制台上方",
        "bgm": "治愈系钢琴主题曲", "bgmRange": "30-60s",
        "timecode": "00:30-01:00", "timeline": "30-40s 驾驶舱空镜 / 40-55s Momo 展示信号 / 55-60s 星野特写反应",
        "voiceConstraint": "Momo 台词1句（40-50s）",
        "ambientSfx": "信号提示音（42s）、舱内低频嗡鸣", "sfxRange": "30-60s",
        "duration": 30.0, "transition": "硬切",
        "videoPromptCn": "手绘水彩风格，飞船驾驶舱内部，弧形舷窗外星空流动，银灰短发青年与圆球白色小机器人站在控制台前，机器人展示旧式信号接收器上的微弱信号，缓慢推近中景，氛围神秘",
        "videoPromptEn": "hand-drawn watercolor style, spaceship cockpit interior with starry sky flowing outside curved window, a young traveler with silver-grey short hair and a small round white robot at the console, the robot showing a faint signal on an old handheld signal receiver, slow push-in medium shot, mysterious atmosphere",
        "videoNegativePromptEn": "photo, realistic, 3d render, deformed, extra limbs, watermark, text, scene change, camera cut",
    },
    {
        "title": "决定启程",
        "videoContent": "星野握紧信号接收器，坚定地说出发，飞船引擎启动",
        "firstFrameDesc": "驾驶舱内，星野手握信号接收器特写，舷窗星空",
        "camera": "固定机位", "shotSize": "近景", "cameraFacing": "正面",
        "spatialPosition": "星野画面中央，接收器在手中",
        "bgm": "治愈系钢琴主题曲", "bgmRange": "60-90s",
        "timecode": "01:00-01:30", "timeline": "60-70s 接收器特写 / 70-85s 星野坚定表情 / 85-90s 引擎启动光效",
        "voiceConstraint": "星野台词1句（70-80s）",
        "ambientSfx": "引擎轰鸣（85s 起）", "sfxRange": "85-90s",
        "duration": 30.0, "transition": "硬切",
        "videoPromptCn": "手绘水彩风格，飞船驾驶舱，银灰短发青年手握旧式金属信号接收器特写，表情坚定，随后引擎启动蓝色光效弥漫，固定机位近景，氛围热血而温暖",
        "videoPromptEn": "hand-drawn watercolor style, spaceship cockpit, close-up of a young traveler with silver-grey short hair gripping an old metal signal receiver, determined expression, then engine startup with blue light effects filling the frame, fixed camera close shot, warm and determined atmosphere",
        "videoNegativePromptEn": "photo, realistic, 3d render, deformed, extra limbs, watermark, text, scene change, camera cut",
    },
    {
        "title": "启程·星空",
        "videoContent": "飞船驶离星港，穿越星海，镜头拉远展现浩瀚宇宙",
        "firstFrameDesc": "飞船尾部视角，星港在身后缩小，前方是浩瀚星海",
        "camera": "缓慢拉远", "shotSize": "大远景", "cameraFacing": "背面",
        "spatialPosition": "飞船画面中央，星港左下，星海铺满背景",
        "bgm": "治愈系钢琴主题曲", "bgmRange": "90-120s",
        "timecode": "01:30-02:00", "timeline": "90-105s 飞船驶离 / 105-120s 拉远展现星海",
        "voiceConstraint": "无台词",
        "ambientSfx": "引擎持续低鸣", "sfxRange": "90-120s",
        "duration": 30.0, "transition": "淡出",
        "videoPromptCn": "手绘水彩风格，飞船驶离星港穿越星海，星港在身后逐渐缩小，浩瀚宇宙星空铺满画面，缓慢拉远大远景，氛围辽阔治愈",
        "videoPromptEn": "hand-drawn watercolor style, a spaceship leaving the spaceport and traveling through the star sea, the spaceport shrinking behind, vast cosmic starry sky filling the frame, slow pull-out extreme wide shot, vast and healing atmosphere",
        "videoNegativePromptEn": "photo, realistic, 3d render, deformed, extra limbs, watermark, text, scene change, camera cut",
    },
]


def step_shots(args):
    code, episodes = get("/api/episodes", base=args.base)
    if code != 200:
        print("[分镜] 获取集列表失败")
        return False
    existing_eps = {e.get("episodeNo"): e for e in (episodes or [])}
    ok = True
    for ep in range(1, args.episodes + 1):
        ep_obj = existing_eps.get(ep)
        if ep_obj is None:
            body = {
                "episodeNo": ep,
                "name": f"第{ep}集",
                "duration": 120,
                "assetWhitelist": "星野, Momo, 星港码头, 飞船驾驶舱, 未知星球, 信号接收器, 治愈系钢琴主题曲, 引擎轰鸣, 信号提示音",
            }
            code, data = post("/api/episodes", body, base=args.base)
            if code != 200 or not data:
                print(f"[分镜] 第{ep}集创建失败")
                ok = False
                continue
            ep_obj = data
            print(f"[分镜] 第{ep}集创建成功 (id={data.get('id')})")
        else:
            print(f"[分镜] 第{ep}集已存在 (id={ep_obj.get('id')})")

        ep_id = ep_obj.get("id")
        code, shots = get(f"/api/episodes/{ep_id}/shots", base=args.base)
        existing_shots = {s.get("shotNo") for s in (shots or [])} if code == 200 else set()
        for i, tpl in enumerate(SHOTS_TEMPLATE[:args.shots], 1):
            if i in existing_shots:
                print(f"[分镜] 第{ep}集 镜头{i} 已存在，跳过")
                continue
            body = {k: v for k, v in tpl.items() if v is not None}
            body["episodeId"] = ep_id
            body["shotNo"] = i
            code, data = post("/api/shots", body, base=args.base)
            if code == 200 and data:
                print(f"[分镜] 第{ep}集 镜头{i}《{tpl['title']}》OK (id={data.get('id')})")
            else:
                print(f"[分镜] 第{ep}集 镜头{i}《{tpl['title']}》失败")
                ok = False
    return ok


# ==================== 主流程 ====================
def main():
    parser = argparse.ArgumentParser(description="MjStudio Agent 接入一键流水线（项目→剧本→资产→分镜）")
    parser.add_argument("--base", default=BASE, help="API 地址")
    parser.add_argument("--project", default="星海旅人", help="目标项目名（不存在则创建）")
    parser.add_argument("--episodes", type=int, default=3, help="剧本/分镜集数")
    parser.add_argument("--shots", type=int, default=4, help="每集镜头数（默认4镜×30s=120s）")
    parser.add_argument("--steps", default="all", help="逗号分隔：project,story,assets,shots（默认 all）")
    args = parser.parse_args()

    steps = ["project", "story", "assets", "shots"] if args.steps == "all" \
        else [s.strip() for s in args.steps.split(",") if s.strip()]

    # 项目环节必须最先执行（后续依赖当前项目上下文）
    if "project" not in steps:
        print("[提示] 未包含 project 环节，假定当前项目已加载")
    else:
        if not step_project(args):
            sys.exit(1)

    if "story" in steps:
        if not step_story(args):
            print("[剧本] 存在失败项")
    if "assets" in steps:
        if not step_assets(args):
            print("[资产] 存在失败项")
    if "shots" in steps:
        if not step_shots(args):
            print("[分镜] 存在失败项")

    print("\nDONE")


if __name__ == "__main__":
    main()
