#!/bin/zsh
# Runs the Unity EditMode tests in batchmode (design: .kiro/specs/build-readme-unity-tests-smoke/design.md §5).
# Result: Research/unity-tests.xml (NUnit 3 XML), log: Research/unity-tests.log.
# Exit codes: 0 all passed, 1 a test failed or Unity exit code is not 0, 2 no result file (or Editor has the
#             project open), 3 no tests ran.
set -euo pipefail
root="${0:A:h:h}"
unity="${THORN_UNITY:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
xml="$root/Research/unity-tests.xml"
log="$root/Research/unity-tests.log"

if [[ -f "$root/UnityProject/Temp/UnityLockfile" ]] && pgrep -if "MacOS/Unity .*projectpath[ =]*$root/UnityProject" > /dev/null; then
  echo "unity-tests: Unity Editor has UnityProject open; close it before running batchmode" >&2
  exit 2
fi

rm -f "$xml"
code=0
# No -quit: with -runTests Unity exits by itself after the run.
"$unity" -batchmode -nographics -projectPath "$root/UnityProject" -runTests -testPlatform EditMode \
  -testResults "$xml" -logFile "$log" || code=$?

if [[ ! -f "$xml" ]]; then
  echo "unity-tests: no result file (Unity exit $code), see $log" >&2
  LC_ALL=C grep -aE 'error CS|Exception' "$log" | head -n 15 >&2 || true
  exit 2
fi

python3 - "$xml" "$code" <<'EOF'
import sys
import xml.etree.ElementTree as ET
run = ET.parse(sys.argv[1]).getroot()
total, passed, failed = (int(run.get(k, 0)) for k in ("total", "passed", "failed"))
code = int(sys.argv[2])
print(f"Unity EditMode: total={total} passed={passed} failed={failed} result={run.get('result')}")
if total == 0:
    print("unity-tests: no tests ran", file=sys.stderr)
    sys.exit(3)
if failed > 0 or code != 0:
    for case in run.iter("test-case"):
        if case.get("result") == "Failed":
            print(f"FAILED {case.get('fullname')}", file=sys.stderr)
    print(f"unity-tests: Unity exit {code}", file=sys.stderr)
    sys.exit(1)
print(f"Unity EditMode: {passed} passed")
EOF
