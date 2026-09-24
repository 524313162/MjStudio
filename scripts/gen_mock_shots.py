# -*- coding: utf-8 -*-
"""
分镜假数据生成脚本：通过 MjStudio 内置 API 生成「集 + 镜头」数据。

用法：
    python scripts/gen_mock_shots.py [--base http://127.0.0.1:6066] [--project 星海旅人] [--episodes 3]

说明：
    - 默认在「星海旅人」项目下生成 3 集分镜，每集 5 个镜头。
    - 幂等：已存在的集/镜头会跳过（不重复创建）。
    - 镜头字段完整（11 项必填 + CN/EN 视频提示词），供分镜页展示与测试。
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


# ==================== 项目 ====================
def load_project(name, base=None):
    quoted = urllib.parse.quote(name)
    code, data = post(f"/api/projects/load/{quoted}", {}, base=base)
    if code == 200 and data:
        print(f"[项目] 已加载：{name}")
        return True
    print(f"[项目] 加载失败：{name}")
    return False


# ==================== 分镜数据 ====================
def list_episodes(base=None):
    code, data = get("/api/episodes", base=base)
    return data if code == 200 and data else []


def list_shots(episode_id, base=None):
    code, data = get(f"/api/episodes/{episode_id}/shots", base=base)
    return data if code == 200 and data else []


def upsert_episode(episode_no, name, duration, asset_whitelist, base=None):
    body = {
        "episodeNo": episode_no,
        "name": name,
        "duration": duration,
        "assetWhitelist": asset_whitelist,
    }
    code, data = post("/api/episodes", body, base=base)
    if code == 200 and data:
        print(f"[集] 第{episode_no}集《{name}》OK (id={data.get('id')})")
        return data
    print(f"[集] 第{episode_no}集《{name}》失败")
    return None


def upsert_shot(episode_id, shot_no, title, fields, base=None):
    body = {
        "episodeId": episode_id,
        "shotNo": shot_no,
        "title": title,
        **fields,
    }
    code, data = post("/api/shots", body, base=base)
    if code == 200 and data:
        print(f"  [镜头] #{shot_no}《{title}》OK (id={data.get('id')})")
        return data
    print(f"  [镜头] #{shot_no}《{title}》失败")
    return None


# ==================== 镜头模板 ====================
def shot_fields(ep_no, shot_no, scene, char, action, camera, size, facing,
                spatial, bgm, bgm_range, tc, timeline, sfx, sfx_range, duration, transition,
                prompt, negative):
    return {
        "videoContent": f"{char}在{scene}中{action}，情绪饱满，光线柔和",
        "camera": camera,
        "shotSize": size,
        "cameraFacing": facing,
        "bgm": bgm,
        "bgmRange": bgm_range,
        "timecode": tc,
        "timeline": timeline,
        "ambientSfx": sfx,
        "sfxRange": sfx_range,
        "duration": duration,
        "transition": transition,
        "videoPromptCn": prompt,
        "videoNegativePromptEn": negative,
    }


# 每集镜头模板（第N集 5 个镜头，内容随集号变化）
def episode_shots(ep_no):
    # 三套不同的场景/动作/角色，按集号轮换，保证每集内容不同
    scenes_pool = [
        ["星港码头", "飞船驾驶舱", "未知星球", "星海深处", "故乡星球"],
        ["星际列车", "空间站大厅", "陨石带", "冰封星球", "星港夜市"],
        ["沙漠星球", "地下实验室", "云海之巅", "废弃星舰", "极光平原"],
    ]
    chars_pool = [
        ["星野", "Momo", "星野", "Momo", "星野"],
        ["Momo", "星野", "Momo", "星野", "Momo"],
        ["星野", "星野", "Momo", "Momo", "星野"],
    ]
    actions_pool = [
        ["眺望星空", "扫描信号", "启动引擎", "导航航线", "降落故乡"],
        ["整理行装", "对接舱门", "躲避陨石", "采集冰晶", "挑选礼物"],
        ["穿越沙暴", "破解密码", "攀上云巅", "修复引擎", "仰望极光"],
    ]
    sizes = ["全景", "中景", "近景", "特写", "全景"]
    facings = ["镜头朝东", "镜头朝南", "镜头朝西", "镜头朝北", "镜头朝东"]
    cameras = ["固定机位·全景", "缓慢推近·中景", "跟随摇移·近景", "固定机位·特写", "航拍俯视·全景"]
    spatials = ["画面左侧，面向星海", "画面中央，悬浮半空", "画面右侧，背对舱门", "画面中央，屏幕前", "画面中央，缓缓降落"]
    bgms = ["治愈钢琴·主旋律", "电子轻音·科技感", "弦乐渐强·希望", "空灵音效·神秘", "温暖钢琴·归乡"]
    bgm_ranges = ["0-8s", "0-8s", "0-8s", "0-8s", "0-8s"]
    transitions = ["硬切", "叠化", "淡入", "闪白", "淡出"]
    sfxs = ["海风声·远处汽笛", "电子蜂鸣·机械运转", "引擎轰鸣·气流声", "星尘沙沙声", "风声渐止·鸟鸣"]
    sfx_ranges = ["0-3s", "0-2s", "0-4s", "0-5s", "0-3s"]

    # 按集号取对应的一套内容（1→0, 2→1, 3→2, 4→0 ...）
    idx = (ep_no - 1) % 3
    scenes = scenes_pool[idx]
    chars = chars_pool[idx]
    actions = actions_pool[idx]

    timelines = [
        f"0-2s {actions[0]} → 2-5s 眼神渐亮 → 5-8s 转身",
        f"0-2s {actions[1]} → 2-5s 屏幕亮起 → 5-8s 报告",
        f"0-2s {actions[2]} → 2-5s 引擎点火 → 5-8s 升空",
        f"0-2s {actions[3]} → 2-5s 星图展开 → 5-8s 确认",
        f"0-2s {actions[4]} → 2-5s 星球显现 → 5-8s 降落",
    ]
    prompts = [
        f"第{ep_no}集镜头{i+1}：{chars[i]}在{scenes[i]}中{actions[i]}，{sizes[i]}，{cameras[i]}，治愈系手绘水彩风格"
        for i in range(5)
    ]
    negative = "模糊，低质量，畸形，多余肢体，解剖错误"

    result = []
    for i in range(5):
        shot_no = i + 1
        tc_start = i * 8
        tc = f"00:{tc_start:02d}.00~00:{tc_start+8:02d}.00"
        result.append((
            shot_no,
            f"镜头{shot_no}·{actions[i]}",
            shot_fields(ep_no, shot_no, scenes[i], chars[i], actions[i], cameras[i],
                        sizes[i], facings[i], spatials[i], bgms[i], bgm_ranges[i], tc, timelines[i],
                        sfxs[i], sfx_ranges[i], 8, transitions[i],
                        prompts[i], negative),
        ))
    return result


# ==================== 主流程 ====================
def main():
    parser = argparse.ArgumentParser(description="生成 MjStudio 分镜假数据（集+镜头）")
    parser.add_argument("--base", default=BASE, help="API 地址")
    parser.add_argument("--project", default="星海旅人", help="目标项目名")
    parser.add_argument("--episodes", type=int, default=3, help="生成集数")
    args = parser.parse_args()

    base = args.base

    # 1. 加载项目
    if not load_project(args.project, base=base):
        sys.exit(1)

    # 2. 生成集 + 镜头
    existing_eps = {e.get("episodeNo"): e for e in list_episodes(base=base)}
    for ep in range(1, args.episodes + 1):
        ep_obj = existing_eps.get(ep)
        if ep_obj is None:
            ep_obj = upsert_episode(ep, f"第{ep}集·启程", 120,
                                    "星野, Momo, 星港码头, 飞船驾驶舱, 治愈钢琴", base=base)
            if ep_obj is None:
                continue
        else:
            print(f"[集] 第{ep}集已存在，跳过 (id={ep_obj.get('id')})")

        ep_id = ep_obj.get("id")
        existing_shots = {s.get("shotNo") for s in list_shots(ep_id, base=base)}
        for shot_no, title, fields in episode_shots(ep):
            if shot_no in existing_shots:
                print(f"  [镜头] #{shot_no}已存在，跳过")
                continue
            upsert_shot(ep_id, shot_no, title, fields, base=base)

    print("\nDONE")


if __name__ == "__main__":
    main()
