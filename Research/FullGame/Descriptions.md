# Описания powerups режима Nest of Thorns
Сгенерировано `Tools/research-client.py` из `Research/Json/survivors_powerups.json` и локализации `resource/localization/vdata/survivors_{powerups,attributes}_{russian,english}.txt` клиента. Не править вручную.
Тексты приведены как в оригинале, вместе с разметкой `<b>`. Плейсхолдеры `{f:…}` не подставлены: их значение подставляет клиент. Числа атрибутов — значения из данных без форматирования клиента (масштаб процентов в `{f:0:attribute_value}` не восстановлен).

## Активные способности (17)

### magic_missile — Магический снаряд / Magic Missile
- ID 10; класс `CSurvivorsPowerUpDefinition_MagicMissile`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Шенделезара / Shendelzare
- Описание (ru): Герой выпускает в ближайшего врага снаряд, который наносит урон и оглушает всех врагов неподалёку.
- Описание (en): Shoots a projectile at the closest enemy, damaging it and stunning nearby enemies.
- Aghanim (ru): Способность получает <b>+{f:2:damage_per_enemy}</b> к урону за каждого врага, убитого ей. Текущий бонус: <b>{f:1:extra_damage}</b>.
- Aghanim (en): Does <b>{f:2:damage_per_enemy}</b> more damage per enemy killed using Magic Missile (currently <b>{f:1:extra_damage}</b>)
- Плейсхолдеры, значение подставляет клиент: `{f:1:extra_damage}`, `{f:2:damage_per_enemy}`
- Базовые атрибуты: Damage_Magical = 20 (Магический урон); Cooldown = 2.4 (Перезарядка); TargetCount = 1 (Число снарядов); StunDuration = 1 (Длительность оглушения); Range = 1000 (Дальность); ProjectileSpeed = 1200 (Скорость снарядов); ProjectileRadius = 50 (нет в локализации); ProjectileAttack = 1 (нет в локализации); ProjectileBounces = 1 (Отскоки снарядов); Radius = 160 (Радиус); DurationEffect = 1 (нет в локализации)
- Атрибуты подсказки: Damage_Magical (Магический урон); Cooldown (Перезарядка); Range (Дальность); TargetCount (Число снарядов); ProjectileBounces (Отскоки снарядов); StunDuration (Длительность оглушения)

### arcane_bolt — Волшебный заряд / Arcane Bolt
- ID 11; класс `CSurvivorsPowerUpDefinition_ArcaneBolt`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Драгонус / Dragonus
- Описание (ru): Герой выпускает в ближайшего врага снаряд, наносящий урон.
- Описание (en): Shoots a damaging projectile at the closest enemy.
- Aghanim (ru): <b>Удваивает</b> число снарядов.
- Aghanim (en): <b>Double</b> the number of bolts fired.
- Базовые атрибуты: Damage_Magical = 7 (Магический урон); Cooldown = 1.2 (Перезарядка); TargetCount = 1 (Число снарядов); Range = 1200 (Дальность); ProjectileSpeed = 800 (Скорость снарядов); KnockbackDistance = 15 (Дальность отталкивания); ProjectileRadius = 50 (нет в локализации); ProjectileAttackInterval = 0.2 (Интервал атак снарядами); ProjectileAttack = 1 (нет в локализации)
- Атрибуты подсказки: Damage_Magical (Магический урон); Cooldown (Перезарядка); Range (Дальность); TargetCount (Число снарядов)

### echo_strike — Вторящий выпад / Echo Slash
- ID 12; класс `CSurvivorsPowerUpDefinition_EchoStrike`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Кез / Kez
- Описание (ru): Герой рассекает врагов перед собой. Дополнительные атаки наносятся по той же области.
- Описание (en): Hits enemies in a line in front of you. Additional strikes will clone the original.
- Aghanim (ru): Атаки совершаются как перед героем, так и <b>позади</b> него.
- Aghanim (en): Echo Slash both in front and <b>behind</b> you.
- Базовые атрибуты: Damage_Physical = 10 (Физический урон); Cooldown = 2.1 (Перезарядка); KnockbackDistance = 12 (Дальность отталкивания); InitialTickDelay = 0.1 (нет в локализации); DamageTicks = 1 (Число срабатываний); Length = 720 (Дальность); Width = 420 (Ширина); TimeBetweenTicks = 1 (Интервал срабатываний)
- Атрибуты подсказки: Damage_Physical (Физический урон); Cooldown (Перезарядка); Length (Дальность); DamageTicks (Число срабатываний)

