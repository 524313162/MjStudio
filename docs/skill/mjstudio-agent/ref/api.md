# MjStudio 内置 API 接口文档（Agent 接入用）

> 本文档是 `mjstudio-agent` 技能的接口参考（ref），按接口文档标准编写：每个接口含 **路径 / 参数 / 请求体 / 返回 / 示例**。
> 所有示例均为**实测通过**的真实请求与响应（2026-09-28 验证）。
> 一键流水线见 `../SKILL.md` 第三节。

## 一、通用约定

| 项             | 说明                                                                                                               |
| -------------- | ------------------------------------------------------------------------------------------------------------------ |
| Base URL       | `http://127.0.0.1:6066`（端口可在设置页修改，改完自动重启 API）                                                    |
| 前置条件       | MjStudio.Host 已启动。就绪检查：`GET /api/projects` 返回项目名数组即就绪                                           |
| 请求体         | JSON，`Content-Type: application/json; charset=utf-8`，字段名 **camelCase**                                        |
| 中文处理       | 请求体 UTF-8（Python 用 `ensure_ascii=False`）；URL 路径中的中文项目名需 URL 编码（`urllib.parse.quote`）          |
| 当前项目上下文 | stories / assets / episodes / shots 等接口都依赖「当前项目」，必须先 `POST /api/projects/load/{name}` 加载（切换） |
| 成功响应       | HTTP 200，响应体为 JSON（对象或数组）                                                                              |
| 错误响应       | 400=参数校验失败 / 404=项目未 load 或 id 不存在，响应体含 `message` 字段                                           |
| 时间字段       | `createdTime` / `updatedTime` 为 **Unix 毫秒**时间戳                                                               |

### 通用返回对象

**BaseEntity 字段**（所有实体都含）：

| 字段          | 类型 | 说明                  |
| ------------- | ---- | --------------------- |
| `id`          | long | 主键                  |
| `createdTime` | long | 创建时间（Unix 毫秒） |
| `updatedTime` | long | 更新时间（Unix 毫秒） |

**删除类接口统一返回**：`{ "message": "已删除" }`

---

## 二、项目（/api/projects）

### 2.1 列出所有项目名

- **`GET /api/projects`**
- 参数：无
- 返回：`string[]` 项目名数组

**示例**

```
GET /api/projects
→ 200
["111","Agent分步测试","Agent测试项目","星海旅人","测试","测试新项目","测试漫剧"]
```

### 2.2 获取当前项目

- **`GET /api/projects/current`**
- 参数：无
- 返回：Project 对象（见 2.4）；未加载项目时 404 `{ "message": "未加载项目" }`

### 2.3 创建项目（立项）

- **`POST /api/projects`**
- 请求体：CreateProjectRequest（见下）
- 返回：Project 对象
- 错误：`name` 为空 → 400 `{ "message": "项目名不能为空" }`

**CreateProjectRequest**

| 字段                | 类型   | 必填 | 说明                               |
| ------------------- | ------ | ---- | ---------------------------------- |
| `name`              | string | 是   | 项目名（唯一，创建后不可改）       |
| `storyName`         | string | 否   | 故事名                             |
| `worldview`         | string | 否   | 世界观                             |
| `description`       | string | 否   | 项目描述                           |
| `totalEpisodes`     | int    | 否   | 总集数                             |
| `episodeDuration`   | int    | 否   | 每集时长（秒，默认 120）           |
| `aspectRatio`       | int    | 否   | 画幅：0=16:9 横屏，1=9:16 竖屏     |
| `targetPlatform`    | string | 否   | 目标平台（抖音/快手/B站等）        |
| `genre`             | string | 否   | 题材                               |
| `audience`          | string | 否   | 目标受众                           |
| `artStyle`          | string | 否   | 美术风格                           |
| `voiceLanguage`     | string | 否   | 配音语言                           |
| `originalOrAdapted` | string | 否   | 原创/改编                          |
| `bgmStyle`          | string | 否   | BGM 风格                           |
| `hasOpeningEnding`  | bool   | 否   | 是否有片头片尾（默认 false）       |
| `deliverables`      | string | 否   | 交付物                             |
| `status`            | int    | 否   | 状态：0=进行中，1=已完结（默认 0） |

