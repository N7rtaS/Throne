# Дизайн: `audit.py` считает покрытие по новой симуляции (Task 5)

## 1. Сейчас
`Tools/audit.py::prototype_coverage()` открывает `Assets/Game/Scripts/Gameplay/PrototypeGame.cs` и `DragonusBuild.cs`. Этих файлов в проекте нет (они в `Удалить/`), аудит падает с `FileNotFoundError`. Поле `prototype` в реестре описывает прототип, а не текущую симуляцию; поля реализации и обязательности (ТЗ §3) нет.

## 2. Архитектура
Всё в `Tools/audit.py`, стандартная библиотека Python 3.

| Часть | Что делает |
|---|---|
| `simulation_coverage(root=UNITY)` | Читает исходники симуляции, возвращает `Coverage` (namedtuple): `ability_classes`, `hero_attributes`, `used_attributes`, `enemy_handled` (условие), `movements`, `pickup_conditions`, `spawn_behaviors`, `difficulty_unsupported` (условия), `field_maps`. При ненайденной конструкции — `CoverageError`. |
| `CsCondition` | Переводит булево выражение C# из симуляции в проверку над записью JSON: `&&`, `\|\|`, `!`, `==`, `!=`, `>`, `<`, скобки, литералы, `x.Prop`, `.EndsWith/StartsWith("…")`. Свойство → поле JSON по `field_maps` (из `GameData.cs`: `Prop = F/I/B/S/NF/NI(v, "m_…"[, умолчание])`, `Key` → ключ, `Class` → `_class`); вычисляемое свойство (`public bool IsBoss => Class.EndsWith("ImperiaBoss");` в `Definitions.cs`) раскрывается рекурсивно. Неизвестное свойство → `CoverageError`. Вычисление — свой маленький разборщик (токены → дерево), без `eval`. |
| `build_registry(types, defs, loc, native, coverage, tests)` | Текущий код `main()` от создания записей до ролей (перенос без изменения логики) плюс `required`, `implementation`, `tests`, `prototype`. Нужна для тестов без записи файлов. |
| `test_references(root=PROJECT/DotNet)` | Строковые литералы `"…"` и `Attr.X` во всех `*.cs` вне `bin/`, `obj/` → `{литерал: ["Файл.cs:Метод", …]}`; метод — ближайший выше `public void Имя(`. |
| `role_errors(registry)` | Список обязательных записей без роли. |
| `main(argv=None)` | Как раньше, плюс `--out DIR` (по умолчанию `Research/FullGame`, нужен тестам). Если `role_errors` не пуст — stderr, код 1, файлы не пишутся. `CoverageError` — stderr, код 2. |

`schema` реестра — 2 (добавлены поля); в `statusValues` добавляется `implementation`.

## 3. Правила уровня `implementation`
Все правила — **Р** (проектное решение аудита: как оценивать покрытие; к механикам оригинала не относятся). Базовый уровень считается по коду, затем `verified` (§3.8).

### 3.1 Powerups (по роли)
- `active-ability`: `_class` есть в `AbilityFactory` → `implemented`, иначе `none`. Это то же правило, что `Run.IsImplemented`.
- `passive-item`: у всех `m_vecMinorUpgradeChoices` все `m_vecGlobalUpgradeAttributes` в `SupportedHeroAttributes` и выборов > 0 → `implemented`, иначе `none` (предмет не предлагается совсем, как в `Run.IsImplemented`).
- `recipe`: правило пассивки и все компоненты `m_vecRecipeItems` не ниже `implemented` (иначе рецепт не собрать) → `implemented`, иначе `none`.
- `innate` со своим классом (`_class` ≠ `CSurvivorsPowerUpDefinition`): как `active-ability` (`kez_innate` → `none`).
- `innate` без класса: атрибуты `m_vecBaseAttributes` и первого малого улучшения: все поддержаны → `implemented`, часть → `partial`, ни одного → `none`.
- `service:*` (`gold`, `pile_of_coal`): `none` — симуляция их не выдаёт (запасная награда — лечение `Rules.FallbackHeal`).

### 3.2 Враги
- Движение (`m_eMovementBehavior`) обработано, если его значение `MovementBehavior.X` (через `ParseMovement` в `GameData.cs`) используется в `Run*.cs`; иначе `none`.
- Условие `handled` из `ReportUnsupportedEnemy` истинно → `implemented`.
- Ложно (`boss_tower` — `_Pillar`, `snotty` — `_Snotty`): `partial`, если врага порождает спавнер или разделение (`referencedBy` содержит `spawners/*` или `enemies/*`), иначе `none` (`snotty` вызывается только способностью `snotty_bat`, которой нет).

### 3.3 Спавнеры
- Поведение `m_eSpawnBehavior` обработано, если есть `case SpawnBehavior.X:` в `SpawnPosition`; для `STATIC_IN_MAP*` дополнительно нужна точка `m_sSpawnInfoTargetName` в `survivors_arena_entities.json` (иначе `Run` пропускает спавнер).
- Уровень = min(поведение: `implemented`/`none`; уровень врага `m_sEnemyName`, `verified` считается как `implemented`).

### 3.4 Подборы
- Ветви `if/else if` в `Run.Drop`, тело которых не содержит `Diagnostics.Report`, — реализованные условия. `pickup == experienceDef` раскрывается в `Key == "experience"` по `TryGetValue("experience", out experienceDef)`.
- Условие истинно → `implemented`, иначе `none`. Ожидаемо: experience, cheese, cookie, gold_coin, gold_bag, treasure, treasure_no_directional_arrow, magnet, nothing — `implemented`; shard, scepter — `none`.