### counter_helix — Ответный оборот / Counter Helix
- ID 13; класс `CSurvivorsPowerUpDefinition_CounterHelix`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Акс / Axe
- Описание (ru): Наносит урон всем врагам вокруг и отталкивает их.
- Описание (en): Hits enemies around you, damaging them and knocking them back.
- Aghanim (ru): Срабатывает с шансом в <b>{f:0:counter_chance}%</b> при получении урона.
- Aghanim (en): <b>{f:0:counter_chance}%</b> to trigger when you receive damage.
- Плейсхолдеры, значение подставляет клиент: `{f:0:counter_chance}`
- Базовые атрибуты: Damage_Physical = 25 (Физический урон); Cooldown = 3.5 (Перезарядка); InitialTickDelay = 0.05 (нет в локализации); KnockbackDistance = 30 (Дальность отталкивания); DamageTicks = 1 (Число срабатываний); Radius = 360 (Радиус)
- Атрибуты подсказки: Damage_Physical (Физический урон); Cooldown (Перезарядка); Radius (Радиус); KnockbackDistance (Дальность отталкивания)

### rot — Гниение / Rot
- ID 14; класс `CSurvivorsPowerUpDefinition_AreaAttack_CircleConstant`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Падж / Pudge
- Описание (ru): Наносит урон врагам вокруг. Имеет отдельную перезарядку для каждого врага.
- Описание (en): Damages enemies around you. Cooldown applies individually to each enemy.
- Aghanim (ru): Замедляет врагов на <b>30%</b> и оставляет за героем облако гнили.
- Aghanim (en): Leave a trail of Rot behind you. Slows enemies for <b>30%</b>.
- Базовые атрибуты: Damage_Magical = 5 (Магический урон); Cooldown = 1 (Перезарядка); Radius = 250 (Радиус)
- Атрибуты подсказки: Damage_Magical (Магический урон); Cooldown (Перезарядка); Radius (Радиус)

### mortimer_kisses — Поцелуйчики Мортимера / Mortimer's Kisses
- ID 15; класс `CSurvivorsPowerUpDefinition_MortimerKisses`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Пороховница / Snapfire
- Описание (ru): Герой выпускает в случайного врага шар лавы, оставляя под ним лужу, которая наносит периодический урон.
- Описание (en): Launches firespit globs at random targets, causing damage over time in an area.
- Aghanim (ru): Из врагов, убитых этой способностью, может появиться целебная печенька.
- Aghanim (en): Enemies killed with Mortimer's Kisses have a chance to spawn a healing cookie.
- Базовые атрибуты: Damage_Magical = 6 (Магический урон); DamageTicks = 5 (Число срабатываний); Cooldown = 5 (Перезарядка); Range = 1000 (Дальность); Radius = 300 (Радиус); ProjectileSpeed = 1200 (Скорость снарядов); TimeBetweenTicks = 0.5 (Интервал срабатываний); TargetCount = 1 (Число снарядов); ProjectileAttackInterval = 0.2 (Интервал атак снарядами)
- Атрибуты подсказки: Damage_Magical (Магический урон); DamageTicks (Число срабатываний); Cooldown (Перезарядка); Radius (Радиус); Range (Дальность); TargetCount (Число снарядов)