**示例（实测）**

```
POST /api/projects
{
  "name": "排序验证项目",
  "storyName": "验证排序",
  "totalEpisodes": 2,
  "aspectRatio": 1,
  "genre": "测试",
  "status": 0
}
→ 200
{
  "name": "排序验证项目",
  "storyName": "验证排序",
  "worldview": null,
  "description": null,
  "totalEpisodes": 2,
  "episodeDuration": 120,
  "aspectRatio": 1,
  "targetPlatform": null,
  "genre": "测试",
  "audience": null,
  "artStyle": null,
  "voiceLanguage": "中文普通话",
  "originalOrAdapted": "原创",
  "bgmStyle": null,
  "hasOpeningEnding": false,
  "deliverables": null,
  "currentStage": 0,
  "status": 0,
  "coverResourceId": null,
  "coverResource": null,
  "stories": [],
  "assets": [],
  "episodes": [],
  "reviews": [],
  "id": 1005,
  "createdTime": 1790601491143,
  "updatedTime": 1790601491143
}
```

### 2.4 加载（切换）当前项目 / 查询项目详情

- **`POST /api/projects/load/{name}`**
- 路径参数：`name` 项目名（**中文名需 URL 编码**）
- 请求体：无
- 返回：Project 对象（完整立项字段 + `id` / `currentStage` / `coverResourceId` / `coverResource` / `stories[]` / `assets[]` / `episodes[]` / `reviews[]` / `createdTime` / `updatedTime`）
- 错误：项目不存在 → 404 `{ "message": "项目 xxx 不存在" }`
- **说明**：这是查询项目详情的唯一途径（无独立 GET by name），同时会把该项目设为当前项目

**示例（实测）**

```
POST /api/projects/load/%E6%8E%92%E5%BA%8F%E9%AA%8C%E8%AF%81%E9%A1%B9%E7%9B%AE   （= /load/排序验证项目）
→ 200
{ "name": "排序验证项目", "id": 1005, "status": 0, "totalEpisodes": 2, ... }
```

### 2.5 更新项目

- **`PUT /api/projects/{id}`**
- 路径参数：`id` 项目 id（long）
- 请求体：CreateProjectRequest（`name` 字段被忽略，项目名不可改）
- 返回：Project 对象
- 错误：id 不存在 → 404

### 2.6 删除项目

- **`DELETE /api/projects/{name}`**
- 路径参数：`name` 项目名（中文名需 URL 编码）
- 返回：`{ "message": "已删除" }`
- **说明**：同时删除项目 DB 文件与资源目录

**示例（实测）**

```
DELETE /api/projects/HTTP%E6%8E%A5%E5%8F%A3%E6%B5%8B%E8%AF%95   （= /delete/HTTP接口测试）
→ 200
{ "message": "已删除" }
```

---

## 三、剧本（/api/stories）

> 全部依赖当前项目上下文（先 load 项目）。

### 3.1 列出当前项目全部剧本

- **`GET /api/stories`**
- 参数：无
- 返回：`Story[]`（Story 对象见 3.3）

### 3.2 按集号获取剧本

- **`GET /api/stories/episode/{no}`**
- 路径参数：`no` 集号（int）
- 返回：Story 对象；不存在 → 404

### 3.3 创建/更新剧本

- **`POST /api/stories`**
- 请求体：StoryUpsertRequest
- 返回：Story 对象
- **行为（实测）**：`id` 非空 → 按 id 更新；`id` 为空 → **一律新建**（服务端不按集号去重，同集号重复 POST 会产生多条记录）。幂等由**调用方**保证：先 `GET /api/stories` 按 `episodeNo` 查重，已存在则跳过或带 `id` 更新

**StoryUpsertRequest**

