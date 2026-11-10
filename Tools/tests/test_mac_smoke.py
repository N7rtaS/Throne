"""Tests of the log check in Tools/mac-smoke.sh (--check-log FILE).
Run: python3 -m unittest discover -s Tools/tests
Exit codes come from the design: .kiro/specs/build-readme-unity-tests-smoke/design.md §4.
"""
import os
import subprocess
import unittest

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPT = os.path.join(TOOLS, "mac-smoke.sh")
LOGS = os.path.join(TOOLS, "tests", "smoke-logs")


def check(name):
    return subprocess.run(["zsh", SCRIPT, "--check-log", os.path.join(LOGS, name)],
                          capture_output=True, text=True, timeout=30)


class MacSmokeLogCheckTests(unittest.TestCase):
    def test_successful_run_exits_zero_and_prints_smoke_line(self):
        result = check("ok.log")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("THORN_MOBILE_SMOKE time=30.0 kills=148", result.stdout)

    def test_missing_smoke_line_exits_four(self):
        self.assertEqual(check("missing.log").returncode, 4)

    def test_zero_kills_exits_five(self):
        self.assertEqual(check("no-kills.log").returncode, 5)

    def test_unhandled_exception_exits_six(self):
        result = check("exception.log")
        self.assertEqual(result.returncode, 6)
        self.assertIn("NullReferenceException", result.stderr)

    def test_absent_log_file_exits_four(self):
        self.assertEqual(check("absent.log").returncode, 4)


if __name__ == "__main__":
    unittest.main()
