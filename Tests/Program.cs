using System.Globalization;
using UsefulTankards;
using UnityEngine;

internal static class Program
{
    internal const string DataKey = "UsefulTankards.Storage.Data";
    internal const string WeightKey = "UsefulTankards.Storage.Weight";
    private static int _passed, _failed;

    private static int Main(string[] args)
    {
        foreach (int version in new[] { 109, 110 })
        foreach (int stack in new[] { 1, 3, 65535 })
        {
            Run($"v{version} stack {stack}: summary weight and complete inventory load", f =>
            {
                f.Mead.m_quality = stack == 1 ? 1 : 2;
                f.Mead.m_stack = stack;
                f.SetData(version, f.Mead);
                Equal(f.Mead.GetWeight(), TankardStorageSystem.GetStoredDrinkWeight(f.Tankard), "weight");
                Equal(0, Inventory.LoadCalls, "weight lookup must not instantiate/load items");
                var opened = f.Open();
                Check(opened.Storage.LoadComplete, "supported fixture must load");
                Equal(stack, opened.Container.Inventory.GetAllItems().Single().m_stack, "stack survives load");
                Check(TankardStorageSystem.IsTankardStorageInventory(opened.Container.Inventory), "loaded storage registers");
            });
        }

        foreach (int version in new[] { 109, 110 })
        foreach (bool cheated in new[] { false, true })
        {
            Run($"v{version} cheated={cheated}: load, save110 and reopen", f =>
            {
                SetOptionalMetadata(f.Mead);
                f.Mead.m_cheated = cheated;
                f.Mead.m_customData["metadata"] = "original";
                f.SetData(version, f.Mead);
                var opened = f.Open();
                Check(opened.Storage.LoadComplete, "supported clean-slate load succeeds");
                bool expected = version == 110 && cheated;
                Equal(expected, opened.Container.Inventory.GetAllItems().Single().m_cheated, "game cheated interpretation");
                opened.Storage.CloseAndDestroy();
                Equal(110, new ZPackage(f.Tankard.m_customData[DataKey]).ReadInt(), "save uses current game format");
                var reopened = f.Open();
                Check(reopened.Storage.LoadComplete, "saved v110 reopens");
                var item = reopened.Container.Inventory.GetAllItems().Single();
                Equal(expected, item.m_cheated, "resave preserves interpreted cheated state");
                Equal(f.Mead.m_stack, item.m_stack, "resave preserves stack");
                Equal("original", item.m_customData["metadata"], "resave preserves metadata");
            });
        }

        Run("summary validation does not resolve unused weights", f =>
        {
            f.SetData(109, f.Mead);
            var opened = f.Open();
            Check(opened.Storage.LoadComplete, "load succeeds");
            Equal(0, ObjectDB.instance!.LookupCount, "validation discarded weight, so no prefab lookup is needed");
        });

        foreach (int version in new[] { 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 111 })
        {
            Run($"unsupported version {version}: raw data survives close", f =>
                AssertRejectedWithoutRewrite(f, InventoryFixtures.EncodeHeader(version, 1)));
        }
        Run("non-base64 data survives close", f => AssertRejectedWithoutRewrite(f, "this is not base64!"));
        Run("truncated item data survives close", f =>
        {
            string valid = InventoryFixtures.Encode(109, new[] { f.Mead });
            byte[] bytes = Convert.FromBase64String(valid);
            AssertRejectedWithoutRewrite(f, Convert.ToBase64String(bytes[..^2]));
        });
        Run("excessive declared item count survives close", f => AssertRejectedWithoutRewrite(f, InventoryFixtures.EncodeHeader(109, 65535)));
        Run("truncated custom-data count survives close", f =>
        {
            ZPackage package = new();
            f.Mead.m_customData["key"] = "value";
            InventoryFixtures.WriteHeader(package.Writer, 109, 1);
            InventoryFixtures.WriteItemPrefix(package.Writer, 109, f.Mead);
            package.Writer.Write((byte)128);
            AssertRejectedWithoutRewrite(f, package.GetBase64());
        });
        foreach (int customCount in new[] { 1, 127, 128, 256 })
        {
            Run($"v109 custom data count {customCount} and optional fields", f =>
            {
                SetOptionalMetadata(f.Mead);
                for (int i = 0; i < customCount; i++) f.Mead.m_customData[$"key{i}"] = $"value{i}";
                f.SetData(109, f.Mead, f.OtherMead());
                Equal(f.Mead.GetWeight(), TankardStorageSystem.GetStoredDrinkWeight(f.Tankard), "weight across variable-length fields");
                Check(f.Open().Storage.LoadComplete, "both entries fully read");
            });
        }
        Run("trailing bytes survive close", f =>
        {
            byte[] bytes = Convert.FromBase64String(InventoryFixtures.Encode(109, new[] { f.Mead }));
            AssertRejectedWithoutRewrite(f, Convert.ToBase64String(bytes.Concat(new byte[] { 1 }).ToArray()));
        });
        Run("zero stack survives close", f =>
        {
            f.Mead.m_stack = 0;
            AssertRejectedWithoutRewrite(f, InventoryFixtures.Encode(109, new[] { f.Mead }));
        });
        Run("missing prefab: partial load cannot replace raw data", f =>
        {
            ItemDrop.ItemData missing = f.Mead.Clone();
            missing.m_dropPrefab = new GameObject("removed_mod_mead");
            missing.m_gridPos = new Vector2i(1, 0);
            f.SetData(109, f.Mead, missing);
            string raw = f.Tankard.m_customData[DataKey];
            InventoryFixtures.LoadedItems[raw] = new[] { f.Mead };
            var opened = f.Open();
            Check(!opened.Storage.LoadComplete, "partial game load must be rejected");
            Equal(1, Inventory.LoadCalls, "missing prefab scenario reaches the game loader");
            Check(!TankardStorageSystem.IsTankardStorageInventory(opened.Container.Inventory), "partial inventory is not registered");
            opened.Storage.CloseAndDestroy();
            Equal(raw, f.Tankard.m_customData[DataKey], "partial data must not overwrite original");
        });
        Run("duplicate serialized grid positions are rejected before loading", f =>
        {
            ItemDrop.ItemData duplicate = f.OtherMead();
            duplicate.m_gridPos = f.Mead.m_gridPos;
            AssertRejectedWithoutRewrite(f, InventoryFixtures.Encode(109, new[] { f.Mead, duplicate }));
        });

        foreach (var change in new (string Name, Action<ItemDrop.ItemData> Apply)[]
        {
            ("prefab", item => item.m_dropPrefab = new GameObject("different_mead")),
            ("stack", item => item.m_stack++),
            ("quality", item => item.m_quality++),
            ("variant", item => item.m_variant++),
            ("crafter ID", item => item.m_crafterID++),
            ("crafter name", item => item.m_crafterName += " changed"),
            ("durability", item => item.m_durability += 0.25f),
            ("world level", item => item.m_worldLevel++),
            ("equipped", item => item.m_equipped = !item.m_equipped),
            ("picked up", item => item.m_pickedUp = !item.m_pickedUp),
            ("cleared cheated flag", item => item.m_cheated = false),
            ("grid coordinate", item => item.m_gridPos = new Vector2i(2, 0)),
            ("missing custom data", item => item.m_customData.Remove("metadata")),
            ("changed custom data", item => item.m_customData["metadata"] = "changed"),
            ("added custom data", item => item.m_customData["unexpected"] = "new"),
            ("case changed custom-data key", item =>
            {
                item.m_customData.Remove("metadata");
                item.m_customData["Metadata"] = "original";
            }),
            ("case changed custom-data value", item => item.m_customData["metadata"] = "Original"),
            ("case-insensitive loaded dictionary with changed key", item =>
                item.m_customData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Metadata"] = "original" })
        })
        {
            Run($"changed {change.Name}: load cannot overwrite original", f =>
            {
                SetOptionalMetadata(f.Mead);
                f.Mead.m_customData["metadata"] = "original";
                f.SetData(110, f.Mead);
                ItemDrop.ItemData changed = InventoryFixtures.LoadedItems[f.Tankard.m_customData[DataKey]].Single().Clone();
                change.Apply(changed);
                AssertLoadedMismatchPreservesData(f, changed);
            });
        }
        Run("equal total with redistributed stacks cannot overwrite original", f =>
        {
            ItemDrop.ItemData second = f.OtherMead();
            second.m_stack = 2;
            f.SetData(109, f.Mead, second);
            ItemDrop.ItemData[] changed = InventoryFixtures.LoadedItems[f.Tankard.m_customData[DataKey]].Select(item => item.Clone()).ToArray();
            changed[0].m_stack--;
            changed[1].m_stack++;
            AssertLoadedMismatchPreservesData(f, changed);
        });
        Run("duplicate loaded grid position cannot replace a distinct saved slot", f =>
        {
            f.SetData(109, f.Mead, f.OtherMead());
            ItemDrop.ItemData[] changed = InventoryFixtures.LoadedItems[f.Tankard.m_customData[DataKey]].Select(item => item.Clone()).ToArray();
            changed[1].m_gridPos = changed[0].m_gridPos;
            AssertLoadedMismatchPreservesData(f, changed);
        });
        foreach (bool empty in new[] { false, true })
        {
            Run($"unexpected extra loaded item with original empty={empty} is rejected", f =>
            {
                f.SetData(109, empty ? Array.Empty<ItemDrop.ItemData>() : new[] { f.Mead });
                ItemDrop.ItemData[] changed = InventoryFixtures.LoadedItems[f.Tankard.m_customData[DataKey]]
                    .Concat(new[] { f.OtherMead() }).ToArray();
                AssertLoadedMismatchPreservesData(f, changed);
            });
        }
        Run("reordered items and custom-data entries retain their identity", f =>
        {
            SetOptionalMetadata(f.Mead);
            f.Mead.m_customData["first"] = "a";
            f.Mead.m_customData["second"] = "b";
            f.SetData(109, f.Mead, f.OtherMead());
            string raw = f.Tankard.m_customData[DataKey];
            ItemDrop.ItemData[] reordered = InventoryFixtures.LoadedItems[raw].Reverse().Select(item => item.Clone()).ToArray();
            foreach (ItemDrop.ItemData item in reordered)
                item.m_customData = item.m_customData.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            InventoryFixtures.LoadedItems[raw] = reordered;
            Check(f.Open().Storage.LoadComplete, "list and dictionary order are not persisted item identity");
        });
        Run("durability quantization and unserialized crafter name are accepted", f =>
        {
            f.Mead.m_durability = 0.179f;
            f.Mead.m_crafterID = 0;
            f.Mead.m_crafterName = "name omitted by the game serializer";
            f.SetData(109, f.Mead);
            Check(f.Open().Storage.LoadComplete, "normalized serialized values are compared instead of pre-save fields");
        });
        Run("game-promoted cheated flag preserves the load", f =>
        {
            f.SetData(109, f.Mead);
            InventoryFixtures.LoadedItems[f.Tankard.m_customData[DataKey]].Single().m_cheated = true;
            Check(f.Open().Storage.LoadComplete, "vanilla loader can promote the flag after a damage check");
        });
        Run("metadata mismatch is not consumable or shown as verified contents", f =>
        {
            f.Mead.m_customData["metadata"] = "original";
            f.SetData(109, f.Mead);
            string raw = f.Tankard.m_customData[DataKey];
            InventoryFixtures.LoadedItems[raw].Single().m_customData.Clear();
            f.Tankard.m_customData[WeightKey] = "12.5";
            Check(!TankardStorageSystem.HasConsumableStoredDrink(f.Player, f.Tankard, f.Profile), "incomplete contents cannot be reported available");
            Check(!TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "incomplete contents cannot be consumed");
            Equal(0, f.Player.ConsumeAttempts, "no game consumption attempted");
            Equal(0, TankardStorageSystem.GetStoredDrinkTooltipLines(f.Tankard).Count, "unverified contents are not displayed");
            Equal(raw, f.Tankard.m_customData[DataKey], "queries preserve raw contents");
            Equal("12.5", f.Tankard.m_customData[WeightKey], "queries preserve weight");
        });
        Run("opening a metadata mismatch reports failure without showing storage", f =>
        {
            f.Mead.m_customData["metadata"] = "original";
            f.SetData(109, f.Mead);
            string raw = f.Tankard.m_customData[DataKey];
            InventoryFixtures.LoadedItems[raw].Single().m_customData.Clear();
            InventoryGui.instance.Grid.Hovered = f.Tankard;
            ZInput.UseDown = true;
            Check(TankardStorageSystem.TryHandleInventoryGuiUseInput(InventoryGui.instance), "Use handled by the owned tankard");
            Check(InventoryGui.instance.Current == null, "incomplete storage never shown");
            Equal(1, f.Player.Messages.Count, "one visible failure message");
            Equal(raw, f.Tankard.m_customData[DataKey], "failed UI opening preserves original");
        });

        Run("registry controls admissions and unregisters on close", f =>
        {
            f.SetData(109, f.Mead);
            var opened = f.Open();
            Inventory inventory = opened.Container.Inventory;
            Check(TankardStorageSystem.CanAddToTankardStorage(inventory, f.Mead), "matching mead allowed");
            ItemDrop.ItemData wrongAmmo = f.Mead.Clone();
            wrongAmmo.m_dropPrefab = new GameObject("arrows");
            wrongAmmo.m_shared = new() { m_itemType = ItemDrop.ItemData.ItemType.Consumable,
                m_consumeStatusEffect = f.Mead.m_shared.m_consumeStatusEffect, m_ammoType = "arrows" };
            Check(!TankardStorageSystem.CanAddToTankardStorage(inventory, wrongAmmo), "wrong ammo is rejected");
            Check(!TankardStorageSystem.CanAddToTankardStorage(inventory, f.Tankard), "nested tankard is rejected");
            opened.Storage.CloseAndDestroy();
            Check(!TankardStorageSystem.IsTankardStorageInventory(inventory), "registry membership removed");
            Check(TankardStorageSystem.CanAddToTankardStorage(inventory, wrongAmmo), "ordinary inventory passes through");
            string saved = f.Tankard.m_customData[DataKey];
            inventory.GetAllItems().Clear();
            ValheimAccess.Changed(inventory);
            opened.Storage.Save();
            opened.Storage.CloseAndDestroy();
            Equal(saved, f.Tankard.m_customData[DataKey], "close detaches event and later calls are harmless");
        });
        Run("registry null-owner registration retains its original owner", f =>
        {
            Inventory inventory = new("detached", null, 3, 1);
            var register = typeof(TankardStorageSystem).GetMethod("RegisterTankardStorageInventory",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var unregister = typeof(TankardStorageSystem).GetMethod("UnregisterTankardStorageInventory",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            try
            {
                register.Invoke(null, new object?[] { inventory, null });
                Check(TankardStorageSystem.IsTankardStorageInventory(inventory), "null-owner inventory retains membership");
                register.Invoke(null, new object?[] { inventory, f.Tankard });
                register.Invoke(null, new object?[] { inventory, null });
                ItemDrop.ItemData foreign = f.OtherMead();
                foreign.m_shared.m_ammoType = "arrows";
                Check(!TankardStorageSystem.CanAddToTankardStorage(inventory, foreign), "null re-registration cannot erase ammo policy owner");
            }
            finally { unregister.Invoke(null, new object[] { inventory }); }
            Check(!TankardStorageSystem.IsTankardStorageInventory(inventory), "membership removed");
        });

        Run("open consumption survives later UI close", f =>
        {
            f.SetData(109, f.Mead);
            var opened = f.Open();
            Equal(1, Inventory.LoadCalls, "one initial load");
            Check(TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out var ammo), "drink consumed");
            Equal(3, ammo.m_stack, "ammo snapshot preserves pre-consumption item");
            int liveStack = opened.Container.Inventory.GetAllItems().Single().m_stack;
            string afterConsume = f.Tankard.m_customData[DataKey];
            opened.Storage.CloseAndDestroy();
            Check(afterConsume == f.Tankard.m_customData[DataKey], "closing must not resurrect a consumed mead");
            Equal(2, liveStack, "same open inventory is mutated");
            Equal(2, InventoryFixtures.LoadedItems[afterConsume].Single().m_stack, "persisted stack decremented exactly once");
            Equal(1, Inventory.LoadCalls, "consumption reuses loaded inventory");
        });
        Run("last open mead stays removed after close", f =>
        {
            f.Mead.m_stack = 1;
            f.SetData(109, f.Mead);
            var opened = f.Open();
            Check(TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "last drink consumed");
            opened.Storage.CloseAndDestroy();
            Check(!f.Tankard.m_customData.ContainsKey(DataKey), "empty data key removed");
            Check(!f.Tankard.m_customData.ContainsKey(WeightKey), "empty weight key removed");
        });
        Run("open tooltip and availability reuse inventory", f =>
        {
            f.SetData(109, f.Mead);
            f.Open();
            Check(TankardStorageSystem.HasConsumableStoredDrink(f.Player, f.Tankard, f.Profile), "available");
            Check(TankardStorageSystem.GetStoredDrinkTooltipLines(f.Tankard).Single().Contains("3/10", StringComparison.Ordinal), "tooltip uses stack");
            Equal(1, Inventory.LoadCalls, "read-only open queries need no snapshot load");
        });
        Run("closed consumption persists exactly one decrement", f =>
        {
            f.SetData(109, f.Mead);
            Check(TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "closed drink consumed");
            Equal(2, InventoryFixtures.LoadedItems[f.Tankard.m_customData[DataKey]].Single().m_stack, "closed storage persists");
        });
        Run("failed consume keeps serialized data", f =>
        {
            f.SetData(109, f.Mead);
            string raw = f.Tankard.m_customData[DataKey];
            f.Player.AllowConsumption = false;
            Check(!TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "failed consume reports false");
            Equal(raw, f.Tankard.m_customData[DataKey], "failed consume does not write storage");
        });
        Run("non-owner cannot consume stored items", f =>
        {
            f.SetData(109, f.Mead);
            string raw = f.Tankard.m_customData[DataKey];
            f.Player.Owner = false;
            Check(!TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "non-owner rejected");
            Equal(raw, f.Tankard.m_customData[DataKey], "non-owner cannot mutate raw data");
        });
        Run("tankard removed from actor inventory cannot be consumed", f =>
        {
            f.SetData(109, f.Mead);
            f.Player.GetInventory().GetAllItems().Remove(f.Tankard);
            Check(!TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "foreign item rejected");
            Check(!TankardStorageSystem.HasConsumableStoredDrink(f.Player, f.Tankard, f.Profile), "foreign item is not available");
            Equal(0, Inventory.LoadCalls, "ownership rejected before materialization");
        });
        Run("immediate save invalidates same-frame availability", f =>
        {
            f.SetData(109, f.Mead);
            var opened = f.Open();
            Check(TankardStorageSystem.HasConsumableStoredDrink(f.Player, f.Tankard, f.Profile), "available initially");
            opened.Container.Inventory.GetAllItems().Clear();
            ValheimAccess.Changed(opened.Container.Inventory);
            Check(!TankardStorageSystem.HasConsumableStoredDrink(f.Player, f.Tankard, f.Profile), "same-frame cached true invalidated");
        });
        foreach (bool removeTankard in new[] { true, false })
        {
            Run(removeTankard ? "callback removes tankard: remaining drinks are not consumed" : "callback changes owner: remaining drinks are not consumed", f =>
            {
                ItemDrop.ItemData second = f.OtherMead();
                f.Mead.m_stack = second.m_stack = 1;
                f.SetData(109, f.Mead, second);
                var opened = f.Open();
                ValheimAccess.AddInventoryChangedHandler(opened.Container.Inventory, () =>
                {
                    if (removeTankard) f.Player.GetInventory().GetAllItems().Remove(f.Tankard);
                    else f.Player.Owner = false;
                });
                Check(TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "first drink consumed");
                Equal(1, f.Player.ConsumeAttempts, "authority loss blocks the next consume call");
                Equal(1, opened.Container.Inventory.GetAllItems().Count, "remaining drink kept");
            });
        }
        Run("callback removes another candidate: no stale item consume call", f =>
        {
            ItemDrop.ItemData second = f.OtherMead();
            f.Mead.m_stack = second.m_stack = 1;
            f.SetData(109, f.Mead, second);
            var opened = f.Open();
            ValheimAccess.AddInventoryChangedHandler(opened.Container.Inventory, () => opened.Container.Inventory.GetAllItems().Clear());
            Check(TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "first drink consumed");
            Equal(1, f.Player.ConsumeAttempts, "callback-removed candidate is skipped before game consume");
        });
        Run("stack-all routes only the active owned tankard UI", f =>
        {
            f.SetData(109, f.Mead);
            var opened = f.Open();
            Check(TryStackAll(opened.Container), "local tankard request is handled");
            Equal(1, InventoryGui.instance.StackAllCalls, "active owned tankard routes to GUI once");
            f.Player.Owner = false;
            Check(TryStackAll(opened.Container), "non-owner tankard request stays intercepted");
            Equal(1, InventoryGui.instance.StackAllCalls, "non-owner does not reach GUI");
            f.Player.Owner = true;
            InventoryGui.instance.Current = null;
            Check(TryStackAll(opened.Container), "inactive tankard request stays intercepted");
            Equal(1, InventoryGui.instance.StackAllCalls, "wrong GUI container does not route");
            InventoryGui.instance.Current = opened.Container;
            f.Player.GetInventory().GetAllItems().Remove(f.Tankard);
            Check(TryStackAll(opened.Container), "removed tankard request stays intercepted");
            Equal(1, InventoryGui.instance.StackAllCalls, "removed tankard does not route");
            f.Player.GetInventory().GetAllItems().Add(f.Tankard);
            Player.m_localPlayer = null;
            Check(TryStackAll(opened.Container), "non-local actor stays intercepted");
            Equal(1, InventoryGui.instance.StackAllCalls, "non-local actor cannot use local UI");
            Player.m_localPlayer = f.Player;
            opened.Storage.CloseAndDestroy();
            Check(TryStackAll(opened.Container), "closed tankard request stays intercepted");
            Equal(1, InventoryGui.instance.StackAllCalls, "closed storage does not route");
            f.Tankard.m_customData[DataKey] = "malformed";
            var incomplete = f.Open();
            Check(!incomplete.Storage.LoadComplete, "unverifiable storage stays incomplete");
            Check(TryStackAll(incomplete.Container), "incomplete tankard request stays intercepted");
            Equal(1, InventoryGui.instance.StackAllCalls, "incomplete storage does not route");
            Container world = new GameObject("world_container").AddComponent<Container>();
            Check(!TryStackAll(world), "ordinary world container falls through");
        });
        Run("cached weight is invariant and does not load inventory", f =>
        {
            f.SetData(109, f.Mead);
            f.Tankard.m_customData[WeightKey] = "1.25";
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Equal(1.25f, TankardStorageSystem.GetStoredDrinkWeight(f.Tankard), "invariant cached float");
            }
            finally { CultureInfo.CurrentCulture = previous; }
            Equal(0, Inventory.LoadCalls, "cache read does not load inventory");
            Equal(0, ObjectDB.instance!.LookupCount, "cache read does not resolve prefabs");
        });
        Run("invalid cached weight migrates without instantiating items", f =>
        {
            f.SetData(109, f.Mead);
            f.Tankard.m_customData[WeightKey] = "NaN";
            Equal(f.Mead.GetWeight(), TankardStorageSystem.GetStoredDrinkWeight(f.Tankard), "invalid cache recomputed");
            Equal(0, Inventory.LoadCalls, "migration does not load inventory");
        });
        Run("missing prefab leaves incomplete weight uncached", f =>
        {
            f.SetData(109, f.Mead);
            ObjectDB.instance!.Prefabs.Clear();
            Equal(0f, TankardStorageSystem.GetStoredDrinkWeight(f.Tankard), "unknown item cannot supply weight");
            Check(!f.Tankard.m_customData.ContainsKey(WeightKey), "partial weight must not be cached");
            Check(f.Tankard.m_customData.ContainsKey(DataKey), "weight query preserves data");
        });

        Run("Use opens owned tankard and consumes input before another prefix", f =>
        {
            InventoryGui.instance.Grid.Hovered = f.Tankard;
            ZInput.UseDown = true;
            Check(TankardStorageSystem.TryHandleInventoryGuiUseInput(InventoryGui.instance), "tankard handled");
            Check(!ZInput.GetButtonDown("Use"), "later EpicLoot prefix cannot open sockets or consume again");
            Check(TankardStorageSystem.IsTankardStorageContainer(InventoryGui.instance.Current), "storage opened");
            ZInput.UseDown = true;
            Check(TankardStorageSystem.TryHandleInventoryGuiUseInput(InventoryGui.instance), "second Use closes");
            Check(InventoryGui.instance.Current == null, "storage closed");
        });
        foreach (string boundary in new[] { "hidden", "ordinary item", "disabled storage", "non-owner", "removed tankard", "world container", "no input" })
        {
            Run($"Use leaves {boundary} to existing handlers", f =>
            {
                var gui = InventoryGui.instance;
                gui.Grid.Hovered = f.Tankard;
                ZInput.UseDown = true;
                switch (boundary)
                {
                    case "hidden": gui.Visible = false; break;
                    case "ordinary item": gui.Grid.Hovered = f.Mead; break;
                    case "disabled storage": f.Profile.TankardStorageSlots = 0; break;
                    case "non-owner": f.Player.Owner = false; break;
                    case "removed tankard": f.Player.GetInventory().RemoveItem(f.Tankard, 1); break;
                    case "world container": gui.Current = new GameObject("chest").AddComponent<Container>(); break;
                    case "no input": ZInput.UseDown = false; break;
                }
                var original = gui.Current;
                Check(!TankardStorageSystem.TryHandleInventoryGuiUseInput(gui), "input not claimed");
                Check(gui.Current == original, "existing UI preserved");
                Equal(boundary != "no input", ZInput.UseDown, "input unchanged");
            });
        }
        foreach (bool open in new[] { false, true })
        foreach (bool prompt in new[] { false, true })
        {
            Run($"EpicLoot GUI shortcut/prompt={prompt} veto preserves input with tankard open={open}", f =>
            {
                EnableEpicLoot();
                if (open) f.Open();
                var gui = InventoryGui.instance;
                gui.Grid.Hovered = f.Tankard;
                var original = gui.Current;
                EpicLoot.InventoryPromptHost.Open = prompt;
                EpicLoot.ShardStones.SocketOverlayInput.Press = !prompt;
                ZInput.UseDown = true;
                Check(!TankardStorageSystem.TryHandleInventoryGuiUseInput(gui), "EpicLoot retains input");
                Check(ZInput.UseDown && gui.Current == original && gui.CloseCalls == 0, "no consumption or UI change");
                if (!prompt) Check(ReferenceEquals(gui, EpicLoot.ShardStones.SocketOverlayInput.LastGui), "actual GUI passed to shortcut overload");
            });
        }
        Run("independent shard panel does not force a container close", f =>
        {
            EnableEpicLoot();
            var gui = InventoryGui.instance;
            gui.Grid.Hovered = f.Tankard;
            gui.OnClose = () => throw new InvalidOperationException("Unexpected legacy socket container transition");
            ZInput.UseDown = true;
            Check(TankardStorageSystem.TryHandleInventoryGuiUseInput(gui), "tankard opens on ordinary Use");
            Equal(0, gui.CloseCalls, "no synthetic socket close");
        });
        Run("registered tankard predicate reads live storage configuration", f =>
        {
            EnableEpicLoot();
            var predicate = EpicLoot.API.Providers[UsefulTankardsPlugin.ModGuid];
            Check(predicate(f.Tankard) && !predicate(f.Mead) && !predicate(null!), "only configured tankards registered");
            f.Profile.TankardStorageSlots = 0;
            Check(!predicate(f.Tankard), "disabled storage stops claiming bare Use");
            f.Profile.TankardStorageSlots = 4;
            Check(predicate(f.Tankard), "re-enabled storage needs no registration rebuild");
        });
        Run("existing same-ID registration is preserved on initialization rejection", f =>
        {
            Func<ItemDrop.ItemData, bool> prior = _ => true;
            EpicLoot.API.Providers[UsefulTankardsPlugin.ModGuid] = prior;
            EnableEpicLoot();
            Check(EpicLootStorageCompatibility.ShouldYieldUse(InventoryGui.instance), "input integration stopped");
            Check(ReferenceEquals(prior, EpicLoot.API.Providers[UsefulTankardsPlugin.ModGuid]), "prior registration preserved");
            Equal(0, EpicLoot.API.RegisterCalls, "duplicate precheck avoids registration side effects");
            Equal(0, EpicLoot.API.UnregisterCalls, "no unowned removal");
        });
        Run("rejected registration does not unregister an unowned provider", f =>
        {
            EpicLoot.API.Reject = true;
            EnableEpicLoot();
            Check(EpicLootStorageCompatibility.ShouldYieldUse(InventoryGui.instance), "rejected registration disables integration");
            Equal(0, EpicLoot.API.UnregisterCalls, "false return is not owned registration");
        });
        Run("missing registry category prevents an unverifiable registration", f =>
        {
            EpicLoot.API.MissingCategory = true;
            EnableEpicLoot();
            Check(EpicLootStorageCompatibility.ShouldYieldUse(InventoryGui.instance), "missing registry category yields input");
            Equal(0, EpicLoot.API.RegisterCalls, "no registration without ownership precheck");
        });
        Run("registration event exception rolls back only our inserted provider", f =>
        {
            EpicLoot.API.Providers["other.mod"] = _ => true;
            EpicLoot.API.ThrowAfterRegister = true;
            EnableEpicLoot();
            Check(EpicLootStorageCompatibility.ShouldYieldUse(InventoryGui.instance), "failed registration is not treated as enabled");
            Check(!EpicLoot.API.Providers.ContainsKey(UsefulTankardsPlugin.ModGuid) && EpicLoot.API.Providers.ContainsKey("other.mod"), "rollback is scoped");
        });
        foreach (bool prompt in new[] { false, true })
        Run($"delegate exception stops integration and unregisters container: prompt={prompt}", f =>
        {
            EnableEpicLoot();
            var predicate = EpicLoot.API.Providers[UsefulTankardsPlugin.ModGuid];
            EpicLoot.InventoryPromptHost.Throw = prompt;
            EpicLoot.ShardStones.SocketOverlayInput.Throw = !prompt;
            Check(EpicLootStorageCompatibility.ShouldYieldUse(InventoryGui.instance), "failed delegate yields input");
            Check(!predicate(f.Tankard) && !EpicLoot.API.Providers.ContainsKey(UsefulTankardsPlugin.ModGuid), "disabled callback cannot claim bare Use");
            int warnings = UsefulTankardsPlugin.Log.Warnings.Count;
            Check(EpicLootStorageCompatibility.ShouldYieldUse(InventoryGui.instance), "subsequent input stays disabled");
            Equal(warnings, UsefulTankardsPlugin.Log.Warnings.Count, "failure logged once");
        });
        Run("predicate failure does not mutate provider dictionary during enumeration", f =>
        {
            EnableEpicLoot();
            TankardTweaks.ThrowProfileLookup = true;
            Check(EpicLoot.API.Providers.Values.All(predicate => !predicate(f.Tankard)), "failed predicate is inactive");
            Check(EpicLoot.API.Providers.ContainsKey(UsefulTankardsPlugin.ModGuid), "cleanup deferred out of enumeration");
            Check(EpicLootStorageCompatibility.ShouldYieldUse(InventoryGui.instance), "input boundary remains disabled");
            Check(!EpicLoot.API.Providers.ContainsKey(UsefulTankardsPlugin.ModGuid), "input boundary completes cleanup");
            TankardTweaks.ThrowProfileLookup = false;
        });
        Run("Reset releases only its registration and supports reinitialization", f =>
        {
            EpicLoot.API.Providers["other.mod"] = _ => true;
            EnableEpicLoot();
            EpicLootStorageCompatibility.Reset();
            Check(EpicLoot.API.Providers.Count == 1 && EpicLoot.API.Providers.ContainsKey("other.mod"), "own callback removed");
            EpicLootStorageCompatibility.Initialize();
            Check(EpicLoot.API.Providers[UsefulTankardsPlugin.ModGuid](f.Tankard), "callback re-registered after reset");
        });
        Run("failed unregister keeps callback inactive until cleanup retry", f =>
        {
            EnableEpicLoot();
            var predicate = EpicLoot.API.Providers[UsefulTankardsPlugin.ModGuid];
            EpicLoot.API.ThrowUnregister = true;
            EpicLootStorageCompatibility.Reset();
            Check(!predicate(f.Tankard), "retained callback cannot take input while unavailable");
            EpicLoot.API.ThrowUnregister = false;
            EpicLootStorageCompatibility.Reset();
            Check(!EpicLoot.API.Providers.ContainsKey(UsefulTankardsPlugin.ModGuid), "later reset retries cleanup");
        });
        Run("missing current API stops input integration without altering storage", f =>
        {
            BepInEx.Bootstrap.Chainloader.PluginInfos[EpicLootStorageCompatibility.PluginGuid] = new() { Instance = "missing API assembly" };
            EpicLootStorageCompatibility.Initialize();
            Check(EpicLootStorageCompatibility.ShouldYieldUse(InventoryGui.instance), "missing contract yields input");
            f.SetData(110, f.Mead);
            var storage = f.Open();
            Check(storage.Storage.LoadComplete, "storage reader remains available");
        });

        if (args.Length > 0)
            File.WriteAllText(args[0], System.Text.Json.JsonSerializer.Serialize(InventoryFixtures.ContractFixtures));
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void EnableEpicLoot()
    {
        BepInEx.Bootstrap.Chainloader.PluginInfos[EpicLootStorageCompatibility.PluginGuid] = new();
        EpicLootStorageCompatibility.Initialize();
    }

    private static void ResetEpicLoot()
    {
        EpicLoot.API.ThrowUnregister = false;
        EpicLootStorageCompatibility.Reset();
        EpicLoot.API.Providers.Clear();
        BepInEx.Bootstrap.Chainloader.PluginInfos.Clear();
        EpicLoot.API.Reject = EpicLoot.API.ThrowAfterRegister = EpicLoot.API.MissingCategory = false;
        EpicLoot.API.RegisterCalls = EpicLoot.API.UnregisterCalls = 0;
        EpicLoot.InventoryPromptHost.Open = EpicLoot.InventoryPromptHost.Throw = false;
        EpicLoot.ShardStones.SocketOverlayInput.Press = EpicLoot.ShardStones.SocketOverlayInput.Throw = false;
        EpicLoot.ShardStones.SocketOverlayInput.LastGui = null;
        TankardTweaks.ThrowProfileLookup = false;
    }

    private static void AssertRejectedWithoutRewrite(Fixture f, string raw)
    {
        f.Tankard.m_customData[DataKey] = raw;
        f.Tankard.m_customData[WeightKey] = "12.5";
        var opened = f.Open();
        Check(!opened.Storage.LoadComplete, "unverifiable data rejected");
        Equal(0, Inventory.LoadCalls, "malformed data rejected before game loader");
        opened.Storage.CloseAndDestroy();
        Equal(raw, f.Tankard.m_customData[DataKey], "original raw data preserved");
        Equal("12.5", f.Tankard.m_customData[WeightKey], "original weight cache preserved");
    }
    private static void AssertLoadedMismatchPreservesData(Fixture f, params ItemDrop.ItemData[] loaded)
    {
        string raw = f.Tankard.m_customData[DataKey];
        f.Tankard.m_customData[WeightKey] = "12.5";
        InventoryFixtures.LoadedItems[raw] = loaded;
        var opened = f.Open();
        Check(!opened.Storage.LoadComplete, "changed loaded fields rejected");
        Equal(1, Inventory.LoadCalls, "prescribed game loader reached once");
        Check(!TankardStorageSystem.IsTankardStorageInventory(opened.Container.Inventory), "incomplete inventory not registered");
        ValheimAccess.Changed(opened.Container.Inventory);
        opened.Storage.Save();
        opened.Storage.CloseAndDestroy();
        Equal(raw, f.Tankard.m_customData[DataKey], "save and close preserve original data");
        Equal("12.5", f.Tankard.m_customData[WeightKey], "save and close preserve original weight");
    }
    private static void SetOptionalMetadata(ItemDrop.ItemData item)
    {
        item.m_quality = 2;
        item.m_variant = 7;
        item.m_crafterID = 123;
        item.m_crafterName = "fixture crafter";
        item.m_worldLevel = 2;
        item.m_equipped = item.m_pickedUp = item.m_cheated = true;
    }
    private static void Run(string name, Action<Fixture> test)
    {
        try
        {
            using Fixture fixture = new();
            test(fixture);
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception error)
        {
            _failed++;
            Console.WriteLine($"FAIL {name}: {error.Message}");
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static bool TryStackAll(Container container)
    {
        // Reflection keeps the same harness compilable against a pre-fix source baseline.
        var method = typeof(TankardStorageSystem).GetMethod("TryStackAllTankardStorageContainer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("local stack-all entry point is absent");
        return (bool)method.Invoke(null, new object[] { container })!;
    }
    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}; expected {expected}, actual {actual}");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Player Player;
        internal readonly ItemDrop.ItemData Tankard, Mead;
        internal readonly TankardProfile Profile = new();
        private readonly List<TankardStorageSystem.TankardStorageContainer> _opened = new();
        internal Fixture()
        {
            ResetEpicLoot();
            Inventory.LoadCalls = 0;
            InventoryFixtures.LoadedItems.Clear();
            UsefulTankardsPlugin.Log.Warnings.Clear();
            TankardTweaks.Profiles.Clear();
            ObjectDB.instance = new();
            Time.frameCount++;
            InventoryGui.instance = new GameObject("gui").AddComponent<InventoryGui>();
            Player = new GameObject("player").AddComponent<Player>();
            Player.m_localPlayer = Player;
            Tankard = Item("tankard", 1);
            Tankard.m_shared.m_ammoType = "mead";
            TankardTweaks.Profiles[Tankard.m_shared] = Profile;
            Player.GetInventory().GetAllItems().Add(Tankard);
            Mead = Item("mead", 3);
            Mead.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
            Mead.m_shared.m_ammoType = "mead";
            Mead.m_shared.m_consumeStatusEffect = new StatusEffect { name = "effect_mead", m_category = "mead" };
            Mead.m_shared.m_weight = 1.5f;
            Mead.m_shared.m_scaleWeightByQuality = 0.5f;
        }
        private static ItemDrop.ItemData Item(string name, int stack)
        {
            GameObject prefab = new(name);
            ItemDrop item = prefab.AddComponent<ItemDrop>();
            item.m_itemData.m_dropPrefab = prefab;
            item.m_itemData.m_shared.m_name = name;
            item.m_itemData.m_stack = stack;
            ObjectDB.instance!.Prefabs[name] = prefab;
            return item.m_itemData;
        }
        internal void SetData(int version, params ItemDrop.ItemData[] items) =>
            Tankard.m_customData[DataKey] = InventoryFixtures.Encode(version, items);
        internal ItemDrop.ItemData OtherMead()
        {
            ItemDrop.ItemData item = Item("other_mead", 1);
            item.m_gridPos = new Vector2i(1, 0);
            item.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
            item.m_shared.m_ammoType = "mead";
            item.m_shared.m_consumeStatusEffect = new StatusEffect { name = "effect_other_mead", m_category = "other_mead" };
            return item;
        }
        internal (TankardStorageSystem.TankardStorageContainer Storage, Container Container) Open()
        {
            GameObject gameObject = new("test_storage");
            var storage = gameObject.AddComponent<TankardStorageSystem.TankardStorageContainer>();
            Container container = gameObject.AddComponent<Container>();
            _opened.Add(storage);
            storage.Initialize(Player, Tankard, Profile, container);
            InventoryGui.instance.Show(container, 1);
            return (storage, container);
        }
        public void Dispose()
        {
            InventoryGui.instance.OnClose = null;
            TankardStorageSystem.CloseOpenTankardStorage();
            foreach (var storage in _opened) storage.CloseAndDestroy();
            ResetEpicLoot();
            ZInput.UseDown = false;
            Player.m_localPlayer = null;
        }
    }
}

// Binary input fixtures encode Valheim inventory fields. The production summary reader
// is the only parser under test. Game Inventory.Load outcomes are prescribed separately.
internal static class InventoryFixtures
{
    internal static readonly Dictionary<string, object> ContractFixtures = new();
    internal static readonly Dictionary<string, ItemDrop.ItemData[]> LoadedItems = new(StringComparer.Ordinal);
    internal static string Encode(int version, IEnumerable<ItemDrop.ItemData> items)
    {
        ZPackage package = new();
        Write(package, version, items);
        return package.GetBase64();
    }
    internal static string EncodeHeader(int version, int count)
    {
        ZPackage package = new();
        WriteHeader(package.Writer, version, count);
        return package.GetBase64();
    }
    internal static void Write(ZPackage package, int version, IEnumerable<ItemDrop.ItemData> source)
    {
        ItemDrop.ItemData[] items = source.Select(item => item.Clone()).ToArray();
        WriteHeader(package.Writer, version, items.Length);
        foreach (ItemDrop.ItemData item in items)
        {
            WriteItemPrefix(package.Writer, version, item);
            if (item.m_customData.Count > 0)
            {
                int count = item.m_customData.Count;
                if (count < 128) package.Writer.Write((byte)count);
                else { package.Writer.Write((byte)((count >> 8) | 128)); package.Writer.Write((byte)count); }
                foreach (var entry in item.m_customData)
                {
                    package.Writer.Write(entry.Key);
                    package.Writer.Write(entry.Value);
                }
            }
            package.Writer.Write((byte)(item.m_cheated ? 1 : 0));
            // Prescribed successful load follows the original serializer's normalization.
            item.m_durability = (int)(item.m_durability * 100f) * 0.01f;
            if (item.m_crafterID == 0) item.m_crafterName = "";
            // The original 1.0.20 reader consumes the old bit without applying it.
            if (version == 109) item.m_cheated = false;
        }
        LoadedItems[package.GetBase64()] = items;
        ContractFixtures[package.GetBase64()] = items.Select(item => new
        {
            hash = PrefabHash(item.m_dropPrefab.name), stack = item.m_stack, quality = item.m_quality,
            // Fault-injection tests mutate LoadedItems; original-reader expectations must not follow them.
            customData = new Dictionary<string, string>(item.m_customData, StringComparer.Ordinal), durability = item.m_durability,
            x = item.m_gridPos.x, y = item.m_gridPos.y, variant = item.m_variant,
            crafterID = item.m_crafterID, crafterName = item.m_crafterName,
            worldLevel = item.m_worldLevel, equipped = item.m_equipped,
            pickedUp = item.m_pickedUp, cheated = item.m_cheated
        }).ToArray();
    }
    internal static void WriteHeader(BinaryWriter writer, int version, int count)
    {
        writer.Write(version);
        writer.Write((ushort)count);
    }
    internal static void WriteItemPrefix(BinaryWriter writer, int version, ItemDrop.ItemData item)
    {
        writer.Write((int)(item.m_durability * 100f)); // fixed-point durability
        writer.Write((byte)item.m_gridPos.x);
        writer.Write((byte)item.m_gridPos.y);
        writer.Write((byte)item.m_worldLevel);
        int flags = 64;
        if (item.m_pickedUp) flags |= 1;
        if (item.m_equipped) flags |= 2;
        if (item.m_quality != 1) flags |= 4;
        if (item.m_stack != 1) flags |= 8;
        if (item.m_variant != 0) flags |= 16;
        if (item.m_crafterID != 0) flags |= 32;
        if (item.m_customData.Count > 0) flags |= 128;
        writer.Write((byte)flags);
        if ((flags & 4) != 0) writer.Write((ushort)item.m_quality);
        if ((flags & 8) != 0) writer.Write((ushort)item.m_stack);
        if ((flags & 16) != 0) writer.Write(item.m_variant);
        if ((flags & 32) != 0) { writer.Write(item.m_crafterID); writer.Write(item.m_crafterName); }
        writer.Write(PrefabHash(item.m_dropPrefab.name));
    }
    internal static int PrefabHash(string value)
    {
        unchecked
        {
            int a = 5381, b = a;
            for (int i = 0; i < value.Length; i += 2)
            {
                a = ((a << 5) + a) ^ value[i];
                if (i + 1 == value.Length) break;
                b = ((b << 5) + b) ^ value[i + 1];
            }
            return a + b * 1566083941;
        }
    }
}
