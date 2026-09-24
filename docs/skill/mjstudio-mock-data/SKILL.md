---
name: mjstudio-mock-data
description: MjStudio 漫剧制作桌面应用「模拟假数据生成」技能。通过内置 API（默认 http://127.0.0.1:6066）批量生成项目、剧本、分镜、资产等演示数据，用于开发调试、UI 验证、功能演示。关键词：假数据、mock、模拟数据、演示数据、seed、造数、测试数据、项目、剧本、分镜、资产。
---

# MjStudio 模拟假数据生成技能（mjstudio-mock-data）

## 一、使用时机

- 需要为 MjStudio 应用填充演示/测试数据（项目、剧本、分镜、资产等）时
- 开发调试 UI、验证 CRUD 功能、做功能演示时
- 需要快速造出"有图片、有声音、有完整剧本"的演示项目时

## 二、前置条件

1. **应用必须已启动**：MjStudio.Host 运行中，内置 API 监听 `http://127.0.0.1:6066`
2. 检查 API 是否就绪：
   ```powershell
   Invoke-RestMethod -Uri 'http://127.0.0.1:6066/api/projects' -Method Get
   ```
   返回项目名数组即就绪；连接被拒则先启动应用：
   ```powershell
   Start-Process dotnet -ArgumentList 'run','--project','src/Host/MjStudio.Host' -RedirectStandardOutput 'startup.log' -RedirectStandardError 'startup.err.log'
   ```
3. Python 3 可用（脚本用标准库 `urllib`，无第三方依赖）

## 三、脚本位置与用法

脚本位于 `scripts/` 目录（随本技能分发，也可独立运行）：

- `gen_mock_data.py`：生成项目 + 剧本数据（调用 API）
- `gen_demo_assets.py`：生成模拟图片/音频文件（纯 Python 标准库，无 PIL）

```bash
# 默认：在「测试漫剧」项目下生成 3 集剧本
python scripts/gen_mock_data.py

# 指定 API 地址
python scripts/gen_mock_data.py --base http://127.0.0.1:6066

# 指定项目名（不存在则自动创建）
python scripts/gen_mock_data.py --project 我的新项目

# 指定生成集数
python scripts/gen_mock_data.py --episodes 5

# 生成模拟图片/音频文件（放入 resources\<项目名>\{images,audio}）
python scripts/gen_demo_assets.py
```

### 参数说明

| 参数         | 默认值                  | 说明                       |
| ------------ | ----------------------- | -------------------------- |
| `--base`     | `http://127.0.0.1:6066` | API 地址                   |
| `--project`  | `测试漫剧`              | 目标项目名（不存在则创建） |
| `--episodes` | `3`                     | 生成剧本集数               |

### 幂等性

- 项目已存在 → 跳过创建
- 某集剧本已存在 → 跳过该集
- 可重复运行，不会产生重复数据

## 四、生成的假数据内容

### 1. 项目数据（`POST /api/projects`）

默认生成一个「星海旅人」科幻治愈项目（若项目名已存在则跳过）：

- 故事名称：星海旅人
- 世界观：在浩瀚星海中，一位旅人寻找失落的故乡星球
- 总集数：12，每集 120 秒
- 画幅：竖屏 9:16（`aspectRatio=1`）
- 平台：抖音；题材：科幻治愈；受众：全年龄
- 美术风格：手绘水彩；配音：中文普通话；原创
- BGM 风格：治愈系钢琴；无片头片尾
- 状态：进行中（`status=0`）

### 2. 剧本数据（`POST /api/stories`）

按 manju-director 剧本格式生成 Markdown 正文，每集包含：

- 本集概要、本集角色列表、剧情热点
- 时长估算模板（≥120s 硬指标）
- 片头/片尾标注（默认无）
- 场景 + 台词（`>` 标台词）

## 五、API 端点速查（造数用）

### 项目

| 方法   | 路径                        | 说明                 |
| ------ | --------------------------- | -------------------- |
| GET    | `/api/projects`             | 列出所有项目名       |
| POST   | `/api/projects`             | 创建项目（立项）     |
| POST   | `/api/projects/load/{name}` | 加载（切换）当前项目 |
| PUT    | `/api/projects/{id}`        | 更新项目             |
| DELETE | `/api/projects/{name}`      | 删除项目             |

### 剧本

| 方法   | 路径                        | 说明                        |
| ------ | --------------------------- | --------------------------- |
| GET    | `/api/stories`              | 列出当前项目全部剧本        |
| GET    | `/api/stories/episode/{no}` | 按集获取剧本                |
| POST   | `/api/stories`              | 创建/更新剧本（按集号幂等） |
| DELETE | `/api/stories/{id}`         | 删除剧本                    |