### frostbite — Обморожение / Frostbite
- ID 16; класс `CSurvivorsPowerUpDefinition_Frostbite`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Кристальная дева / Crystal Maiden
- Описание (ru): Замораживает самого сильного врага в радиусе действия, отчего снаряды наносят ему больше урона.
- Описание (en): Freezes the strongest enemy in range and making them take more damage from projectile attacks.
- Aghanim (ru): Когда начальная цель погибает, обморожение <b>распространяется</b> на врагов рядом.
- Aghanim (en): Enemies that die while frostbitten will <b>shatter</b>, causing nearby enemies to be frostbitten.
- Базовые атрибуты: TargetCount = 2 (Число снарядов); Damage_Magical = 0 (Магический урон); Cooldown = 3.5 (Перезарядка); Range = 800 (Дальность); ModifierDamagePerTick = 4 (Маг. урон за срабатывание); FreezeDuration = 1.5 (Длит. приморозки); TimeBetweenTicks = 0.5 (Интервал срабатываний); FreezeStrength = 1 (Сила приморозки); ProjectileDamagePercent = 0.25 (Множитель урона снарядов); ProjectileAttack = 1 (нет в локализации); DurationEffect = 1 (нет в локализации)
- Атрибуты подсказки: ModifierDamagePerTick (Маг. урон за срабатывание); Cooldown (Перезарядка); FreezeDuration (Длит. приморозки); Range (Дальность); TargetCount (Число снарядов); ProjectileDamagePercent (Множитель урона снарядов)

### laguna_blade — Раскалённая молния / Laguna Blade
- ID 17; класс `CSurvivorsPowerUpDefinition_LagunaBlade`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Лина / Lina
- Описание (ru): Молния, наносящая урон самому сильному врагу в радиусе действия.
- Описание (en): Damages the strongest enemy in range with a bolt of lightning.
- Aghanim (ru): Позволяет ударить по одной цели <b>несколькими</b> молниями.
- Aghanim (en): Laguna blade can target the <b>same enemy multiple times</b>.
- Базовые атрибуты: TargetCount = 1 (Число снарядов); Damage_Magical = 40 (Магический урон); Cooldown = 3 (Перезарядка); Range = 1000 (Дальность); ProjectileAttack = 1 (нет в локализации)
- Атрибуты подсказки: Damage_Magical (Магический урон); Cooldown (Перезарядка); Range (Дальность); TargetCount (Число снарядов)

### land_mine — Неконтактная мина / Proximity Mines
- ID 18; класс `CSurvivorsPowerUpDefinition_LandMine`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Минёры / Techies
- Описание (ru): Герой устанавливает под собой неконтактную мину, которая после активации взорвёт всех врагов рядом.
- Описание (en): Plants a proximity mine at your position. After an arming period the mine will be triggered by nearby enemies.
- Aghanim (ru): Позволяет устанавливать <b>стазисные капканы</b> и <b>радиомины</b>.
- Aghanim (en): Can now plant <b>Stasis Traps</b> and <b>Remote Mines</b>
- Базовые атрибуты: Damage_Physical = 30 (Физический урон); Cooldown = 4 (Перезарядка); Radius = 350 (Радиус); LifeTime = 20 (нет в локализации); ArmingTime = 0.7 (Ускорение подготовки); TriggerTime = 0.85 (нет в локализации)
- Атрибуты подсказки: Damage_Physical (Физический урон); Cooldown (Перезарядка); Radius (Радиус)

### knife_throw — Бросок ножа / Knife Throw
- ID 19; класс `CSurvivorsPowerUpDefinition_KnifeThrow`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Бакит / Bakeet
- Описание (ru): Герой бросает нож перед собой.
- Описание (en): Throws a knife straight ahead.
- Aghanim (ru): Делает <b>неограниченным</b> число пробитий ножами.
- Aghanim (en): Knifes have <b>infinite pierce</b>.
- Базовые атрибуты: Damage_Physical = 12 (Физический урон); Cooldown = 1.2 (Перезарядка); TargetCount = 1 (Число снарядов); ProjectileAttackInterval = 0.15 (Интервал атак снарядами); ProjectileSpeed = 1500 (Скорость снарядов); Range = 1500 (Дальность); KnockbackDistance = 5 (Дальность отталкивания); ProjectileRadius = 70 (нет в локализации); ProjectileHorizontalOffset = 60 (нет в локализации); ProjectileAttack = 1 (нет в локализации); ProjectileAttackInterval = 0.15 (Интервал атак снарядами)
- Атрибуты подсказки: Damage_Physical (Физический урон); Cooldown (Перезарядка); TargetCount (Число снарядов); ProjectileHitCount (Число пробитий)

