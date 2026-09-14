using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
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
    // EpicLoot's socket UI uses First (800) and can consume Use over any item.
    // Our handler only claims tankard input after respecting its modifier and prompt.
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
    private static Func<bool>? _socketGridOpen;
    private static ConfigEntry<KeyCode>? _socketModifier;
    private static bool _unavailable;

    internal static void Initialize()
    {
        Reset();
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin))
        {
            return;
        }

        try
        {
            Assembly assembly = plugin.Instance.GetType().Assembly;
            _promptOpen = ResolveFlag(assembly, "EpicLoot.InventoryPromptHost", "IsOpen");
            _socketGridOpen = ResolveFlag(assembly, "EpicLoot.ShardStones.SocketsUI", "IsSocketGridOpen");
            Type config = assembly.GetType("EpicLoot.Config.ELConfig", throwOnError: true)!;
            _socketModifier = AccessTools.Field(config, "SocketOverlayModifier")?.GetValue(null) as ConfigEntry<KeyCode>
                ?? throw new MissingFieldException(config.FullName, "SocketOverlayModifier");
            UsefulTankardsPlugin.Log.LogInfo("EpicLoot tankard input compatibility enabled (method priority 801).");
        }
        catch (Exception error)
        {
            Reset();
            _unavailable = true;
            UsefulTankardsPlugin.Log.LogWarning($"EpicLoot storage input compatibility is unavailable; leaving Use input to EpicLoot. {error.Message}");
        }
    }

    private static Func<bool> ResolveFlag(Assembly assembly, string typeName, string propertyName)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        MethodInfo getter = AccessTools.PropertyGetter(type, propertyName)
            ?? throw new MissingMethodException(typeName, "get_" + propertyName);
        return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
    }

    // Resolve once at startup; neither reflection searches nor Invoke allocations on Update.
    internal static bool ShouldYieldUse => _unavailable || (_promptOpen?.Invoke() ?? false) ||
        (_socketModifier != null && Input.GetKey(_socketModifier.Value));

    internal static bool IsSocketGridOpen => _socketGridOpen?.Invoke() ?? false;

    internal static void Reset()
    {
        _promptOpen = null;
        _socketGridOpen = null;
        _socketModifier = null;
        _unavailable = false;
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
