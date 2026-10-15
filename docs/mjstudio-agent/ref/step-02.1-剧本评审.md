> 加载时机：剧本落库后。目标：`review-put --stage 剧本`，通过才进第三步。

# 2.1 剧本评审（stage=1）

## 一、评审流程
```bash
python scripts/mj.py story-list                       # 拉全部剧本
python scripts/mj.py story-get --episode-no 1         # 逐集核对
python scripts/mj.py review-put --stage 剧本 --decision 通过 --content-file 剧本评审.md
# 不通过：
python scripts/mj.py review-revision --stage 剧本 --issues-file 剧本问题.md
```
- `decision`：1 通过 / 2 打回 / 3 升级
- 打回 → 修复后**全量重跑本文件全部规则**，不信任上轮「已修正」
- 打回超 5 次服务端自动升级 → 停止自动流程，交用户决策

## 二、评审规则（逐项验证，不符即打回）
1. **集数齐全**：`story-list` 条数 = 项目 `totalEpisodes`；集号连续无缺
2. **每集时长 ≥ 120 秒**（或 `episodeDuration`）：正文附 timing.md 自算明细，逐项加总达标
3. **本集角色列表完整**（`characterList` 非空）：含全部出场角色 + 一次性有台词角色 + 旁白
4. **台词量匹配画面节奏**：不多不少；旁白克制
5. **片头/片尾标注**：与立项 `hasOpeningEnding` 一致（默认无）
6. **立项字段一致性**：题材/受众/美术风格/配音语言/世界观与项目字段相符
7. **真实产物**：`content` 为完整正文，非空占位、非"示例/待补"
8. **AIGC 溯源块与水印零容忍**：扫描正文是否混入 `AIGC:` / Label / ContentProducer / ProduceID / ContentPropagator / PropagateID /「内容由AI生成，仅供参考」等，发现即打回删除（带 AIGC 块的产物无法通过抖音审核）
9. **引用一致性**：不得擅自改写后续环节才定稿的角色外貌/声线

## 三、内容审查（当前模型支持时执行）
- 故事完整：起承转合齐全，不是片段
- 每章 ≥ 1 个剧情热点
- 角色行为符合世界观
- 模型不支持内容审查时：不做内容审查，提醒用户自行审查，**不允许退化为数量/命名核对**

## 四、不一致处理
剧情/角色/时长任一与项目立项字段不一致 → 先提醒用户是否继续，不主动放行。

## 五、输出格式（`--content-file` 评审正文，`--issues-file` 问题清单）
```markdown
# 评审结论：剧本
结论：✅ Pass / ❌ Revise
覆盖集数：第1集 ~ 第N集
问题清单：
- RV-01: [问题描述]（期望 vs 实际）
- RV-02: ...
建议：打回剧本环节重写 / 升级用户
```
