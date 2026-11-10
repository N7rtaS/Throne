# Дизайн: сохранения, сборка ThornThrone.Progression (Task 12)

Требования — `requirements.md`. В оригинале прогресс хранится в аккаунте Dota (Steam), локального формата нет, поэтому все правила ниже — проектные решения (**Р**), кроме требований ТЗ §9. Отличие «прогресс локально» уже записано в `Differences.md` Б6; дополняется строкой о сохранениях.

## Архитектура

```
UnityProject/Assets/Game/Scripts/Progression/        asmdef ThornThrone.Progression (noEngineReferences)
  Profile.cs          Profile, ProfileKind, RunResult, ProfileSettings, JoystickMode, RewardJournal
  ProfileJson.cs      ToJson / FromJson вручную через JObject, строгая проверка полей
  SaveFormat.cs       заголовок THORNSAVE, SHA-256, Encode / TryDecode
  SaveStore.cs        Load, Save, SaveResult, LoadResult, статусы
  ProfileMigrations.cs  цепочка IProfileMigration (сейчас пустая)
  FileSystem.cs       IFileSystem, DiskFileSystem
UnityProject/Assets/Game/Scripts/Mobile/Platform/
  ProfileStorage.cs   Open(kind) → SaveStore поверх DiskFileSystem(persistentDataPath/Saves)
DotNet/ThornThrone.Progression/ThornThrone.Progression.csproj   компилирует Scripts/Progression/**
DotNet/ThornThrone.Simulation.Tests/  + ProjectReference на Progression
  SaveStoreTests.cs, ProfileTests.cs, FakeFileSystem.cs
```

- Progression не ссылается на Simulation: профиль хранит строки (имена героев, сложностей, атрибутов), а смысл им даёт App. Проверка, что атрибут улучшения существует в `survivors_attributes`, — задача 31.
- `ThornThrone.Mobile` получает ссылку на `ThornThrone.Progression`. `MobileGame` в этой задаче профиль не загружает: первым потребителем станет экран настроек (задача 13). **Р**, см. вопрос владельцу.
- Тесты — в существующем проекте `ThornThrone.Simulation.Tests` (класс-фикстура на фичу), чтобы команда проверки осталась одной. **Р**.

## Сериализатор

**Р.** `Newtonsoft.Json` 13.0.2, ручное отображение через `JObject`/`JsonTextWriter`, как `GameData`. Новой зависимости нет: пакет уже в `ThornThrone.Simulation.csproj` и в asmdef (`precompiledReferences`). Ручное отображение выбрано вместо `JsonConvert.DeserializeObject<T>`:
- нет рефлексии, поэтому IL2CPP-стриппинг не удалит сеттеры и не нужен `link.xml`;
- отсутствующее поле — ошибка, а не ноль (правило `product.md`);
- порядок полей в файле фиксирован, словари и множества пишутся отсортированными (`StringComparer.Ordinal`): одинаковый профиль даёт одинаковые байты.

Альтернативы: `System.Text.Json` нет в netstandard2.1 без пакета и в Unity; `JsonUtility` требует UnityEngine; свой двоичный формат хуже для диагностики.

## Данные (схема 1)

| Поле JSON | Тип | Правило |
|---|---|---|
| `schemaVersion` | int | `ProfileSchema.Current = 1` |
| `rulesVersion` | string | `ProfileSchema.RulesVersion = "original-6972"`: правила фазы 1 по клиенту 6972 (`SourceVersions.json`). Фаза 2 даст другую строку |
| `kind` | `"user"` / `"test"` | совпадает с видом хранилища, иначе файл повреждён |
| `currency` | long ≥ 0 | золото (`m_nMinigameCurrency`, Mechanics §9) |
| `metaUpgrades` | объект `атрибут → уровень` (int ≥ 0) | имена атрибутов `m_vecMetaProgressionTiers`; проверка имён — задача 31 |
| `unlocks` | массив строк без повторов | идентификаторы задаёт задача 16/32 (например `echelon:footmen_frenzy`) |
| `results` | массив `RunResult` | |
| `settings` | `ProfileSettings` | |
| `journal` | массив строк без повторов | идентификаторы применённых наград и покупок |

`RunResult`: `id` (string, он же ключ журнала для награды), `rulesVersion`, `hero`, `difficulty`, `victory` (bool), `durationSeconds` (double ≥ 0), `level` (int ≥ 1), `kills` (int ≥ 0), `score` (long ≥ 0), `gold` (long ≥ 0). Экран итогов (урон по способностям) — задача 30; если понадобится хранить больше, схема 2 с миграцией.

`ProfileSettings` (значения по умолчанию **Р**, задача 13 может их уточнить через миграцию или без неё, если поле не меняет смысл):

