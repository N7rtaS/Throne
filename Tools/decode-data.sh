#!/bin/zsh
# macOS: decode survivors VData and sound events with Source2Viewer CLI, convert KV3 to JSON.
# Input:  NestOfThorns/ (run extract-resources.py first)
# Output: Research/Decoded/..., Research/Json/survivors_*.json, Research/Json/soundevents_*.json
set -euo pipefail
root="${0:A:h:h}"
cli="${THORN_S2V_CLI:-$root/Tools/Source2Viewer/cli-macos/Source2Viewer-CLI}"
if [[ ! -x "$cli" ]]; then
  echo "Source2Viewer CLI not found: $cli" >&2
  echo "Download cli-macos-arm64.zip (ValveResourceFormat 20.1) into Tools/Source2Viewer/cli-macos" >&2
  exit 1
fi

decoded="$root/Research/Decoded/scripts/events/crownfall"
"$cli" -i "$root/NestOfThorns/scripts/events/crownfall" -o "$decoded" -d --recursive

# Sound events used by the mode. soundevents_crownfall holds shared Crownfall UI/music events.
sounds="$root/Research/Decoded/soundevents"
mkdir -p "$sounds"
for name in soundevents_survivors soundevents_crownfall; do
  "$cli" -i "$root/NestOfThorns/soundevents/$name.vsndevts_c" -o "$sounds/$name.vsndevts" -d
done

# Arena entity lump: named points (player spawn, vases, elite rooms, boss) → Research/Json/survivors_arena_entities.json
arena="$root/NestOfThorns/maps/scenes/crownfall/survivors_arena.vpk"
if [[ -f "$arena" ]]; then
  ents="$(mktemp -d)"
  "$cli" -i "$arena" -f "maps/scenes/crownfall/survivors_arena/entities/default_ents.vents_c" -o "$ents/ents" -d > /dev/null
  python3 "$root/Tools/arena-entities.py" "$ents/ents"
  rm -rf "$ents"
  # Grid navigation (walkable floor, boss room): copied as is, read by NavGrid.
  python3 -c "
import sys; sys.path.insert(0, '$root/Tools'); from vpk import Vpk
v = Vpk('$arena'); open('$root/Research/Decoded/survivors_arena.gnv', 'wb').write(v.read(v.entries['maps/scenes/crownfall/survivors_arena.gnv']))"
  cp "$root/Research/Decoded/survivors_arena.gnv" "$root/UnityProject/Assets/Game/Data/Source/survivors_arena.gnv.bytes"
  cp "$root/Research/Json/survivors_arena_entities.json" "$root/UnityProject/Assets/Game/Data/Source/"
fi

node --input-type=module -e "
import fs from 'node:fs';
import path from 'node:path';
import { convertFolder, parseKV3 } from '$root/Tools/kv3-to-json.mjs';
const [vdata, sounds, json] = process.argv.slice(1);
console.log(JSON.stringify(convertFolder(vdata, json)));
for (const name of fs.readdirSync(sounds).filter(n => n.endsWith('.vsndevts'))) {
  const obj = parseKV3(fs.readFileSync(path.join(sounds, name), 'utf8'));
  fs.writeFileSync(path.join(json, name.replace(/\.vsndevts$/, '.json')), JSON.stringify(obj, null, 2) + '\n');
  console.log(name, Object.keys(obj).length, 'events');
}
" "$decoded" "$sounds" "$root/Research/Json"
