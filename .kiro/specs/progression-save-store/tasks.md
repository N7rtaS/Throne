# Задачи: сохранения, сборка ThornThrone.Progression (Task 12)

- [x] 1. Каркас сборки: `Scripts/Progression/ThornThrone.Progression.asmdef` (`noEngineReferences`, Newtonsoft), `DotNet/ThornThrone.Progression/ThornThrone.Progression.csproj`, `ProjectReference` в `ThornThrone.Simulation.Tests`, ссылка из `ThornThrone.Mobile.asmdef`. (Тр. 1)
- [x] 2. Тесты до кода: `FakeFileSystem.cs`, `ProfileTests.cs`, `SaveStoreTests.cs` по списку из `design.md`. `dotnet test` не компилируется (типов нет). (Тр. 2–9)
- [x] 3. `Profile.cs`, `ProfileJson.cs`: модель, значения по умолчанию, журнал, строгое чтение и детерминированная запись. Зелёные `ProfileTests`. (Тр. 2, 3.2, 3.3)
- [x] 4. `SaveFormat.cs`: заголовок, SHA-256, `Encode`/`TryDecode` с диагностикой. (Тр. 3.1, 3.2)
- [x] 5. `FileSystem.cs`: `IFileSystem`, `DiskFileSystem`. (Тр. 9.1, 9.2)
- [x] 6. `ProfileMigrations.cs`, `SaveStore.cs`: `Save`, `Load`, `Preserve`, статусы. Зелёные `SaveStoreTests`. (Тр. 4–8)
- [x] 7. `Scripts/Mobile/Platform/ProfileStorage.cs` и EditMode-тест `ProfileStorageUsesPersistentDataPath`. (Тр. 9.3)
- [x] 8. Проверки: `dotnet test DotNet/ThornThrone.Simulation.Tests`, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh`. (Критерии)
- [x] 9. Документы: `Mechanics.md` §19 «Сохранения профиля» (решения С1–С11), `Differences.md` Б6 (локальные файлы вместо аккаунта), `README.md` (каталог сохранений), `product.md` (числа тестов, состояние). (Тр. 1–9)
- [x] 10. Отметить Task 12 в `roadmap.md`, один коммит по правилам `tech.md` (`Roadmap: Task 12`).