| Поле | По умолчанию | Допустимо |
|---|---|---|
| `quality` | `"auto"` | `auto`, `low`, `medium`, `high` |
| `targetFps` | 60 | 30, 60 |
| `masterVolume`, `musicVolume`, `effectsVolume` | 1.0 | [0, 1] |
| `uiScale` | 1.0 | [0.8, 1.3] |
| `joystick` | `"floating"` | `floating`, `fixed` (как `TouchSettings` в задаче 11) |
| `leftHanded` | false | |
| `vibration` | true | |
| `damageNumbers` | true | |

Журнал: `RewardJournal.TryAdd(id)` → false, если id уже есть. Журнал не усекается (**Р**): за забег добавляется одна запись награды и по записи на покупку, за годы это тысячи строк, десятки килобайт.

## Формат файла

```
THORNSAVE 1 <длина JSON в байтах> <sha256 JSON, 64 hex в нижнем регистре>\n
{ ...JSON профиля... }
```

- **Р.** SHA-256 (`System.Security.Cryptography.SHA256`, есть в netstandard2.1 и IL2CPP). CRC32 тоже ловит порчу, но в .NET Standard 2.1 его нет, а свой код ради экономии на файле в десятки КБ не нужен.
- `1` — версия контейнера, независима от `schemaVersion`.
- Разбор: заголовок по первому `\n` (не дальше 128 байт), длина совпадает с остатком, сумма совпадает, затем `ProfileJson.FromJson`. Любая ошибка → `DecodeError { File, Check, Field, Message }`.

## Алгоритм записи (`Save`)

Пути: `M = <kind>.save`, `B = M.bak`, `T = M.tmp`.

1. `bytes = Encode(profile)`.
2. `fs.Write(T, bytes)` — создать или обрезать, записать, `Flush(true)`. Исключение `IOException` (в том числе нехватка места) → удалить `T` (ошибки удаления игнорируются), вернуть `SaveResult(IoError, сообщение)`.
3. `fs.Read(T)` и `TryDecode` → при ошибке удалить `T`, вернуть `VerifyFailed`.
4. Если `M` существует: если `M` целый — `fs.Delete(B)` (если есть), `fs.Move(M, B)`; если повреждён — `Preserve(M)` (переименовать в `M.corruptN`), `B` не трогать.
5. `fs.Move(T, M)`. Вернуть `Saved`.

Состояния при сбое после каждого шага (проверяются циклом по точке сбоя в тесте):

| Сбой после | M | B | T | Загрузится |
|---|---|---|---|---|
| во время 2 | старый | старый | обрезанный | старый M |
| 3 | старый | старый | новый | старый M, T удалён |
| удаления B | старый | — | новый | старый M |
| `Move(M, B)` | — | старый M | новый | B (последнее подтверждённое) |
| 5 | новый | старый M | — | новый M |

**Р.** Если нет ни M, ни B, а T целый (сбой при первом сохранении или после ручного удаления), T становится основным. Если M или B есть, T всегда удаляется: его сохранение не было подтверждено, а последнее подтверждённое есть в M или B.

**Р.** Переименование в одном каталоге (`File.Move`, POSIX `rename(2)`) атомарно, `File.Replace` не используется: его поведение в Mono/IL2CPP на Android не проверено. Сброс каталога (`fsync` директории) в .NET недоступен; после внезапного отключения питания переименование теоретически может откатиться, но тогда остаются M или B, и загрузка выбирает целый.

## Алгоритм загрузки (`Load`)

1. Прочитать и разобрать M, B, T (каких нет — пропустить).
2. M целый → `Loaded` (или `Migrated`).
3. Иначе B целый → `RestoredFromBackup`; повреждённый M → `Preserve`.
4. Иначе нет ни M, ни B, T целый → `Move(T, M)`, `Loaded`.
5. Иначе есть хотя бы один повреждённый M или B → `Preserve` каждого повреждённого, новый профиль, `ResetAfterCorruption`, `PreservedFiles` = новые имена.
6. Иначе → новый профиль, `Created`.
7. Оставшийся T удалить. Повреждённый T (оборванная запись) не считается повреждённым профилем: он не был подтверждён, сообщения о порче нет.
8. Повреждённый B сохраняется как `.corruptN`, даже если M целый: иначе следующее `Save` удалило бы его при ротации (уточнение при реализации, тест `CorruptBackupIsPreservedWhenMainIsWhole`).

`Load` не записывает новый профиль на диск: его запишет первое `Save` (настройки, покупка, конец забега). Повреждённые файлы к этому моменту уже переименованы, поэтому повторный запуск даст `Created`, а сообщение покажется один раз. **Р.**

`LoadResult { Profile, Status, PreservedFiles, Errors }`. Текст сообщения и его показ — UI (задачи 13/16).

## Версии