| 字段            | 类型   | 必填 | 说明                     |
| --------------- | ------ | ---- | ------------------------ |
| `id`            | long   | 否   | 空=创建，非空=按 id 更新 |
| `episodeNo`     | int    | 是   | 集号（客户端幂等查重键） |
| `title`         | string | 是   | 标题                     |
| `characterList` | string | 否   | 本集角色列表             |
| `content`       | string | 否   | 剧本正文（Markdown）     |
| `duration`      | int    | 否   | 本集时长（秒）           |

**Story 对象（返回）**：`id` / `projectId` / `episodeNo` / `title` / `characterList` / `content` / `duration` / `chapters[]` / `createdTime` / `updatedTime`

**示例（实测）**

```
POST /api/stories
{
  "episodeNo": 1,
  "title": "第1集 信号",
  "characterList": "星野（主角）、Momo（机器人）",
  "content": "# 第1集 信号\n\n## 本集概要\n星野在废弃观测站收到神秘信号……",
  "duration": 120
}
→ 200
{
  "episodeNo": 1,
  "title": "第1集 信号",
  "characterList": "星野（主角）、Momo（机器人）",
  "content": "# 第1集 信号\n\n## 本集概要\n……",
  "duration": 120,
  "chapters": [],
  "id": 1001,
  "projectId": 1005,
  "createdTime": 1790602000000,
  "updatedTime": 1790602000000
}
```

### 3.4 获取单个剧本

- **`GET /api/stories/{id}`**
- 路径参数：`id`（long）
- 返回：Story 对象；不存在 → 404

### 3.5 删除剧本

- **`DELETE /api/stories/{id}`**
- 路径参数：`id`（long）
- 返回：`{ "message": "已删除" }`

---

## 四、资产（/api/assets）

> 全部依赖当前项目上下文（先 load 项目）。

### 4.1 列出资产

- **`GET /api/assets?type={n}`**
- Query 参数：`type` 可选，1~7（1=角色 2=场景 3=道具 4=BGM 5=音乐 6=音效 7=声线）；不传返回全部
- 返回：`Asset[]`

### 4.2 获取单个资产

- **`GET /api/assets/{id}`**
- 返回：Asset 对象；不存在 → 404

### 4.3 创建/更新资产

- **`POST /api/assets`**
- 请求体：AssetUpsertRequest
- 返回：Asset 对象
- **行为（实测）**：`id` 非空 → 按 id 更新；`id` 为空 → **一律新建**（服务端不按名称去重）。幂等由**调用方**保证：先 `GET /api/assets` 按 `name` 查重，已存在则跳过或带 `id` 更新

**AssetUpsertRequest**

| 字段             | 类型   | 必填 | 说明                                            |
| ---------------- | ------ | ---- | ----------------------------------------------- |
| `id`             | long   | 否   | 空=创建，非空=更新                              |
| `assetType`      | int    | 是   | 1=角色 2=场景 3=道具 4=BGM 5=音乐 6=音效 7=声线 |
| `name`           | string | 是   | 资产名（客户端幂等查重键）                      |
| `description`    | string | 否   | 描述                                            |
| `prompt`         | string | 否   | 正向提示词                                      |
| `negativePrompt` | string | 否   | 反向提示词                                      |
| `order`          | int    | 否   | 排序                                            |

**Asset 对象（返回）**：`id` / `projectId` / `assetType` / `name` / `description` / `prompt` / `negativePrompt` / `resourceId` / `order` / `parentAssetId` / `children[]` / `projectRefs[]` / `createdTime` / `updatedTime`

**示例**

```
POST /api/assets
{
  "assetType": 1,
  "name": "星野",
  "description": "主角，22 岁观测员",
  "prompt": "young female observer, short black hair, blue jumpsuit, anime style",
  "negativePrompt": "lowres, blurry, extra fingers",
  "order": 1
}
→ 200
{ "id": 2001, "assetType": 1, "name": "星野", "prompt": "young female observer, ...", ... }
```

### 4.4 更新提示词

- **`PUT /api/assets/{id}/prompts`**
- 请求体：`{ "prompt": "...", "negativePrompt": "..." }`
- 返回：Asset 对象

### 4.5 设置引用项目（全量覆盖）

