---
name: mjstudio-agent
description: MjStudio 漫剧制作桌面应用「Agent 接入」技能。模拟/接入 AI Agent 通过内置 API（默认 http://127.0.0.1:6066）自动完成漫剧制作前期全流程：自动创建项目（立项）→ 自动创建剧本 → 自动创建资产（角色/场景/道具/BGM/音效）→ 自动创建分镜（集+镜头，含 CN/EN 视频提示词）。一键流水线脚本幂等可重跑。完整接口文档（路径/参数/请求体/返回/实测示例）见 ref/api.md。关键词：agent 接入、自动创建项目、自动创建剧本、自动创建资产、自动创建分镜、流水线、pipeline、API 造数、接口文档、立项、分镜镜头。
---

# MjStudio Agent 接入技能（mjstudio-agent）

## 一、使用时机

- 模拟/接入 AI Agent 向 MjStudio 写入漫剧制作前期数据时
- 需要一键自动完成「项目 → 剧本 → 资产 → 分镜」四个环节时
- Agent 需要按环节逐步调用 API（而非一键脚本）时，参考第五节 API 速查

## 二、前置条件

1. **应用必须已启动**：MjStudio.Host 运行中，内置 API 监听 `http://127.0.0.1:6066`
2. 检查 API 是否就绪：
   ```powershell
   Invoke-RestMethod -Uri 'http://127.0.0.1:6066/api/projects' -Method Get
   ```
   返回项目名数组即就绪；连接被拒则先启动应用（`scripts/start-mjstudio.ps1`）
3. Python 3 可用（脚本用标准库 `urllib`，无第三方依赖）

## 三、一键流水线（推荐）

```bash
# 默认：创建「星海旅人」项目 + 3 集剧本 + 全套资产 + 每集 4 个镜头
python docs/skill/mjstudio-agent/scripts/agent_pipeline.py

# 指定项目名 / 集数 / 每集镜头数 / API 地址
python docs/skill/mjstudio-agent/scripts/agent_pipeline.py --project 我的漫剧 --episodes 5 --shots 6

# 只跑部分环节（默认 all）
python docs/skill/mjstudio-agent/scripts/agent_pipeline.py --steps project,story
python docs/skill/mjstudio-agent/scripts/agent_pipeline.py --steps assets,shots
```

### 参数说明

| 参数         | 默认值                  | 说明                                   |
| ------------ | ----------------------- | -------------------------------------- |
| `--base`     | `http://127.0.0.1:6066` | API 地址                               |
| `--project`  | `星海旅人`              | 目标项目名（不存在则自动创建）         |
| `--episodes` | `3`                     | 剧本/分镜集数                          |
| `--shots`    | `4`                     | 每集镜头数（每镜 30s，合计 120s）      |
| `--steps`    | `all`                   | 逗号分隔：`project,story,assets,shots` |

### 幂等性（可重复运行，不产生重复数据）

> 注意：**服务端 POST 在 `id` 为空时一律新建、不去重**（实测）。下表是**脚本的客户端查重策略**——先 GET 列表查重，已存在则跳过。自行调 API 的 agent 必须自己实现同样的查重逻辑。

| 环节 | 幂等键         | 行为                        |
| ---- | -------------- | --------------------------- |
| 项目 | 项目名         | 已存在 → 跳过创建，直接加载 |
| 剧本 | 集号 episodeNo | 该集已存在 → 跳过           |
| 资产 | 资产名 name    | 同名已存在 → 跳过           |
| 集   | 集号 episodeNo | 已存在 → 复用其 id          |
| 镜头 | 集内 shotNo    | 该镜已存在 → 跳过           |

## 四、流水线各环节产出

### 1. 项目（`POST /api/projects` + `POST /api/projects/load/{name}`）

立项字段完整写入：故事名/世界观/总集数/每集时长/画幅(竖屏9:16)/平台/题材/受众/美术风格/配音语言/BGM风格/交付物/状态(进行中)。创建后**立即 load 为当前项目**（后续所有 API 都依赖当前项目上下文）。

### 2. 剧本（`POST /api/stories`，脚本按集号查重跳过）

每集 Markdown 正文按 manju-director 剧本格式：本集概要 / 本集角色列表 / 剧情热点 / 时长估算（≥120s 硬指标）/ 片头片尾（默认无）/ 场景+台词（`>` 标台词）。

### 3. 资产（`POST /api/assets`，脚本按名称查重跳过）

按 AssetTypeEnum 分类型创建（含正向 Prompt + 反向 NegativePrompt）：

| 类型             | 枚举值 | 示例                           |
| ---------------- | ------ | ------------------------------ |
| 角色 Character   | 1      | 星野（主角）、Momo（机器人）   |
| 场景 Scene       | 2      | 星港码头、飞船驾驶舱、未知星球 |
| 道具 Prop        | 3      | 信号接收器                     |
| BGM              | 4      | 治愈系钢琴主题曲               |
| 音效 SoundEffect | 6      | 引擎轰鸣、信号提示音           |

### 4. 分镜（`POST /api/episodes` + `POST /api/shots`）

- 每集一个 Episode（集号/名称/时长/资产白名单）
- 每集 N 个 Shot（默认 4 镜 × 30s = 120s），字段完整：
  镜头号/标题/视频内容/首帧描述/运镜(默认固定机位)/景别/朝向/空间位置/BGM/时间码/时间轴/时长/转场 + **VideoPromptCn / VideoPromptEn / VideoNegativePromptEn**（CN/EN 双视频提示词）

## 五、接口使用方案（详见 ref/api.md）

> **完整接口文档在 `ref/api.md`**（按接口文档标准编写）：每个接口含 **路径 / 参数 / 请求体字段表 / 返回对象 / 实测示例**，覆盖 项目/剧本/资产/分镜/工作流 全部端点，另含通用约定（Base URL/编码/当前项目上下文/错误处理）、Agent 接入约定、Python 最小调用示例。Agent 逐步调用 API 时以该文档为准。

核心要点（速记）：

- **Base URL**：`http://127.0.0.1:6066`，请求体 JSON camelCase + UTF-8
- **当前项目上下文**：stories/assets/episodes/shots 都依赖先 `POST /api/projects/load/{name}` 加载（中文名需 URL 编码）
- **查询项目详情**：无独立 GET by name，用 `POST /api/projects/load/{name}`（返回完整 Project 对象）或 `GET /api/projects/current`
- **写入顺序固定**：项目 → load → 剧本 → 资产 → 分镜
- **幂等由调用方保证**：服务端 POST 不去重（`id` 为空一律新建），写入前先 GET 列表查重（项目按名/剧本按集号/资产按名/镜头按集内镜号），已存在则跳过或带 `id` 更新
- **生成环节**（图片/视频/配音/合成）走工作流 API（`/api/workflow/*`），见 ref/api.md 第六节
