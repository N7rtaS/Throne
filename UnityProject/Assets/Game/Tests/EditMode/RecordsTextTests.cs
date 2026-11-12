using System.Collections.Generic;
using NUnit.Framework;
using ThornThrone.App;
using ThornThrone.Mobile;
using ThornThrone.Progression;
using TMPro;
using UnityEditor;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Texts of the local records (.kiro/specs/unlocks-records-test-profile, Т2.2, Т2.3, Mechanics §39 З4, З5): the
    /// main menu table «Рекорды / Счёт» and the line of an echelon card.
    /// </summary>
    public sealed class RecordsTextTests
    {
        private static RecordRow Row(string hero, int echelon, bool victory, long score) => new RecordRow
        {
            Result = new RunResult { Hero = hero, Difficulty = AppFlow.UnlockId(echelon), Victory = victory, Score = score },
            Echelon = echelon,
        };

        [Test]
        public void EmptyTableSaysNoResults()
        {
            Assert.That(RecordsText.Menu(null, GameUi.HeroTitle), Is.EqualTo("<b>Рекорды<pos=78%>Счёт</b>\nПока нет результатов"));
            Assert.That(RecordsText.Menu(new List<RecordRow>(), GameUi.HeroTitle), Does.EndWith("\nПока нет результатов"));
        }

        [Test]
        public void TableRowsShowPlaceHeroEchelonOutcomeAndScore()
        {
            var text = RecordsText.Menu(new[] { Row("kez", 8, true, 1339100), Row("dragonus", 1, false, 330000) }, GameUi.HeroTitle);
            Assert.That(text, Is.EqualTo("<b>Рекорды<pos=78%>Счёт</b>" +
                                         "\n1. Кез, эшелон 8, победа<pos=78%>1 339 100" +
                                         "\n2. Драгонус, эшелон 1, поражение<pos=78%>330 000"));
        }

        [Test]
        public void HeroTitlesAreRussianAndUnknownKeyStays()
        {
            Assert.That(GameUi.HeroTitle("dragonus"), Is.EqualTo("Драгонус"));
            Assert.That(GameUi.HeroTitle("shen"), Is.EqualTo("Шен"));
            Assert.That(GameUi.HeroTitle("kez"), Is.EqualTo("Кез"));
            Assert.That(GameUi.HeroTitle("nobody"), Is.EqualTo("nobody"));
        }

        [Test]
        public void CardShowsBestScoreAndVictoriesOnlyAfterARun()
        {
            Assert.That(RecordsText.Card(null), Is.Empty);
            Assert.That(RecordsText.Card(new HeroEchelonRecord { Hero = "dragonus", Echelon = 1 }), Is.Empty);
            Assert.That(RecordsText.Card(new HeroEchelonRecord { Runs = 2, Victories = 1, BestScore = 330000, HasVictory = true }),
                Is.EqualTo("Рекорд 330 000, побед 1"));
            Assert.That(RecordsText.Card(new HeroEchelonRecord { Runs = 1, BestScore = 40 }), Is.EqualTo("Рекорд 40, побед 0"));
        }

        [Test]
        public void FontCoversRecordsTexts()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Art/Fonts/PTSans-Regular SDF.asset");
            var texts = RecordsText.Menu(null, GameUi.HeroTitle) + RecordsText.Menu(new[] { Row("shen", 3, true, 1234567890) }, GameUi.HeroTitle)
                        + RecordsText.Menu(new[] { Row("kez", 2, false, 1) }, GameUi.HeroTitle)
                        + RecordsText.Card(new HeroEchelonRecord { Runs = 1, Victories = 1, BestScore = 9 });
            var missing = new System.Text.StringBuilder();
            bool tag = false;
            foreach (char c in texts)
            {
                if (c == '<') tag = true;
                if (!tag && c >= ' ' && !font.HasCharacter(c)) missing.Append($"U+{(int)c:X4} '{c}' ");
                if (c == '>') tag = false;
            }
            Assert.That(missing.ToString(), Is.Empty);
        }
    }
}