### 分镜（集 + 镜头）

| 方法   | 路径                              | 说明                        |
| ------ | --------------------------------- | --------------------------- |
| GET    | `/api/episodes`                   | 列出当前项目全部集          |
| POST   | `/api/episodes`                   | 创建/更新集                 |
| GET    | `/api/episodes/{episodeId}/shots` | 列出某集全部镜头            |
| POST   | `/api/shots`                      | 创建/更新镜头               |
| PUT    | `/api/shots/{id}/prompts`         | 更新镜头视频提示词（CN/EN） |
| DELETE | `/api/shots/{id}`                 | 删除镜头                    |

### 资产

| 方法   | 路径                        | 说明                        |
| ------ | --------------------------- | --------------------------- |
| GET    | `/api/assets?type={n}`      | 列出资产（可按类型过滤）    |
| POST   | `/api/assets`               | 创建/更新资产               |
| PUT    | `/api/assets/{id}/prompts`  | 更新资产提示词（正向/反向） |
| PUT    | `/api/assets/{id}/resource` | 关联资源（图片/音频）       |
| DELETE | `/api/assets/{id}`          | 删除资产                    |

### 资源

| 方法   | 路径                       | 说明                      |
| ------ | -------------------------- | ------------------------- |
| GET    | `/api/resources`           | 列出当前项目全部资源      |
| POST   | `/api/resources/upload`    | 上传资源文件（multipart） |
| GET    | `/api/resources/{id}/file` | 下载资源文件              |
| DELETE | `/api/resources/{id}`      | 删除资源                  |

## 六、枚举值速查

### 资产类型（AssetTypeEnum）

| 值  | 名称             |
| --- | ---------------- |
| 1   | 角色 Character   |
| 2   | 场景 Scene       |
| 3   | 道具 Prop        |
| 4   | BGM              |
| 5   | 音乐 Music       |
| 6   | 音效 SoundEffect |
| 7   | 声线 Voice       |

### 资源用途（ResourcePurposeEnum）

| 值  | 名称                   |
| --- | ---------------------- |
| 0   | 资产主图 AssetImage    |
| 1   | 资产音频 AssetAudio    |
| 2   | 声线种子 VoiceSeed     |
| 3   | 镜头视频 ShotVideo     |
| 4   | 放大视频 UpscaledVideo |
| 5   | 配音片段 DubbingClip   |
| 6   | 成片 FinalVideo        |
| 7   | 封面 Cover             |
| 99  | 其它 Other             |

### 画幅（AspectRatioEnum）

| 值  | 名称      |
| --- | --------- |
| 1   | 竖屏 9:16 |
| 2   | 横屏 16:9 |
| 3   | 方形 1:1  |

### 项目状态（ProjectStatusEnum）

| 值  | 名称              |
| --- | ----------------- |
| 0   | 进行中 InProgress |
| 1   | 已完结 Completed  |

## 七、扩展：造更多类型数据

### 造资产（含图片/音频）

```python
# 1. 创建资产
POST /api/assets
{
  "assetType": 1,          # 角色
  "name": "星野",
  "description": "星际旅人",
  "prompt": "手绘水彩风格，年轻旅人，银发蓝眼，星际制服",
  "negativePrompt": "模糊，低质量，变形",
  "order": 1
}

# 2. 关联资源（图片/音频）
PUT /api/assets/{id}/resource?relativePath=images/demo_character.png&mediaType=image/png&purpose=0
```

### 造分镜（集 + 镜头）

```python
# 1. 创建集
POST /api/episodes
{ "episodeNo": 1, "name": "第1集", "duration": 120 }

# 2. 创建镜头
POST /api/shots
{
  "episodeId": 1,
  "shotNo": 1,
  "title": "开场",
  "videoContent": "星港码头夜景",
  "firstFrameDesc": "星野站在码头望向星空",
  "camera": "固定机位",
  "shotSize": "全景",
  "duration": 8,
  "transition": "淡入"
}
```

## 八、注意事项

1. **必须先加载项目**再操作剧本/分镜/资产（这些接口依赖"当前项目"）
2. 中文项目名在 URL 中需 URL 编码（脚本已处理）
3. 资源文件需先放到 `<ResourcesRoot>\<项目名>\{images,audio,video,voice,final}` 目录，再通过 `AttachResource` 关联
4. 脚本幂等，可安全重复运行
