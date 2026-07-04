using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UsefulTankards;

internal static class TankardStorageSystem
{
    private const string StorageDataKey = "UsefulTankards.Storage.Data";
    private const string StorageSlotsKey = "UsefulTankards.Storage.Slots";

    private static readonly HashSet<Inventory> StorageInventories = new();
    private static readonly Dictionary<Inventory, ItemDrop.ItemData> InventoryOwners = new();
    private static readonly HashSet<string> WarnedIncompleteLoads = new(StringComparer.Ordinal);
    private static Player? _cachedStoredDrinkPlayer;
    private static ItemDrop.ItemData? _cachedStoredDrinkTankard;
    private static int _cachedStoredDrinkFrame = -1;
    private static bool _cachedStoredDrinkResult;

    internal static bool IsTankardStorageInventory(Inventory inventory)
    {
        return inventory != null && StorageInventories.Contains(inventory);
    }

    internal static bool IsTankardStorageContainer(Container? container)
    {
        return container != null && container.GetComponent<TankardStorageContainer>() != null;
    }

    internal static bool CanAddToTankardStorage(Inventory inventory, ItemDrop.ItemData item)
    {
        if (!IsTankardStorageInventory(inventory))
        {
            return true;
        }

        return IsAllowedStoredDrink(inventory, item);
    }

    internal static bool CanAddToTankardStorage(Inventory inventory, GameObject prefab)
    {
        if (!IsTankardStorageInventory(inventory))
        {
            return true;
        }

        if ((Object)(object)prefab == null)
        {
            return false;
        }

        ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
        return itemDrop != null && IsAllowedStoredDrink(inventory, itemDrop.m_itemData);
    }

    internal static bool TrySaveTankardStorageContainer(Container container)
    {
        TankardStorageContainer? storage = container != null ? container.GetComponent<TankardStorageContainer>() : null;
        if (storage == null)
        {
            return false;
        }

        storage.Save();
        return true;
    }

    internal static float GetStoredDrinkWeight(ItemDrop.ItemData tankard)
    {
        if (!TryLoadStoredInventorySnapshot(tankard, out Inventory inventory))
        {
            return 0f;
        }

        float weight = 0f;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            weight += item.GetWeight();
        }

