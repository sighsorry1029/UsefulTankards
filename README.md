# UsefulTankards
![](https://i.ibb.co/0V68tjjV/Screenshot-2026-07-04-130544.png) <br>
Turn tankards into practical adventuring gear: store meads inside them, drink from storage, move while drinking, tune animation speed, cooldown reduction, buff duration, durability, recipes, and ServerSync-backed settings. ValheimCuisine's Goblet of Kings is supported too.

![](https://i.ibb.co/1YspPyys/mead2.gif) <br>
Drink from a tankard without standing still. Movement and drinking speed are configurable.

![](https://i.ibb.co/zW76QByy/mead1.gif) <br>
Better tankards can extend buff duration and shorten potion cooldowns.

![](https://i.ibb.co/YFsjWBKM/Screenshot-2026-07-04-100927.png) <br>
Tankards are not repairable by default, making Dvergr tankards especially valuable. Repairability can be enabled per tankard if your setup provides a valid repair path.

![](https://i.ibb.co/TDqRBjrs/Screenshot-2026-07-04-121830.png) <br>
Tankards carry their own mead storage, and their tooltip shows both tankard bonuses and stored meads.

![](https://i.ibb.co/VG67S3g/Screenshot-2026-07-04-121941.png) <br>
Modded meads can be stored too. ValheimCuisine's Goblet of Kings is detected and configured automatically.

![](https://i.ibb.co/HLYWkHC8/Screenshot-2026-07-04-121817.png) <br>
When stored meads are currently usable, a tankard can drink them directly from its storage.
Use DataForge if you want to align custom mead effect durations: https://thunderstore.io/c/valheim/p/sighsorry/DataForge/

## What It Adds

- Tankards can store meads in their own small inventory.
- Stored meads count toward item weight and are shown in the tankard tooltip.
- Using a tankard drinks usable stored meads without needing them loose in your inventory.
- Tankard drinking can allow partial movement instead of fully locking the player in place.
- Tankard drinking animation speed can be tuned.
- Health, stamina, and eitr-over-time potions can get shorter cooldowns.
- Long-duration buffs can get longer durations.
- Tankards gain configurable durability so they can act like limited-use drinking tools.
- Tankard and Anniversary Tankard recipes can be configured.
- ServerSync keeps gameplay-affecting settings aligned on dedicated servers.

## Tankard Progression

Each supported tankard has its own settings for durability, repairability, potion cooldown reduction, buff duration bonus, and storage slots.

- `Tankard`: small storage and a modest bonus.
- `TankardAnniversary`: stronger than the basic tankard.
- `Tankard_dvergr`: the strongest vanilla tankard profile.
- `VC_GoK`: supported when ValheimCuisine is installed, using the Dvergr profile defaults.

## Storage

Press the interact key while hovering a tankard in your inventory to open its internal storage. Only drinkable consumables with a consume status effect can be placed inside. Stored meads are saved on change, so tooltip contents, dropped tankards, and item weight stay in sync.

When a tankard is used, UsefulTankards checks the stored meads and drinks any that can currently be consumed. Food rules, potion categories, and active status effect categories are respected.

## Potion And Buff Bonuses

UsefulTankards treats instant/over-time recovery effects differently from long-duration utility effects.

- Effects with health, stamina, or eitr over time are treated as potion-style effects and can receive cooldown reduction.
- Other timed effects are treated as buffs and can receive duration bonuses.

This makes tankards useful without turning every drink into the same kind of bonus.

## Configuration

Configuration is server-synced by default.

General options include:

- `Movement While Drinking`: 0 keeps the vanilla movement lock, 1 allows normal movement, and values in between allow partial movement.
- `Tankard Animation Speed`: 1 keeps vanilla speed, 2 is twice as fast, and 3 is three times as fast.

Per-tankard options include:

- durability
- repairability
- potion cooldown reduction
- buff duration bonus
- storage slots

Recipe options are available for:

- `Tankard`
- `TankardAnniversary`

## Compatibility

UsefulTankards is designed to stay lightweight and config-driven. ValheimCuisine's `VC_GoK` is detected automatically when ValheimCuisine is installed.

## Localization

UsefulTankards includes built-in localized tooltip text for the tankard storage prompt and tankard bonus lines.
