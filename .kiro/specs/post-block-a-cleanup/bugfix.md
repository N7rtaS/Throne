# Служебные правки после блока A

## Текущее поведение
1. Каждая сборка `MobileBuilder.BuildMacSmoke`/`BuildAndroid` меняет `UnityProject/Assets/Game/Scenes/Mobile.unity`: у всех объектов сцены новые локальные `fileID`, рабочее дерево после проверки грязное.
2. `Research/mac-build.log`, `Research/android-build.log`, `Research/android-build-report.json` хранятся в git и меняются при каждой сборке.
3. `Tools/mac-screenshot.sh` с относительным путём (`Builds/Verify/x.png`) передаёт плееру относительный `-logFile`; плеер, запущенный через `open`, пишет лог не туда, скрипт ждёт строку smoke в несуществующем файле.
4. Проверяющий агент пишет скриншоты и логи smoke в `Research/Скриншоты/`, они остаются незакоммиченными.
5. Раздел «Текущее состояние» в `product.md` устарел (54 теста, нет `AssetRegistry.json`).

## Ожидаемое поведение
1. Две сборки подряд без изменения `MobileBuilder.cs` и входных ассетов дают побайтно одинаковую сцену; `git status` сцены чистый.
2. Логи и отчёт последней сборки не в git (`.gitignore`), файлы на диске остаются.
3. `Tools/mac-screenshot.sh <относительный путь>` создаёт PNG и лог по этому пути относительно текущего каталога.
4. Проверяющий пишет свои артефакты в `Builds/Verify/` (в `.gitignore` через `/Builds/`).
5. `product.md` отражает фактические числа тестов из запусков.

## Неизменное поведение
- Сцена SHALL CONTINUE TO содержать корневые объекты `Camera` (Camera, UniversalAdditionalCameraData, тег MainCamera) и `Mobile Game` (MobileGame) с теми же назначенными ассетами.
- `MobileBuilder.CreateScene` SHALL CONTINUE TO создавать сцену с нуля, если файла нет.
- `Tools/mac-smoke.sh` SHALL CONTINUE TO завершаться кодом 0 с `THORN_MOBILE_SMOKE … kills > 0`.
- `Tools/mac-screenshot.sh` с абсолютным путём SHALL CONTINUE TO работать как раньше.
- Тесты симуляции, инструментов и EditMode SHALL CONTINUE TO проходить.

## Шаги воспроизведения
1. `git status` чистый, `Tools/mac-smoke.sh`.
2. `git diff UnityProject/Assets/Game/Scenes/Mobile.unity` — 20 строк: заменены якоря `--- !u!N &<fileID>` и все ссылки `{fileID: …}` (например, `834658459` → `1895156928`).
3. `Tools/mac-screenshot.sh Builds/Verify/rel-test.png` из корня — лог не появляется рядом с PNG.
