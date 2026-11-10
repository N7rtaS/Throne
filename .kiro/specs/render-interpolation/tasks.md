# План: интерполяция представления при 60 FPS
- [x] 1. Тесты до кода в `RenderInterpolationTests.cs`: `AlphaIsFractionOfStep`, `PreviousPositionIsPositionBeforeLastStep`, `InterpolateLerpsByAlpha`, `TeleportResetsPreviousPosition`, `BossSpawnTeleportResetsPreviousPosition`, `RaysTeleportResetsBossPreviousPosition`, `ReusedSlotHasNoInterpolationTrail`, `StopKeepsAlpha`, `ChoiceOpenedMidFrameDropsWholeSteps`; запустить, убедиться в падении — _Требования 1–7_
- [x] 2. `Vec2.Lerp`; поля `PreviousPosition` в `Enemy`, `Projectile`, `Pickup`; `Run.PreviousHeroPosition`, `Run.Alpha`, `Run.Interpolate` — _Требования 1, 2, 8_
- [x] 3. `Run.Update`: снимок перед `Tick`, доводка появившихся по `SpawnId`, правило остатка накопителя при остановке — _Требования 1, 3, 4, 7_
- [x] 4. Сброс предыдущей позиции: `Teleport`, `SpawnBoss`, перенос Империи к лучам, `SpawnEnemy`/`SpawnPickup` — _Требования 5, 6, 7_
- [x] 5. Полный `dotnet test`, все тесты зелёные, включая проверки выделений памяти — _Требования 11, 12_
- [x] 6. `WorldRenderer.Draw` и `MobileGame` (герой, камера, Империя, `SetMoving`) на `run.Interpolate`; зоны, телеграфы, элитные комнаты без интерполяции — _Требования 8, 9, 10_
- [x] 7. macOS smoke `Tools/mac-smoke.sh` и скриншот в `Research/Скриншоты/` — _Критерии приёмки_
- [x] 8. `Mechanics.md` §17 (Р1–Р4), обновить строку «Мобильный слой: нет интерполяции» в `roadmap.md`, отметить Task 9, коммит
