# Storage regression harness

Run from the repository root with a .NET SDK capable of targeting .NET 8:

```powershell
dotnet run --project Tests/StorageRegression.csproj --configuration Debug
```

The console runner returns exit code 1 when any case fails. It has no NuGet package dependencies. Build outputs stay under `Tests/bin` and `Tests/obj`; the production .NET Framework project does not compile these files.

The project links the actual `../TankardStorage.cs`. It tests that source's parsing, storage registry, admission checks, item ownership checks, live-inventory reuse, serialization decisions, event cleanup, cache invalidation, and stack-all dispatch guards. It does not copy those policies into a second implementation.

Coverage includes:

- Inventory version 109, compact flag-dependent fields, ushort counts, custom-data counts at 127/128, quality-adjusted weight, and weight queries without `Inventory.Load`.
- Unsupported versions, malformed base64, truncated fields, invalid stacks, excessive declared counts, trailing bytes, and incomplete game loads preserving the original serialized data when closed.
- Registry membership, null-owner re-registration, ammo policy, removal on close, and immediate-save handler detachment.
- Consuming from an open storage inventory and closing it without restoring consumed items, including consuming the final item.
- Closed-storage consumption, failed consumption, non-owner actors, and tankards outside the actor's inventory.
- Immediate-save callbacks removing the tankard, changing ownership, or removing another candidate during a multi-drink use.
- Same-frame availability invalidation, tooltip reuse, invariant weight caches, invalid caches, and missing-prefab weight handling.
- Routing local stack-all only for the active, open, completely loaded, owned tankard UI; ordinary containers remain unhandled.

## Boundary and limitations

`GameDoubles.cs` supplies only the game members used by the linked source. Binary fixtures encode inventory field layouts, and a fixture registry prescribes what game `Inventory.Load` returns. A partial-load case deliberately prescribes fewer items. The fixture writer acts as game `Inventory.Save`, making the production code's saved result observable. This verifies the mod's response to those game outcomes; it does **not** validate the real Valheim serializer, all historical save files, prefab construction, or ItemDataManager transfer behavior.

The doubles use managed object references, synchronous inventory events, and explicit `CloseAndDestroy` calls. They do not model Unity's destroyed-object null operator, native components, deferred destruction, `Update` scheduling, Harmony patch application/order, actual `InventoryGui.StackAll` item movement, drag state, RPCs, or network authority. `Player.IsOwner` is an input controlled by each test, not a network simulation. Consumption models the relevant order (status effect application before item removal and the Changed event), not Valheim's full food/status-effect rules.

`TankardTweaks`, `ValheimAccess`, logging, and the Unity/game types are adapters here; their real implementations are covered by the production build and require in-game integration checks. In particular, passing this harness is not evidence that the Harmony attack patch selects the right actor, that other mods' prefixes/finalizers cooperate, or that all remote clients reject an unauthorized request.

The harness targets .NET 8 while production targets .NET Framework 4.8. `CS8600` is suppressed here because .NET 8 adds nullable annotations to dictionary `TryGetValue` that the production target lacks. The suppression does not change production build settings.

## Original game contract checks

After the production Debug build, export the regression fixtures and run the .NET 9 / Mono.Cecil checker:

```powershell
dotnet run --project Tests/StorageRegression.csproj -c Debug -- Tests/obj/v109-fixtures.json
dotnet run --project Tests/GameContracts.csproj -c Debug -p:BaseIntermediateOutputPath=obj/GameContracts/ -p:BaseOutputPath=bin/GameContracts/ -- bin/Debug/UsefulTankards.dll 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed' Tests/obj/v109-fixtures.json
```

Supply the preserved original dedicated-server Managed directory as the second argument to repeat against the server API. Keep the intermediate/output overrides: the two test projects share this directory.

This checks all merged game/Unity instruction references, detects field instructions against constants, resolves 30 UsefulTankards Harmony targets and their named arguments/result/field injections, and checks 15 cached reflection contracts. It also invokes original `Inventory.Changed(false, false)` on an empty managed inventory and decodes the exported fixtures through original `ItemData.Load`. This independently checks the fixture layout against the real game reader, including prefab hashes, stacks, qualities, custom data, cheated flags and full byte consumption. The stored-item loader itself remains prescribed in the storage harness.

