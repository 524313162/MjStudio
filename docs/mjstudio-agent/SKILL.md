---
name: mjstudio-agent
description: MjStudio AI 漫剧制作技能（API 驱动版）。通过 MjStudio 内置 HTTP API（默认 http://127.0.0.1:6066）完成漫剧/短剧/AI动画剧的立项、剧本、分镜三个环节，每环节自带评审子步骤并把结论写回软件评审表；项目/剧本/集/镜头/评审全部落库到 MjStudio，不落本地文件。脚本仅依赖 Python3 标准库，复制整个文件夹即可迁移到任何 Agent。当用户提出用 MjStudio 做漫剧、做剧本、做分镜、立项建项目时必须加载本技能。
agent_created: true
---

# MjStudio AI 漫剧制作技能（三步版：立项 → 剧本 → 分镜）

## 一、使用时机（硬约束）
- 用户要用 **MjStudio** 做漫剧（漫剧/短剧/AI动画剧/竖屏横屏剧情片），或要求「建项目 / 写剧本 / 做分镜」且上下文指向 MjStudio，**必须先加载本技能**，按其流程执行。
- 本技能当前只覆盖 **三个环节**：`第一步 立项建项目` → `第二步 剧本` → `第三步 分镜`。资产/声线/音乐/音效/视频/配音/合成**尚未纳入**，用户提到时如实说明「后续扩展」，不得臆造接口或空数据充数。
- 与旧技能 `manju-director`（纯文件系统版）**不冲突但不可混用**：本技能的数据唯一落点是 MjStudio 数据库（经 API），不要再往 `00_项目/01_剧本/03_分镜` 目录写文件。

## 二、迁移与安装（可随意迁移到任何 Agent）
整个技能 = 一个自包含文件夹，拷贝到目标 Agent 的 skills 目录即可：
```
mjstudio-agent/
├─ SKILL.md          本文件（唯一入口与调度）
├─ config.json       迁移时只改这两个字段：baseUrl / exePath
├─ scripts/mj.py     API 客户端（仅 Python3 标准库，无 requests 等依赖）
└─ ref/              各环节详细规则（按需加载，见第七节）
```
- **换机器**：改 `config.json` 的 `exePath`（MjStudio.Host.exe 绝对路径）与 `baseUrl`（端口在 MjStudio 设置页可改）。
- **换端口**：`python scripts/mj.py --base-url http://127.0.0.1:XXXX health`（或 `--config 路径`）。
- **无界面模式**：exePath 可指向同目录的 `MjStudio.WebApi.exe`（纯 API，不依赖 WPF）。

## 三、开工第一件事：环境就绪
```bash
python scripts/mj.py ensure          # API 不通则自动拉起 exe 并轮询 /health 直到就绪
python scripts/mj.py settings-get    # 可选：查看 dbRoot（数据库目录）/ resourcesRoot（资源目录）
```
- 若 `ensure` 报「exePath 未配置或不存在」→ 手工启动 MjStudio 后重试，或修正 `config.json`。
- **API 未就绪 = 环节阻塞**：必须如实报告用户，不得跳过、不得用假数据/空文件充数。

## 四、命令速查（`scripts/mj.py`，工作目录建议为技能根目录）
| 环节 | 命令 |
|---|---|
| 就绪 | `ensure` / `health` / `settings-get` |
| 立项 | `project-list` / `project-current` / `project-load --name X` / `project-get --name X`（=详情）/ `ensure-project --name X ...` / `project-delete --name X` |
| 剧本 | `story-list` / `story-get --episode-no 1` / `story-upsert --episode-no 1 --title "第1集 信号" --character-list-file 角色.txt --content-file 第1集.md --duration 130` / `story-delete --id 1001` |
| 分镜·集 | `episode-list` / `episode-upsert --episode-no 1 --name "第1集" --duration 130 --asset-whitelist-file 白名单.md --camera-switch-table-file 机位表.md --audio-cue-table-file 音频点表.md --dialogue-list-file 台词.json` |
| 分镜·镜头 | `shot-list --episode-id 1001` / `shot-upsert --episode-no 1 --data-file shot.json` / `shot-upsert-many --episode-no 1 --data-file shots.json` / `shot-delete --id 3001` |
| 评审 | `review-get --stage 剧本` / `review-put --stage 剧本 --decision 通过 --content-file 评审.md --issues-file 问题.md` / `review-revision --stage 分镜 --issues-file 问题.md` |

