using System.Globalization;
using UsefulTankards;
using UnityEngine;

internal static class Program
{
    internal const string DataKey = "UsefulTankards.Storage.Data";
    internal const string WeightKey = "UsefulTankards.Storage.Weight";
    private static int _passed, _failed;

    private static int Main()
    {
        for (int version = 100; version <= 106; version++)
        {
            int fixtureVersion = version;
            Run($"v{version}: summary weight and complete inventory load", f =>
            {
                f.Mead.m_quality = fixtureVersion >= 101 ? 2 : 1;
                f.SetData(fixtureVersion, f.Mead);
                Equal(f.Mead.GetWeight(), TankardStorageSystem.GetStoredDrinkWeight(f.Tankard), "weight");
                Equal(0, Inventory.LoadCalls, "weight lookup must not instantiate/load items");
                var opened = f.Open();
                Check(opened.Storage.LoadComplete, "supported fixture must load");
                Equal(3, opened.Container.Inventory.GetAllItems().Single().m_stack, "stack survives load");
                Check(TankardStorageSystem.IsTankardStorageInventory(opened.Container.Inventory), "loaded storage registers");
            });
        }

        Run("summary validation does not resolve unused weights", f =>
        {
            f.SetData(106, f.Mead);
            var opened = f.Open();
            Check(opened.Storage.LoadComplete, "load succeeds");
            Equal(0, ObjectDB.instance!.LookupCount, "validation discarded weight, so no prefab lookup is needed");
        });

        foreach (int version in new[] { 99, 107 })
        {
            Run($"unsupported version {version}: raw data survives close", f =>
                AssertRejectedWithoutRewrite(f, InventoryFixtures.EncodeHeader(version, 1)));
        }
        Run("non-base64 data survives close", f => AssertRejectedWithoutRewrite(f, "this is not base64!"));
        Run("truncated item data survives close", f =>
        {
            string valid = InventoryFixtures.Encode(106, new[] { f.Mead });
            byte[] bytes = Convert.FromBase64String(valid);
            AssertRejectedWithoutRewrite(f, Convert.ToBase64String(bytes[..^2]));
        });
        Run("negative item count survives close", f => AssertRejectedWithoutRewrite(f, InventoryFixtures.EncodeHeader(106, -1)));
        Run("negative custom-data count survives close", f =>
        {
            ZPackage package = new();
            InventoryFixtures.WriteHeader(package.Writer, 104, 1);
            InventoryFixtures.WriteItemPrefix(package.Writer, 104, f.Mead);
            package.Writer.Write(-1);
            AssertRejectedWithoutRewrite(f, package.GetBase64());
        });
        Run("missing prefab: partial load cannot replace raw data", f =>
        {
            ItemDrop.ItemData missing = f.Mead.Clone();
            missing.m_dropPrefab = new GameObject("removed_mod_mead");
            f.SetData(106, f.Mead, missing);
            string raw = f.Tankard.m_customData[DataKey];
            InventoryFixtures.LoadedItems[raw] = new[] { f.Mead };
            var opened = f.Open();
            Check(!opened.Storage.LoadComplete, "partial game load must be rejected");
            Check(!TankardStorageSystem.IsTankardStorageInventory(opened.Container.Inventory), "partial inventory is not registered");
            opened.Storage.CloseAndDestroy();
            Equal(raw, f.Tankard.m_customData[DataKey], "partial data must not overwrite original");
        });

        Run("registry controls admissions and unregisters on close", f =>
        {
            f.SetData(106, f.Mead);
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
            f.SetData(106, f.Mead);
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
            f.SetData(106, f.Mead);
            var opened = f.Open();
            Check(TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "last drink consumed");
            opened.Storage.CloseAndDestroy();
            Check(!f.Tankard.m_customData.ContainsKey(DataKey), "empty data key removed");
            Check(!f.Tankard.m_customData.ContainsKey(WeightKey), "empty weight key removed");
        });
        Run("open tooltip and availability reuse inventory", f =>
        {
            f.SetData(106, f.Mead);
            f.Open();
            Check(TankardStorageSystem.HasConsumableStoredDrink(f.Player, f.Tankard, f.Profile), "available");
            Check(TankardStorageSystem.GetStoredDrinkTooltipLines(f.Tankard).Single().Contains("3/10", StringComparison.Ordinal), "tooltip uses stack");
            Equal(1, Inventory.LoadCalls, "read-only open queries need no snapshot load");
        });
        Run("closed consumption persists exactly one decrement", f =>
        {
            f.SetData(106, f.Mead);
            Check(TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "closed drink consumed");
            Equal(2, InventoryFixtures.LoadedItems[f.Tankard.m_customData[DataKey]].Single().m_stack, "closed storage persists");
        });
        Run("failed consume keeps serialized data", f =>
        {
            f.SetData(106, f.Mead);
            string raw = f.Tankard.m_customData[DataKey];
            f.Player.AllowConsumption = false;
            Check(!TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "failed consume reports false");
            Equal(raw, f.Tankard.m_customData[DataKey], "failed consume does not write storage");
        });
        Run("non-owner cannot consume stored items", f =>
        {
            f.SetData(106, f.Mead);
            string raw = f.Tankard.m_customData[DataKey];
            f.Player.Owner = false;
            Check(!TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "non-owner rejected");
            Equal(raw, f.Tankard.m_customData[DataKey], "non-owner cannot mutate raw data");
        });
        Run("tankard removed from actor inventory cannot be consumed", f =>
        {
            f.SetData(106, f.Mead);
            f.Player.GetInventory().GetAllItems().Remove(f.Tankard);
            Check(!TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "foreign item rejected");
            Check(!TankardStorageSystem.HasConsumableStoredDrink(f.Player, f.Tankard, f.Profile), "foreign item is not available");
            Equal(0, Inventory.LoadCalls, "ownership rejected before materialization");
        });
        Run("immediate save invalidates same-frame availability", f =>
        {
            f.SetData(106, f.Mead);
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
                f.SetData(106, f.Mead, second);
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
            f.SetData(106, f.Mead, second);
            var opened = f.Open();
            ValheimAccess.AddInventoryChangedHandler(opened.Container.Inventory, () => opened.Container.Inventory.GetAllItems().Clear());
            Check(TankardStorageSystem.TryConsumeStoredDrinks(f.Player, f.Tankard, f.Profile, out _), "first drink consumed");
            Equal(1, f.Player.ConsumeAttempts, "callback-removed candidate is skipped before game consume");
        });
        Run("stack-all routes only the active owned tankard UI", f =>
        {
            f.SetData(106, f.Mead);
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
            f.SetData(106, f.Mead);
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
            f.SetData(106, f.Mead);
            f.Tankard.m_customData[WeightKey] = "NaN";
            Equal(f.Mead.GetWeight(), TankardStorageSystem.GetStoredDrinkWeight(f.Tankard), "invalid cache recomputed");
            Equal(0, Inventory.LoadCalls, "migration does not load inventory");
        });
        Run("missing prefab leaves incomplete weight uncached", f =>
        {
            f.SetData(106, f.Mead);
            ObjectDB.instance!.Prefabs.Clear();
            Equal(0f, TankardStorageSystem.GetStoredDrinkWeight(f.Tankard), "unknown item cannot supply weight");
            Check(!f.Tankard.m_customData.ContainsKey(WeightKey), "partial weight must not be cached");
            Check(f.Tankard.m_customData.ContainsKey(DataKey), "weight query preserves data");
        });

        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
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
            Inventory.LoadCalls = 0;
            InventoryFixtures.LoadedItems.Clear();
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
            foreach (var storage in _opened) storage.CloseAndDestroy();
            Player.m_localPlayer = null;
        }
    }
}