- **Р.** `ProfileMigrations` — список шагов `IProfileMigration { From; Apply(JObject) }`, `From → From+1`, применяются к `JObject` до строгой проверки. Сейчас шагов нет, `MinimumSupported = 1`. `SaveStore` принимает цепочку и текущую версию в конструкторе, чтобы тест проверил механизм на тестовой миграции 1 → 2.
- Схема меньше минимальной или без шага миграции → повреждённый файл (п. 7.2). Схема больше текущей → повреждённый файл, сохраняется как `.corruptN` (п. 7.3). **Р**: откат APK вне объёма, данные не теряются, но игрок начнёт новый профиль.
- `rulesVersion` профиля при загрузке не проверяется и при `Save` перезаписывается текущей; результаты хранят свою.

## IFileSystem

```csharp
public interface IFileSystem
{
    bool Exists(string name);
    byte[] Read(string name);                 // IOException, если нет
    void Write(string name, byte[] bytes);    // создать/обрезать, записать, сбросить на диск
    void Move(string from, string to);        // to не должен существовать
    void Delete(string name);                 // нет файла — не ошибка
}
```

- Имена плоские, без каталогов. `DiskFileSystem(root)` создаёт каталог, отклоняет имена с `/`, `\`, `..`, пустые (`ArgumentException`), пишет через `FileStream` + `Flush(true)`.
- Фейковая ФС в тестах: словарь имя → байты; квота байт (нет места: пишет префикс и бросает `IOException`); счётчик операций и точка сбоя (бросает `SimulatedCrash` перед N-й изменяющей операцией, при записи оставляет половину байт); журнал операций для проверки изоляции.

## Platform (Unity)

`ThornThrone.Mobile.Platform.ProfileStorage`:
- `RootPath => Path.Combine(Application.persistentDataPath, "Saves")`;
- `Open(ProfileKind kind) => new SaveStore(new DiskFileSystem(RootPath), kind)`.

Проверяется EditMode-тестом (путь), без записи в каталог редактора.

## Решения с метками

| № | Решение | Метка |
|---|---|---|
| С1 | Формат: заголовок `THORNSAVE 1 len sha256` + JSON | **Р** |
| С2 | Newtonsoft через JObject вручную, строгая проверка, неизвестное поле — ошибка | **Р** |
| С3 | Порядок записи tmp → проверка → (целый M → B) → tmp → M; повреждённый M не становится резервным | **Р** (ТЗ §9 задаёт только этапы) |
| С4 | Целый tmp используется только при отсутствии M и B | **Р** |
| С5 | Повреждённые файлы → `.corruptN`, не удаляются | **Р** (ТЗ §9: сохранить для диагностики) |
| С6 | Новее текущей схемы → как повреждённый | **Р** |
| С7 | Настройки по умолчанию (таблица) | **Р** |
| С8 | `rulesVersion = "original-6972"`, результаты хранят свою | **Р** |
| С9 | Журнал без усечения | **Р** |
| С10 | Тесты в `ThornThrone.Simulation.Tests`, сборка Progression — отдельный csproj | **Р** |
| С11 | `MobileGame` профиль пока не загружает | **Р** |

## Тестовая стратегия

dotnet (`SaveStoreTests`, `ProfileTests`, фейковая ФС):
- `ProfileRoundTripsAllFields`, `EncodingIsDeterministic`;
- `NewProfileHasDefaults`, `JournalAppliesOperationOnce`;
- `CorruptMainIsRestoredFromBackup` (порча байта, обрезка, неверная сумма), `CorruptMainIsPreserved`;
- `BothCorruptCreateNewProfileAndPreserveFiles`, `SecondLoadAfterResetIsCreated`;
- `InterruptedSaveNeverLosesConfirmedProfile` — цикл по каждой точке сбоя `Save`, ожидание: загружен старый или новый профиль, не пустой;
- `NoSpaceKeepsPreviousFiles` — `IoError`, M и B побайтно прежние, T нет;
- `CorruptMainIsNotRotatedIntoBackup`;
- `OldSchemaIsMigrated` (тестовая миграция 1 → 2), `UnsupportedOldSchemaIsTreatedAsCorrupt`, `NewerSchemaIsPreservedNotOverwritten`;
- `MissingRequiredFieldIsCorrupt`, `UnknownFieldIsCorrupt`, `NegativeCurrencyIsCorrupt`, `WrongKindIsCorrupt`;
- `UserAndTestProfilesAreIsolated`;
- `ValidTempIsPromotedWhenNoOtherFiles`, `StaleTempIsDeleted`;
- `DiskFileSystemRoundTripsInTempDirectory`, `DiskFileSystemRejectsPathNames`.

EditMode: `ProfileStorageUsesPersistentDataPath`.

## Риски

- Newtonsoft читает числа как `long`/`double`; диапазоны проверяются явно.
- Добавление ссылки на новую asmdef в `ThornThrone.Mobile` требует перекомпиляции Unity: проверяется `Tools/unity-tests.sh` и `Tools/mac-smoke.sh`.
- Обновление APK сохраняет `persistentDataPath`; проверка на телефоне — блок G.
