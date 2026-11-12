#!/bin/zsh
# Automatic macOS smoke check (design: .kiro/specs/build-readme-unity-tests-smoke/design.md §4).
# Builds Builds/MacSmoke (MobileBuilder.BuildMacSmoke), runs the player with -thornSmoke in a 1280x720 window,
# waits for it to quit and checks its log.
# Usage: Tools/mac-smoke.sh [--no-build] [--settings-restart] [--flow] [--hud-alloc] [--test-profile] [extra player args, e.g. -thornBoss]
#        Tools/mac-smoke.sh --check-log FILE        (only the log check, step 4)
#        Tools/mac-smoke.sh --check-flow-log FILE   (THORN_FLOW ok=1, then the log check)
#        Tools/mac-smoke.sh --check-hud-alloc-log FILE (THORN_HUD_ALLOC with zero bytes, then the log check)
#        Tools/mac-smoke.sh --check-boss-log FILE   (THORN_BOSS spawned=1 attacks>0, then the log check without kills)
#        Tools/mac-smoke.sh --check-force-log FILE KEY[,KEY…] (THORN_FORCE with hits of every key, then the log check)
#        Tools/mac-smoke.sh --check-elite-log FILE   (the elite room's scepter was chosen, then the log check)
#        Tools/mac-smoke.sh --check-recipe-log FILE KEY (THORN_RECIPE key=KEY combined=1, then the log check)
#        Tools/mac-smoke.sh --check-test-profile-log FILE (THORN_TEST_PROFILE ok=1 unlocked=8 echelon=8, then the log check)
# --test-profile: run 1 with -thornProfile=test (log mac-smoke-test-profile.log, every echelon open, the run on 8),
#   run 2 with -thornProfile=user must quit with code 18 (mac-smoke-user-refused.log); user.save* of
#   ~/Library/Application Support/<CFBundleIdentifier>/Saves are compared by SHA-256 before and after.
# -thornBoss (design: .kiro/specs/thorn-boss-smoke-fix/design.md): the hero has no abilities, so kills are not
#   required; Imperia must spawn and attack (THORN_BOSS line), the rest of the log check is the same.
# --settings-restart (design: .kiro/specs/settings-screen/design.md §5): run 1 changes settings (-thornSettings),
#   run 2 expects them after the restart (-thornExpectSettings), run 3 restores the defaults of the test profile.
# --flow (design: .kiro/specs/screen-flow/design.md §4): run 1 walks the screens on a new test profile (-thornFlow,
#   log mac-smoke-flow-1.log), run 2 checks the saved result and unlock after the restart (-thornExpectFlow).
# --hud-alloc (design: .kiro/specs/hud-alloc-flaky-fix/design.md §4): builds the player without Development Build
#   (-thornRelease, Builds/MacRelease) and runs it with -thornHudAlloc (log mac-smoke-hud-alloc.log); the
#   THORN_HUD_ALLOC line must report 0 bytes of the HUD and of the canvas rebuild after warm-up, measured frames,
#   label changes and a calibration above zero. Release Mono players have no profiler counter: the bytes come from
#   the Mono heap (4 KB blocks). -thornHudAlloc as a player arg of the usual run checks the Development build, where
#   the exact "GC Allocated In Frame" counter is used.
# Exit codes: 0 ok, 2 build failed or player missing, 3 player timed out, 4 no THORN_MOBILE_SMOKE line,
#             5 kills=0, 6 unhandled exception in the log, 7 player exit code is not 0,
#             8 -thornInputScript=FILE given and no THORN_INPUT_SCRIPT ok=1 line (design: .kiro/specs/touch-controls-contract),
#             9 -thornSettings / -thornExpectSettings given and no THORN_SETTINGS_SET / THORN_SETTINGS ok=1 line,
#             10 -thornFlow / -thornExpectFlow given and no THORN_FLOW ok=1 line,
#             11 --hud-alloc and no THORN_HUD_ALLOC line, or it reports allocations or a measurement that saw nothing,
#             12 -thornBoss given and no THORN_BOSS line with spawned=1 and attacks above zero,
#             13 -thornHero=KEY given and the THORN_MOBILE_SMOKE line is not about hero=KEY (Roadmap Task 19),
#             14 -thornForce=KEY[,KEY…] given and the THORN_FORCE line misses a key, reports it refused or with 0 hits
#                (design: .kiro/specs/rot-counter-helix/design.md, Mechanics §28 Р11). With -thornForce the run lasts
#                30 s and longer until every key has hits, at most 65 game seconds (Mechanics §30 Р9).
#                KEY+scepter / KEY+shard gives the ability and its Aghanim's Scepter / Shard (Mechanics §34 Р16).
#             15 -thornElite given and no THORN_ELITE_SCEPTER ok=1 line or no THORN_AGHANIM source=Scepter line: the first
#                elite room's scepter was not dropped, collected or chosen (design: .kiro/specs/aghanim-upgrades/design.md).
#             16 -thornShowChoice=SOURCE given and no THORN_CHOICE_SHOT line: the choice of SOURCE (level, item, scepter,
#                shard) did not open or was not shot (the picture goes to -thornScreenshot, see Tools/mac-screenshot.sh).
#             17 -thornRecipe=KEY given and no THORN_RECIPE key=KEY line with combined=1: the components of recipe KEY were
#                brought to the maximum and a treasure given, but the «Комбинация предметов» card was not offered or the
#                recipe was not combined by the choice (design: .kiro/specs/recipes-item-ability-matrix, Mechanics §35).
#             18 --test-profile / -thornProfile=test (design: .kiro/specs/unlocks-records-test-profile, Mechanics §39):
#                no THORN_TEST_PROFILE ok=1 unlocked=8 echelon=8 line, the run did not use the test profile, the log's
#                saves folder is not the one checked, -thornProfile=user was not refused with code 18, or a user.save*
#                file of the player's saves folder appeared, changed or vanished.
# -thornHero=KEY (e.g. -thornHero=kez): the smoke run picks this hero on the hero screen instead of Dragonus.
set -euo pipefail
root="${0:A:h:h}"
unity="${THORN_UNITY:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
log="${THORN_SMOKE_LOG:-$root/Research/mac-smoke.log}"
timeout="${THORN_SMOKE_TIMEOUT:-180}"
app="$root/Builds/MacSmoke/ThornThrone.app"