        return weight;
    }

    internal static List<string> GetStoredDrinkTooltipLines(ItemDrop.ItemData tankard)
    {
        List<string> lines = new();
        if (!TryLoadStoredInventorySnapshot(tankard, out Inventory inventory))
        {
            return lines;
        }

        Dictionary<string, StoredDrinkSummary> summaries = new(StringComparer.Ordinal);
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (item == null)
            {
                continue;
            }

            string name = LocalizeItemName(item);
            int stack = Math.Max(1, item.m_stack);
            int capacity = Math.Max(stack, item.m_shared?.m_maxStackSize ?? stack);
            if (!summaries.TryGetValue(name, out StoredDrinkSummary summary))
            {
                summaries[name] = new StoredDrinkSummary(name, stack, capacity);
                continue;
            }

            summary.Stack += stack;
            summary.Capacity += capacity;
        }

        foreach (StoredDrinkSummary summary in summaries.Values)
        {
            string amount = summary.Capacity > 1 ? $" {summary.Stack}/{summary.Capacity}" : "";
            lines.Add($"- {summary.Name}{amount}");
        }

        return lines;
    }

    internal static bool TryHandleInventoryGuiUseInput(InventoryGui inventoryGui)
    {
        if (inventoryGui == null ||
            Player.m_localPlayer == null)
        {
            return false;
        }

        Container? currentContainer = ValheimAccess.GetCurrentContainer(inventoryGui);
        if (IsTankardStorageContainer(currentContainer))
        {
            if (!ZInput.GetButtonDown("Use"))
            {
                return false;
            }

            ZInput.ResetButtonStatus("Use");
            ValheimAccess.CloseContainer(inventoryGui);
            return true;
        }

        if (currentContainer != null || !ZInput.GetButtonDown("Use"))
        {
            return false;
        }

        InventoryGrid? playerGrid = ValheimAccess.GetPlayerGrid(inventoryGui);
        ItemDrop.ItemData hoveredItem = playerGrid != null
            ? playerGrid.GetItem(new Vector2i(Mathf.RoundToInt(Input.mousePosition.x), Mathf.RoundToInt(Input.mousePosition.y)))
            : null!;
        if (!TankardTweaks.TryGetProfile(hoveredItem, out TankardProfile profile) || profile.TankardStorageSlots <= 0)
        {
            return false;
        }

        OpenTankardStorage(Player.m_localPlayer, hoveredItem, profile);
        ZInput.ResetButtonStatus("Use");
        return true;
    }

    internal static void CloseTankardStorage(Container container)
    {
        container?.GetComponent<TankardStorageContainer>()?.CloseAndDestroy();
    }

    internal static bool TryConsumeStoredDrinks(Player player, ItemDrop.ItemData tankard, TankardProfile profile, out ItemDrop.ItemData consumedAmmo)
    {
        consumedAmmo = null!;
        if (player == null ||
            tankard == null ||
            profile.TankardStorageSlots <= 0)
        {
            return false;
        }

        if (!tankard.m_customData.TryGetValue(StorageDataKey, out string rawData) || string.IsNullOrWhiteSpace(rawData))
        {
            return false;
        }

        Inventory inventory = LoadTankardStorageInventory(player, tankard, profile, out _, out _, out bool loadComplete);
        if (!loadComplete)
        {
            return false;
        }

        try
        {
            bool consumedAny = false;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = items.Count - 1; i >= 0; --i)
            {
                ItemDrop.ItemData item = items[i];
                if (!CanConsumeStoredDrinkQuietly(player, tankard, item))
                {
                    continue;
                }

                ItemDrop.ItemData beforeConsume = item.Clone();
                if (!player.ConsumeItem(inventory, item))
                {
                    continue;
                }

                consumedAmmo ??= beforeConsume;
                consumedAny = true;
            }

            if (!consumedAny)
            {
                return false;
            }

            SaveTankardStorageInventory(tankard, inventory);
            ClearStoredDrinkCheckCache();
            ValheimAccess.Changed(player.GetInventory());
            return true;
        }
        finally
        {
            UnregisterTankardStorageInventory(inventory);
        }
    }

    internal static bool HasConsumableStoredDrink(Player player, ItemDrop.ItemData tankard, TankardProfile profile)
    {
        if (player == null ||
            tankard == null ||
            profile.TankardStorageSlots <= 0)
        {
            return false;
        }

        if (ReferenceEquals(_cachedStoredDrinkPlayer, player) &&
            ReferenceEquals(_cachedStoredDrinkTankard, tankard) &&
            _cachedStoredDrinkFrame == Time.frameCount)
        {
            return _cachedStoredDrinkResult;
        }

        bool result = HasConsumableStoredDrinkUncached(player, tankard, profile);
        _cachedStoredDrinkPlayer = player;
        _cachedStoredDrinkTankard = tankard;
        _cachedStoredDrinkFrame = Time.frameCount;
        _cachedStoredDrinkResult = result;
        return result;
    }

    private static bool HasConsumableStoredDrinkUncached(Player player, ItemDrop.ItemData tankard, TankardProfile profile)
    {
        if (!tankard.m_customData.TryGetValue(StorageDataKey, out string rawData) || string.IsNullOrWhiteSpace(rawData))
        {
            return false;
        }

        Inventory inventory = LoadTankardStorageInventory(player, tankard, profile, out _, out _, out bool loadComplete);
        if (!loadComplete)
        {
            return false;
        }

        try
        {
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (CanConsumeStoredDrinkQuietly(player, tankard, item))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            UnregisterTankardStorageInventory(inventory);
        }
    }

    private static void OpenTankardStorage(Player player, ItemDrop.ItemData tankard, TankardProfile profile)
    {
        int slots = ResolveStorageSlots(tankard, profile);
        ResolveGridSize(slots, out int width, out int height);
        GameObject storageObject = new("UsefulTankards_TankardStorage");
        storageObject.transform.position = player.transform.position;
        TankardStorageContainer storage = storageObject.AddComponent<TankardStorageContainer>();
        Container container = storageObject.AddComponent<Container>();
        storage.Initialize(player, tankard, profile, container, slots, width, height);
        if (!storage.LoadComplete)
        {
            storage.CloseAndDestroy();
            return;
        }

        InventoryGui.instance.Show(container, 1);
    }

    private static Inventory LoadTankardStorageInventory(Player player, ItemDrop.ItemData tankard, TankardProfile profile, out int width, out int height, out bool loadComplete)
    {
        int slots = ResolveStorageSlots(tankard, profile);
        ResolveGridSize(slots, out width, out height);
        Inventory inventory = CreateTankardStorageInventory(tankard, width, height);
        loadComplete = true;
        if (tankard.m_customData.TryGetValue(StorageDataKey, out string rawData) && !string.IsNullOrWhiteSpace(rawData))
        {
            try
            {
                inventory.Load(new ZPackage(rawData));
                loadComplete = IsStorageLoadComplete(tankard, rawData, inventory);
            }
            catch (Exception exception)
            {
                UsefulTankardsPlugin.Log.LogWarning($"Could not read tankard storage for {TankardTweaks.GetCleanPrefabName(tankard)}: {exception.GetBaseException().Message}");
                loadComplete = false;
            }
        }

        RegisterTankardStorageInventory(inventory, tankard);
        return inventory;
    }

    private static bool TryLoadStoredInventorySnapshot(ItemDrop.ItemData tankard, out Inventory inventory)
    {
        inventory = null!;
        if (tankard == null ||
            !TankardTweaks.TryGetProfile(tankard, out TankardProfile profile) ||
            !tankard.m_customData.TryGetValue(StorageDataKey, out string rawData) ||
            string.IsNullOrWhiteSpace(rawData))
        {
            return false;
        }

        int slots = ResolveStorageSlots(tankard, profile);
        ResolveGridSize(slots, out int width, out int height);
        inventory = CreateTankardStorageInventory(tankard, width, height);
        try
        {
            inventory.Load(new ZPackage(rawData));
            if (!IsStorageLoadComplete(tankard, rawData, inventory))
            {
                inventory = null!;
                return false;
            }

            return inventory.GetAllItems().Count > 0;
        }
        catch (Exception exception)
        {
            UsefulTankardsPlugin.Log.LogWarning($"Could not read tankard storage for {TankardTweaks.GetCleanPrefabName(tankard)}: {exception.GetBaseException().Message}");
            inventory = null!;
            return false;
        }
    }

    private static void SaveTankardStorageInventory(ItemDrop.ItemData tankard, Inventory inventory)
    {
        if (tankard == null || inventory == null)
        {
            return;
        }

        ZPackage package = new();
        inventory.Save(package);
        string rawData = package.GetBase64();
        if (inventory.GetAllItems().Count == 0)
        {
            tankard.m_customData.Remove(StorageDataKey);
        }
        else
        {
            tankard.m_customData[StorageDataKey] = rawData;
        }

        ClearStoredDrinkCheckCache();
    }

    private static Inventory CreateTankardStorageInventory(ItemDrop.ItemData tankard, int width, int height)
    {
        string name = tankard?.m_shared?.m_name ?? "$item_tankard";
        return new Inventory(name, tankard?.GetIcon(), width, height);
    }

    private static bool IsStorageLoadComplete(ItemDrop.ItemData tankard, string rawData, Inventory inventory)
    {
        if (!TryReadExpectedStorageStack(rawData, out int expectedEntries, out int expectedStack) || expectedEntries <= 0)
        {
            return true;
        }

        int actualStack = 0;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            actualStack += Math.Max(0, item.m_stack);
        }

        if (actualStack >= expectedStack)
        {
            return true;
        }

        WarnIncompleteLoad(tankard, expectedStack, actualStack);
        return false;
    }

    private static bool TryReadExpectedStorageStack(string rawData, out int expectedEntries, out int expectedStack)
    {
        expectedEntries = 0;
        expectedStack = 0;
        try
        {
            ZPackage package = new(rawData);
            int version = package.ReadInt();
            int itemCount = package.ReadInt();
            if (version != 106)
            {
                return false;
            }

            for (int i = 0; i < itemCount; ++i)
            {
                string prefabName = package.ReadString();
                int stack = package.ReadInt();
                _ = package.ReadSingle();
                _ = package.ReadVector2i();
                _ = package.ReadBool();
                _ = package.ReadInt();
                _ = package.ReadInt();
                _ = package.ReadLong();
                _ = package.ReadString();
                int customDataCount = package.ReadInt();
                for (int j = 0; j < customDataCount; ++j)
                {
                    _ = package.ReadString();
                    _ = package.ReadString();
                }

                _ = package.ReadInt();
                _ = package.ReadBool();
                if (string.IsNullOrWhiteSpace(prefabName))
                {
                    continue;
                }

                expectedEntries++;
                expectedStack += Math.Max(0, stack);
            }

            return true;
        }
        catch (Exception exception)
        {
            UsefulTankardsPlugin.Log.LogWarning($"Could not inspect tankard storage payload: {exception.GetBaseException().Message}");
            return false;
        }
    }

    private static void WarnIncompleteLoad(ItemDrop.ItemData tankard, int expectedStack, int actualStack)
    {
        string tankardName = TankardTweaks.GetCleanPrefabName(tankard);
        string key = $"{tankardName}:{expectedStack}:{actualStack}";
        if (!WarnedIncompleteLoads.Add(key))
        {
            return;
        }

        UsefulTankardsPlugin.Log.LogWarning($"Tankard storage for {tankardName} loaded only {actualStack}/{expectedStack} stored item stack. The stored data was preserved and the tankard storage was not opened.");
    }

    private static int ResolveStorageSlots(ItemDrop.ItemData tankard, TankardProfile profile)
    {
        int slots = Math.Max(0, profile.TankardStorageSlots);
        if (tankard != null)
        {
            tankard.m_customData[StorageSlotsKey] = slots.ToString();
        }

        return slots;
    }

    private static void ResolveGridSize(int slots, out int width, out int height)
    {
        int normalized = Math.Max(1, slots);
        width = Mathf.Clamp(normalized, 1, 5);
        height = Mathf.CeilToInt(normalized / 5f);
    }

    private static void RegisterTankardStorageInventory(Inventory inventory, ItemDrop.ItemData tankard)
    {
        if (inventory == null)
        {
            return;
        }

        StorageInventories.Add(inventory);
        if (tankard != null)
        {
            InventoryOwners[inventory] = tankard;
        }
    }

    private static void UnregisterTankardStorageInventory(Inventory inventory)
    {
        if (inventory == null)
        {
            return;
        }

        StorageInventories.Remove(inventory);
        InventoryOwners.Remove(inventory);
    }

    private static void ClearStoredDrinkCheckCache()
    {
        _cachedStoredDrinkPlayer = null;
        _cachedStoredDrinkTankard = null;
        _cachedStoredDrinkFrame = -1;
        _cachedStoredDrinkResult = false;
    }

    private static string LocalizeItemName(ItemDrop.ItemData item)
    {
        string name = item.m_shared?.m_name ?? "";
        if (Localization.instance == null || string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return Localization.instance.Localize(name);
    }

    private static bool IsAllowedStoredDrink(Inventory inventory, ItemDrop.ItemData item)
    {
        if (item == null ||
            item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable ||
            (Object)(object)item.m_shared.m_consumeStatusEffect == null ||
            TankardTweaks.TryGetProfile(item, out _))
        {
            return false;
        }

        if (!InventoryOwners.TryGetValue(inventory, out ItemDrop.ItemData tankard) || tankard == null)
        {
            return true;
        }

        string tankardAmmoType = tankard.m_shared.m_ammoType;
        return string.IsNullOrWhiteSpace(tankardAmmoType) ||
               string.Equals(item.m_shared.m_ammoType, tankardAmmoType, StringComparison.OrdinalIgnoreCase) ||
               ((Object)(object)item.m_dropPrefab != null && string.Equals(((Object)item.m_dropPrefab).name, tankardAmmoType, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StoredDrinkSummary
    {
        internal StoredDrinkSummary(string name, int stack, int capacity)
        {
            Name = name;
            Stack = stack;
            Capacity = capacity;
        }

        internal string Name { get; }
        internal int Stack { get; set; }
        internal int Capacity { get; set; }
    }

    private static bool CanConsumeStoredDrinkQuietly(Player player, ItemDrop.ItemData tankard, ItemDrop.ItemData item)
    {
        if (player == null || !IsAllowedStoredDrinkForTankard(tankard, item))
        {
            return false;
        }

        if (item.m_shared.m_food > 0f && !player.CanEat(item, showMessages: false))
        {
            return false;
        }

        StatusEffect effect = item.m_shared.m_consumeStatusEffect;
        return (Object)(object)effect == null ||
               (!player.GetSEMan().HaveStatusEffect(effect.NameHash()) && !player.GetSEMan().HaveStatusEffectCategory(effect.m_category));
    }

    private static bool IsAllowedStoredDrinkForTankard(ItemDrop.ItemData tankard, ItemDrop.ItemData item)
    {
        if (item == null ||
            item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable ||
            (Object)(object)item.m_shared.m_consumeStatusEffect == null ||
            TankardTweaks.TryGetProfile(item, out _))
        {
            return false;
        }

        string tankardAmmoType = tankard?.m_shared?.m_ammoType ?? "";
        return string.IsNullOrWhiteSpace(tankardAmmoType) ||
               string.Equals(item.m_shared.m_ammoType, tankardAmmoType, StringComparison.OrdinalIgnoreCase) ||
               ((Object)(object)item.m_dropPrefab != null && string.Equals(((Object)item.m_dropPrefab).name, tankardAmmoType, StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class TankardStorageContainer : MonoBehaviour
    {
        private Player? _player;
        private ItemDrop.ItemData? _tankard;
        private Inventory? _inventory;
        private Container? _container;
        private Action? _saveHandler;
        private bool _loadComplete = true;
        private bool _closed;

        internal bool LoadComplete => _loadComplete;

        internal void Initialize(Player player, ItemDrop.ItemData tankard, TankardProfile profile, Container container, int slots, int width, int height)
        {
            _player = player;
            _tankard = tankard;
            _container = container;
            _inventory = LoadTankardStorageInventory(player, tankard, profile, out width, out height, out _loadComplete);

            ValheimAccess.SetContainerFields(container, tankard.m_shared.m_name, width, height, _inventory, inUse: true);
            tankard.m_customData[StorageSlotsKey] = slots.ToString();
            AttachImmediateSave();
            ValheimAccess.Changed(player.GetInventory());
        }

        private void Update()
        {
            if (!_closed &&
                _container != null &&
                InventoryGui.instance != null &&
                ValheimAccess.GetCurrentContainer(InventoryGui.instance) != _container)
            {
                CloseAndDestroy();
                return;
            }

            if (_player != null)
            {
                transform.position = _player.transform.position;
            }
        }

        internal void Save()
        {
            if (_tankard == null || _inventory == null || !_loadComplete)
            {
                return;
            }

            SaveTankardStorageInventory(_tankard, _inventory);
            ValheimAccess.Changed(_player?.GetInventory());
        }

        private void AttachImmediateSave()
        {
            if (!_loadComplete || _inventory == null || _saveHandler != null)
            {
                return;
            }

            _saveHandler = Save;
            ValheimAccess.AddInventoryChangedHandler(_inventory, _saveHandler);
        }

        private void DetachImmediateSave()
        {
            if (_saveHandler == null)
            {
                return;
            }

            ValheimAccess.RemoveInventoryChangedHandler(_inventory, _saveHandler);
            _saveHandler = null;
        }

        internal void CloseAndDestroy()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            Save();
            DetachImmediateSave();
            if (_inventory != null)
            {
                UnregisterTankardStorageInventory(_inventory);
            }

            Destroy(gameObject);
        }
    }
}
