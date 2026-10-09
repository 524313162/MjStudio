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
| 错误响应       | 400=参数校验失败 / 404=项目未 load 或 id 不存在 / 409=资源冲突（如项目名已存在），响应体含 `message` 字段          |
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
- 错误：`name` 为空 → 400 `{ "message": "项目名不能为空" }`；项目名已存在 → 409 `{ "message": "项目「xxx」已存在" }`

**CreateProjectRequest**

| 字段                | 类型   | 必填 | 说明                                                 |
| ------------------- | ------ | ---- | ---------------------------------------------------- |
| `name`              | string | 是   | 项目名（唯一，创建后不可改）                         |
| `storyName`         | string | 否   | 故事名                                               |
| `worldview`         | string | 否   | 世界观                                               |
| `description`       | string | 否   | 项目描述                                             |
| `totalEpisodes`     | int    | 否   | 总集数                                               |
| `episodeDuration`   | int    | 否   | 每集时长（秒，默认 120）                             |
| `aspectRatio`       | int    | 否   | 画幅：1=9:16 竖屏，2=16:9 横屏，3=1:1 方形（默认 1） |
| `targetPlatform`    | string | 否   | 目标平台（抖音/快手/B站等）                          |
| `genre`             | string | 否   | 题材                                                 |
| `audience`          | string | 否   | 目标受众                                             |
| `artStyle`          | string | 否   | 美术风格                                             |
| `voiceLanguage`     | string | 否   | 配音语言                                             |
| `originalOrAdapted` | string | 否   | 原创/改编                                            |
| `bgmStyle`          | string | 否   | BGM 风格                                             |
| `hasOpeningEnding`  | bool   | 否   | 是否有片头片尾（默认 false）                         |
| `deliverables`      | string | 否   | 交付物                                               |
| `status`            | int    | 否   | 状态：0=进行中，1=已完结（默认 0）                   |

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
> 工作流模板存放在程序目录 `workflows/` 下（文件名不含 `.json`）。
> 两种提交方式：**参数化发起**（`POST /api/workflow`，注入节点参数后异步执行，返回 `taskId` 轮询）与**原始提交**（`POST /api/workflow/raw`，直接提交完整 JSON，同步返回 ComfyUI 原始响应）。

### 6.1 列出可用工作流模板

- **`GET /api/workflow`**
- 参数：无
- 返回：`string[]` 模板名数组（不含 `.json`）

### 6.2 读取工作流模板

- **`GET /api/workflow/file?name={name}`**
- Query 参数：`name` 模板名（必填）
- 返回：WorkflowFileResponse（见下）
- 错误：缺 `name` → 400；模板不存在 → 404

### 6.3 保存工作流模板（新建或覆盖）

- **`POST /api/workflow/file`**
- 请求体：WorkflowFileRequest
- 返回：WorkflowFileResponse（`content` 为规范化缩进后的 JSON）
- 错误：缺 `name` → 400；JSON 非法 → 400

**WorkflowFileRequest**

| 字段      | 类型   | 必填     | 说明                   |
| --------- | ------ | -------- | ---------------------- |
| `name`    | string | 是       | 模板名（不含 `.json`） |
| `content` | string | 保存时是 | 工作流 JSON 内容       |

**WorkflowFileResponse**

| 字段      | 类型   | 说明                   |
| --------- | ------ | ---------------------- |
| `success` | bool   | 是否成功               |
| `message` | string | 失败原因（成功时为空） |
| `name`    | string | 模板名                 |
| `content` | string | 工作流 JSON 内容       |

### 6.4 删除工作流模板

- **`DELETE /api/workflow/file?name={name}`**
- Query 参数：`name` 模板名（必填）
- 返回：WorkflowFileResponse（`message`="已删除"）
- 错误：缺 `name` → 400；模板不存在 → 400

### 6.5 直接提交完整工作流 JSON（同步）

