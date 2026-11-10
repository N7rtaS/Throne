# Задачи: порядок экранов (Task 16)
- [x] 1. Сборка `ThornThrone.App`: `Scripts/App/ThornThrone.App.asmdef`, `DotNet/ThornThrone.App/ThornThrone.App.csproj`, ссылка из тестового проекта, `ThornThrone.Mobile` и `ThornThrone.Mobile.Tests` (треб. 1, 2).
- [x] 2. Тесты `AppFlowTests` и `DebugWinFinishesRunWithVictory` до кода; убедиться, что падают (треб. 1–14, 17).
- [x] 3. `Run.DebugWin()` (треб. 17).
- [x] 4. `AppFlow`: экраны, переходы, создание `Run` на `echelon_N`, эшелоны из профиля (Ф1, Ф2), `Record` через журнал (Ф3, Ф4), `Abandon` (Ф5), `PlayAgain`, `RetryFailedSave` (Ф11), предвыбор эшелона (Ф6). `dotnet test` зелёный (треб. 1–14).
- [x] 5. `difficulty_ru.json`: генерация в `Tools/import-unity-data.sh`, тест Python, `FontBaker.Characters` читает файл, `MobileBuilder` назначает его `MobileGame` (Ф12, треб. 8).
- [x] 6. EditMode `ScreenFlowTests` до кода (треб. 8, 13, 15).
- [x] 7. `GameUi`: меню (Ф9), экран героя, экран сложности с замком (Ф6), результат (Ф8), строка ошибки записи (Ф11); `BackNavigation` (Ф10); `FlowScreens` (треб. 1, 8, 13, 15).
- [x] 8. `MobileGame`: убрать `StartRun`, `run = flow.Run`, `Finish`/`Abandon`, `Suspend` → `flow.RetryFailedSave`, обычный smoke через автомат (треб. 2, 11, 12, 16, 18).
- [x] 9. Smoke `-thornFlow`, `-thornExpectFlow` (Ф13), `Tools/mac-smoke.sh --flow` (код 10), `test_mac_smoke.py`, `Tools/README.md` (треб. 19, 20).
- [x] 10. `Mechanics.md` §23 (Ф1–Ф13 с метками, факты оригинала §1 дизайна), `Differences.md` Б11, обновить §8 ссылкой на §23.
- [x] 11. Проверки: `dotnet test`, `python3 -m unittest discover -s Tools/tests`, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh`, `Tools/mac-smoke.sh --flow`, `-thornBoss`, `-thornElite`, скриншоты экранов героя, сложности и результата.
- [x] 12. `roadmap.md` ✅, коммит `Roadmap: Task 16`.

## Итог
- `dotnet test`: 143 (18 новых: 17 `AppFlowTests`, `DebugWinFinishesRunWithVictory`). EditMode: 101 (12 новых `ScreenFlowTests`). Python: 104 (9 новых).
- `Tools/mac-smoke.sh`: `kills=29`; `--flow`: `THORN_FLOW ok=1 screens=Menu>Hero>Difficulty>Run>Results>Run>Menu>Hero>Difficulty results=1 unlocked=2`, после перезапуска `THORN_FLOW ok=1 results=1 unlocked=2`. `-thornElite` — код 0. `-thornBoss` — код 5 (`kills=0`): smoke босса снимает способности героя, так было и до задачи (`tmp-cyrillic-hud/design.md`: «Убито 0»).
- Скриншоты: `Research/Скриншоты/task16-hero.png`, `task16-difficulty.png`, `task16-results.png`.