2026-09-09 results: **43 storage tests passed**; **1,444 instruction references, 30 Harmony targets, 15 reflection contracts and 12 original-reader fixtures passed** for both Valheim 1.0.7 client/server originals. The private Changed invocation notified its callback exactly once. These are managed-code/metadata tests, not Harmony installation or actual Unity/Mono gameplay tests.

Still required in Valheim: fresh plugin initialization; client/host/dedicated-server config synchronization and ownership checks; open, drink, drag/stack/move/drop, close and reconnect/save-load round trips; optional ValheimCuisine/custom meads; missing prefabs; animation restoration; and shutdown/event cleanup. Existing embedded storage versions other than 109 are intentionally rejected without rewriting; no legacy migration is tested or provided.

## EpicLoot input compatibility (2026-09-14)

The storage harness now passes **56 tests**, including 13 input/transition cases. They exercise the production input handler with prescribed external UI state: opening and closing, input consumption, hidden UI, ordinary items, disabled storage, ownership/membership, world containers, no input, EpicLoot veto with storage open/closed, and socket close callbacks that succeed, leave the overlay open, or remove the tankard. The doubles do not execute EpicLoot's prompt/modifier getters, socket serialization, or Harmony ordering.

Pass the supplied `EpicLoot.dll` as a fourth argument to `GameContracts` (after the fixture JSON) to check the two static bool getters, configured modifier field, relative Update patch priority, and absence of a hard EpicLoot/Newtonsoft assembly reference. These are original metadata checks, not execution of the optional accessors in Unity/Mono.

Validated against EpicLoot's BepInPlugin version **0.14.5** (DLL SHA-256 `9A0B06F3E05E8880F985AD68158D847750DA5F73EAEDEFABE5E047CE7ABEF103`). Its assembly file version still reads 0.13.0. The final Debug DLL passed **1,469 instruction references, 30 Harmony targets, 15 game reflection contracts, 3 EpicLoot reflection contracts, and 12 original-reader fixtures** against Valheim **1.0.12** client build 25253764 and dedicated-server build 25253791 originals. Production build: zero warnings/errors; deployed to the default Steam game's BepInEx/plugins directory with matching SHA-256. No Release package was generated for this compatibility patch.

Still required in the actual game: EpicLoot absent/present; Keep Inventory Open Over Items true/false; ordinary and socketed tankards; configured modifier changes; confirm prompts; controller socket interactions; drag/stack/consume and both UI transition directions; mead counts and socket data after close/reconnect; host/client ownership and dedicated-server startup.

### Follow-up: actual Harmony attribute processing

The first compatibility build failed in the user's game. Its class-level priority of 801 passed the original Cecil attribute check, but the installed HarmonyX `HarmonyMethodExtensions.Merge(master, detail)` leaves priority unset when the method's priority is -1, losing the class-level value. Processing the actual DLLs with `Harmony.CreateClassProcessor` reproduced effective priority **400 for both EpicLoot and the first UsefulTankards patch**. With EpicLoot loaded first, the order was not corrected.

Priority and `HarmonyBefore` now live on the Prefix method itself. Run the installed Harmony's class processor under Windows PowerShell (.NET Framework), without applying detours or executing Unity/game methods:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests/HarmonyInputOrder.ps1 -EpicLootDll C:/path/to/EpicLoot.dll -FishingDll C:/path/to/TrollingFishing.dll
```

This test failed on the earlier DLL and passes on the corrected DLLs: UsefulTankards **801**, TrollingFishing **801**, EpicLoot **400**. The original-metadata checker also now requires a method-level priority. The Debug DLLs were deployed via MSBuild to both the default Steam plugins directory and the supplied Gale profile's existing mod directories, with matching SHA-256. Startup logs identify the method-priority compatibility build. Release 1.0.6 was packaged after the same regression, contract, and effective-order checks. Actual gameplay after the final color-tag replacement has not been run by the agent.