- **`POST /api/workflow/raw`**
- 请求体：WorkflowRawRequest
- 返回：WorkflowRawResponse（`result` 为 ComfyUI 原始响应 JSON 字符串，含 `prompt_id` / `node_errors`）
- 错误：缺内容 → 400；ComfyUI 不可达 → 400

**WorkflowRawRequest**

| 字段      | 类型   | 必填 | 说明            |
| --------- | ------ | ---- | --------------- |
| `content` | string | 是   | 完整工作流 JSON |

**WorkflowRawResponse**

| 字段      | 类型   | 说明                          |
| --------- | ------ | ----------------------------- |
| `success` | bool   | 是否成功                      |
| `message` | string | 失败原因                      |
| `result`  | string | ComfyUI 原始响应 JSON（美化） |

### 6.6 发起工作流（参数化，异步）

- **`POST /api/workflow`**
- 请求体：WorkflowRunRequest
- 返回：WorkflowResponse（`taskId` + `status`，立即返回，不阻塞）
- 错误：缺 `workflow` → 400

**WorkflowRunRequest**

| 字段         | 类型                             | 必填 | 说明                                                          |
| ------------ | -------------------------------- | ---- | ------------------------------------------------------------- |
| `workflow`   | string                           | 是   | 工作流模板名（对应 `workflows/` 下文件名，不含 `.json`）      |
| `nodeInputs` | map<string, map<string, object>> | 否   | 节点输入注入：nodeId → (inputKey → value)                     |
| `images`     | map<string, string>              | 否   | 图片注入：nodeId → 图片绝对路径（LoadImage 节点）             |
| `outputNode` | string                           | 否   | 输出节点 ID（SaveImage/SaveVideo/SaveAudio）                  |
| `mediaType`  | string                           | 否   | 产物媒体类型（image/png、video/mp4、audio/wav）               |
| `subDir`     | string                           | 否   | 产物落盘子目录（images/audio/video/voice/final，默认 images） |
| `fileName`   | string                           | 否   | 产物文件名（含扩展名）                                        |
| `purpose`    | int                              | 否   | 资源用途（ResourcePurposeEnum，见 7.3）                       |
| `timeoutSec` | int                              | 否   | 轮询超时（秒，默认 1800）                                     |

**WorkflowResponse**

| 字段           | 类型   | 说明                                     |
| -------------- | ------ | ---------------------------------------- |
| `success`      | bool   | 是否成功                                 |
| `taskId`       | string | 任务 ID（用于 6.7 轮询）                 |
| `status`       | string | 状态（pending/running/succeeded/failed） |
| `message`      | string | 提示信息                                 |
| `mediaType`    | string | 产物媒体类型（完成后填充）               |
| `resourceId`   | long   | 产物资源 id（完成后填充）                |
| `relativePath` | string | 产物相对资源目录路径（完成后填充）       |

### 6.7 查询任务状态/产物（轮询）

- **`GET /api/workflow/{taskId}`**
- 路径参数：`taskId`（6.6 返回的任务 ID）
- 返回：WorkflowResponse（`status` 为 `succeeded` 时含 `mediaType` / `resourceId` / `relativePath`）
- 错误：任务不存在 → 404

**示例（参数化发起 + 轮询）**

```
POST /api/workflow
{
  "workflow": "text2img",
  "nodeInputs": { "6": { "text": "a young female observer, anime style" } },
  "outputNode": "9",
  "mediaType": "image/png",
  "subDir": "images",
  "fileName": "xingye.png",
  "purpose": 0
}
→ 200
{ "success": true, "taskId": "a1b2c3", "status": "pending", "message": "已提交" }

GET /api/workflow/a1b2c3
→ 200
{ "success": true, "taskId": "a1b2c3", "status": "succeeded",
  "mediaType": "image/png", "resourceId": 5001, "relativePath": "images/xingye.png" }
```

---

## 七、资源（/api/resources）

> 全部依赖当前项目上下文（先 load 项目）。资源文件统一存放于 `<ResourcesRoot>\<项目名>\{images,audio,video,voice,final}`。

### 7.1 列出当前项目全部资源

