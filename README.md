# MjStudio · AI漫剧制作软件

基于 `manju-director` 技能改造的 WPF 桌面软件。启动时**选择/加载项目**，进程内**内置 HTTP API**，所有 ComfyUI 调用统一走 API（不再使用 Python 脚本），数据存 **SQLite（项目下）**，图片/视频/音频等二进制存**项目下 `resources` 目录**。评审保留给 agent（经 API 拉取内容做评审、写回结论）。

## 一、技术架构（Clean Architecture）

```
MjStudio.slnx
└─ src/
   ├─ Domain/MjStudio.Domain.Shared      # 枚举/配置（资产类型/画幅/环节/评审状态/媒体类型）
   ├─ Domain/MjStudio.Domain             # 实体模型（Project/Asset/Story/Episode/Shot/Resource/Review/...）
   ├─ Infrastructure/MjStudio.Infrastructure  # DbContext(SQLite) + 项目存储 + 资源存储(resources)
   ├─ Application/MjStudio.Application.Shared # 服务契约 + CurrentProject
   ├─ Application/MjStudio.Application     # 服务实现（项目/资产/剧本/分镜/资源/评审/设置）
   ├─ WebApi/MjStudio.WebApi             # 内置 HTTP API（控制器 + ComfyUI 客户端 + 工作流引擎）
   └─ Host/MjStudio.Host                 # WPF 桌面端（启动选项目 + 内嵌 Kestrel + 设置改端口重启）
```

## 二、项目存储结构

每个项目一个文件夹（根目录默认 `D:\00-ai-project\MjStudio`，可在设置改）：

```
<项目名>/
├─ mjstudio.db          # SQLite 数据库（全部结构化数据）
└─ resources/           # 二进制资源
   ├─ images/           # 资产图片（角色/场景/道具）
   ├─ audio/            # BGM/音乐/音效
   ├─ video/            # 镜头视频/放大/配音
   ├─ voice/            # 声线种子
   └─ final/            # 成片
```

## 三、启动方式

### 1. WPF 桌面端（主入口）

```
dotnet run --project src/Host/MjStudio.Host
```

- 启动即内置启动 HTTP API（默认 `http://127.0.0.1:6066`）
- 顶栏可**选择/加载/新建项目**
- 设置页可改 **API 端口**（改完点「重启 API」生效）、ComfyUI 地址、项目根目录

### 2. 纯 API / CLI 模式（供 agent 调用）

```
dotnet run --project src/WebApi/MjStudio.WebApi --no-launch-profile
```

独立启动内置 API，不依赖 WPF。

## 四、内置 API 端点

| 方法    | 路径                            | 说明                                                       |
| ------- | ------------------------------- | ---------------------------------------------------------- |
| GET     | `/health`                       | 健康检查                                                   |
| GET     | `/api/projects`                 | 列出所有项目名                                             |
| POST    | `/api/projects`                 | 创建项目（立项字段）                                       |
| POST    | `/api/projects/load/{name}`     | 加载（切换）当前项目                                       |
| GET     | `/api/projects/current`         | 当前项目                                                   |
| PUT     | `/api/projects/{id}`            | 更新项目                                                   |
| DELETE  | `/api/projects/{name}`          | 删除项目（含文件夹）                                       |
| GET     | `/api/assets?type=`             | 按类型列资产（character/scene/prop/bgm/music/soundEffect） |
| POST    | `/api/assets`                   | 创建/更新资产（**含 CN/EN 提示词**）                       |
| PUT     | `/api/assets/{id}/prompts`      | 更新资产提示词（CN/EN）                                    |
| GET     | `/api/stories`                  | 列剧本                                                     |
| POST    | `/api/stories`                  | 创建/更新剧本                                              |
| GET     | `/api/episodes`                 | 列集                                                       |
| POST    | `/api/episodes`                 | 创建/更新集（白名单/机位表/音频出现点表/台词清单）         |
| GET     | `/api/episodes/{id}/shots`      | 列镜头                                                     |
| POST    | `/api/shots`                    | 创建/更新镜头（11 项字段 + CN/EN 视频提示词）              |
| PUT     | `/api/shots/{id}/prompts`       | 更新镜头视频提示词（CN/EN）                                |
| GET     | `/api/reviews`                  | 列评审记录                                                 |
| GET     | `/api/reviews/{stage}`          | 取某环节评审                                               |
| PUT     | `/api/reviews/{stage}`          | 写入评审结论（agent 调用）                                 |
| POST    | `/api/reviews/{stage}/revision` | 记录打回（超 5 次自动升级）                                |
| GET     | `/api/resources`                | 列资源                                                     |
| GET     | `/api/resources/{id}/file`      | 下载资源文件                                               |
| POST    | `/api/resources/upload`         | 上传资源（multipart）                                      |
| GET     | `/api/workflow`                 | 列出可用工作流模板                                         |
| POST    | `/api/workflow`                 | 发起 ComfyUI 工作流（返回 taskId）                         |
| GET     | `/api/workflow/{taskId}`        | 查询任务状态/产物                                          |
| GET/PUT | `/api/settings`                 | 读取/更新设置                                              |

### 环节枚举（stage）

`init / script / storyboard / asset / voice / music / soundEffect / video / dubbing / compose`

### 资产类型枚举（assetType）

`character / scene / prop / bgm / music / soundEffect`

## 五、ComfyUI 工作流调用（替代脚本）

工作流模板位于程序 `workflows/` 目录（随程序输出）。发起请求示例：

```json
POST /api/workflow
{
  "workflow": "01.文生图（ZImage）-角色视图",
  "nodeInputs": {
    "67": { "text": "正向提示词EN" },
    "71": { "text": "反向提示词EN" },
    "68": { "width": 1472, "height": 832 },
    "70": { "seed": 12345 },
    "9":  { "filename_prefix": "manju/character/阿乐" }
  },
  "outputNode": "9",
  "mediaType": "image/png",
  "subDir": "images",
  "fileName": "阿乐.png",
  "purpose": 0
}
```

- 引擎自动：读模板 → 注入节点 → 提交 ComfyUI → 轮询 → **从 output 目录取回源件**（避免 8188/view 水印）→ 落盘项目 `resources` → 返回 `resourceId`
- 图片参考（视频/图片编辑）用 `images` 字段：`{ "201": "C:/abs/path/ref.png" }`（自动复制到 ComfyUI input）

## 六、数据结构要点（对齐技能）

- **Asset**：按类型存事实信息 + **CN/EN 正向/反向提示词**（需求 6/7）。场景额外存三维度（`sceneState` 状态 / `sceneFacing` 面 / `sceneRegion` 区域）+ 全貌总纲/布局/空间逻辑描述点。
- **Shot**：分镜 11 项字段（视频内容/首帧/运镜/空间方位/BGM/全局TC/时间轴/发声约束/环境音效/时长/转场）+ **CN/EN 视频提示词** + 参考图数量 N。
- **Review**：每环节一条，`decision`（pending/approved/revision/escalated）+ 问题清单 + 打回计数（>5 自动升级）。
- **Resource**：只存相对路径 + 元数据，文件本体在 `resources/`。

## 七、评审流程（保留给 agent）

agent 通过 API 拉取环节内容（资产/分镜/视频提示词等）→ 按技能评审规则核对 → `PUT /api/reviews/{stage}` 写回结论；打回用 `POST /api/reviews/{stage}/revision`（自动计数，超 5 次升级 escalated）。