# Step 4: checks a player log; prints the smoke line on success. A second argument "no-kills" skips the kills check
# (-thornBoss, where the hero has no abilities).
check_log()
{
  local file="$1" mode="${2:-}" line kills
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
  if [[ "$mode" == no-kills ]]; then print -r -- "$line"; return 0; fi
  kills="$(print -r -- "$line" | sed -nE 's/.* kills=([0-9]+).*/\1/p')"
  if [[ -z "$kills" || "$kills" == 0 ]]; then echo "mac-smoke: no kills: $line" >&2; return 5; fi
  print -r -- "$line"
}

# Screen flow smoke: the THORN_FLOW line must say ok=1 (code 10 otherwise).
check_flow()
{
  local file="$1" line
  line="$(LC_ALL=C grep -a '^THORN_FLOW ' "$file" 2> /dev/null | tail -n 1 || true)"
  if [[ "$line" != "THORN_FLOW ok=1 "* ]]; then
    echo "mac-smoke: screen flow failed: ${line:-no THORN_FLOW line}, see $file" >&2
    return 10
  fi
  print -r -- "$line"
}

# HUD allocation smoke: 0 bytes of the HUD and of the canvas, the measurement ran and its calibration saw bytes.
check_hud_alloc()
{
  local file="$1" line
  line="$(LC_ALL=C grep -a '^THORN_HUD_ALLOC ' "$file" 2> /dev/null | tail -n 1 || true)"
  if [[ -z "$line" ]]; then echo "mac-smoke: no THORN_HUD_ALLOC line in ${file}" >&2; return 11; fi
  local pair key value bytes="" canvas="" frames="" changes="" calibration="" method=""
  for pair in ${(z)line}; do
    key="${pair%%=*}"; value="${pair#*=}"
    case "$key" in
      bytes) bytes="$value" ;;
      canvas) canvas="$value" ;;
      frames) frames="$value" ;;
      changes) changes="$value" ;;
      calibration) calibration="$value" ;;
      method) method="$value" ;;
    esac
  done
  if [[ "$bytes" != 0 || "$canvas" != 0 || ! "$frames" =~ '^[1-9][0-9]*$' || ! "$changes" =~ '^[1-9][0-9]*$' \
        || ! "$calibration" =~ '^[1-9][0-9]*$' || -z "$method" || "$method" == none ]]; then
    echo "mac-smoke: HUD allocates or was not measured: $line" >&2
    return 11
  fi
  print -r -- "$line"
}