### 3.5 Атрибуты
- В `SupportedHeroAttributes` или читается в симуляции как `Attr.X` (вне объявлений в `Attributes.cs`) → `implemented`, иначе `none`.
- Роль `meta-progression` — не выше `partial`: постоянной прокачки ещё нет (блок E).

### 3.6 Сложности
- Есть истинное условие из `ReportUnsupportedRules` (`EnableMeteorModifier`, `EnemyTouchMovementSlowDuration > 0`) или дополнительный спавнер ниже `implemented` → `partial`, иначе `implemented`.

### 3.7 Составные: герой, уровень, режим
- Герой: min по `m_vecStartingPowerUps` и `m_vecInnatePowerUps`; если min = `none`, то `partial` (сам забег героем запускается). Ожидаемо: dragonus — `implemented`, shen и kez — `partial` (врождённые не работают).
- Уровень и режим: все ссылки (`references`) не ниже `implemented` → `implemented`, иначе `partial`.

### 3.8 `verified`
`implemented` → `verified`, если ключ записи встречается в тестах строковым литералом (`"magic_missile"`), для атрибутов — также `Attr.<Имя>`. Поле `tests` заполняется у всех записей, где ключ найден; у `none` и `partial` оно только показывает ссылки и уровень не повышает. Атрибуты `[TestCase(...)]` относятся к методу под ними. Это означает «есть автоматическая проверка», не «подтверждено наблюдением» (ТЗ §3); метка достоверности правил остаётся в `Mechanics.md`.

## 4. Решения, которые нужно утвердить

**Р1. Конфликт демо и правила `verified`.** По правилу задачи («verified — есть ссылка в `DotNet/**`») `magic_missile` (`MagicMissileTests`) и `absorber` (`AbsorberGrowsByEatingHorde`) получат `verified`, а демо в roadmap ждёт `implemented`. Предлагаю правило задачи; демо читать как «не ниже `implemented`». Альтернатива — `verified` только по явному списку тестовых сценариев (`testScenario`), который ведётся вручную; тогда демо выполняется буквально, но `verified` ни у кого не будет, пока список не заполнен.

**Р2. Обязательность.** `required = true` у всех записей девяти категорий: объём фазы 1 в `product.md` («43 врага, 43 спавнера, 8 сложностей…») совпадает со всеми определениями данных. Сейчас роль есть у всех записей, поэтому аудит проходит; проверка ловит будущие записи с ролью `unknown`.

**Р3. `prototype` заморожен.** Значения переносятся в константу `PROTOTYPE_PARTIAL` в `audit.py` (70 ключей с `partial` из текущего `ContentRegistry.json`), остальные — `none`. Исходники прототипа больше не читаются.

**Р4. Проверка ссылок тестами — по строковому литералу.** Просто и без запуска тестов. Риск: литерал может встречаться в тесте, который не проверяет запись по существу (например, `"experience"` как вспомогательный ключ). Поле `tests` показывает, откуда взят `verified`, чтобы это было видно при ревью.

**Р5. Разбор C# регулярными выражениями и малым разборщиком условий** вместо запуска кода симуляции. Риск: переписывание `ReportUnsupportedEnemy`, `Drop`, `SpawnPosition` в другой форме ломает разбор. Это заметно сразу: аудит падает с кодом 2 и называет конструкцию; тесты аудита это ловят.

## 5. Тестовая стратегия
`Tools/tests/test_audit.py` (unittest, стандартная библиотека), запуск `python3 -m unittest discover -s Tools/tests`. Данные — реальные `Research/Json` и исходники симуляции; ожидаемые значения — из данных и кода, перечисленного выше.
- `simulation_coverage`: классы `AbilityFactory` = {ArcaneBolt, MagicMissile}; 20 атрибутов `SupportedHeroAttributes`; 7 поведений спавна; движения Tracking/Linear/LinearSinWave/Stationary.
- `CsCondition`: `&&`/`||`/`!`/скобки/`EndsWith` на синтетических записях; неизвестное свойство → `CoverageError`.
- Демо и правила §3 на реальном реестре (`build_registry` без Dota, `native=None`): magic_missile ≥ implemented, echo_strike none, absorber ≥ implemented, kez_innate none, yasha implemented, blade_mail none, yasha_and_kaya implemented, boss_tower partial, snotty none, shard none, treasure implemented, dragonus verified, kez partial, meteor_mayhem partial.
- `verified`: `tests` у magic_missile содержит `MagicMissileTests.cs:…`; partial не становится verified.
- `prototype`: значения совпадают с замороженным списком.
- Роли: `role_errors` находит запись с ролью `unknown`; `main(["--out", tmp])` с такой записью завершается кодом 1 и не создаёт файлы.
- Повторяемость: два вызова `build_registry` дают одинаковый JSON.

Симуляция не меняется, `dotnet test` запускается как регрессия. Unity-слой не затрагивается.

## 6. Документы
- `Mechanics.md` §1: столбец «Реализовано в симуляции» (implemented + verified) и строка о правилах `implementation` со ссылкой на этот дизайн.
- `Tools/README.md`: шаг 4 — что считает аудит, коды выхода, тесты аудита.
- `Differences.md` не меняется: это инструмент, не отличие от оригинала.