### track — Метка / Track
- ID 21; класс `CSurvivorsPowerUpDefinition_Track`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Охотник за головами / Bounty Hunter
- Описание (ru): Наносит урон самому сильному врагу в радиусе действия и накладывает на него метку, которая увеличивает урон по нему и шанс получить золото за его убийство.
- Описание (en): Damages the strongest enemy in range and marks them. Marked enemies take extra damage and have a higher chance to drop gold when killed.
- Aghanim (ru): Каждая сокровищница даёт <b>два предмета</b>.
- Aghanim (en): Treasure Pickups grant <b>two items</b>.
- Базовые атрибуты: TargetCount = 1 (Число снарядов); Damage_Physical = 6 (Физический урон); Cooldown = 10 (Перезарядка); Range = 1500 (Дальность); IncomingDamageAmplification = 0.2 (Бонус к урону); IncomingDamageAmplificationDuration = 10 (Длит. бонуса к урону); ProjectileAttack = 1 (нет в локализации); DurationEffect = 1 (нет в локализации)
- Атрибуты подсказки: Damage_Physical (Физический урон); Cooldown (Перезарядка); TargetCount (Число снарядов); IncomingDamageAmplification (Бонус к урону); IncomingDamageAmplificationDuration (Длит. бонуса к урону)

### snotty_bat — Сопливчик / Snotty The Bat
- ID 22; класс `CSurvivorsPowerUpDefinition_Snotty`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Падж / Pudge
- Описание (ru): Сопливчик летает вокруг героя, подбирая предметы и нанося урон по области, замедляющий врагов.
- Описание (en): Snotty will fly around you, picking up objects, and dealing area damage that slows enemies.
- Aghanim (ru): У Сопливчика появился друг! Теперь у вас будут <b>2</b> летучие мыши.
- Aghanim (en): Snotty has a friend! You'll now have <b>2</b> bats.
- Базовые атрибуты: Damage_Physical = 10 (Физический урон); Cooldown = 6 (Перезарядка); Radius = 300 (Радиус); SlowDuration = 1.5 (Длительность замедления); SlowStrength = 0.5 (Замедление передвижения); DurationEffect = 1 (нет в локализации); PickupRadius = 50 (Радиус подбора)
- Атрибуты подсказки: Damage_Physical (Физический урон); Cooldown (Перезарядка); Radius (Радиус); SlowDuration (Длительность замедления); SlowStrength (Замедление передвижения); PickupRadius (Радиус подбора)

### annihilation — Аннигиляция / Annihilation
- ID 102; класс `CSurvivorsPowerUpDefinition_InstantAttack`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Скри'Ок / Skree'auk
- Описание (ru): Шквал отмщения, наносящий огромный физический урон многим врагам, но с долгой перезарядкой.
- Описание (en): Unleash physical vengeance upon many enemies around you. High damage, long cooldown.
- Aghanim (ru): Скри'Ок дарует вам <b>дополнительную жизнь</b>.
- Aghanim (en): Skree'auk blesses you with an <b>extra life</b>.
- Базовые атрибуты: Damage_Physical = 220 (Физический урон); TargetCount = 50 (Число снарядов); Cooldown = 60 (Перезарядка); Range = 1600 (Дальность); ProjectileAttack = 1 (нет в локализации)
- Атрибуты подсказки: Damage_Physical (Физический урон); TargetCount (Число снарядов); Cooldown (Перезарядка); Range (Дальность)

### chain_frost — Цепная стужа / Chain Frost
- ID 103; класс `CSurvivorsPowerUpDefinition_ProjectileAttack`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Лич / Lich
- Описание (ru): Герой выпускает в ближайшего врага медленный снаряд, который наносит урон, замедляет всех вокруг и отскакивает в другого врага.
- Описание (en): Shoots a slow-moving projectile at the closest enemy, dealing damage, slowing the area, and then bouncing to another enemy.
- Aghanim (ru): При каждом отскоке есть шанс в <b>50%</b>, что снаряд разделится надвое.
- Aghanim (en): Each time Chain Frost bounces to a new target, it has a <b>50%</b> chance to split into two projectiles.
- Базовые атрибуты: Damage_Magical = 60 (Магический урон); Cooldown = 14 (Перезарядка); TargetCount = 1 (Число снарядов); StunDuration = 0.2 (Длительность оглушения); Range = 800 (Дальность); ProjectileBounces = 5 (Отскоки снарядов); ProjectileSpeed = 1000 (Скорость снарядов); ProjectileRadius = 90 (нет в локализации); FreezeSplashRadius = 130 (Радиус приморозки); FreezeDuration = 2 (Длит. приморозки); FreezeStrength = 0.25 (Сила приморозки); ProjectileAttack = 1 (нет в локализации); DurationEffect = 1 (нет в локализации)
- Атрибуты подсказки: Damage_Magical (Магический урон); Cooldown (Перезарядка); Range (Дальность); TargetCount (Число снарядов); ProjectileBounces (Отскоки снарядов)

