# План исправления
- [x] 1. Требования и шаги воспроизведения (`bugfix.md`), причина и выбор варианта (`design.md`)
- [x] 2. Тесты воспроизведения `EnemyEventTests`; до исправления не собираются/падают _(1.1–1.3, 2.1–2.3)_
- [x] 3. `SimEvent`: `SpawnId`, `EnemyDef`, `Direction`; перегрузки `Emit` для врага, снаряда, подбираемого объекта _(2.1–2.3, 3.3, 3.5)_
- [x] 4. `WorldRenderer.Consume` строит труп из события; `CorpseCount`/`CorpseAt`; `PrimitiveMesh` в EditMode _(2.4, 3.1, 3.2)_
- [x] 5. EditMode `WorldRendererTests` _(2.4, 3.1)_
- [x] 6. `dotnet test`, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh` _(3.3–3.6)_
- [x] 7. Отметить Task 44 в `roadmap.md`; коммит
