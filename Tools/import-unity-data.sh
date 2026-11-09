#!/bin/zsh
# macOS: copy the decoded data (Research/Json) into the Unity project. The mobile game reads these JSON
# files directly (GameData.Load); the friend's ScriptableObject importer is no longer used.
set -euo pipefail
root="${0:A:h:h}"
cp "$root"/Research/Json/survivors_*.json "$root/UnityProject/Assets/Game/Data/Source/"
echo "Copied $(ls "$root"/Research/Json/survivors_*.json | wc -l | tr -d ' ') files to UnityProject/Assets/Game/Data/Source"
