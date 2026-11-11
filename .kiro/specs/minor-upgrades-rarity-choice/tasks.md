# Задачи: малые улучшения, редкость, правила выбора (Task 26)

- [x] 0. Утверждение владельцем: В1 (пассивки на максимуме), В2 (урон другого типа), В3 (запасная награда и её размер)
- [x] 1. Тесты данных: редкость `Common` у 52 вариантов, диагностика неизвестной редкости (Т2.1) — красные
- [x] 2. `UpgradeRarity`, разбор `m_unRarity` в `GameData.Choices` с диагностикой (Т2.1)
- [x] 3. Тесты малых улучшений: предложение на максимуме, прибавка по данным для 17 × вариант, суммирование, правило типа урона, обработчик видит значение (Т1) — красные
- [x] 4. `OwnedPowerUp.MinorCounts`, `Build.UpgradeMinor`, применение в `Recalculate`, кандидаты `Minor` с весом 0.35 и броском варианта, правило §4 дизайна (Т1)
- [x] 5. Тесты заполненных слотов и пассивок на максимуме (Т3, Р4–Р5) — красные, затем исправления
- [x] 6. Тесты запасной награды (Т4) — красные; `Rules.FallbackPowerUp`/`FallbackGoldPickup`, `Run.Choices.cs` `GrantFallbackReward`, убрать `FallbackHeal`
- [x] 7. Тесты карточек (Т5) — красные; `Build/ChoiceOffer.cs` (`ChoiceOffer`, `AttributeChange`, `OfferKind`), заполнение без выделений в `GenerateChoices`
- [x] 8. Переписать `ChoicesAreDistinctValidAndImplemented` и `FallbackWhenNothingIsLeft` под новое поведение (design §7); тесты неизменного поведения (Т6)
- [x] 9. Unity: `Scripts/Mobile/ChoiceText.cs`, `GameUi.ShowChoices` через него; EditMode `ChoiceTextTests` (Т2.3, Т5.6)
- [x] 10. `Mechanics.md` (новый раздел: Р1–Р7, тип урона, запасная награда, карточки; обновить §4, §15, §16, §25 Р1, «Не сделано» §26/§32), `Differences.md` (A19, тип урона малого улучшения)
- [x] 11. `dotnet test` полностью, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh`
- [x] 12. Отметка в `roadmap.md`, коммит
