using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UsefulTankards;

internal static class TankardStorageSystem
{
    private const string StorageDataKey = "UsefulTankards.Storage.Data";
    private const string StorageSlotsKey = "UsefulTankards.Storage.Slots";
    private const float LimitMessageInterval = 1.5f;

    private static readonly HashSet<Inventory> StorageInventories = new();
    private static readonly Dictionary<Inventory, ItemDrop.ItemData> InventoryOwners = new();
    private static float _nextLimitMessageTime;

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

    internal static bool CanAddTankardToPlayerInventory(Inventory inventory, ItemDrop.ItemData item, Inventory? fromInventory = null)
    {
        if (!UsefulTankardsPlugin.ModEnabled ||
            UsefulTankardsPlugin.MaxTankardsInInventory.Value <= 0 ||
            inventory == null ||
            item == null ||
            !TankardTweaks.TryGetProfile(item, out _))
        {
            return true;
        }

        Player player = Player.m_localPlayer;
        Inventory playerInventory = player != null ? player.GetInventory() : null!;
        if (playerInventory == null || !ReferenceEquals(inventory, playerInventory))
        {
            return true;
        }

        if (ReferenceEquals(fromInventory, inventory) || ContainsExactItem(inventory, item))
        {
            return true;
        }

        int movingStack = Math.Max(1, item.m_stack);
        if (CountTankards(inventory) + movingStack <= UsefulTankardsPlugin.MaxTankardsInInventory.Value)
        {
            return true;
        }

        NotifyTankardLimitReached();
        return false;
    }

    internal static bool CanAddTankardToPlayerInventory(Inventory inventory, GameObject prefab)
    {
        if (!UsefulTankardsPlugin.ModEnabled ||
            UsefulTankardsPlugin.MaxTankardsInInventory.Value <= 0 ||
            inventory == null ||
            prefab == null ||
            (Object)(object)prefab == null ||
            !TankardTweaks.TryGetProfile(prefab, out _))
        {
            return true;
        }

        Player player = Player.m_localPlayer;
        Inventory playerInventory = player != null ? player.GetInventory() : null!;
        if (playerInventory == null || !ReferenceEquals(inventory, playerInventory))
        {
            return true;
        }

        ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
        int movingStack = itemDrop != null ? Math.Max(1, itemDrop.m_itemData.m_stack) : 1;
        if (CountTankards(inventory) + movingStack <= UsefulTankardsPlugin.MaxTankardsInInventory.Value)
        {
            return true;
        }

        NotifyTankardLimitReached();
        return false;
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

    internal static bool TryHandleInventoryGuiUseInput(InventoryGui inventoryGui)
    {
        if (!UsefulTankardsPlugin.ModEnabled ||
            !UsefulTankardsPlugin.TankardStorageEnabled ||
            inventoryGui == null ||
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
        if (!UsefulTankardsPlugin.ModEnabled ||
            !UsefulTankardsPlugin.TankardStorageEnabled ||
            !UsefulTankardsPlugin.DrinkStoredMeadsOnUseEnabled ||
            player == null ||
            tankard == null ||
            profile.TankardStorageSlots <= 0)
        {
            return false;
        }

        Inventory inventory = LoadTankardStorageInventory(player, tankard, profile, out _, out _);
        try
        {
            bool consumedAny = false;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
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
        if (!UsefulTankardsPlugin.ModEnabled ||
            !UsefulTankardsPlugin.TankardStorageEnabled ||
            !UsefulTankardsPlugin.DrinkStoredMeadsOnUseEnabled ||
            player == null ||
            tankard == null ||
            profile.TankardStorageSlots <= 0)
        {
            return false;
        }

        Inventory inventory = LoadTankardStorageInventory(player, tankard, profile, out _, out _);
        try
        {
            return inventory.GetAllItems().Any(item => CanConsumeStoredDrinkQuietly(player, tankard, item));
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
        InventoryGui.instance.Show(container, 1);
    }

    private static Inventory LoadTankardStorageInventory(Player player, ItemDrop.ItemData tankard, TankardProfile profile, out int width, out int height)
    {
        int slots = ResolveStorageSlots(tankard, profile);
        ResolveGridSize(slots, out width, out height);
        Inventory inventory = CreateTankardStorageInventory(tankard, width, height);
        if (tankard.m_customData.TryGetValue(StorageDataKey, out string rawData) && !string.IsNullOrWhiteSpace(rawData))
        {
            try
            {
                inventory.Load(new ZPackage(rawData));
            }
            catch (Exception exception)
            {
                UsefulTankardsPlugin.Log.LogWarning($"Could not read tankard storage for {TankardTweaks.GetCleanPrefabName(tankard)}: {exception.GetBaseException().Message}");
            }
        }

        RegisterTankardStorageInventory(inventory, tankard);
        return inventory;
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
    }

    private static Inventory CreateTankardStorageInventory(ItemDrop.ItemData tankard, int width, int height)
    {
        string name = tankard?.m_shared?.m_name ?? "$item_tankard";
        return new Inventory(name, tankard?.GetIcon(), width, height);
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

    private static int CountTankards(Inventory inventory)
    {
        int count = 0;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (TankardTweaks.TryGetProfile(item, out _))
            {
                count += Math.Max(1, item.m_stack);
            }
        }

        return count;
    }

    private static bool ContainsExactItem(Inventory inventory, ItemDrop.ItemData item)
    {
        return inventory.GetAllItems().Any(existing => ReferenceEquals(existing, item));
    }

    private static void NotifyTankardLimitReached()
    {
        if (MessageHud.instance == null || Time.unscaledTime < _nextLimitMessageTime)
        {
            return;
        }

        _nextLimitMessageTime = Time.unscaledTime + LimitMessageInterval;
        string template = TankardLocalization.Localize(TankardLocalization.TankardLimitReachedKey);
        string message = string.Format(template, UsefulTankardsPlugin.MaxTankardsInInventory.Value);
        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
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
        private bool _closed;

        internal void Initialize(Player player, ItemDrop.ItemData tankard, TankardProfile profile, Container container, int slots, int width, int height)
        {
            _player = player;
            _tankard = tankard;
            _container = container;
            _inventory = LoadTankardStorageInventory(player, tankard, profile, out width, out height);

            ValheimAccess.SetContainerFields(container, tankard.m_shared.m_name, width, height, _inventory, inUse: true);
            tankard.m_customData[StorageSlotsKey] = slots.ToString();
            Save();
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
            if (_tankard == null || _inventory == null)
            {
                return;
            }

            SaveTankardStorageInventory(_tankard, _inventory);
        }

        internal void CloseAndDestroy()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            Save();
            if (_inventory != null)
            {
                UnregisterTankardStorageInventory(_inventory);
            }

            Destroy(gameObject);
        }
    }
}