### swashbuckle — Лихой выпад / Swashbuckle
- ID 120; класс `CSurvivorsPowerUpDefinition_Swashbuckle`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Пангольер / Pangolier
- Описание (ru): Герой совершает несколько быстрых ударов по горизонтали.
- Описание (en): Slashes enemies horizontally with several quick swipes.
- Aghanim (ru): Добавляет удары по <b>вертикали</b>.
- Aghanim (en): Swashbuckle now also <b>slashes vertically</b>
- Базовые атрибуты: Damage_Physical = 8 (Физический урон); Cooldown = 1.6 (Перезарядка); KnockbackDistance = 7 (Дальность отталкивания); DamageTicks = 2 (Число срабатываний); Length = 520 (Дальность); Width = 300 (Ширина); TimeBetweenTicks = 0.18 (Интервал срабатываний); Directions = 2 (Число направлений)
- Атрибуты подсказки: Damage_Physical (Физический урон); Cooldown (Перезарядка); Length (Дальность); Directions (Число направлений); DamageTicks (Число срабатываний)

### stampede — Набег / Stampede
- ID 130; класс `CSurvivorsPowerUpDefinition_Stampede`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Кентавр-вождь / Centaur Warrunner
- Описание (ru): Герой ускоряется, становится неуязвимым и наносит физический урон врагам неподалёку.
- Описание (en): Charge forward, gaining movement speed, becoming invulnerable, and trampling enemies with physical damage.
- Aghanim (ru): Физический урон всех способностей увеличивается на <b>{f:0:speed_damage_multiplier}%</b> от скорости передвижения героя. Текущий бонус: <b>{f:0:actual_damage_multiplier}%</b>.
- Aghanim (en): Your physical damage from all abilities does a percentage more damage based on <b>{f:0:speed_damage_multiplier}%</b> of your movement speed. (Current boost <b>{f:0:actual_damage_multiplier}%</b>)
- Плейсхолдеры, значение подставляет клиент: `{f:0:actual_damage_multiplier}`, `{f:0:speed_damage_multiplier}`
- Базовые атрибуты: Damage_Physical = 10 (Физический урон); Cooldown = 30 (Перезарядка); KnockbackDistance = 30 (Дальность отталкивания); Duration = 3.5 (Длительность); StampedeMovementSpeed = 700 (Скорость при срабатывании); Radius = 200 (Радиус); DurationEffect = 1 (нет в локализации)
- Атрибуты подсказки: Damage_Physical (Физический урон); Cooldown (Перезарядка); Radius (Радиус); Duration (Длительность); StampedeMovementSpeed (Скорость при срабатывании)

### spirits — Духи / Spirits
- ID 210; класс `CSurvivorsPowerUpDefinition_Spirits`; роль `active-ability`; максимальный ранг 9
- Герой-источник: Ио / Io
- Описание (ru): Духи, которые кружат вокруг героя, нанося урон задетым врагам, а затем взрываются, нанося урон по области.
- Описание (en): Summon spirits that dance in a circle, dealing damage to all units it passes through and eventually explodes and deals damage in an area.
- Aghanim (ru): Духи <b>взрываются</b> при каждом соприкосновении с врагами.
- Aghanim (en): Spirits trigger <b>explosions</b> upon impact.
- Базовые атрибуты: TargetCount = 1 (Число снарядов); Damage_Magical = 7 (Магический урон); Cooldown = 5 (Перезарядка); Range = 350 (Дальность); Radius = 300 (Радиус); Duration = 4 (Длительность); DurationEffect = 1 (нет в локализации)
- Атрибуты подсказки: Damage_Magical (Магический урон); Cooldown (Перезарядка); TargetCount (Число снарядов); Duration (Длительность)

## Пассивные предметы (16)