**中文一律走文件**：用 `--xxx-file` 传 UTF-8 文件，不要把中文 JSON 拼在命令行里（Windows 各 shell 编码不一致会乱码）。`--data-file` 传完整 payload（对象或数组），优先级高于单字段参数。

## 五、三步流程总览
```
用户提出做漫剧
 └─ 0 前置：mj.py ensure（API 就绪）
 └─ 第一步 立项（导演）
     引导确认 9 项 → ensure-project 建项目并 load → PUT /api/reviews/0 写立项评审
 └─ 第二步 剧本（编剧）
     逐集写剧本 → story-upsert（按 episodeNo 幂等）→ 2.1 剧本评审 → review-put stage=1
 └─ 第三步 分镜（分镜师）
     episode-upsert（集 + 白名单/机位表/音频点表/台词清单）
       → shot-upsert-many（逐镜 11 项字段，按 shotNo 幂等）
       → 3.1 分镜评审 → review-put stage=2
       → 【分镜评审通过后必须请用户审核确认】用户确认后本技能当前范围结束
 └─ 后续（资产/声线/音乐/音效/视频/配音/合成）：本版本未纳入，等后续扩展
```

**评审与打回规则**（三环节通用）：
- `decision`：0 待评审 / 1 通过 / 2 打回 / 3 升级。通过才进下一环节。
- 打回 → `review-revision --stage X --issues-file 问题.md`（计数 +1，超过 5 次服务端自动置为「升级」）→ 修复后**全量重跑该环节全部评审规则**，不信任上轮"已修正"。
- 升级 → 停止自动流程，交用户决策。
- **阻塞即报告**：接口不通/数据无法真实写入 → 标记阻塞并报告用户，不得放行。

## 六、数据落位映射（本技能的核心：Markdown 内容 ↔ API 字段）
旧技能把内容写文件，本技能把内容**写进 API 字段**。映射固定如下，不得自行发明落点：

### 6.1 立项 → `POST /api/projects`
| 立项项 | 字段 |
|---|---|
| 项目名（唯一，创建后不可改） | `name` |
| 故事名 | `storyName` |
| 世界观一句话 | `worldview` |
| 项目描述 | `description` |
| 总集数 / 每集时长 | `totalEpisodes` / `episodeDuration`（默认 120） |
| 画幅 | `aspectRatio`：1=9:16 竖屏，2=16:9 横屏，3=1:1 |
| 目标平台 / 题材 / 受众 | `targetPlatform` / `genre` / `audience` |
| 美术风格 + **风格锚点块** | `artStyle`（正向锚点 + 反向排雷词写进这一个字段，全剧统一引用） |
| 配音语言 / 原创改编 / BGM风格 | `voiceLanguage` / `originalOrAdapted` / `bgmStyle` |
| 片头片尾 / 交付物 / 状态 | `hasOpeningEnding`（默认 false）/ `deliverables` / `status`（0 进行中） |

> 存储路径不由本技能决定：项目数据与资源目录由 MjStudio 设置（`dbRoot` / `resourcesRoot`）管理，`settings-get` 可查。**不要**再按旧技能去问用户「工作目录」。

### 6.2 剧本 → `POST /api/stories`（每集一条）
| 剧本内容 | 字段 |
|---|---|
| 集号 | `episodeNo`（幂等键） |
| 标题 | `title`（如「第1集 信号」） |
| **本集角色列表**（含一次性有台词角色、旁白） | `characterList`（纯文本，分镜环节据此提资产） |
| 剧本正文（固定 Markdown 模板） | `content` |
| 本集时长（秒） | `duration` |

### 6.3 分镜 → `POST /api/episodes`（集）+ `POST /api/shots`（镜头）
**集级四表**（分镜文件头部的四张表正好对应 episode 的四个 string 字段）：

| 分镜产出 | 字段 |
|---|---|
| 集号 / 集名 / 时长 | `episodeNo`（幂等键）/ `name` / `duration` |
| 本集资产白名单 | `assetWhitelist` |
| 本集同场景机位切换表 | `cameraSwitchTable` |
| 本集音频出现点表 | `audioCueTable` |
| **第N集台词清单 JSON** | `dialogueList`（把 JSON 文本整段存入该字段） |

**镜头级 11 项字段 → shot 字段**（一一对应，缺一即不合格）：

