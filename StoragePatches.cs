using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace UsefulTankards;

[HarmonyPatch(typeof(Container), "Awake")]
internal static class UsefulTankardsContainerAwakePatch
{
    private static bool Prefix(Container __instance)
    {
        return !TankardStorageSystem.IsTankardStorageContainer(__instance);
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.IsOwner))]
internal static class UsefulTankardsContainerIsOwnerPatch
{
    private static bool Prefix(Container __instance, ref bool __result)
    {
        if (!TankardStorageSystem.IsTankardStorageContainer(__instance))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
internal static class UsefulTankardsContainerSetInUsePatch
{
    private static bool Prefix(Container __instance, bool inUse)
    {
        if (!TankardStorageSystem.IsTankardStorageContainer(__instance))
        {
            return true;
        }

        ValheimAccess.SetContainerInUse(__instance, inUse);
        return false;
    }
}

[HarmonyPatch(typeof(Container), "Save")]
internal static class UsefulTankardsContainerSavePatch
{
    private static bool Prefix(Container __instance)
    {
        return !TankardStorageSystem.TrySaveTankardStorageContainer(__instance);
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.StackAll))]
internal static class UsefulTankardsContainerStackAllPatch
{
    private static bool Prefix(Container __instance)
    {
        return !TankardStorageSystem.TryStackAllTankardStorageContainer(__instance);
    }
}

[HarmonyPatch(typeof(Container), "Load")]
internal static class UsefulTankardsContainerLoadPatch
{
    private static bool Prefix(Container __instance, ref bool __result)
    {
        if (!TankardStorageSystem.IsTankardStorageContainer(__instance))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(InventoryGui), "Update")]
internal static class UsefulTankardsInventoryGuiUpdatePatch
{
    // EpicLoot's input uses First (800). Its current GUI-aware shortcut decision
    // retains prompt, modifier, remapping, and focused gamepad input ownership.
    // Put priority on the method: the installed HarmonyX drops a class-only
    // priority while merging an unannotated Prefix's default (-1).
    [HarmonyPriority(Priority.First + 1)]
    [HarmonyBefore(EpicLootStorageCompatibility.PluginGuid)]
    private static void Prefix(InventoryGui __instance)
    {
        TankardStorageSystem.TryHandleInventoryGuiUseInput(__instance);
    }
}

internal static class EpicLootStorageCompatibility
{
    internal const string PluginGuid = "randyknapp.mods.epicloot";
    private static Func<bool>? _promptOpen;
    private static Func<InventoryGui, bool>? _shardSlotPress;
    private static Func<string, bool>? _unregisterContainer;
    private static bool _containerRegistered;
    private static bool _registrationPending;
    private static bool _unavailable;
    private static bool _warned;

    internal static void Initialize()
    {
        Reset();
        if (_containerRegistered || _registrationPending) return;
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin))
        {
            return;
        }

