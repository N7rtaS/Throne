# Задачи: реестр ресурсов (Task 7)

- [x] 1. Тесты `Tools/tests/test_asset_registry.py` на синтетике: CRC32, RERL-граф, корни героев и звуков, `.meta` (GUID, импортёр, сжатие), статусы и переопределения, справочные треугольники и `models`, `missingRequired`, детерминизм (Т1–Т6). Проверено, что падают до реализации.
- [x] 2. `Tools/asset-registry.py`: манифест и CRC32, файлы карты, RERL-граф, корни сущностей, `usedBy` (Т1, design §3).
- [x] 3. Выходы Unity: связывание по Т2.1, `.meta`, сжатие текстур, справочные треугольники glb, `orphanOutputs` (Т2).
- [x] 4. Статусы и `Tools/asset-registry-overrides.json` (пустой `{}`) (Т3, Р8–Р9).
- [x] 5. Раздел `models` и таблица Markdown без проверки бюджетов (Т4, Р3–Р4).
- [x] 6. `missingRequired`, `inputs` с SHA-256, детерминированная запись (Т5–Т6).
- [x] 7. Тесты на реальных данных; запуск на проекте → `Research/FullGame/AssetRegistry.json`; повторный запуск даёт тот же SHA-1 файла.
- [x] 8. Документы: `Tools/README.md` (шаг, коды выхода), ТЗ §2/§13 (реестр создан). Решения Р — в design.md §5, в `Mechanics.md`/`Differences.md` не нужны (не игровые правила, отличий от оригинала нет).
- [x] 9. `python3 -m unittest discover -s Tools/tests` (90 тестов, из них 28 новых) и `dotnet test DotNet/ThornThrone.Simulation.Tests` (77) зелёные; отметка в `roadmap.md`; коммит.

## Число недостающих сущностей
Отчёт даёт `missingRequired: 27 of 94`, а не «17» из roadmap: 16 пассивок (`passive-item`), 9 рецептов (`recipe`) и 2 врождённые (`skywrath_mage_innate`, `vengeful_spirit_innate`). Ни у одной из них в данных нет ресурсов (`resources` пуст, моделей и звуков нет). Число «17» в roadmap ничем не подтверждено: в `ContentRegistry.json` такого счётчика нет (`missingResources` пуст у всех записей), вероятно, оно записано по памяти или спутано с «17 из 26 пассивок» из той же дорожной карты. Строка в `roadmap.md` исправлена на фактическое число.
