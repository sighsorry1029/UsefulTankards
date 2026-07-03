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

    private static readonly HashSet<Inventory> StorageInventories = new();
    private static readonly Dictionary<Inventory, ItemDrop.ItemData> InventoryOwners = new();

    internal static bool IsTankardStorageInventory(Inventory inventory)
    {
        return inventory != null && StorageInventories.Contains(inventory);
    }

    internal static bool IsTankardStorageContainer(Container container)
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

    internal static bool TryHandleInventoryGuiUseInput(InventoryGui inventoryGui)
    {
        if (!UsefulTankardsPlugin.EnableMod.Value ||
            !UsefulTankardsPlugin.TankardStorage.Value ||
            inventoryGui == null ||
            Player.m_localPlayer == null)
        {
            return false;
        }

        if (IsTankardStorageContainer(inventoryGui.m_currentContainer))
        {
            if (!ZInput.GetButtonDown("Use"))
            {
                return false;
            }

            ZInput.ResetButtonStatus("Use");
            inventoryGui.CloseContainer();
            return true;
        }

        if (inventoryGui.m_currentContainer != null || !ZInput.GetButtonDown("Use"))
        {
            return false;
        }

        ItemDrop.ItemData hoveredItem = inventoryGui.m_playerGrid.GetItem(new Vector2i(Mathf.RoundToInt(Input.mousePosition.x), Mathf.RoundToInt(Input.mousePosition.y)));
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
        if (!UsefulTankardsPlugin.EnableMod.Value ||
            !UsefulTankardsPlugin.TankardStorage.Value ||
            !UsefulTankardsPlugin.DrinkStoredMeadsOnUse.Value ||
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
            player.GetInventory()?.Changed();
            return true;
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

            _inventory.m_onChanged += Save;
            container.m_name = tankard.m_shared.m_name;
            container.m_width = width;
            container.m_height = height;
            container.m_inventory = _inventory;
            container.m_inUse = true;
            tankard.m_customData[StorageSlotsKey] = slots.ToString();
            Save();
        }

        private void Update()
        {
            if (!_closed &&
                _container != null &&
                InventoryGui.instance != null &&
                InventoryGui.instance.m_currentContainer != _container)
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
                _inventory.m_onChanged -= Save;
                UnregisterTankardStorageInventory(_inventory);
            }

            Destroy(gameObject);
        }
    }
}
