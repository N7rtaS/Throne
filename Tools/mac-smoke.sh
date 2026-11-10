#!/bin/zsh
# Automatic macOS smoke check (design: .kiro/specs/build-readme-unity-tests-smoke/design.md §4).
# Builds Builds/MacSmoke (MobileBuilder.BuildMacSmoke), runs the player with -thornSmoke in a 1280x720 window,
# waits for it to quit and checks its log.
# Usage: Tools/mac-smoke.sh [--no-build] [--settings-restart] [extra player args, e.g. -thornBoss -thornElite]
#        Tools/mac-smoke.sh --check-log FILE   (only the log check, step 4)
# --settings-restart (design: .kiro/specs/settings-screen/design.md §5): run 1 changes settings (-thornSettings),
#   run 2 expects them after the restart (-thornExpectSettings), run 3 restores the defaults of the test profile.
# Exit codes: 0 ok, 2 build failed or player missing, 3 player timed out, 4 no THORN_MOBILE_SMOKE line,
#             5 kills=0, 6 unhandled exception in the log, 7 player exit code is not 0,
#             8 -thornInputScript=FILE given and no THORN_INPUT_SCRIPT ok=1 line (design: .kiro/specs/touch-controls-contract),
#             9 -thornSettings / -thornExpectSettings given and no THORN_SETTINGS_SET / THORN_SETTINGS ok=1 line.
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
restart=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build) build=0; shift ;;
    --settings-restart) restart=1; shift ;;
    *) break ;;
  esac
done

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
# One player run with the smoke checks of its log; returns the exit code of the script.
run_once()
{
  local runlog="$1"; shift
  rm -f "$runlog"
  # Run the binary directly (not through open) to get its exit code.
  "$player" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -thornSmoke -logFile "$runlog" "$@" > /dev/null 2>&1 &
  local pid=$! waited
  for (( waited = 0; waited < timeout; waited++ )); do
    kill -0 $pid 2> /dev/null || break
    sleep 1
  done
  if kill -0 $pid 2> /dev/null; then
    kill $pid 2> /dev/null || true
    echo "mac-smoke: player did not quit within ${timeout}s, see $runlog" >&2
    return 3
  fi
  local rc=0
  wait $pid || rc=$?
  if [[ " $* " == *" -thornInputScript="* ]]; then
    local script_line
    script_line="$(LC_ALL=C grep -a '^THORN_INPUT_SCRIPT ' "$runlog" | tail -n 1 || true)"
    if [[ "$script_line" != *" ok=1 "* ]]; then
      echo "mac-smoke: input script failed: ${script_line:-no THORN_INPUT_SCRIPT line}, see $runlog" >&2
      return 8
    fi
    print -r -- "$script_line"
  fi
  if [[ " $* " == *" -thornSettings="* ]]; then
    local set_line
    set_line="$(LC_ALL=C grep -a '^THORN_SETTINGS_SET ' "$runlog" | tail -n 1 || true)"
    if [[ -z "$set_line" ]]; then
      echo "mac-smoke: settings not set: $(LC_ALL=C grep -a '^THORN_SETTINGS ' "$runlog" | tail -n 1 || echo 'no THORN_SETTINGS_SET line'), see $runlog" >&2
      return 9
    fi
    print -r -- "$set_line"
  fi
  if [[ " $* " == *" -thornExpectSettings="* ]]; then
    local settings_line
    settings_line="$(LC_ALL=C grep -a '^THORN_SETTINGS ' "$runlog" | tail -n 1 || true)"
    if [[ "$settings_line" != "THORN_SETTINGS ok=1 "* ]]; then
      echo "mac-smoke: settings check failed: ${settings_line:-no THORN_SETTINGS line}, see $runlog" >&2
      return 9
    fi
    print -r -- "$settings_line"
  fi
  check_log "$runlog" || return $?
  if (( rc != 0 )); then echo "mac-smoke: player exit code $rc, see $runlog" >&2; return 7; fi
}
if (( restart )); then
  changed="targetFps=30,uiScale=1.2,joystick=fixed,side=right,masterVolume=0.5,voiceVolume=0.3"
  defaults="targetFps=60,uiScale=1,joystick=floating,side=left,masterVolume=1,voiceVolume=1"
  run_once "${log%.log}-settings-1.log" -thornSettings="$changed" "$@" || exit $?
  run_once "${log%.log}-settings-2.log" -thornExpectSettings="$changed" "$@" || exit $?
  run_once "$log" -thornSettings="$defaults" -thornExpectSettings="$defaults" "$@" || exit $?
  exit 0
fi
run_once "$log" "$@" || exit $?
