# Задачи: TextMeshPro, кириллический шрифт, HUD без аллокаций (Task 10)

- [x] 1. Скачать `PT_Sans-Web-Regular.ttf` и `OFL.txt` (Google Fonts, design Р1) в `UnityProject/Assets/Game/Art/Fonts/` как `PTSans-Regular.ttf`, `OFL.txt`. (Тр. 3)
- [x] 2. asmdef `ThornThrone.Mobile` и `ThornThrone.Mobile.Tests`: ссылка `Unity.TextMeshPro`. (Тр. 1)
- [x] 3. Тесты до кода: `Tests/EditMode/HudTextTests.cs` — `HudUpdateDoesNotAllocate`, `HudTextSkipsUnchangedValue`, `HudTimeIsZeroPadded`, `FontCoversUiText`, `GameUiUsesOnlyTmpText`. Запуск `Tools/unity-tests.sh`: не компилируются или падают по нужной причине. (Тр. 4–10)
- [x] 4. `Editor/FontBaker.cs`: импорт TMP Essential Resources, набор символов, статический SDF-атлас, ошибка при недостающих символах, сохранение без смены GUID, шрифт по умолчанию в `TMP Settings`. Запустить `FontBaker.Bake` в batchmode, проверить `THORN_FONT_BAKE_OK` и GUID после второго запуска. (Тр. 2, 4, 5)
- [x] 5. `GameUi` на `TextMeshProUGUI`, класс `HudText`, `UpdateHud` через кэш и `SetText(format, value)`, `→` → `»`. (Тр. 6, 8, 9, 10, Р2)
- [x] 6. `MobileGame.uiFont`, `MobileBuilder.CreateScene` записывает ссылку и падает без ассета. (Тр. 7)
- [x] 7. `Tools/unity-tests.sh` — все EditMode-тесты зелёные; `dotnet test DotNet/ThornThrone.Simulation.Tests` — зелёный. (Критерии)
- [x] 8. `Tools/mac-smoke.sh` — `THORN_MOBILE_SMOKE` kills > 0; скриншот HUD с кириллицей `Research/Скриншоты/task10-tmp-hud.png`. (Критерии)
- [x] 9. Документы: `README.md` (шрифты, лицензия, запекание), `Tools/README.md`, `Differences.md` Б7, `tech.md` (строка про TMP), `product.md` (число EditMode-тестов). (Тр. 3, 12)
- [x] 10. Отметить Task 10 в `roadmap.md`, один коммит по правилам `tech.md` (`Roadmap: Task 10`).
- [x] 11. По скриншоту: метки «00:00» и «II» не отображались (TMP скрывает строку, которая не помещается по высоте). Тест `GameUiLabelsAreNotTruncated`, высота метки времени 80, у «II» `Overflow`. (Тр. 6, design §6)