// Binary input fixtures encode Valheim inventory fields. The production summary reader
// is the only parser under test. Game Inventory.Load outcomes are prescribed separately.
internal static class InventoryFixtures
{
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
            if (version >= 104)
            {
                package.Writer.Write(item.m_customData.Count);
                foreach (var entry in item.m_customData)
                {
                    package.Writer.Write(entry.Key);
                    package.Writer.Write(entry.Value);
                }
            }
            if (version >= 105) package.Writer.Write(0);
            if (version >= 106) package.Writer.Write(false);
        }
        LoadedItems[package.GetBase64()] = items;
    }
    internal static void WriteHeader(BinaryWriter writer, int version, int count)
    {
        writer.Write(version);
        writer.Write(count);
    }
    internal static void WriteItemPrefix(BinaryWriter writer, int version, ItemDrop.ItemData item)
    {
        writer.Write(item.m_dropPrefab.name);
        writer.Write(item.m_stack);
        writer.Write(100f);
        writer.Write(item.m_gridPos.x);
        writer.Write(item.m_gridPos.y);
        writer.Write(false);
        if (version >= 101) writer.Write(item.m_quality);
        if (version >= 102) writer.Write(0);
        if (version >= 103) { writer.Write(123L); writer.Write("fixture crafter"); }
    }
}
