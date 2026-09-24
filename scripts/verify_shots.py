# -*- coding: utf-8 -*-
"""验证星海旅人分镜数据"""
import json
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


api("POST", "/api/projects/load/" + urllib.parse.quote("星海旅人"), {})
eps = api("GET", "/api/episodes")
for e in eps:
    shots = api("GET", f"/api/episodes/{e['id']}/shots")
    print(f"第{e['episodeNo']}集《{e['name']}》 {len(shots)}个镜头")
    for s in shots[:2]:
        print("  #", s["shotNo"], s["title"], "| BGM:", s.get("bgm"), "| 音效:", s.get("ambientSfx"))