- **`GET /api/resources`**
- 参数：无
- 返回：`Resource[]`

**Resource 对象**：`id` / `projectId` / `relativePath`（相对资源目录，如 `images/x.png`）/ `mediaType` / `purpose` / `duration` / `createdTime` / `updatedTime`

### 7.2 下载资源文件

- **`GET /api/resources/{id}/file`**
- 路径参数：`id`（long）
- 返回：二进制文件流（`Content-Type` 按扩展名推断：png/jpg/mp4/wav/mp3 等）
- 错误：资源不存在 → 404 `{ "message": "资源 {id} 不存在" }`

### 7.3 上传资源文件（multipart）

- **`POST /api/resources/upload`**
- 请求体：`multipart/form-data`，字段如下（**注意：不是 JSON**）
  | 字段 | 必填 | 说明 |
  | ----------- | ---- | ------------------------------------------------- |
  | `subDir` | 是 | 子目录（images/audio/video/voice/final） |
  | `fileName` | 是 | 文件名（含扩展名） |
  | `mediaType` | 是 | MIME 类型（image/png、audio/mpeg 等） |
  | `purpose` | 是 | ResourcePurposeEnum 枚举值（见下表） |
  | `file` | 是 | 文件二进制 |
- 返回：Resource 对象
- 限制：单文件最大 500MB

**ResourcePurposeEnum**

| 值  | 名称     |
| --- | -------- |
| 0   | 资产主图 |
| 1   | 资产音频 |
| 2   | 声线种子 |
| 3   | 镜头视频 |
| 4   | 放大视频 |
| 5   | 配音片段 |
| 6   | 成片     |
| 7   | 封面     |
| 99  | 其它     |

**示例（PowerShell 5.1，multipart）**

```powershell
$bytes = [System.IO.File]::ReadAllBytes("D:\tmp\xingye.png")
$content = [System.Net.Http.MultipartFormDataContent]::new()
$content.Add([System.Net.Http.StringContent]::new("images"), "subDir")
$content.Add([System.Net.Http.StringContent]::new("xingye.png"), "fileName")
$content.Add([System.Net.Http.StringContent]::new("image/png"), "mediaType")
$content.Add([System.Net.Http.StringContent]::new("0"), "purpose")
$fileContent = [System.Net.Http.ByteArrayContent]::new($bytes)
$fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new("image/png")
$content.Add($fileContent, "file", "xingye.png")
Invoke-RestMethod -Uri "http://127.0.0.1:6066/api/resources/upload" -Method Post -Body $content
```

### 7.4 删除资源

- **`DELETE /api/resources/{id}`**
- 路径参数：`id`（long）
- 返回：`{ "message": "已删除" }`

---

## 八、评审（/api/reviews）

> 评审保留给 agent：agent 经 API 拉取环节内容做评审，结论经 API 写回。全部依赖当前项目上下文（先 load 项目）。

### 8.1 获取项目全部评审记录

- **`GET /api/reviews`**
- 参数：无
- 返回：`Review[]`

### 8.2 获取某环节评审记录

- **`GET /api/reviews/{stage}`**
- 路径参数：`stage` 环节（StageEnum，见下表）
- 返回：Review 对象；不存在 → 404

### 8.3 写入/更新评审结论

- **`PUT /api/reviews/{stage}`**
- 路径参数：`stage`（StageEnum）
- 请求体：ReviewUpsertRequest
- 返回：Review 对象

**ReviewUpsertRequest**

| 字段       | 类型   | 必填 | 说明                           |
| ---------- | ------ | ---- | ------------------------------ |
| `decision` | int    | 是   | 评审结论（ReviewDecisionEnum） |
| `content`  | string | 否   | 评审正文                       |
| `issues`   | string | 否   | 问题清单（编号列表）           |

**Review 对象**：`id` / `projectId` / `stage` / `decision` / `content` / `issues` / `revisionCount` / `createdTime` / `updatedTime`

**StageEnum**