# Boss smoke: Imperia spawned and attacked at least once (code 12 otherwise); the fields are parsed, ok= is not trusted.
check_boss()
{
  local file="$1" line
  line="$(LC_ALL=C grep -a '^THORN_BOSS ' "$file" 2> /dev/null | tail -n 1 || true)"
  if [[ -z "$line" ]]; then echo "mac-smoke: no THORN_BOSS line in ${file}" >&2; return 12; fi
  local pair key value spawned="" attacks=""
  for pair in ${(z)line}; do
    key="${pair%%=*}"; value="${pair#*=}"
    case "$key" in
      spawned) spawned="$value" ;;
      attacks) attacks="$value" ;;
    esac
  done
  if [[ "$spawned" != 1 || ! "$attacks" =~ '^[1-9][0-9]*$' ]]; then
    echo "mac-smoke: Imperia did not spawn or attack: $line" >&2
    return 12
  fi
  print -r -- "$line"
}
# Forced upgrades (-thornForce=KEY[,KEY…]): every key is on the THORN_FORCE line as key=rank:hits with rank and
# hits above zero (code 14 otherwise).
check_force()
{
  local file="$1" keys="$2" line key pair found
  line="$(LC_ALL=C grep -a '^THORN_FORCE' "$file" 2> /dev/null | tail -n 1 || true)"
  if [[ -z "$line" ]]; then echo "mac-smoke: no THORN_FORCE line in ${file}" >&2; return 14; fi
  for key in ${(s:,:)keys}; do
    found=""
    for pair in ${(z)line}; do
      [[ "${pair%%=*}" == "$key" ]] && found="${pair#*=}"
    done
    if [[ ! "$found" =~ '^[1-9][0-9]*:[1-9][0-9]*$' ]]; then
      echo "mac-smoke: forced upgrade ${key} was refused or made no hits: $line" >&2
      return 14
    fi
  done
  print -r -- "$line"
}
# Elite smoke (-thornElite): the first room's scepter was chosen (code 15 otherwise).
check_elite_scepter()
{
  local file="$1" line choice
  line="$(LC_ALL=C grep -a '^THORN_ELITE_SCEPTER ' "$file" 2> /dev/null | tail -n 1 || true)"
  choice="$(LC_ALL=C grep -a '^THORN_AGHANIM source=Scepter ' "$file" 2> /dev/null | head -n 1 || true)"
  if [[ "$line" != "THORN_ELITE_SCEPTER ok=1 "* || -z "$choice" ]]; then
    echo "mac-smoke: no scepter choice from the elite room: ${line:-no THORN_ELITE_SCEPTER line}, see $file" >&2
    return 15
  fi
  print -r -- "$choice"
}
# Recipe smoke (-thornRecipe=KEY): the recipe was combined through its card (code 17 otherwise).
check_recipe()
{
  local file="$1" key="$2" line
  line="$(LC_ALL=C grep -a "^THORN_RECIPE key=${key} " "$file" 2> /dev/null | tail -n 1 || true)"
  if [[ "$line" != "THORN_RECIPE key=${key} offered=1 combined=1"* ]]; then
    echo "mac-smoke: recipe ${key} was not combined by its card: ${line:-no THORN_RECIPE line}, see $file" >&2
    return 17
  fi
  print -r -- "$line"
}
# Test profile smoke (-thornProfile=test): all 8 echelons open, the run on echelon 8 (code 18 otherwise).
check_test_profile()
{
  local file="$1" line
  line="$(LC_ALL=C grep -a '^THORN_TEST_PROFILE ' "$file" 2> /dev/null | tail -n 1 || true)"
  if [[ "$line" != "THORN_TEST_PROFILE ok=1 unlocked=8 echelon=8 "* ]]; then
    echo "mac-smoke: test profile check failed: ${line:-no THORN_TEST_PROFILE line}, see $file" >&2
    return 18
  fi
  if ! LC_ALL=C grep -aq '^THORN_PROFILE kind=test ' "$file"; then
    echo "mac-smoke: the run did not use the test profile, see $file" >&2
    return 18
  fi
  print -r -- "$line"
}
# SHA-256 of every user.save* file in a saves folder, one line per file (empty without files).
user_saves_digest()
{
  local dir="$1" f
  [[ -d "$dir" ]] || return 0
  for f in "$dir"/user.save*(N); do shasum -a 256 "$f"; done
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
if [[ "${1:-}" == "--check-flow-log" ]]; then
  check_flow "${2:?log file}" || exit $?
  check_log "$2"
  exit $?
fi
if [[ "${1:-}" == "--check-boss-log" ]]; then
  check_boss "${2:?log file}" || exit $?
  check_log "$2" no-kills
  exit $?
fi
if [[ "${1:-}" == "--check-force-log" ]]; then
  check_force "${2:?log file}" "${3:?keys}" || exit $?
  check_log "$2"
  exit $?
fi
if [[ "${1:-}" == "--check-elite-log" ]]; then
  check_elite_scepter "${2:?log file}" || exit $?
  check_log "$2"
  exit $?
fi
if [[ "${1:-}" == "--check-recipe-log" ]]; then
  check_recipe "${2:?log file}" "${3:?recipe key}" || exit $?
  check_log "$2"
  exit $?
fi
if [[ "${1:-}" == "--check-test-profile-log" ]]; then
  check_test_profile "${2:?log file}" || exit $?
  check_log "$2"
  exit $?
fi
if [[ "${1:-}" == "--check-hud-alloc-log" ]]; then
  check_hud_alloc "${2:?log file}" || exit $?
  check_log "$2"
  exit $?
fi

build=1
restart=0
flow=0
hud_alloc=0
test_profile=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build) build=0; shift ;;
    --test-profile) test_profile=1; shift ;;
    --settings-restart) restart=1; shift ;;
    --flow) flow=1; shift ;;
    --hud-alloc) hud_alloc=1; shift ;;
    *) break ;;
  esac