### yasha — Яша / Yasha
- ID 1; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: MovementSpeed (Скорость передвижения)
- Глобальные атрибуты малых улучшений: [COMMON] MovementSpeed = 25 (Скорость передвижения)

### blade_mail — Лезвийный доспех / Blade Mail
- ID 2; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: Armor (Сопротивление урону); DamageReflection (Отражение урона)
- Глобальные атрибуты малых улучшений: [COMMON] Armor = 0.1 (Сопротивление урону), DamageReflection = 1 (Отражение урона)

### kaya — Кайя / Kaya
- ID 3; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: DamageMultiplier_Magical (Множитель маг. урона)
- Глобальные атрибуты малых улучшений: [COMMON] DamageMultiplier_Magical = 0.15 (Множитель маг. урона)

### reaver — Губитель / Reaver
- ID 4; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: MaxHP (Макс. здоровье)
- Глобальные атрибуты малых улучшений: [COMMON] MaxHP = 50 (Макс. здоровье)

### ring_of_tarrasque — Кольцо Тарраска / Ring of Tarrasque
- ID 5; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: HPRegen (Восстановление здоровья)
- Глобальные атрибуты малых улучшений: [COMMON] HPRegen = 0.5 (Восстановление здоровья)

### tome_of_knowledge — Книга знаний / Tome of Knowledge
- ID 6; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: ExpMultiplier (Множитель опыта)
- Глобальные атрибуты малых улучшений: [COMMON] ExpMultiplier = 0.1 (Множитель опыта)

### octarine_core — Октариновое ядро / Octarine Core
- ID 7; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: CooldownReductionMultiplier (Уменьшение перезарядок)
- Глобальные атрибуты малых улучшений: [COMMON] CooldownReductionMultiplier = 0.05 (Уменьшение перезарядок)

### bloodstone — Камень крови / Bloodstone
- ID 8; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: AoEIncrease (Увеличение радиусов)
- Глобальные атрибуты малых улучшений: [COMMON] AoEIncrease = 0.1 (Увеличение радиусов)

### world_chasm_artifact — Артефакт мировой бездны / World Chasm Artifact
- ID 9; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: PickupRadius (Радиус подбора)
- Глобальные атрибуты малых улучшений: [COMMON] PickupRadius = 150 (Радиус подбора)

### crystalys — Кристалис / Crystalys
- ID 20; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: CriticalStrike_Physical_Chance (Шанс критического урона); CriticalStrike_Physical_DamageMultiplier (Критический физический урон)
- Глобальные атрибуты малых улучшений: [COMMON] CriticalStrike_Physical_DamageMultiplier = 0.35 (Критический физический урон)

### timeless_relic — Извечная реликвия / Timeless Relic
- ID 23; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 2
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: DurationExtension (Доп. длительность эффектов)
- Глобальные атрибуты малых улучшений: [COMMON] DurationExtension = 1 (Доп. длительность эффектов)

### echo_stone — Набор специалиста / Specialist Array
- ID 90; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 2
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: TargetCount (Число снарядов)
- Глобальные атрибуты малых улучшений: [COMMON] TargetCount = 1 (Число снарядов)

### sange — Сандж / Sange
- ID 91; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: KnockbackDistanceMultiplier (Усиление отталкивания)
- Глобальные атрибуты малых улучшений: [COMMON] KnockbackDistanceMultiplier = 0.2 (Усиление отталкивания)

### phylactery — Филактерия / Phylactery
- ID 97; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: Projectile_BonusMagicDamage (Доп. урон от снарядов)
- Глобальные атрибуты малых улучшений: [COMMON] Projectile_BonusMagicDamage = 5 (Доп. урон от снарядов)

### demon_edge — Лезвие демона / Demon Edge
- ID 98; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 3
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: BonusPhysicalDamage (Доп. физический урон)
- Глобальные атрибуты малых улучшений: [COMMON] BonusPhysicalDamage = 5 (Доп. физический урон)

### apex — Величие / Apex
- ID 101; класс `CSurvivorsPowerUpDefinition`; роль `passive-item`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: ActiveAbilitySlots (Доп. ячейки способностей)
- Глобальные атрибуты малых улучшений: [COMMON] ActiveAbilitySlots = 1 (Доп. ячейки способностей)

## Рецепты (9)

