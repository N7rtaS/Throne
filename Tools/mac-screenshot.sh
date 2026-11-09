#!/bin/zsh
# Runs the macOS smoke player (Builds/MacSmoke, MobileBuilder.BuildMacSmoke) in a window and saves a PNG.
# Usage: Tools/mac-screenshot.sh out.png [extra player args, e.g. -thornShowcase -thornCloseup]
set -euo pipefail
root="${0:A:h:h}"
out="${1:?output png}"; shift
tga="$(mktemp -t thorn).tga"
rm -f "$tga"
open -n "$root/Builds/MacSmoke/ThornThrone.app" --args -screen-fullscreen 0 -screen-width 1600 -screen-height 720 \
  -thornSmoke -thornScreenshot="$tga" -logFile "${out%.png}.log" "$@"
# The smoke run logs THORN_MOBILE_SMOKE after the screenshot (or when the hero dies).
log="${out%.png}.log"
for i in {1..60}; do LC_ALL=C grep -aq THORN_MOBILE_SMOKE "$log" 2>/dev/null && break; sleep 2; done
pkill -f "MacSmoke/ThornThrone.app" || true
sips -s format png "$tga" --out "$out" > /dev/null
rm -f "$tga"
echo "$out"
