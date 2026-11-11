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


def check_flow(name):
    return subprocess.run(["zsh", SCRIPT, "--check-flow-log", os.path.join(LOGS, name)],
                          capture_output=True, text=True, timeout=30)


def check_hud_alloc(name):
    return subprocess.run(["zsh", SCRIPT, "--check-hud-alloc-log", os.path.join(LOGS, name)],
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


class MacSmokeFlowCheckTests(unittest.TestCase):
    """--flow (design: .kiro/specs/screen-flow/design.md §4): code 10 without THORN_FLOW ok=1."""
    def test_successful_flow_exits_zero_and_prints_both_lines(self):
        result = check_flow("flow-ok.log")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("THORN_FLOW ok=1 screens=Menu>Hero>Difficulty>Run>Results>Run>Menu>Hero>Difficulty", result.stdout)
        self.assertIn("THORN_MOBILE_SMOKE time=30.0 kills=131", result.stdout)
    def test_failed_flow_exits_ten_and_names_the_step(self):
        result = check_flow("flow-failed.log")
        self.assertEqual(result.returncode, 10)
        self.assertIn("failed=locked_echelon_2", result.stderr)
    def test_log_without_flow_line_exits_ten(self):
        self.assertEqual(check_flow("ok.log").returncode, 10)
    def test_absent_log_exits_ten(self):
        self.assertEqual(check_flow("absent.log").returncode, 10)


class MacSmokeHudAllocCheckTests(unittest.TestCase):
    """--hud-alloc (design: .kiro/specs/hud-alloc-flaky-fix/design.md §4): code 11 unless 0 bytes were measured."""
    def test_zero_bytes_exits_zero_and_prints_both_lines(self):
        result = check_hud_alloc("hud-alloc-ok.log")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("THORN_HUD_ALLOC bytes=0 canvas=0 frames=1198 changes=41 method=mono_heap", result.stdout)
        self.assertIn("THORN_MOBILE_SMOKE time=30.0 kills=29", result.stdout)

    def test_hud_bytes_exit_eleven(self):
        result = check_hud_alloc("hud-alloc-bytes.log")
        self.assertEqual(result.returncode, 11)
        self.assertIn("bytes=4096", result.stderr)

    def test_canvas_bytes_exit_eleven(self):
        self.assertEqual(check_hud_alloc("hud-alloc-canvas.log").returncode, 11)

    def test_measurement_that_saw_nothing_exits_eleven(self):
        self.assertEqual(check_hud_alloc("hud-alloc-unmeasured.log").returncode, 11)

    def test_no_measured_frames_exits_eleven(self):
        self.assertEqual(check_hud_alloc("hud-alloc-no-frames.log").returncode, 11)

    def test_log_without_hud_alloc_line_exits_eleven(self):
        self.assertEqual(check_hud_alloc("ok.log").returncode, 11)

    def test_absent_log_exits_eleven(self):
        self.assertEqual(check_hud_alloc("absent.log").returncode, 11)
def check_boss(name):
    return subprocess.run(["zsh", SCRIPT, "--check-boss-log", os.path.join(LOGS, name)],
                          capture_output=True, text=True, timeout=30)
class MacSmokeBossCheckTests(unittest.TestCase):
    """-thornBoss (design: .kiro/specs/thorn-boss-smoke-fix/design.md): Imperia spawned and attacked, kills not required."""
    def test_boss_spawned_and_attacked_exits_zero_without_kills(self):
        result = check_boss("boss-ok.log")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("THORN_BOSS ok=1 spawned=1 attacks=6", result.stdout)
        self.assertIn("THORN_MOBILE_SMOKE time=9.2 kills=0", result.stdout)
    def test_boss_without_attacks_exits_twelve(self):
        result = check_boss("boss-no-attacks.log")
        self.assertEqual(result.returncode, 12)
        self.assertIn("attacks=0", result.stderr)
    def test_boss_not_spawned_exits_twelve_even_with_ok(self):
        self.assertEqual(check_boss("boss-not-spawned.log").returncode, 12)
    def test_log_without_boss_line_exits_twelve(self):
        self.assertEqual(check_boss("boss-no-line.log").returncode, 12)
    def test_boss_log_with_exception_exits_six(self):
        result = check_boss("boss-exception.log")
        self.assertEqual(result.returncode, 6)
        self.assertIn("NullReferenceException", result.stderr)
    def test_boss_log_without_smoke_line_exits_four(self):
        self.assertEqual(check_boss("boss-no-smoke-line.log").returncode, 4)
    def test_absent_boss_log_exits_twelve(self):
        self.assertEqual(check_boss("absent.log").returncode, 12)
    def test_usual_check_still_requires_kills_on_boss_log(self):
        self.assertEqual(check("boss-ok.log").returncode, 5)
if __name__ == "__main__":
    unittest.main()
