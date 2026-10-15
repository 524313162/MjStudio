> 本文件是技能自包含的**精简接口速查**（只含三步流程用到的接口）。
> 完整版见仓库 `docs/api.md`（含资产/工作流/资源/批量生成等全部接口）。
> 所有请求/响应示例均为实测通过。

# MjStudio 内置 API 速查（立项 / 剧本 / 分镜 / 评审）

## 通用约定
| 项 | 说明 |
|---|---|
| Base URL | `http://127.0.0.1:6066`（设置页可改端口，改完软件自动重启 API） |
| 前置 | MjStudio 已启动。就绪检查 `GET /health` 返回 healthy |
| 请求体 | JSON，`Content-Type: application/json; charset=utf-8`，字段 **camelCase** |
| 中文 | 请求体 UTF-8；**URL 路径中的中文项目名必须 URL 编码** |
| 当前项目 | stories / episodes / shots / reviews 全部依赖「当前项目」，必须先 `POST /api/projects/load/{name}` |
| 成功/错误 | 200；400=参数校验失败 / 404=项目未 load 或 id 不存在 / 409=冲突（项目名已存在）；非 200 响应体含 `message` |
| 时间字段 | `createdTime` / `updatedTime` 为 Unix 毫秒 |

## 一、健康检查
```
GET /health → {"status":"healthy","service":"MjStudio","time":"..."}
```

## 二、项目 `/api/projects`
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/projects` | 列出全部项目名 → `string[]` |
| POST | `/api/projects` | 创建项目（409=重名，400=名为空） |
| POST | `/api/projects/load/{name}` | 加载为当前项目（**也是查询详情的唯一途径**） |
| GET | `/api/projects/current` | 当前项目；未加载 → 404 |
| PUT | `/api/projects/{id}` | 更新（`name` 被忽略，项目名不可改） |
| DELETE | `/api/projects/{name}` | 删除项目（含 DB 与资源目录） |

**CreateProjectRequest**：`name`(必) / `storyName` / `worldview` / `description` / `totalEpisodes` / `episodeDuration`(默认120) / `aspectRatio`(1=9:16,2=16:9,3=1:1，默认1) / `targetPlatform` / `genre` / `audience` / `artStyle` / `voiceLanguage` / `originalOrAdapted` / `bgmStyle` / `hasOpeningEnding`(默认false) / `deliverables` / `status`(0进行中,1已完结)

```json
POST /api/projects
{"name":"星海旅人","storyName":"星海旅人","totalEpisodes":20,"aspectRatio":1,"genre":"科幻","status":0}
→ {"id":1005,"name":"星海旅人","currentStage":0,"stories":[],"assets":[],"episodes":[],...}
```

## 三、剧本 `/api/stories`（依赖当前项目）
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/stories` | 全部剧本 |
| GET | `/api/stories/episode/{no}` | 按集号取 |
| POST | `/api/stories` | 创建/更新（`id` 空=新建，非空=更新） |
| GET | `/api/stories/{id}` | 单个 |
| DELETE | `/api/stories/{id}` | 删除 |

**StoryUpsertRequest**：`id`(可选) / `episodeNo`(必，客户端幂等键) / `title`(必) / `characterList` / `content`(Markdown 正文) / `duration`

> ⚠️ 服务端不按集号去重，`id` 为空时一律新建。用 `scripts/mj.py story-upsert`（内置查重）。

## 四、分镜 `/api/episodes` + `/api/shots`（依赖当前项目）
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/episodes` | 全部集 |
| GET | `/api/episodes/{no}` | 按集号取集 |
| POST | `/api/episodes` | 创建/更新集 |
| GET | `/api/episodes/{episodeId}/shots` | 某集全部镜头 |
| GET | `/api/shots/{id}` | 单个镜头 |
| POST | `/api/shots` | 创建/更新镜头 |
| PUT | `/api/shots/{id}/prompts` | 更新视频提示词（后续环节用） |
| DELETE | `/api/shots/{id}` | 删除镜头 |

**EpisodeUpsertRequest**：`id` / `episodeNo`(必) / `name` / `duration` / `assetWhitelist` / `cameraSwitchTable` / `audioCueTable` / `dialogueList`

**ShotUpsertRequest**：`id` / `episodeId`(必) / `shotNo`(必，集内幂等键) / `title` / `videoContent` / `firstFrameDesc` / `camera` / `shotSize` / `cameraFacing` / `spatialPosition` / `bgm` / `bgmRange` / `timecode` / `timeline` / `voiceConstraint` / `ambientSfx` / `sfxRange` / `duration`(float 秒) / `transition` / `videoPromptCn` / `videoPromptEn` / `videoNegativePromptEn`

> ⚠️ 集、镜头同样不去重，`id` 为空时一律新建。用 `episode-upsert` / `shot-upsert-many`（内置查重）。

## 五、评审 `/api/reviews`（依赖当前项目）
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/reviews` | 全部环节评审 |
| GET | `/api/reviews/{stage}` | 某环节评审 |
| PUT | `/api/reviews/{stage}` | 写入/更新结论 |
| POST | `/api/reviews/{stage}/revision` | 记一次打回（计数+1，**>5 次自动置为升级**） |

**stage**：0 立项 / 1 剧本 / 2 分镜 / 3 资产 / 4 声线 / 5 音乐 / 6 音效 / 7 视频 / 8 配音 / 9 合成
**decision**：0 待评审 / 1 通过 / 2 打回 / 3 升级
**ReviewUpsertRequest**：`decision`(必) / `content`(评审正文) / `issues`(问题清单)

## 六、设置 `/api/settings`
`GET /api/settings` → `comfyUiBaseUrl` / `comfyUiLaunchPath` / `comfyUiOutputDir` / `apiPort` / `apiHost` / `dbRoot` / `resourcesRoot` / `workflowDir`

## 七、最小调用示例（Python 标准库）
```python
import json, urllib.parse, urllib.request
BASE = "http://127.0.0.1:6066"

def call(method, path, body=None):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json; charset=utf-8")
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read().decode("utf-8"))

call("POST", "/api/projects", {"name": "我的漫剧", "totalEpisodes": 3, "aspectRatio": 1})
call("POST", "/api/projects/load/" + urllib.parse.quote("我的漫剧"))          # 中文名必须编码
call("POST", "/api/stories", {"episodeNo": 1, "title": "第1集", "content": "# 第1集\n..."})
```
日常使用请直接跑 `scripts/mj.py`，它已处理编码、幂等查重与错误提示。
