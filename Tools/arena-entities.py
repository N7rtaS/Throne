#!/usr/bin/env python3
"""Named points of the Skywrath arena from its entity lump, in Source units.

Input:  text dump of maps/scenes/crownfall/survivors_arena/entities/default_ents.vents_c (Source2Viewer -d)
Output: Research/Json/survivors_arena_entities.json
        { "targets": { "player_spawn": [[x, y, z]], "destructible_spawn_vase": [...], ... },
          "camera": [x, y, z], "fogBounds": [[minx, miny], [maxx, maxy]] }
Spawners with STATIC_IN_MAP use m_sSpawnInfoTargetName to pick these points.

Usage: python3 Tools/arena-entities.py DUMP
"""
import collections
import json
import os
import re
import sys

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main():
    text = open(sys.argv[1], encoding="utf-8", errors="replace").read()
    targets = collections.defaultdict(list)
    camera, fog = None, None
    for block in re.split(r"====\d+====", text):
        kv = dict(re.findall(r'^(\S+)\s+"?([^"\n]*)"?\s*$', block, re.M))
        cls = kv.get("classname")
        if cls == "worldspawn" and "_dotatilegrid_fogbounds_min" in kv:
            lo = [float(v) for v in kv["_dotatilegrid_fogbounds_min"].split()[:2]]
            hi = [float(v) for v in kv["_dotatilegrid_fogbounds_max"].split()[:2]]
            fog = [lo, hi]
        if "origin" not in kv:
            continue
        origin = [float(v) for v in kv["origin"].split()[:3]]
        name = kv.get("targetname", "").replace("[PR#]", "")
        if cls == "info_target" and name:
            targets[name].append(origin)
        elif cls == "point_camera":
            camera = origin
    out = {"targets": dict(sorted(targets.items())), "camera": camera, "fogBounds": fog}
    path = os.path.join(PROJECT, "Research", "Json", "survivors_arena_entities.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print(f"arena entities: {sum(len(v) for v in targets.values())} points, {len(targets)} names")


if __name__ == "__main__":
    main()
