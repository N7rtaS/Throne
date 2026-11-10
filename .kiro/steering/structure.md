# Структура проекта и соглашения

## Корень
| Путь | Назначение |
|---|---|
| `UnityProject/` | Unity-проект игры |
| `DotNet/ThornThrone.Simulation/` | csproj, который компилирует исходники симуляции из `UnityProject` для `dotnet test` (своих .cs файлов нет) |
| `DotNet/ThornThrone.Simulation.Tests/` | NUnit-тесты симуляции |
| `Tools/` | Скрипты macOS: извлечение, декодирование, аудит, импорт, конвертация моделей, сборка |
| `Research/Json/` | Канонические данные режима `survivors_*.json` (источник для тестов и Unity) |
| `Research/FullGame/` | Контракт механик и реестры (`Mechanics.md`, `Differences.md`, `*Registry.json`) |
| `Research/Наблюдение/`, `скрины/` | Наблюдения за оригиналом: план записи, скриншоты (видео не в git) |
| `NestOfThorns/` | Оригинальные ресурсы Source 2 из Dota 2 + `manifest.tsv`. Входные данные инструментов и тестов, не Unity-ассеты. Не редактировать вручную |
| `Builds/` | Сборки APK и macOS (не в git) |
| `Удалить/` | Устаревшие файлы, ждут решения владельца (не в git). Не использовать как источник и не удалять без запроса |

## UnityProject/Assets/Game
| Путь | Namespace | Назначение |
|---|---|---|
| `Scripts/Simulation/` | `ThornThrone.Simulation` | Чистая симуляция, asmdef `ThornThrone.Simulation` без ссылок на движок |
| `Scripts/Simulation/Run*.cs` | | `sealed partial class Run`: основной цикл (`Run.cs`), босс (`Run.Boss.cs`), элитные комнаты (`Run.Elite.cs`) |
| `Scripts/Simulation/Rules.cs` | | Константы и параметры правил (шаг и т.д.) |
| `Scripts/Simulation/Core/` | | `Rng`, `Vec2`, `SpatialGrid`, `NavGrid`, `FlowField` |
| `Scripts/Simulation/Content/` | | `GameData` (загрузка JSON), `Definitions` (типизированные определения) |
| `Scripts/Simulation/Abilities/`, `Build/`, `Stats/`, `World/` | | Способности, билд/слоты/улучшения, атрибуты, сущности и пулы |
| `Scripts/Mobile/` | `ThornThrone.Mobile` | Представление Unity: `MobileGame` (владеет `Run`), `GameInput`, `GameUi`, `WorldRenderer`, `HeroView`, `EnemyVisual`. asmdef `ThornThrone.Mobile` (ссылки: Simulation, Input System, URP, uGUI, Newtonsoft) |
| `Editor/` | `ThornThrone.Editor` | `MobileBuilder`, `ArenaBaker`, `EnemyBaker` (без asmdef, сборка `Assembly-CSharp-Editor`) |
| `Tests/EditMode/` | `ThornThrone.Mobile.Tests` | EditMode-тесты Unity-слоя, asmdef `ThornThrone.Mobile.Tests` (только Editor), запуск `Tools/unity-tests.sh` |
| `Scenes/Mobile.unity` | | Единственная сцена, генерируется `MobileBuilder` |
| `Data/Source/` | | Копия `survivors_*.json` и `survivors_arena.gnv.bytes` (через `Tools/import-unity-data.sh`, не править вручную) |
| `Data/Localization/` | | Русская локализация (`powerups_ru.json`) |
| `Art/Converted/`, `Art/Shaders/` | | Сконвертированные модели (glb, LFS), запечённые враги/арена, шейдеры |

## Слои
- Симуляция ничего не знает о представлении. Представление читает пулы `Run` (`Enemies`, `Projectiles`, `Pickups`) и `Events`, передаёт ввод в `run.Update(step, move, dash)`.
- Новая игровая логика — в `Scripts/Simulation` с тестами в `DotNet`. В `Scripts/Mobile` — только отображение, ввод и UI.
- Новая способность или особое поведение подключается через реестр обработчиков; общие алгоритмы переиспользуются.
- Данные после загрузки превращаются в типизированные структуры; исходные определения неизменяемы, переопределения баланса хранятся отдельно.
- Целевое разделение по ТЗ §8: App, Data, Simulation, Presentation, Input, UI, Platform.

## Стиль кода
- Идентификаторы и XML-комментарии `/// <summary>` — на английском. Русский — только в пользовательских строках и путях документов.
- Комментарии к правилам ссылаются на источник: `Mechanics §15`, `ТЗ_Мобильная_версия_v2.md §4`.
- Скобки в стиле Allman, `sealed` классы, `readonly` поля. Тесно связанные типы допускаются в одном файле (`Abilities.cs`, `Entities.cs`).
- Следовать стилю соседнего кода, не делать попутных рефакторингов.

## Тесты
- Namespace `ThornThrone.Simulation.Tests`; один класс-фикстура на фичу (`MagicMissileTests`, `RewardSourceTests`, `SimulationTests`).
- Имена тестов — описательные предложения в PascalCase: `ShenStartsWithMagicMissileFromData`.
- Хелперы в `TestData` (`SimulationTests.cs`): `Load()`, `NewRun(hero, difficulty, seed, rules)`, `Simulate(run, seconds, steer)`, `DisableAbilities(run)`, `Bot(run)`.
- Тесты используют реальные данные (`Research/Json` и VPK арены из `NestOfThorns`), ожидаемые значения берутся из данных и `Mechanics.md`.
- Поведение со случайностью проверяется циклом по нескольким seed. Стиль утверждений NUnit: `Assert.That(x, Is.EqualTo(y).Within(eps))`.
- После изменения симуляции запускать `dotnet test DotNet/ThornThrone.Simulation.Tests`, все тесты должны проходить.
- Код Unity-слоя, который нельзя проверить `dotnet test` (загрузка `TextAsset`, чистые функции представления), проверяется EditMode-тестами в `Assets/Game/Tests/EditMode` (`Tools/unity-tests.sh`).

## Язык документов
- ТЗ, `Research/*.md`, `Tools/README.md` — на русском.
- Код и комментарии в коде — на английском.
- Сообщения коммитов — на русском (формат в `tech.md`, раздел Git).
- Ответы пользователю — на русском.
