# Task 28. Задачи

Решения В1–В5 утверждены владельцем 12.11.2026 (`design.md` §0).

- [x] 1. Тесты рецептов до кода (`RecipeTests`, Т1, Т2): условие, вес 0.42 только в сундуке, выбор объединяет, событие, конкуренция, трезубец, вместо запасной награды, изменения на карточке, без выделений; `[TestCase]` на 9 рецептов. Убедиться, что падают.
- [x] 2. `Build`: `OfferKind.Recipe`, `CanCombine`, вес в `OfferWeight`, `Combine`, `Describe` для рецепта; убрать `CombineRecipes` из `Upgrade` (Т1.1–Т1.9, Т2.1–Т2.2).
- [x] 3. `Run`: событие `RecipeCombined` из `Run.Choices.cs`, `ForceCombine` (Т1.10).
- [x] 4. Переписать тесты автоматического объединения на `ForceCombine` (design §6), прежние ожидания.
- [x] 5. Тест матрицы `ItemAbilityMatrixTests` (450 пар, правило из сырого JSON, отпечаток боя) и точечные тесты Kez/Track (Т4).
- [x] 6. `AbilityBase.EffectDuration` по `DurationEffect` (В4); исправить прочие расхождения, найденные матрицей, или вынести владельцу (Т4.5).
- [x] 7. Аудит: `SupportedHeroAttributes` + атрибуты Shen, правила `gold`/`pile_of_coal` в `Tools/audit.py` с тестами, `[TestCase]` для пассивок, тест врождённой Dragonus; `python3 Tools/audit.py` — 47/47 (Т5).
- [x] 8. Unity: `ChoiceText` для рецепта, `RecipeNotice` и строка в `GameUi`, обработка события в `MobileGame`, smoke `-thornRecipe`, код 17 в `Tools/mac-smoke.sh`; EditMode-тесты (Т2.3, Т3).
- [x] 9. `Mechanics.md` §35 и ссылки в §12, §16, §25; `Differences.md` Б20.
- [x] 10. Прогоны: `dotnet test`, `python3 -m unittest discover -s Tools/tests`, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh -thornRecipe=yasha_and_kaya`, `-thornRecipe=sny_trident`.
- [x] 11. `roadmap.md` — отметка Task 28; коммит.
