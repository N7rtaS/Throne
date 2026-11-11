# План исправления

- [x] 1. Требования и шаги воспроизведения (`bugfix.md`)
- [x] 2. Точка расширения `IProjectileHitEffect` в `Abilities.cs` и вызов в `Run.UpdateProjectiles` (без изменения поведения) _(1.3, 2.3)_
- [x] 3. Тест воспроизведения `GridQueryTests.PiercingShotHitsSameEnemiesWhenHitEffectQueriesArea`; до исправления падает: «zombie 1 on the path: expected 45, was 50» _(1.2, 2.2)_
- [x] 4. `SpatialGrid`: стек буферов, `GridQuery` (`ref struct`, `Dispose`), `Query(center, radius)`, проверка порядка освобождения _(2.1, 2.4, 2.5, 2.6)_
- [x] 5. `Run.cs`: убрать `queryBuffer`, 7 мест перевести на `using var … = grid.Query(...)` _(2.1, 3.1, 3.2)_
- [x] 6. Тесты `SpatialGrid`: вложенность, совпадение с запросом в свой буфер, порядок освобождения, отсутствие выделений _(2.1, 2.4, 2.5, 3.1)_
- [x] 7. Полный `dotnet test`, все тесты зелёные, ожидания старых тестов не менялись _(3.3–3.6)_
- [x] 8. Правило блока C в `roadmap.md` уточнить механизмом; отметить Task 43; коммит