| 分镜字段 | API 字段 |
|---|---|
| 镜头号 | `shotNo`（集内幂等键）+ `episodeId` 必填 |
| 镜头标题 | `title`（如「镜头1」） |
| ① 视频内容 | `videoContent` |
| ② 首帧画面描述 | `firstFrameDesc` |
| ③ 运镜（景别+机位+运镜） | 景别→`shotSize`，运镜/机位→`camera` |
| ④ 空间方位 | `spatialPosition` |
| ⑤ 资产引用 | **暂写进 `videoContent` 末尾一行** `【资产引用】[角色:小木][场景:小木家-白天-镜头朝东]`（见下方注） |
| ⑥ BGM | `bgm`（曲目名或"无"） |
| ⑥b BGM 区间（起播/停止秒点） | `bgmRange` |
| ⑦ 全局时间码 TC | `timecode` |
| ⑧ 镜头时间轴 | `timeline` |
| ⑨ 发声约束 | `voiceConstraint` |
| ⑩ 环境音效 / 音效触发点 | `ambientSfx` / `sfxRange` |
| ⑪ 时长 / 转场 | `duration`（秒，float）/ `transition` |
| 镜头朝向（面） | `cameraFacing` |
| 视频提示词（后续环节产出） | `videoPromptCn` / `videoPromptEn` / `videoNegativePromptEn`（本版本不产出） |

> **注（唯一一处妥协）**：`ShotUpsertRequest` 目前没有独立的「资产引用」字段。约定暂挂在 `videoContent` 末尾固定一行 `【资产引用】…`，保证镜头级资产可追溯。若后续 MjStudio 给 Shot 增加 `assetRefs` 字段，只需把这一行的落点改掉，其余流程不变。

### 6.4 评审 → `PUT /api/reviews/{stage}`
`stage`：0 立项 / 1 剧本 / 2 分镜 / 3 资产 / 4 声线 / 5 音乐 / 6 音效 / 7 视频 / 8 配音 / 9 合成。
`decision`：0 待评审 / 1 通过 / 2 打回 / 3 升级；`content`=评审正文，`issues`=编号问题清单。

## 七、步骤索引（按需加载，不要一次全读）
| 步骤 | 环节 | 详细规则 |
|---|---|---|
| 0 | 通用纪律 / 时长公式 | [ref/timing.md](ref/timing.md) |
| 1 | 第一步 立项 | [ref/step-01-立项.md](ref/step-01-立项.md) |
| 2 | 第二步 剧本 | [ref/step-02-剧本.md](ref/step-02-剧本.md) |
| 2.1 | 剧本评审 | [ref/step-02.1-剧本评审.md](ref/step-02.1-剧本评审.md) |
| 3 | 第三步 分镜 | [ref/step-03-分镜.md](ref/step-03-分镜.md) |
| 3.1 | 分镜评审 | [ref/step-03.1-分镜评审.md](ref/step-03.1-分镜评审.md) |
| — | 接口速查（自包含精简版） | [ref/api.md](ref/api.md)（完整版在仓库 `docs/api.md`） |

**执行纪律**：只加载当前步骤的 ref；一环节做完立刻走该环节的 .1 评审；通过才进下一步。

## 八、通用纪律（违反即返工）
1. **写入顺序固定**：`ensure（就绪）→ ensure-project（建项目并 load）→ 剧本 → 集 → 镜头`。stories/episodes/shots 全部依赖「当前项目」，项目没 load = 404。
2. **幂等由客户端保证**：服务端 POST 在 `id` 为空时一律新建、不去重。`mj.py` 的 `story-upsert / episode-upsert / shot-upsert(-many)` 已内置查重（剧本按集号、集按集号、镜头按集内镜号），**禁止绕过脚本直接裸 POST**。
3. **中文编码**：请求体 UTF-8；中文项目名走 URL 编码（脚本已处理）；正文一律 `--xxx-file` 传入。
4. **真实产出**：剧本正文、集四表、镜头 11 项必须写全，禁止空占位、禁止"模拟"。
5. **错误解读**：400=参数校验失败，404=项目未 load 或 id 不存在，409=冲突（如项目名已存在）。非 200 先读 `message`。
6. **一环一确认**：分镜评审通过后必须请用户审核；未获确认不进入后续环节（本版本到分镜为止，确认后即收尾）。
