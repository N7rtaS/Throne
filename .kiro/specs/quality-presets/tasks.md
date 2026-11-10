# Task 14. Пресеты качества — задачи

- [x] 1. EditMode-тесты `QualityPresetsTests` (Т1–Т5, Т8) и правки `GameSettingsTests` — до кода.
- [x] 2. `Scripts/Mobile/QualityPresets.cs`: `DeviceInfo`, `Resolve`, `Level`, `Names`, `RenderScale` (Т2–Т4).
- [x] 3. `ISettingsTargets.SetQuality` возвращает пресет; `UnitySettingsTargets.SetQuality`; `GameSettings.AppliedQuality`, «Авто (…)», подпись «Качество» (Т5, Т6).
- [x] 4. `Editor/QualityPresetBuilder.cs`: `Low_RPAsset`, поля трёх ассетов, уровни `QualitySettings`; вызов из `BuildAndroid`/`BuildMacSmoke`, меню (Т1–Т3). Запустить в batchmode, закоммитить ассеты и `QualitySettings.asset`.
- [x] 5. Smoke: уровень и масштаб в `THORN_SETTINGS` (Т8).
- [x] 6. `Mechanics.md` §20 П3 и новый §21, `Differences.md` Б8/Б9.
- [x] 7. Проверки: `dotnet test`, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh` с тремя пресетами, скриншоты (Т7, Т9).
- [x] 8. Roadmap, коммит.
