using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ThornThrone.Mobile;
using ThornThrone.Simulation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// TextMeshPro HUD: PT Sans SDF covers every UI string, GameUi has no legacy Text, the HUD writes text only on
    /// change and does not allocate after warm-up (design: .kiro/specs/tmp-cyrillic-hud/design.md §4).
    /// </summary>
    public sealed class HudTextTests
    {
        private const string FontPath = "Assets/Game/Art/Fonts/PTSans-Regular SDF.asset";
        private const string SourceData = "Assets/Game/Data/Source";
        private const string Localization = "Assets/Game/Data/Localization/powerups_ru.json";
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o) Object.DestroyImmediate(o);
            created.Clear();
            var ui = GameObject.Find("UI");
            if (ui) Object.DestroyImmediate(ui);
            if (EventSystem.current) Object.DestroyImmediate(EventSystem.current.gameObject);
        }

        private static TMP_FontAsset Font()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Assert.That(font, Is.Not.Null, FontPath);
            return font;
        }

        private TMP_Text NewLabel()
        {
            var go = new GameObject("Label", typeof(RectTransform));
            created.Add(go);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = Font();
            return label;
        }

        private static GameData LoadSourceData()
        {
            return GameData.Load(
                category => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_{category}.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena_entities.json").text,
                () => AssetDatabase.LoadAssetAtPath<TextAsset>($"{SourceData}/survivors_arena.gnv.bytes").bytes);
        }

        [Test]
        public void HudTextSkipsUnchangedValue()
        {
            var label = NewLabel();
            var hud = new HudText(label, "Убито {0}");
            Assert.That(hud.Set(5), Is.True);
            Assert.That(label.text, Is.EqualTo("Убито 5"));
            label.text = "x";
            Assert.That(hud.Set(5), Is.False);
            Assert.That(label.text, Is.EqualTo("x"));
            Assert.That(hud.Set(6), Is.True);
            Assert.That(label.text, Is.EqualTo("Убито 6"));
            hud.Reset();
            Assert.That(hud.Set(6), Is.True);
        }

        [Test]
        public void HudTimeIsZeroPadded()
        {
            var label = NewLabel();
            var hud = new HudText(label, "{0:00}:{1:00}");
            hud.Set(1, 5);
            Assert.That(label.text, Is.EqualTo("01:05"));
            hud.Set(12, 0);
            Assert.That(label.text, Is.EqualTo("12:00"));
            Assert.That(hud.Set(12, 0), Is.False);
        }

        [Test]
        public void FontCoversUiText()
        {
            var font = Font();
            Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            var required = new SortedSet<char>();
            for (char c = ' '; c <= '~'; c++) required.Add(c);
            for (char c = 'А'; c <= 'я'; c++) required.Add(c);
            required.Add('Ё');
            required.Add('ё');
            var json = JObject.Parse(File.ReadAllText(Localization));
            foreach (var p in json.Properties()) AddText(required, (string)p.Value);
            // String literals of the presentation code (comments stripped first).
            var literal = new Regex("\"(?:[^\"\\\\\\n]|\\\\.)*\"");
            foreach (var path in Directory.GetFiles("Assets/Game/Scripts/Mobile", "*.cs"))
            foreach (var line in File.ReadAllLines(path))
            {
                string code = line.TrimStart().StartsWith("//") ? "" : line;
                foreach (Match m in literal.Matches(code)) AddText(required, m.Value);
            }
            var missing = new StringBuilder();
            foreach (char c in required)
                if (!font.HasCharacter(c)) missing.Append($"U+{(int)c:X4} '{c}' ");
            Assert.That(missing.ToString(), Is.Empty);
        }

        private static void AddText(SortedSet<char> set, string text)
        {
            foreach (char c in text) if (c >= ' ') set.Add(c);
        }

        [Test]
        public void GameUiUsesOnlyTmpText()
        {
            var font = Font();
            new GameUi(null, font);
            var root = GameObject.Find("UI");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<UnityEngine.UI.Text>(true), Is.Empty);
            var labels = root.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(labels.Length, Is.GreaterThan(10));
            foreach (var label in labels) Assert.That(label.font, Is.SameAs(font), label.name);
        }

        [Test]
        public void GameUiLabelsAreNotTruncated()
        {
            var ui = new GameUi(null, Font());
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            ui.ShowRun();
            ui.UpdateHud(run, new GameInput());
            var root = GameObject.Find("UI");
            int checkedLabels = 0;
            var problems = new StringBuilder();
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                label.ForceMeshUpdate(true, true);
                // TMP hides a whole line that does not fit the rect height (PT Sans line ≈ 1.29 em).
                if (label.isTextTruncated)
                    problems.Append($"{label.transform.parent.name}/{label.name} '{label.text}' needs {label.preferredHeight:0} of {label.rectTransform.rect.height:0}; ");
                checkedLabels++;
            }
            Assert.That(problems.ToString(), Is.Empty);
            Assert.That(checkedLabels, Is.GreaterThan(10));
        }

        [Test]
        public void HudUpdateDoesNotAllocate()
        {
            var ui = new GameUi(null, Font());
            var input = new GameInput();
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            // The HUD is under test, not survival: the idle hero must outlive the boss phase.
            run.DebugInvulnerable = true;
            ui.ShowRun();
            var labels = new TMP_Text[4];
            string[] names = { "Level", "Time", "Kills", "Boss" };
            for (int i = 0; i < names.Length; i++)
                labels[i] = GameObject.Find("UI/Safe area/HUD").transform.Find(names[i]).GetComponent<TMP_Text>();
            var last = new string[labels.Length];
            var changes = new int[labels.Length];
            System.Action hud = () => ui.UpdateHud(run, input);
            // The TMP mesh rebuild after SetText happens on the canvas update; it is measured separately.
            System.Action canvas = Canvas.ForceUpdateCanvases;

            Frames(60, false);
            Frames(300, true);
            Assert.That(changes[1], Is.GreaterThanOrEqualTo(9), "time text changes in 10 s without the boss");
            Assert.That(changes[2], Is.GreaterThan(0), "kills text changes without the boss");

            run.SpawnBossNow();
            Assert.That(run.BossSpawned, Is.True);
            Frames(30, false);
            Assert.That(labels[3].gameObject.activeSelf, Is.True, "boss label is shown");
            System.Array.Clear(changes, 0, changes.Length);
            Frames(300, true);
            Assert.That(changes[1], Is.GreaterThanOrEqualTo(9), "time text changes in 10 s with the boss");
            Assert.That(changes[3], Is.GreaterThan(0), "boss HP text changes");

            void Frames(int count, bool measure)
            {
                for (int i = 0; i < count; i++)
                {
                    if (run.Choosing) run.Choose(0);
                    run.Update(run.Rules.Step, Vec2.Zero, false);
                    run.ClearEvents();
                    Assert.That(run.Finished, Is.False, "run must last through the measurement");
                    for (int l = 0; l < labels.Length; l++) last[l] = labels[l].text;
                    int allocations = measure ? Allocations(hud) : 0;
                    int rebuild = measure ? Allocations(canvas) : 0;
                    if (!measure)
                    {
                        hud();
                        canvas();
                    }
                    int changed = 0;
                    string which = "";
                    for (int l = 0; l < labels.Length; l++)
                    {
                        if (labels[l].text == last[l]) continue;
                        changed++;
                        changes[l]++;
                        which += $" '{last[l]}' -> '{labels[l].text}'";
                    }
                    // Editor only: TMP_Text.SetText(string, float…) mirrors the text into a string for the
                    // inspector under #if UNITY_EDITOR (char[] + string, 2 GC.Alloc samples per call). Players do
                    // not have this copy, so everything above it is an allocation of the HUD and must be zero.
                    string context = $"frame {i}, time {run.Time:0.00}, hud {allocations}, canvas {rebuild}, changed labels:{which}";
                    Assert.That(allocations, Is.EqualTo(measure ? EditorSetTextAllocations * changed : 0), context);
                    Assert.That(rebuild, Is.Zero, context);
                }
            }
        }

        /// <summary>GC.Alloc samples of the editor-only string mirror in one TMP_Text.SetText(format, …) call.</summary>
        private const int EditorSetTextAllocations = 2;

        /// <summary>GC.Alloc samples on this thread during <paramref name="action"/> (as Is.Not.AllocatingGCMemory).</summary>
        private static int Allocations(System.Action action)
        {
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            return recorder.sampleBlockCount;
        }

        [Test]
        public void HudUpdateWithoutChangesDoesNotAllocate()
        {
            var ui = new GameUi(null, Font());
            var input = new GameInput();
            var run = new Run(LoadSourceData(), new Rules(), "dragonus", null, 7);
            ui.ShowRun();
            ui.UpdateHud(run, input);
            Canvas.ForceUpdateCanvases();
            // Same run state: no label changes, so not even the editor-only TMP string mirror runs.
            Assert.That(() =>
            {
                ui.UpdateHud(run, input);
                Canvas.ForceUpdateCanvases();
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
