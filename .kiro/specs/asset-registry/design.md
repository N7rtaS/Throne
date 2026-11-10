# Дизайн: реестр ресурсов `AssetRegistry.json` (Task 7)

Объём утверждён владельцем 10.11.2026: граф, CRC32, выходы Unity (путь, GUID, импортёр, сжатие текстур), статусы, отчёт `missingRequired`. Треугольники — только справочные поля; бюджеты, код 2, файл исключений и `budgetViolations` не делаются (бюджеты — блок F по замерам на устройстве). Поле `referencedBy` не пишется: оно выводится из `references`.

## 1. Место в конвейере
Шаг после `convert-models.py` и запекания (`Tools/README.md`):
```sh
python3 Tools/asset-registry.py            # → Research/FullGame/AssetRegistry.json, таблица моделей в stdout
```
Входы — только файлы проекта: `NestOfThorns/` (+ `manifest.tsv`, карта VPK), `Research/extract-report.json`, `Research/Json/survivors_*.json`, `Research/FullGame/{ContentRegistry,SoundRegistry,SourceVersions}.json`, `UnityProject/Assets/Game/Art/Converted/**` с `.meta`, `Tools/asset-registry-overrides.json`. Dota и Steam не открываются, внешние программы не запускаются (Т6.3). Симуляция и Unity-слой не меняются.

## 2. Модули скрипта `Tools/asset-registry.py`
| Функция | Что делает |
|---|---|
| `read_manifest()` | строки `manifest.tsv` → словарь по пути |
| `read_sources()` | читает каждый файл `NestOfThorns/`, сверяет CRC32; для `.vpk` открывает карту (`vpk.Vpk.read` сверяет CRC сам) и заводит записи `<vpk>:<путь>`; собирает RERL |
| `link_graph()` | RERL → `references` (сначала внутри своего контейнера, потом в другом); запись VPK ссылается на все свои файлы; цели вне реестра → `summary.unresolved` |
| `entity_roots()` | сущность → корни (раздел 3) |
| `reach()` | обход от корней каждой сущности → `usedBy` |
| `read_meta()` | `.meta`: `guid`, первый ключ-импортёр, блоки `platformSettings` |
| `unity_outputs()`, `link_outputs()` | обход `Converted/`, правила Т2.1, несвязанные → `orphanOutputs` |
| `glb_triangles(path, style)` | JSON-чанк glb: по узлам с мешем, режим 4 — `count/3`, 5/6 — `count−2`; фильтр стиля как `HeroView.IsStyleMesh` |
| `model_views()` | справочный раздел `models` |
| `read_overrides()` | `Tools/asset-registry-overrides.json`, проверки Т3.3 |
| `main()` | ошибки → код 1 без записи; иначе JSON через `.tmp` + `os.replace`, сводка и таблица в stdout |

Стиль как у `audit.py`: один файл, `argparse`, `main(argv)` возвращает код, `--out DIR`, `--root DIR`.

## 3. Корни сущностей
- **Определения**: `ContentRegistry.definitions[*].resources` (vmdl, vpcf) → `compiled_path`.
- **Герои и враги с моделью героя** (`m_bUseHeroModel`, сейчас `imperia`): все записи под `heroModelFolders[m_nDOTAHeroID]` из `extract-report.json` + `econItems[m_vecEconItems].assets`. Так Shen (HeroID 20) и Kez (145) получают записи, хотя ещё не сконвертированы.
- **Звуки**: событие `SoundRegistry`, строка которого встречается в данных сущности (`m_pszPlayerHitSoundEvent` и т.п.), → её `vsnd`. Остальные события и файлы `*.vsndevts_c` — корни режима `game_modes/crownfall_act4`.
- **Уровень** `levels/skywrath` → запись `maps/scenes/crownfall/survivors_arena.vpk` → все файлы карты.

## 4. Формат `AssetRegistry.json`
```json
{
 "schema": 1,
 "inputs": {"NestOfThorns/manifest.tsv": "<sha256>", "...": "..."},
 "tools": {"source2viewer": "Version: 20.1…", "converter": "Tools/convert-models.py (Source2Viewer CLI, glb)", "registry": "Tools/asset-registry.py"},
 "statusValues": ["converted", "baked", "recreated", "none", "unused"],
 "summary": {"records": 6304, "byStatus": {}, "byKind": {}, "unresolved": {}, "orphanOutputs": [],
             "requiredEntities": 94, "missingRequired": []},
 "models": {"heroes/dragonus": {"role": "hero", "combine": "sum", "style": 0, "visibleTriangles": 46458,
            "parts": [{"glb": "models/heroes/skywrath_mage/skywrath_mage.glb", "triangles": 3270, "visibleTriangles": 3270}]}},
 "assets": {
  "models/heroes/skywrath_mage/skywrath_mage.vmdl_c": {
   "kind": "vmdl_c", "crc32": "772f96a0", "bytes": 715928, "archive": "316", "reason": "hero-101",
   "references": ["materials/…vmat_c"], "usedBy": ["heroes/dragonus"],
   "outputs": [{"path": "Assets/Game/Art/Converted/models/heroes/skywrath_mage/skywrath_mage.glb",
                "guid": "9ba625c5010514a5b96c0e165405d258", "importer": "ScriptedImporter:glTFast",
                "tool": "Tools/convert-models.py (Source2Viewer CLI, glb)", "stage": "converted",
                "triangles": 3270, "visibleTriangles": 3270}],
   "status": "converted", "verification": "unverified"
  }
 }
}
```
У текстур в выходе есть `compression: {"Android": {"maxTextureSize": 2048, "textureFormat": "Automatic", "textureCompression": "Compressed"}, ...}` (имена по `TextureImporterFormat` / `TextureImporterCompression`). Ключ записи — путь в `NestOfThorns/`, файлы карты — `maps/scenes/crownfall/survivors_arena.vpk:<путь>`.

