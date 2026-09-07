# Storage regression harness

Run from the repository root with a .NET SDK capable of targeting .NET 8:

```powershell
dotnet run --project Tests/StorageRegression.csproj --configuration Release
```

The console runner returns exit code 1 when any case fails. It has no NuGet package dependencies. Build outputs stay under `Tests/bin` and `Tests/obj`; the production .NET Framework project does not compile these files.

The project links the actual `../TankardStorage.cs`. It tests that source's parsing, storage registry, admission checks, item ownership checks, live-inventory reuse, serialization decisions, event cleanup, cache invalidation, and stack-all dispatch guards. It does not copy those policies into a second implementation.

Coverage includes:

- Inventory versions 100–106, quality-adjusted weight, and weight queries without `Inventory.Load`.
- Unsupported versions, malformed base64, truncated fields, negative counts, and incomplete game loads preserving the original serialized data when closed.
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

## Differential check used during this change

The same harness was run against commit `64f6f82`'s storage source and the updated source. The baseline compiled and reported **22 passed, 10 failed**; the updated source reported **32 passed, 0 failed**. Baseline failures included open-storage consumption being overwritten on close, an emptied storage being restored, repeated snapshot loading, authority/callback guards, unnecessary weight lookup, and the absent local stack-all entry point. These are behavior assertions against the actual source, not source-text matching.

To repeat the baseline check without modifying production files, after the normal run has created `Tests/obj`:

```powershell
git show 64f6f82:TankardStorage.cs | Set-Content -LiteralPath Tests/obj/BaselineStorage.cs -Encoding UTF8
dotnet run --project Tests/StorageRegression.csproj --configuration Release -p:StorageSource=obj/BaselineStorage.cs -p:OutputPath=bin/Baseline/ -p:IntermediateOutputPath=obj/Baseline/
```

The baseline run is expected to fail. Stack-all is invoked through reflection in the test so the pre-fix source still compiles and reports its absent entry point as a separate failure.

Still required in Valheim: client/host/dedicated-server startup and ownership checks; open, drink, move/drop, close, reconnect and reload inventory round trips; the real local and world-container stack-all actions; custom meads and missing optional mods; Harmony interaction and callback reentrancy; and Unity destruction/event cleanup during plugin shutdown.
