# -*- coding: utf-8 -*-
"""创建 Momo 角色并绑定到星海旅人项目"""
import json
import urllib.request

BASE = "http://127.0.0.1:6066"


def api(method, path, body=None):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body else None
    req = urllib.request.Request(BASE + path, data=data,
                                 headers={"Content-Type": "application/json"}, method=method)
    with urllib.request.urlopen(req, timeout=10) as r:
        raw = r.read().decode("utf-8")
        return json.loads(raw) if raw else None


a = api("POST", "/api/assets", {"assetType": 1, "name": "Momo", "description": "伙伴：智能机器人，电子音", "order": 2})
print("created Momo id=", a.get("id"))
a2 = api("PUT", f"/api/assets/{a.get('id')}/refs", {"projectIds": [1000]})
print("bound refs=", [r.get("projectId") for r in (a2.get("projectRefs") or [])])