- **`PUT /api/assets/{id}/refs`**
- 请求体：`{ "projectIds": [1, 2] }`（**项目 id 数组**，全量覆盖，传空数组=清除全部引用）
- 返回：Asset 对象

### 4.6 关联资源文件

- **`PUT /api/assets/{id}/resource?relativePath=&mediaType=&purpose=`**
- Query 参数：
  | 参数 | 必填 | 说明 |
  | --- | --- | --- |
  | `relativePath` | 是 | 相对**资源目录**的路径，如 `images/x.png`、`audio/bgm.mp3` |
  | `mediaType` | 是 | MIME 类型，如 `image/png`、`audio/mpeg` |
  | `purpose` | 是 | ResourcePurposeEnum 枚举值 |
  | `duration` | 否 | 时长（秒，音频用） |
- 返回：Resource 对象

### 4.7 删除资产

- **`DELETE /api/assets/{id}`**
- 返回：`{ "message": "已删除" }`

---

## 五、分镜（集 + 镜头）

> 全部依赖当前项目上下文（先 load 项目）。镜头依赖集 id。

### 5.1 列出当前项目全部集

- **`GET /api/episodes`**
- 返回：`Episode[]`

### 5.2 按集号获取集

- **`GET /api/episodes/{no}`**
- 路径参数：`no` 集号（int）
- 返回：Episode 对象；不存在 → 404

### 5.3 创建/更新集

- **`POST /api/episodes`**
- 请求体：EpisodeUpsertRequest
- 返回：Episode 对象
- **行为（实测）**：`id` 非空 → 按 id 更新；`id` 为空 → **一律新建**（服务端不按集号去重）。幂等由**调用方**保证：先 `GET /api/episodes` 按 `episodeNo` 查重，已存在则复用其 id

**EpisodeUpsertRequest**

| 字段                | 类型   | 必填 | 说明                     |
| ------------------- | ------ | ---- | ------------------------ |
| `id`                | long   | 否   | 空=创建，非空=更新       |
| `episodeNo`         | int    | 是   | 集号（客户端幂等查重键） |
| `name`              | string | 否   | 集名称                   |
| `duration`          | int    | 否   | 时长（秒）               |
| `assetWhitelist`    | string | 否   | 资产白名单               |
| `cameraSwitchTable` | string | 否   | 运镜切换表               |
| `audioCueTable`     | string | 否   | 音频提示表               |
| `dialogueList`      | string | 否   | 台词列表                 |

### 5.4 列出某集全部镜头

- **`GET /api/episodes/{episodeId}/shots`**
- 路径参数：`episodeId`（long）
- 返回：`Shot[]`

### 5.5 获取单个镜头

- **`GET /api/shots/{id}`**
- 路径参数：`id`（long）
- 返回：Shot 对象；不存在 → 404

### 5.6 创建/更新镜头

- **`POST /api/shots`**
- 请求体：ShotUpsertRequest
- 返回：Shot 对象
- **行为（实测）**：`id` 非空 → 按 id 更新；`id` 为空 → **一律新建**（服务端不按镜头号去重）。幂等由**调用方**保证：先 `GET /api/episodes/{episodeId}/shots` 按 `shotNo` 查重，已存在则跳过或带 `id` 更新

**ShotUpsertRequest**

| 字段                    | 类型   | 必填 | 说明                           |
| ----------------------- | ------ | ---- | ------------------------------ |
| `id`                    | long   | 否   | 空=创建，非空=更新             |
| `episodeId`             | long   | 是   | 所属集 id                      |
| `shotNo`                | int    | 是   | 集内镜头号（客户端幂等查重键） |
| `title`                 | string | 否   | 镜头标题                       |
| `videoContent`          | string | 否   | 视频内容描述                   |
| `firstFrameDesc`        | string | 否   | 首帧描述                       |
| `camera`                | string | 否   | 运镜（默认「固定机位」）       |
| `shotSize`              | string | 否   | 景别                           |
| `cameraFacing`          | string | 否   | 朝向                           |
| `spatialPosition`       | string | 否   | 空间位置                       |
| `bgm`                   | string | 否   | BGM                            |
| `bgmRange`              | string | 否   | BGM 区间                       |
| `timecode`              | string | 否   | 时间码                         |
| `timeline`              | string | 否   | 时间轴                         |
| `voiceConstraint`       | string | 否   | 配音约束                       |
| `ambientSfx`            | string | 否   | 环境音效                       |
| `sfxRange`              | string | 否   | 音效区间                       |
| `duration`              | float  | 否   | 时长（秒）                     |
| `transition`            | string | 否   | 转场                           |
| `videoPromptCn`         | string | 否   | 视频提示词（中文）             |
| `videoPromptEn`         | string | 否   | 视频提示词（英文）             |
| `videoNegativePromptEn` | string | 否   | 视频反向提示词（英文）         |