done
build_args=()
if (( hud_alloc )); then
  app="$root/Builds/MacRelease/ThornThrone.app"
  build_args=(-thornRelease)
fi

if (( build )); then
  editor_lock || exit 2
  buildlog="$root/Research/mac-build.log"
  code=0
  "$unity" -batchmode -nographics -quit -projectPath "$root/UnityProject" \
    -executeMethod ThornThrone.Editor.MobileBuilder.BuildMacSmoke "${build_args[@]}" -logFile "$buildlog" || code=$?
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
# Runs the player with -thornSmoke and waits for it; its exit code goes to player_rc. Returns 3 on a timeout.
player_rc=0
launch_player()
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
  player_rc=0
  wait $pid || player_rc=$?
}
run_once()
{
  local runlog="$1"; shift
  launch_player "$runlog" "$@" || return $?
  local rc=$player_rc
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
  if [[ " $* " == *" -thornFlow "* || " $* " == *" -thornExpectFlow "* ]]; then
    check_flow "$runlog" || return $?
  fi
  if [[ " $* " == *" -thornHudAlloc "* ]]; then
    check_hud_alloc "$runlog" || return $?
  fi
  if [[ " $* " == *" -thornBoss "* ]]; then
    check_boss "$runlog" || return $?
    check_log "$runlog" no-kills || return $?
  else
    check_log "$runlog" || return $?
  fi
  local arg
  for arg in "$@"; do
    if [[ "$arg" == -thornHero=* ]] && ! LC_ALL=C grep -aq "^THORN_MOBILE_SMOKE hero=${arg#-thornHero=} " "$runlog"; then
      echo "mac-smoke: the run was not played by ${arg#-thornHero=}, see $runlog" >&2
      return 13
    fi
    if [[ "$arg" == -thornForce=* ]]; then
      check_force "$runlog" "${arg#-thornForce=}" || return $?
    fi
    if [[ "$arg" == -thornElite ]]; then
      check_elite_scepter "$runlog" || return $?
    fi
    if [[ "$arg" == -thornProfile=test ]]; then
      check_test_profile "$runlog" || return $?
    fi
    if [[ "$arg" == -thornRecipe=* ]]; then
      check_recipe "$runlog" "${arg#-thornRecipe=}" || return $?
    fi
    if [[ "$arg" == -thornShowChoice=* ]] && ! LC_ALL=C grep -aq "^THORN_CHOICE_SHOT source=" "$runlog"; then
      echo "mac-smoke: the ${arg#-thornShowChoice=} choice was not shot, see $runlog" >&2
      return 16
    fi
  done
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
if (( flow )); then
  run_once "${log%.log}-flow-1.log" -thornFlow "$@" || exit $?
  run_once "$log" -thornExpectFlow "$@" || exit $?
  exit 0
fi
if (( hud_alloc )); then
  run_once "${log%.log}-hud-alloc.log" -thornHudAlloc "$@" || exit $?
  exit 0
fi
if (( test_profile )); then
  # Mechanics §39 З9: the player's saves folder (persistentDataPath/Saves of the macOS player) must not change.
  bundle="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Contents/Info.plist")"
  saves="$HOME/Library/Application Support/$bundle/Saves"
  before="$(user_saves_digest "$saves")"
  run_once "${log%.log}-test-profile.log" -thornProfile=test "$@" || exit $?
  if ! LC_ALL=C grep -aF "THORN_PROFILE kind=test " "${log%.log}-test-profile.log" | grep -aqF "root=$saves"; then
    echo "mac-smoke: the saves folder of the log is not $saves, see ${log%.log}-test-profile.log" >&2
    exit 18
  fi
  # A smoke run refuses the user profile with code 18 (Т4.4).
  refused="${log%.log}-user-refused.log"
  launch_player "$refused" -thornProfile=user "$@" || exit $?
  if (( player_rc != 18 )) || ! LC_ALL=C grep -aq '^THORN_TEST_PROFILE ok=0 failed=user_profile_in_smoke' "$refused" \
      || LC_ALL=C grep -aq '^THORN_PROFILE kind=user ' "$refused"; then
    echo "mac-smoke: -thornProfile=user was not refused (exit $player_rc), see $refused" >&2
    exit 18
  fi
  after="$(user_saves_digest "$saves")"
  if [[ "$before" != "$after" ]]; then
    echo "mac-smoke: user.save* changed in $saves" >&2
    exit 18
  fi
  echo "mac-smoke: user.save* unchanged in $saves ($(print -r -- "$before" | grep -c . || true) files)"
  exit 0
fi
run_once "$log" "$@" || exit $?