| 值  | 名称 |
| --- | ---- |
| 0   | 立项 |
| 1   | 剧本 |
| 2   | 分镜 |
| 3   | 资产 |
| 4   | 声线 |
| 5   | 音乐 |
| 6   | 音效 |
| 7   | 视频 |
| 8   | 配音 |
| 9   | 合成 |

**ReviewDecisionEnum**

| 值  | 名称   |
| --- | ------ |
| 0   | 待评审 |
| 1   | 通过   |
| 2   | 打回   |
| 3   | 升级   |

### 8.4 记录一次打回（计数+1，超 5 次自动升级）

- **`POST /api/reviews/{stage}/revision`**
- 路径参数：`stage`（StageEnum）
- 请求体：ReviewUpsertRequest（可选，`issues` 记录本次打回原因）
- 返回：Review 对象（`revisionCount` 已 +1；超过 5 次 `decision` 自动置为 3=升级）

---

## 九、设置（/api/settings）

### 9.1 获取设置

- **`GET /api/settings`**
- 参数：无
- 返回：MjStudioOptions 对象（见下）

### 9.2 更新设置

- **`PUT /api/settings`**
- 请求体：MjStudioOptions
- 返回：MjStudioOptions 对象
- **说明**：端口（`apiPort`）变更需调用方重启 API 才生效（WPF 端会自动调 `ApiServerManager.Restart`）

**MjStudioOptions**

| 字段                | 类型   | 说明                                             |
| ------------------- | ------ | ------------------------------------------------ |
| `comfyUiBaseUrl`    | string | ComfyUI 服务地址（默认 `http://127.0.0.1:8188`） |
| `comfyUiLaunchPath` | string | ComfyUI 程序启动地址（可执行文件路径）           |
| `comfyUiOutputDir`  | string | ComfyUI output 目录（取回源件用）                |
| `apiPort`           | int    | 内置 API 监听端口（默认 6066）                   |
| `apiHost`           | string | 内置 API 监听地址（默认 `127.0.0.1`）            |
| `dbRoot`            | string | 数据库目录（每个项目一个 `<项目名>.db`）         |
| `resourcesRoot`     | string | 资源目录（所有项目媒体文件统一存放处）           |
| `workflowDir`       | string | 工作流模板目录（默认 `workflows`）               |

---

## 十、健康检查

- **`GET /health`**
- 参数：无
- 返回：`{ "status": "healthy", "service": "MjStudio", "time": "<UTC ISO8601>" }`
- **说明**：可用于就绪探测（比 `GET /api/projects` 更轻量）

---

## 十一、Agent 接入约定

1. **写入顺序固定**：项目 → load → 剧本 → 资产 → 分镜（分镜依赖集 id，集依赖项目上下文）
2. **幂等由调用方保证**：服务端 POST 在 `id` 为空时一律新建、不去重。写入前先 GET 列表查重（项目按名/剧本按集号/资产按名/集按集号/镜头按集内镜号），已存在则跳过或带 `id` 更新（`agent_pipeline.py` 即此策略）
3. **中文处理**：请求体 JSON 用 UTF-8（`ensure_ascii=False`）；URL 路径中的中文项目名需 `urllib.parse.quote`
4. **错误处理**：非 200 响应体含 `message` 字段；404 通常是项目未 load 或 id 不存在；409 是资源冲突（如项目名已存在）
5. **后续环节**（图片/视频/配音/合成）走工作流 API（`/api/workflow/*`）：参数化发起用 `POST /api/workflow` + `GET /api/workflow/{taskId}` 轮询；直接提交完整 JSON 用 `POST /api/workflow/raw`
6. **资源文件**：上传用 `POST /api/resources/upload`（multipart），下载用 `GET /api/resources/{id}/file`；资产关联资源用 `PUT /api/assets/{id}/resource`
7. **评审**：agent 经 `PUT /api/reviews/{stage}` 写结论、`POST /api/reviews/{stage}/revision` 记打回（>5 次自动升级）

## 十二、最小调用示例（Python 标准库，实测通过）

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
