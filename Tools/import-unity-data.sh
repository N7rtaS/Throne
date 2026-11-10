#!/bin/zsh
# macOS: copy the decoded data (Research/Json) into the Unity project. The mobile game reads these JSON
# files directly (GameData.Load); the friend's ScriptableObject importer is no longer used.
set -euo pipefail
root="${0:A:h:h}"
cp "$root"/Research/Json/survivors_*.json "$root/UnityProject/Assets/Game/Data/Source/"
echo "Copied $(ls "$root"/Research/Json/survivors_*.json | wc -l | tr -d ' ') files to UnityProject/Assets/Game/Data/Source"
# Echelon names and descriptions for the difficulty screen (design: .kiro/specs/screen-flow/design.md Ф12).
python3 "$root/Tools/import-localization.py" \
  "$root/NestOfThorns/resource/localization/vdata/survivors_difficulty/survivors_difficulty_russian.txt" \
  "$root/UnityProject/Assets/Game/Data/Localization/difficulty_ru.json"
