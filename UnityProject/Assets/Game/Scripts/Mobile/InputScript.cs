using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ThornThrone.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Smoke input scenario (-thornInputScript=FILE): plays touches through a virtual Input System Touchscreen, the
    /// same path as a real screen (EnhancedTouch → GameInput → TouchController → Run), and checks expectations
    /// (design: .kiro/specs/touch-controls-contract/design.md §3.11, Р13). Line format, times in run seconds,
    /// positions as shares of the safe area or <c>dash</c> for the dash button center:
    /// <code>
    /// 0.5 down 1 0.20 0.30 | move 1 dash | up 1 | cancel 1 | expect dashes 1 | expect moved true | expect joystick false
    /// </code>
    /// </summary>
    public sealed class InputScript
    {
        private struct Line
        {
            public int Number;
            public float Time;
            public string Command;
            public string[] Args;
        }

        private readonly List<Line> lines = new List<Line>();
        private readonly Touchscreen screen;
        private int next;
        private int dashes;
        private bool started;
        private Vec2 start;
        private float maxDistance;

        public bool Finished { get; private set; }
        public bool Ok { get; private set; } = true;
        public string Failure { get; private set; }

        public InputScript(string path)
        {
            int number = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                number++;
                string text = raw;
                int comment = text.IndexOf('#');
                if (comment >= 0) text = text.Substring(0, comment);
                var parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (parts.Length < 2) throw new FormatException($"{path}:{number}: '{raw}'");
                var args = new string[parts.Length - 2];
                Array.Copy(parts, 2, args, 0, args.Length);
                lines.Add(new Line { Number = number, Time = Float(parts[0]), Command = parts[1], Args = args });
            }
            screen = InputSystem.AddDevice<Touchscreen>("ThornInputScript");
        }

        /// <summary>Counts dash events of the last update; call before <see cref="Run.ClearEvents"/>.</summary>
        public void CountEvents(Run run)
        {
            for (int i = 0; i < run.EventCount; i++)
                if (run.Events[i].Kind == SimEventKind.HeroDashed) dashes++;
            if (!started) return;
            maxDistance = Mathf.Max(maxDistance, Mathf.Sqrt(Vec2.DistanceSquared(run.HeroPosition, start)));
        }

        /// <summary>Runs the lines whose time has come; logs THORN_INPUT_SCRIPT when the last one ran.</summary>
        public void Update(Run run, GameInput input)
        {
            if (Finished) return;
            if (!started)
            {
                started = true;
                start = run.HeroPosition;
            }
            while (next < lines.Count && lines[next].Time <= run.Time && Ok)
            {
                var line = lines[next++];
                try
                {
                    Execute(line, input);
                }
                catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException)
                {
                    Fail(line, e.Message);
                }
            }
            if (next < lines.Count && Ok) return;
            Finished = true;
            InputSystem.RemoveDevice(screen);
            Debug.Log($"THORN_INPUT_SCRIPT ok={(Ok ? 1 : 0)} dashes={dashes} moved={(maxDistance > MovedDistance ? "true" : "false")} " +
                      $"distance={maxDistance:0}" + (Ok ? "" : $" failed={Failure}"));
        }

        /// <summary>Hero distance from the scenario start that counts as moved (Source units).</summary>
        private const float MovedDistance = 50;

        private void Execute(Line line, GameInput input)
        {
            var a = line.Args;
            switch (line.Command)
            {
                case "down": Touch(int.Parse(a[0]), UnityTouchPhase.Began, Position(a, 1, input)); break;
                case "move": Touch(int.Parse(a[0]), UnityTouchPhase.Moved, Position(a, 1, input)); break;
                case "up": Touch(int.Parse(a[0]), UnityTouchPhase.Ended, Last(int.Parse(a[0]))); break;
                case "cancel": Touch(int.Parse(a[0]), UnityTouchPhase.Canceled, Last(int.Parse(a[0]))); break;
                case "expect": Expect(line, input); break;
                default: throw new FormatException($"unknown command '{line.Command}'");
            }
        }

        private void Expect(Line line, GameInput input)
        {
            string what = line.Args[0], value = line.Args[1];
            string actual;
            switch (what)
            {
                case "dashes": actual = dashes.ToString(CultureInfo.InvariantCulture); break;
                case "moved": actual = maxDistance > MovedDistance ? "true" : "false"; break;
                case "joystick": actual = input.Controller.JoystickActive ? "true" : "false"; break;
                default: throw new FormatException($"unknown expectation '{what}'");
            }
            if (actual != value) Fail(line, $"{what}={actual},expected={value}");
        }

        private void Fail(Line line, string message)
        {
            Ok = false;
            Failure = $"line{line.Number}:{message.Replace(' ', '_')}";
        }

        private readonly Dictionary<int, Vector2> positions = new Dictionary<int, Vector2>();

        private Vector2 Last(int id) => positions.TryGetValue(id, out var p) ? p : Vector2.zero;

        private static Vector2 Position(string[] a, int index, GameInput input)
        {
            if (a[index] == "dash") return input.Controller.Layout.DashButton.center;
            var safe = Screen.safeArea;
            return new Vector2(safe.xMin + Float(a[index]) * safe.width, safe.yMin + Float(a[index + 1]) * safe.height);
        }

        private void Touch(int id, UnityTouchPhase phase, Vector2 position)
        {
            positions[id] = position;
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, phase = phase, position = position });
        }

        private static float Float(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