### sange_and_yasha — Сандж и Яша / Sange and Yasha
- ID 92; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: KnockbackDistanceMultiplier (Усиление отталкивания); MovementSpeed (Скорость передвижения)
- Глобальные атрибуты малых улучшений: [COMMON] KnockbackDistanceMultiplier = 0.8 (Усиление отталкивания), MovementSpeed = 100 (Скорость передвижения)
- Рецепт: `yasha` (ID 1) + `sange` (ID 91)

### kaya_and_sange — Кайя и Сандж / Kaya and Sange
- ID 93; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: KnockbackDistanceMultiplier (Усиление отталкивания); DamageMultiplier_Magical (Множитель маг. урона)
- Глобальные атрибуты малых улучшений: [COMMON] KnockbackDistanceMultiplier = 0.8 (Усиление отталкивания), DamageMultiplier_Magical = 0.6 (Множитель маг. урона)
- Рецепт: `kaya` (ID 3) + `sange` (ID 91)

### yasha_and_kaya — Яша и Кайя / Yasha and Kaya
- ID 94; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: MovementSpeed (Скорость передвижения); DamageMultiplier_Magical (Множитель маг. урона)
- Глобальные атрибуты малых улучшений: [COMMON] MovementSpeed = 100 (Скорость передвижения), DamageMultiplier_Magical = 0.6 (Множитель маг. урона)
- Рецепт: `yasha` (ID 1) + `kaya` (ID 3)

### heart_of_tarrasque — Сердце Тарраска / Heart of Tarrasque
- ID 95; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: MaxHP (Макс. здоровье); HPRegen (Восстановление здоровья)
- Глобальные атрибуты малых улучшений: [COMMON] MaxHP = 200 (Макс. здоровье), HPRegen = 3 (Восстановление здоровья)
- Рецепт: `reaver` (ID 4) + `ring_of_tarrasque` (ID 5)

### daedalus — Дедал / Daedalus
- ID 99; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: CriticalStrike_Physical_Chance (Шанс критического урона); CriticalStrike_Physical_DamageMultiplier (Критический физический урон); BonusPhysicalDamage (Доп. физический урон)
- Глобальные атрибуты малых улучшений: [COMMON] CriticalStrike_Physical_Chance = 0.3 (Шанс критического урона), CriticalStrike_Physical_DamageMultiplier = 2.5 (Критический физический урон), BonusPhysicalDamage = 20 (Доп. физический урон)
- Рецепт: `crystalys` (ID 20) + `demon_edge` (ID 98)

### khanda — Кханда / Khanda
- ID 100; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: CriticalStrike_Projectile_Chance (Шанс критического урона снарядами); CriticalStrike_Projectile_DamageMultiplier (Критический урон снарядами); Projectile_BonusMagicDamage (Доп. урон от снарядов)
- Глобальные атрибуты малых улучшений: [COMMON] CriticalStrike_Projectile_Chance = 0.3 (Шанс критического урона снарядами), CriticalStrike_Projectile_DamageMultiplier = 2.5 (Критический урон снарядами), Projectile_BonusMagicDamage = 20 (Доп. урон от снарядов)
- Рецепт: `crystalys` (ID 20) + `phylactery` (ID 97)

### sny_trident — Трезубец / Trident
- ID 140; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: KnockbackDistanceMultiplier (Усиление отталкивания); MovementSpeed (Скорость передвижения); DamageMultiplier_Magical (Множитель маг. урона)
- Глобальные атрибуты малых улучшений: [COMMON] KnockbackDistanceMultiplier = 1 (Усиление отталкивания), MovementSpeed = 125 (Скорость передвижения), DamageMultiplier_Magical = 0.75 (Множитель маг. урона)
- Рецепт: `sange_and_yasha` (ID 92) + `kaya` (ID 3)

### kns_trident — Трезубец / Trident
- ID 141; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: KnockbackDistanceMultiplier (Усиление отталкивания); MovementSpeed (Скорость передвижения); DamageMultiplier_Magical (Множитель маг. урона)
- Глобальные атрибуты малых улучшений: [COMMON] KnockbackDistanceMultiplier = 1 (Усиление отталкивания), MovementSpeed = 125 (Скорость передвижения), DamageMultiplier_Magical = 0.75 (Множитель маг. урона)
- Рецепт: `kaya_and_sange` (ID 93) + `yasha` (ID 1)

