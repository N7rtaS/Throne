# Задачи: Aghanim — скипетр и осколок (Task 27)

- [x] 0. Утверждение владельцем: В0 (разбиение), В1 (скипетры комнаты), В2 (источник осколка), В3 (без кандидатов), В4а–г (Stampede, Rot, Laguna, Land Mine)

## Часть 1 — механизм и эффекты-числа
- [x] 1. Тесты данных: `ScepterUpgradeable`/`ShardUpgradeable`, `m_scepterUpgradeDefinition` Rot и Knife Throw, поля Aghanim с диагностикой (Т4.3, design §4) — красные
- [x] 2. `PowerUpDef.ScepterUpgradeable`, `ShardUpgradeable`, `Scepter`; разбор в `GameData`
- [x] 3. Тесты Build: `UpgradeScepter`/`UpgradeShard`, повтор → ложь, `Recalculate` из флага, ранг после скипетра не сбрасывает (Т4) — красные
- [x] 4. `OwnedPowerUp.Scepter`/`Shard`, `Build.UpgradeScepter`/`UpgradeShard`, применение в `Recalculate`
- [x] 5. Тесты экрана: `ChoiceSource`, кандидаты, ≤ 3, равные веса, без кандидатов → золото, очередь, переброс, пауза таймера (Т3) — красные
- [x] 6. `ChoiceSource`, `OfferKind.Scepter`/`Shard`, `GenerateChoices(rng, source)`, карточка с изменениями `m_scepterUpgradeDefinition`; `Run.Aghanim.cs` (`PendingScepters`, `PendingShards`, `ChoiceSource`, `DebugGrant`); правки `Drop`/`Collect`/`OpenChoices`/`Choose`/`Tick`
- [x] 7. Тесты выдачи: 4 комнаты × один скипетр (по В1), нет `pickup:scepter`, осколок по В2 (Т1, Т2) — красные; правило комнаты в `Run.Kill`/`Run.Aghanim.cs`, `EliteRoom.ScepterDropped`
- [x] 8. Тесты эффектов части 1 (design §5, §7): Arcane Bolt, Magic Missile скипетр (счёт убийств без `IKillEffect`: через `owned.Stats.Kills` после скипетра) и осколок, Knife Throw (+ `MaxHits` 128), Annihilation, Track, Swashbuckle, Echo Strike, Stampede, Laguna Blade — красные
- [x] 9. Реализация эффектов части 1 в файлах способностей; Stampede — множитель в `DealDamage` через помощник в `Run.Aghanim.cs`
- [x] 10. Неизменное поведение: все прежние тесты способностей; `StepsDoNotAllocateAfterWarmUp` со скипетрами
- [x] 11. Unity: `ChoiceText` (заголовок, подстановка `{f:N:name}`, строка Aghanim на обычной карточке), `GameUi.ShowChoices` (заголовок, текст `LocScepterAbilityDesc`); EditMode `ChoiceTextTests` (Т7)
- [x] 12. `SmokeForce`: ключи `+scepter`/`+shard`; smoke `-thornElite` выбирает скипетр (Т8)

## Часть 2 — эффекты с новыми сущностями (Task 27б по решению В0)
- [ ] 13. `IKillEffect` (вызов из `Run.Kill` до `RetireEnemy`), `Run.HeroHitSerial`; тесты точек вызова — красные, затем реализация
- [ ] 14. Тесты и реализация: Counter Helix (ответ на урон, 0.15 с), Rot (заморозка, след облаков, общая перезарядка по врагу)
- [ ] 15. Тесты и реализация: Chain Frost (деление 50%), Frostbite (раскол 200)
- [ ] 16. Тесты и реализация: Land Mine (−20%, капкан, радиомина, вакуум, `GroundObject.Variant`)
- [ ] 17. Тесты и реализация: Spirits (взрыв при касании), Mortimer Kisses (печенье 5%), Snotty Bat (две мыши)

## Завершение
- [x] 18. `Mechanics.md` §34 (все Р этой задачи; обновить «Не сделано» §15, §26, §28–§32, §12.5, §16), `Differences.md` (осколок по В2; правило комнаты по В1, если отходит от буквы данных; эффекты Б19 — отображение Aghanim без частиц)
- [x] 19. `dotnet test` полностью, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh` (`-thornElite`, `-thornForce` со скипетрами)
- [x] 20. Отметка в `roadmap.md` (и строка Task 27б по В0), коммит
