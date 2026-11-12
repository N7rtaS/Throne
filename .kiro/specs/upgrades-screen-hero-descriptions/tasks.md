# Task 33. Экран «Улучшения» и описание героев — задачи

Решения В1–В5 утверждены владельцем 12.11.2026 (все рекомендации).

- [x] 1. dotnet-тесты `UpgradesScreenTests` на `MetaProgression.Rows` (порядок, уровень 0, значение уровня, максимум, недостача) — сначала красные (У2.2–У2.7)
- [x] 2. `MetaUpgradeRow`, `MetaProgression.Rows`, `AppFlow.UpgradeRows`/`UpgradesAvailable` (У2)
- [x] 3. dotnet-тесты переходов и покупки на экране: `AppScreen.Upgrades`, `OpenUpgrades`, `Back`, `BuyUpgrade` на Upgrades, бонус в следующем забеге (У1.1, У1.2, У1.5, У3) → реализация в `AppFlow`
- [x] 4. dotnet-тесты `HeroInfo.Of` (три героя по данным, ошибка при неизвестном ID) → `HeroInfo.cs`, `AppFlow.HeroInfo` (Г1.2–Г1.4)
- [x] 5. EditMode `UpgradesTextTests`, `HeroTextTests` → `UpgradesText.cs`, `HeroText.cs` (У2.1, У2.4–У2.6, Г1.1, Г1.2)
- [x] 6. `GameUi`: панель «Улучшения», карточки, кнопка и строка «Доступны улучшения!» на экране сложности, карточки героев с описанием, «Улучшения и сложность» на итоге (У1.3, У1.4, У2, У3.3, Г1)
- [x] 7. `FlowScreens`, `BackScreen.Upgrades`/`BackAction.ToDifficulty`, `MobileGame` (Back) (У1.2, У3.1)
- [x] 8. EditMode `ScreenFlowTests`: экран, покупка, нехватка, «МАКС.», Back, атлас (критерии приёмки)
- [x] 9. Smoke `-thornFlow`: шаги `upgrades`, `upgrades_back`; `-thornFlowShot=upgrades|hero`
- [x] 10. `Mechanics.md` §40, правки §23 Ф6, Ф8, §38 М6; `Differences.md` Б11, Б24 (Д1)
- [x] 11. Полный `dotnet test`, `Tools/unity-tests.sh`, smoke со скриншотами экранов героя и улучшений в `Research/Скриншоты/`
- [x] 12. Отметка в `roadmap.md`, коммит