### 5.7 更新镜头视频提示词

- **`PUT /api/shots/{id}/prompts`**
- 请求体：`{ "promptCn": "...", "promptEn": "...", "negativePromptEn": "..." }`
- 返回：Shot 对象

### 5.8 删除镜头

- **`DELETE /api/shots/{id}`**
- 返回：`{ "message": "已删除" }`

---

## 六、工作流（/api/workflow）

> 图片/视频/配音/合成等生成环节走工作流 API，ComfyUI 需已启动。

| 方法   | 路径                       | 参数                                       | 说明                                                               |
| ------ | -------------------------- | ------------------------------------------ | ------------------------------------------------------------------ |
| GET    | `/api/workflow/file?name=` | `name` 模板名                              | 读取工作流模板 JSON                                                |
| POST   | `/api/workflow/file`       | 请求体 `{ "name": "...", "json": "..." }`  | 保存工作流（校验+规范化缩进）                                      |
| DELETE | `/api/workflow/file?name=` | `name` 模板名                              | 删除工作流                                                         |
| POST   | `/api/workflow/raw`        | 请求体 `{ "json": "..." }` 完整工作流 JSON | 直接提交到 ComfyUI，返回原始响应（含 `prompt_id` / `node_errors`） |

---

## 七、Agent 接入约定

1. **写入顺序固定**：项目 → load → 剧本 → 资产 → 分镜（分镜依赖集 id，集依赖项目上下文）
2. **幂等由调用方保证**：服务端 POST 在 `id` 为空时一律新建、不去重。写入前先 GET 列表查重（项目按名/剧本按集号/资产按名/集按集号/镜头按集内镜号），已存在则跳过或带 `id` 更新（`agent_pipeline.py` 即此策略）
3. **中文处理**：请求体 JSON 用 UTF-8（`ensure_ascii=False`）；URL 路径中的中文项目名需 `urllib.parse.quote`
4. **错误处理**：非 200 响应体含 `message` 字段；404 通常是项目未 load 或 id 不存在
5. **后续环节**（图片/视频/配音/合成）走工作流 API（`/api/workflow/*`）

## 八、最小调用示例（Python 标准库，实测通过）

```python
import json, urllib.parse, urllib.request

BASE = "http://127.0.0.1:6066"

def call(method, path, body=None):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json; charset=utf-8")
    with urllib.request.urlopen(req, timeout=10) as resp:
        return json.loads(resp.read().decode("utf-8"))

# 1. 创建项目
project = call("POST", "/api/projects",
               {"name": "我的漫剧", "totalEpisodes": 3, "aspectRatio": 1, "status": 0})
# 2. 加载为当前项目（中文名需 quote）
call("POST", f"/api/projects/load/{urllib.parse.quote('我的漫剧')}")
# 3. 查询项目列表
names = call("GET", "/api/projects")
# 4. 查询项目详情（load 返回完整对象）
detail = call("POST", f"/api/projects/load/{urllib.parse.quote('我的漫剧')}")
# 5. 创建剧本（按集号幂等）
story = call("POST", "/api/stories",
             {"episodeNo": 1, "title": "第1集", "content": "# 第1集\n...", "duration": 120})
# 6. 删除项目（中文名需 quote）
call("DELETE", f"/api/projects/{urllib.parse.quote('我的漫剧')}")
```