### ynk_trident — Трезубец / Trident
- ID 143; класс `CSurvivorsPowerUpDefinition`; роль `recipe`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
- Атрибуты подсказки: KnockbackDistanceMultiplier (Усиление отталкивания); MovementSpeed (Скорость передвижения); DamageMultiplier_Magical (Множитель маг. урона)
- Глобальные атрибуты малых улучшений: [COMMON] KnockbackDistanceMultiplier = 1 (Усиление отталкивания), MovementSpeed = 125 (Скорость передвижения), DamageMultiplier_Magical = 0.75 (Множитель маг. урона)
- Рецепт: `yasha_and_kaya` (ID 94) + `sange` (ID 91)

## Врождённые (3)

### vengeful_spirit_innate — Нескончаемый ужас / Unending Terror
- ID 500; класс `CSurvivorsPowerUpDefinition`; роль `innate`; максимальный ранг 1
- Описание (ru): Нанося магический урон, Шен снижает физическое сопротивление жертв.
- Описание (en): Shen's magical damage reduces the physical resistance of enemies.
- Aghanim: нет в данных
- Атрибуты подсказки: MagicDamageWeakensPhysicalDamageResistance_Percentage (Снижение физ. сопротивления от маг. урона); MagicDamageWeakensPhysicalDamageResistance_Duration (Длительность снижения физ. сопротивления от маг. урона)
- Глобальные атрибуты малых улучшений: [COMMON] MagicDamageWeakensPhysicalDamageResistance_Percentage = 0.3 (Снижение физ. сопротивления от маг. урона), MagicDamageWeakensPhysicalDamageResistance_Duration = 5 (Длительность снижения физ. сопротивления от маг. урона)

### skywrath_mage_innate — Потомок Скайраса / Scion of Skywrath
- ID 501; класс `CSurvivorsPowerUpDefinition`; роль `innate`; максимальный ранг 1
- Описание (ru): У Драгонуса меньше здоровья, но все его способности выпускают больше снарядов.
- Описание (en): Dragonus has less health but shoots additional projectiles with all of his abilities.
- Aghanim: нет в данных
- Атрибуты подсказки: TargetCount (Число снарядов); MaxHP (Макс. здоровье)
- Глобальные атрибуты малых улучшений: [COMMON] TargetCount = 1 (Число снарядов), MaxHP = -20 (Макс. здоровье)

### kez_innate — Слабое место / Spot Weakness
- ID 502; класс `CSurvivorsPowerUpDefinition_InstantAttack`; роль `innate`; максимальный ранг 1
- Описание (ru): Кез передвигается быстрее и периодически накладывает на самых сильных врагов рядом метку, делающую следующую физическую атаку по ним критической.
- Описание (en): Kez has bonus movement speed and will periodically mark the strongest nearby enemies, causing the next Physical attack to critically strike.
- Aghanim: нет в данных
- Базовые атрибуты: MovementSpeed = 20 (Скорость передвижения); TargetCount = 1 (Число снарядов); Damage_Magical = 0 (Магический урон); Cooldown = 6 (Перезарядка); Range = 1500 (Дальность); Radius = 180 (Радиус); VulnerabilityDamagePercent = 2 (Урон при уязвимости); VulnerabilityDuration = 5.5 (Длительность уязвимости); VulnerabilityStunOnHitDuration = 1 (Длит. оглушения при уязвимости)
- Атрибуты подсказки: MovementSpeed (Скорость передвижения); VulnerabilityDamagePercent (Урон при уязвимости); VulnerabilityDuration (Длительность уязвимости); VulnerabilityStunOnHitDuration (Длит. оглушения при уязвимости); Range (Дальность); Cooldown (Перезарядка); TargetCount (Число снарядов); Radius (Радиус)

## Служебные (2)

### gold — Мешочек с золотом / Bag of Gold
- ID 999; класс `CSurvivorsPowerUpDefinition`; роль `service:gold`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных

### pile_of_coal — Гора угля / Pile of Coal
- ID 1000; класс `CSurvivorsPowerUpDefinition`; роль `service:not-rollable`; максимальный ранг 1
- Описание: нет в данных
- Aghanim: нет в данных