## 5. Правила и решения
Решения инструмента, не игровые правила, поэтому в `Mechanics.md` не вносятся; отличий от оригинала нет — `Differences.md` не меняется.

| № | Решение | Метка |
|---|---|---|
| Р1 | Записи на все строки манифеста и все файлы карты, а не только на достижимые: граф полный, недостижимое получает `unused` | Р |
| Р2 | Обязательные сущности — категории `heroes`, `enemies`, `powerups` (роли active-ability, passive-item, recipe, innate), `pickups`, `levels` с `required && reachable`. Атрибуты, сложности, спавнеры, режим и служебные powerups ресурсов не имеют | Р |
| Р3 | Видимый вид считается так, как его выбирает код (`HeroView.IsStyleMesh`, `EnemyBaker.IsStyleMesh`): без `_dummy`, с фильтром стиля, стиль без своих мешей откатывается к 0. Уровень → карта задан в коде (`LEVEL_MAPS`): в `survivors_levels.json` карта не названа | Р |
| Р4 | Вид героя/босса — сумма тела и предметов (`looks.json`); вид врага — максимум по моделям `enemies.json` (ваза выбирает одну из 5). `*_physics.glb` связываются с исходной моделью, в вид не входят | Р |
| Р5 | `missingRequired` — отчёт, не ошибка: пассивки, рецепты и две врождённые не ссылаются на ресурсы в данных, их иконки ищутся в блоке F | Р |
| Р8 | `none` — ресурс, достижимый из обязательной сущности, без выхода. Частицы `vpcf` и звуки сейчас почти все `none` (VFX и звук не воссозданы). Если есть и конвертированный, и запечённый выход — статус `baked` | Р |
| Р9 | `recreated`, причина `unused` и `verification: verified` ставятся только по `Tools/asset-registry-overrides.json`; сейчас файл пустой (`{}`) | Р |

## 6. Текущие числа (запуск 10.11.2026)
- Записей 6 304 (5 792 строки манифеста + 512 файлов карты): `converted` 1 075, `baked` 34, `none` 3 837, `unused` 1 358.
- Неразрешённых ссылок 51 от 50 файлов на 9 различных целей вне манифеста (те же 9, что в `extract-report.json: unresolved`), выходов без источника 11 (`*.json` конвертера, срезы 3D-текстуры `default_colorwarp3d_*_z00N`, `physics_default.png`, текстуры инструментов карты).
- Обязательных сущностей 94, без записей 27: 16 пассивок, 9 рецептов, `skywrath_mage_innate`, `vengeful_spirit_innate`.
- Справочно: Dragonus 46 458 видимых треугольников, Империя 60 800, Snotty 22 148, скелет 10 708.

## 7. Тестовая стратегия
`Tools/tests/test_asset_registry.py` (`unittest`):
- синтетический проект во временном каталоге: ресурсы с RERL, собранные в тесте; glb с мешами `m_lod0`, `m_style1_lod0`, `m._dummy`; `.meta` с GUID, импортёром и `platformSettings`;
- граф: `references` из RERL, транзитивный `usedBy`, запись на каждую строку, CRC32 и размер, `unresolved`, корни героев (папка + econ), звуки по сущности и режиму, нет `referencedBy`;
- ошибки входов → код 1 без записи: CRC32, отсутствующий файл, выход без `.meta`, неверные переопределения;
- выходы: glb/PNG/`.mat` с GUID и импортёром, сжатие Android `51 → ASTC_8x8`, `_orm_` и `_physics`, запечённый `.asset` через `enemies.json`, `orphanOutputs`;
- статусы `converted`, `baked`, `none`, `unused`, `recreated` + `verified`;
- `missingRequired` по правилу Р2, вывод числа;
- треугольники по стилям и откат стиля, раздел `models` (сумма и максимум), строка таблицы;
- детерминизм: два запуска → одинаковые байты, `inputs` и версия Source2Viewer;
- реальные данные (пропуск без `NestOfThorns/manifest.tsv`): код 0, записи карты, `skywrath_mage.glb` ↔ `skywrath_mage.vmdl_c` с GUID `9ba625c5010514a5b96c0e165405d258`, виды Dragonus, Империи и всех врагов `enemies.json`, каждая обязательная сущность либо используется, либо в `missingRequired`.

## 8. Риски
- Размер JSON 4,6 МБ в git (основное — выходы PNG со сжатием и `references`). Файл детерминирован, поэтому диффы между запусками малы.
- PNG связываются по имени `vtex` (имена с хешем уникальны; повтор один — `unnamed_1_vmorf`, связывается со всеми совпавшими). Производные `*_orm_<n>.png` и грани куба связываются с исходной текстурой по префиксу.
- Время запуска ~3 с: чтение 1,6 ГБ с CRC32 и RERL.
