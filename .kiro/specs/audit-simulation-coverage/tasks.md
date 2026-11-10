# Задачи: `audit.py` считает покрытие по новой симуляции (Task 5)

- [x] 1. Тесты до кода: `Tools/tests/test_audit.py` с проверками из `design.md` §5; убедиться, что они падают (нет `simulation_coverage`, `build_registry`). (Т1–Т4)
- [x] 2. `CsCondition` и `field_maps` из `GameData.cs`/`Definitions.cs`, `CoverageError`. (Т1.3, Т1.4)
- [x] 3. `simulation_coverage()`: `AbilityFactory`, `SupportedHeroAttributes`, `Attr.X`, `ReportUnsupportedEnemy`, движения, `Drop`, `SpawnPosition`, `ReportUnsupportedRules`; удалить `prototype_coverage()`. (Т1.1, Т1.2)
- [x] 4. `test_references()` по `DotNet/**/*.cs`. (Т2.2)
- [x] 5. `build_registry()` из `main()` без изменения логики ссылок, достижимости и ролей; поля `required`, `implementation`, `tests`; `prototype` из `PROTOTYPE_PARTIAL`; `schema` 2, `statusValues`, `summary`. (Т2, Т3, Т4.1, Т5.1)
- [x] 6. `main(argv)`: `--out`, `role_errors` → код 1 без записи файлов, `CoverageError` → код 2. (Т4.2, Т5.2)
- [x] 7. Тесты аудита зелёные; `python3 Tools/audit.py` проходит; проверить демо в `ContentRegistry.json`; повторный запуск без изменений.
- [x] 8. `Mechanics.md` §1, `Tools/README.md`. (design §6)
- [x] 9. `dotnet test DotNet/ThornThrone.Simulation.Tests` зелёный; отметка в `roadmap.md`; коммит.
