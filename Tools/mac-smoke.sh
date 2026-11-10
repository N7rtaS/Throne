#!/bin/zsh
# Automatic macOS smoke check (design: .kiro/specs/build-readme-unity-tests-smoke/design.md §4).
# Builds Builds/MacSmoke (MobileBuilder.BuildMacSmoke), runs the player with -thornSmoke in a 1280x720 window,
# waits for it to quit and checks its log.
# Usage: Tools/mac-smoke.sh [--no-build] [extra player args, e.g. -thornBoss -thornElite]
#        Tools/mac-smoke.sh --check-log FILE   (only the log check, step 4)
# Exit codes: 0 ok, 2 build failed or player missing, 3 player timed out, 4 no THORN_MOBILE_SMOKE line,
#             5 kills=0, 6 unhandled exception in the log, 7 player exit code is not 0,
#             8 -thornInputScript=FILE given and no THORN_INPUT_SCRIPT ok=1 line (design: .kiro/specs/touch-controls-contract).
set -euo pipefail
root="${0:A:h:h}"
unity="${THORN_UNITY:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
log="${THORN_SMOKE_LOG:-$root/Research/mac-smoke.log}"
timeout="${THORN_SMOKE_TIMEOUT:-180}"
app="$root/Builds/MacSmoke/ThornThrone.app"

# Step 4: checks a player log; prints the smoke line on success.
check_log()
{
  local file="$1" line kills
  if [[ ! -f "$file" ]]; then echo "mac-smoke: log not found: $file" >&2; return 4; fi
  line="$(LC_ALL=C grep -a '^THORN_MOBILE_SMOKE ' "$file" | tail -n 1 || true)"
  if [[ -z "$line" ]]; then echo "mac-smoke: no THORN_MOBILE_SMOKE line in $file" >&2; return 4; fi
  # Unhandled exceptions start a line with the exception type name (Debug.LogException / player format).
  local errors
  errors="$(LC_ALL=C grep -aE '^[A-Za-z_][A-Za-z0-9_.]*Exception(: |$)|Crash!!!' "$file" | grep -av '^THORN_' || true)"
  if [[ -n "$errors" ]]; then
    echo "mac-smoke: unhandled exception in $file:" >&2
    print -r -- "$errors" | head -n 10 >&2
    return 6
  fi
  kills="$(print -r -- "$line" | sed -nE 's/.* kills=([0-9]+).*/\1/p')"
  if [[ -z "$kills" || "$kills" == 0 ]]; then echo "mac-smoke: no kills: $line" >&2; return 5; fi
  print -r -- "$line"
}

# Batchmode cannot open a project that an Editor already has open.
editor_lock()
{
  if [[ -f "$root/UnityProject/Temp/UnityLockfile" ]] && pgrep -if "MacOS/Unity .*projectpath[ =]*$root/UnityProject" > /dev/null; then
    echo "mac-smoke: Unity Editor has UnityProject open; close it before running batchmode" >&2
    return 2
  fi
}

if [[ "${1:-}" == "--check-log" ]]; then
  check_log "${2:?log file}"
  exit $?
fi

build=1
if [[ "${1:-}" == "--no-build" ]]; then build=0; shift; fi

if (( build )); then
  editor_lock || exit 2
  buildlog="$root/Research/mac-build.log"
  code=0
  "$unity" -batchmode -nographics -quit -projectPath "$root/UnityProject" \
    -executeMethod ThornThrone.Editor.MobileBuilder.BuildMacSmoke -logFile "$buildlog" || code=$?
  if (( code != 0 )) || ! LC_ALL=C grep -aq THORN_MAC_BUILD_OK "$buildlog"; then
    echo "mac-smoke: build failed (Unity exit $code), see $buildlog" >&2
    LC_ALL=C grep -aE 'error|Exception' "$buildlog" | tail -n 15 >&2 || true
    exit 2
  fi
fi

# The binary is named after PlayerSettings.productName ("Терновый трон"); read it from Info.plist.
exe="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist" 2> /dev/null || true)"
player="$app/Contents/MacOS/$exe"
if [[ -z "$exe" || ! -x "$player" ]]; then echo "mac-smoke: player not found in $app" >&2; exit 2; fi

mkdir -p "${log:h}"
rm -f "$log"
# Run the binary directly (not through open) to get its exit code.
"$player" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -thornSmoke -logFile "$log" "$@" > /dev/null 2>&1 &
pid=$!
for (( waited = 0; waited < timeout; waited++ )); do
  kill -0 $pid 2> /dev/null || break
  sleep 1
done
if kill -0 $pid 2> /dev/null; then
  kill $pid 2> /dev/null || true
  echo "mac-smoke: player did not quit within ${timeout}s, see $log" >&2
  exit 3
fi
rc=0
wait $pid || rc=$?

if [[ " $* " == *" -thornInputScript="* ]]; then
  script_line="$(LC_ALL=C grep -a '^THORN_INPUT_SCRIPT ' "$log" | tail -n 1 || true)"
  if [[ "$script_line" != *" ok=1 "* ]]; then
    echo "mac-smoke: input script failed: ${script_line:-no THORN_INPUT_SCRIPT line}, see $log" >&2
    exit 8
  fi
  print -r -- "$script_line"
fi
check_log "$log" || exit $?
if (( rc != 0 )); then echo "mac-smoke: player exit code $rc, see $log" >&2; exit 7; fi
