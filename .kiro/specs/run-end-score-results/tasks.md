# Task 30. Задачи
Решения В1–В5 (`design.md` §0) утверждены владельцем 12.11.2026.
- [x] 1. Тесты до кода: фикстура `RunEndTests` (design §7), правка `AppFlowTests` (`Score == run.Score`, `OutcomeCarriesSummary`). Запустить, убедиться, что падают по нужной причине: нет API или неверный исход (Т1–Т4, Т7.1).
- [x] 2. Фиксация: `if (Finished) return;` после способностей, снарядов, наземных объектов и спутников в `Tick`; проверка `Finished` в `Collect`, `OpenChoices`, `ForceUpgrade` (Т1.1–1.3, design §1 Р1).
- [x] 3. Отложенное поражение `fallen` в `Run.Lives.cs`: остаток шага смерти, `Finish(false)` в конце `Tick` (Т2, В2, design §2 Р3).
- [x] 4. Счёт: `Rules.ScorePerKill = 20`, `Run.Score` в `Kill` (Т3, В1, design §3 Р4).
- [x] 5. Время владения: `OwnedPowerUp.AcquiredAt/ReleasedAt`, `Build.History`, `Run.StampHistory` (Т4.1, design §4).
- [x] 6. `RunSummary`, `PowerUpResult`, `Run.Summary` (лениво, вне шага) (Т1.4–1.5, Т4.2–4.5, design §1 Р2, §4).
- [x] 7. `AppFlow.Record`: `Score = Run.Score`, `RunOutcome.Summary` (Т3.4, Т6.1, Т6.3).
- [x] 8. Unity: `ResultsText` (чистые функции) и EditMode-тесты `ResultsTextTests`; `GameUi.ShowResults` и раскладка итогов; EditMode-проверка раскладки на 1280×720 при 130 % (Т5, Т7.2). До 11 строк в таблице плюс заголовок, остаток — «и ещё N».
- [x] 9. `Mechanics.md` §37, правки §23 Ф4, §24, §25 Р8; `Differences.md` Б11, Б21 (design §6).
- [x] 10. Прогоны: полный `dotnet test DotNet/ThornThrone.Simulation.Tests`, `Tools/unity-tests.sh` (Т6.4).
- [x] 11. `roadmap.md` — отметка Task 30; коммит.
