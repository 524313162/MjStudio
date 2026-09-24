# -*- coding: utf-8 -*-
"""给 BGM/音效资产绑定项目引用（AssetProjectRefs）"""
import sqlite3

DB = r"D:\00-ai-project\MjStudio\mjstudio.db"
PROJECT_ID = 1000  # 星海旅人
ASSET_IDS = list(range(9, 19))  # 9-13 BGM, 14-18 音效

conn = sqlite3.connect(DB)
cur = conn.cursor()

# 查看表结构
cur.execute("SELECT sql FROM sqlite_master WHERE type='table' AND name='AssetProjectRefs'")
print("AssetProjectRefs DDL:", cur.fetchone())

# 查看现有引用
cur.execute("SELECT * FROM AssetProjectRefs")
print("existing refs:", cur.fetchall())

# 查看资产
cur.execute("SELECT Id, Name, AssetType, ProjectId FROM Assets WHERE AssetType IN (4,6)")
for r in cur.fetchall():
    print("asset:", r)

# 插入引用
now = 0
for aid in ASSET_IDS:
    cur.execute(
        "INSERT OR IGNORE INTO AssetProjectRefs (AssetId, ProjectId, CreatedTime, UpdatedTime) VALUES (?,?,?,?)",
        (aid, PROJECT_ID, now, now),
    )
conn.commit()
print("inserted refs for", ASSET_IDS)

# 验证
cur.execute("SELECT * FROM AssetProjectRefs")
print("after refs:", cur.fetchall())
conn.close()
