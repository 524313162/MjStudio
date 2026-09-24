# -*- coding: utf-8 -*-
"""删除指定项目所有镜头（用于重新生成新字段假数据）"""
import json
import sys
import urllib.request
import urllib.parse

BASE = "http://127.0.0.1:6066"


def api(method, path, body=None):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body else None
    req = urllib.request.Request(BASE + path, data=data,
                                 headers={"Content-Type": "application/json"}, method=method)
    with urllib.request.urlopen(req, timeout=10) as r:
        raw = r.read().decode("utf-8")
        return json.loads(raw) if raw else None


def main():
    project = sys.argv[1] if len(sys.argv) > 1 else "星海旅人"
    api("POST", "/api/projects/load/" + urllib.parse.quote(project), {})
    eps = api("GET", "/api/episodes")
    total = 0
    for e in eps:
        shots = api("GET", f"/api/episodes/{e['id']}/shots")
        for s in shots:
            api("DELETE", f"/api/shots/{s['id']}")
            total += 1
    print(f"deleted {total} shots in project {project}")


if __name__ == "__main__":
    main()