        try
        {
            Assembly assembly = plugin.Instance.GetType().Assembly;
            Func<bool> prompt = Bind<Func<bool>>(assembly, "EpicLoot.InventoryPromptHost", "get_IsOpen", typeof(bool));
            Func<InventoryGui, bool> shardPress = Bind<Func<InventoryGui, bool>>(assembly,
                "EpicLoot.ShardStones.SocketOverlayInput", "IsShardSlotPress", typeof(bool), typeof(InventoryGui));
            var register = Bind<Func<string, Func<ItemDrop.ItemData, bool>, bool>>(assembly,
                "EpicLoot.API", "RegisterItemContainer", typeof(bool), typeof(string), typeof(Func<ItemDrop.ItemData, bool>));
            var unregister = Bind<Func<string, bool>>(assembly, "EpicLoot.API", "UnregisterItemContainer", typeof(bool), typeof(string));
            var providers = Bind<Func<Dictionary<string, List<string>>>>(assembly,
                "EpicLoot.API", "GetRegisteredProviders", typeof(Dictionary<string, List<string>>));
            if (!providers().TryGetValue("ItemContainer", out List<string> owners) || owners == null)
                throw new InvalidOperationException("the item-container registry is unavailable");
            if (owners.Contains(UsefulTankardsPlugin.ModGuid))
                throw new InvalidOperationException("the tankard container ID is already registered");

            _unregisterContainer = unregister;
            // Register can insert the callback and then throw from its reload event.
            // The precheck makes this rollback ours without removing a prior provider.
            _registrationPending = true;
            if (!register(UsefulTankardsPlugin.ModGuid, HasTankardStorage))
            {
                _registrationPending = false;
                throw new InvalidOperationException("the tankard container registration was rejected");
            }
            _containerRegistered = true;
            _registrationPending = false;
            _promptOpen = prompt;
            _shardSlotPress = shardPress;
            UsefulTankardsPlugin.Log.LogInfo("EpicLoot tankard input compatibility enabled (GUI shortcut and item-container registration).");
        }
        catch (Exception error)
        {
            StopIntegration(error);
            ReleaseContainerRegistration();
        }
    }

    private static T Bind<T>(Assembly assembly, string typeName, string methodName, Type result, params Type[] parameters) where T : Delegate
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, parameters, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters)
            throw new MissingMethodException(typeName, methodName);
        return (T)Delegate.CreateDelegate(typeof(T), method);
    }

    // Resolve once at startup; preserve EpicLoot's GUI target/focus and shortcut policy.
    internal static bool ShouldYieldUse(InventoryGui gui)
    {
        if (_unavailable)
        {
            ReleaseContainerRegistration();
            return true;
        }
        try
        {
            return (_promptOpen?.Invoke() ?? false) || (_shardSlotPress?.Invoke(gui) ?? false);
        }
        catch (Exception error)
        {
            StopIntegration(error);
            ReleaseContainerRegistration();
            return true;
        }
    }

    private static bool HasTankardStorage(ItemDrop.ItemData item)
    {
        if (!_containerRegistered || _unavailable) return false;
        try
        {
            return TankardTweaks.TryGetProfile(item, out TankardProfile profile) && profile.TankardStorageSlots > 0;
        }
        catch (Exception error)
        {
            // Do not mutate EpicLoot's provider dictionary while it is enumerating.
            // The next input boundary or Reset releases our inactive registration.
            StopIntegration(error);
            return false;
        }
    }

    private static void StopIntegration(Exception error)
    {
        _unavailable = true;
        _promptOpen = null;
        _shardSlotPress = null;
        if (_warned) return;
        _warned = true;
        UsefulTankardsPlugin.Log.LogWarning($"EpicLoot storage input compatibility is unavailable; leaving Use input to EpicLoot. {error.GetBaseException().Message}");
    }

    private static void ReleaseContainerRegistration()
    {
        if (_containerRegistered || _registrationPending)
        {
            try
            {
                _unregisterContainer!(UsefulTankardsPlugin.ModGuid);
            }
            catch (Exception error)
            {
                StopIntegration(error);
                return; // Retain the cleanup handle for Reset or the next input boundary.
            }
        }
        _containerRegistered = false;
        _registrationPending = false;
        _unregisterContainer = null;
    }

    internal static void Reset()
    {
        _promptOpen = null;
        _shardSlotPress = null;
        _unavailable = true;
        ReleaseContainerRegistration();
        if (_containerRegistered || _registrationPending) return;
        _unavailable = false;
        _warned = false;
    }
}

[HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
internal static class UsefulTankardsInventoryGuiCloseContainerPatch
{
    private static void Prefix(InventoryGui __instance, out Container __state)
    {
        __state = ValheimAccess.GetCurrentContainer(__instance)!;
    }

    private static void Postfix(Container __state)
    {
        TankardStorageSystem.CloseTankardStorage(__state);
    }
}

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
internal static class UsefulTankardsInventoryGuiHidePatch
{
    private static void Prefix(InventoryGui __instance, out Container __state)
    {
        __state = ValheimAccess.GetCurrentContainer(__instance)!;
    }

    private static void Postfix(Container __state)
    {
        TankardStorageSystem.CloseTankardStorage(__state);
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), new[] { typeof(ItemDrop.ItemData), typeof(int) })]
internal static class UsefulTankardsInventoryCanAddItemPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (TankardStorageSystem.CanAddToTankardStorage(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), new[] { typeof(GameObject), typeof(int) })]
internal static class UsefulTankardsInventoryCanAddPrefabPatch
{
    private static bool Prefix(Inventory __instance, GameObject prefab, ref bool __result)
    {
        if (TankardStorageSystem.CanAddToTankardStorage(__instance, prefab))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
internal static class UsefulTankardsInventoryAddItemPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (TankardStorageSystem.CanAddToTankardStorage(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(GameObject), typeof(int) })]
internal static class UsefulTankardsInventoryAddPrefabPatch
{
    private static bool Prefix(Inventory __instance, GameObject prefab, ref bool __result)
    {
        if (TankardStorageSystem.CanAddToTankardStorage(__instance, prefab))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
internal static class UsefulTankardsInventoryAddItemAtPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (TankardStorageSystem.CanAddToTankardStorage(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) })]
internal static class UsefulTankardsInventoryAddItemAmountPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (TankardStorageSystem.CanAddToTankardStorage(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData) })]
internal static class UsefulTankardsInventoryMoveItemPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item)
    {
        return TankardStorageSystem.CanAddToTankardStorage(__instance, item);
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
internal static class UsefulTankardsInventoryMoveItemAmountPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (TankardStorageSystem.CanAddToTankardStorage(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}
