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

[HarmonyPatch(typeof(Container), nameof(Container.IsInUse))]
internal static class UsefulTankardsContainerIsInUsePatch
{
    private static bool Prefix(Container __instance, ref bool __result)
    {
        if (!TankardStorageSystem.IsTankardStorageContainer(__instance))
        {
            return true;
        }

        __result = ValheimAccess.GetContainerInUse(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.Save))]
internal static class UsefulTankardsContainerSavePatch
{
    private static bool Prefix(Container __instance)
    {
        return !TankardStorageSystem.TrySaveTankardStorageContainer(__instance);
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.Load))]
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

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
internal static class UsefulTankardsInventoryGuiUpdatePatch
{
    private static void Prefix(InventoryGui __instance)
    {
        TankardStorageSystem.TryHandleInventoryGuiUseInput(__instance);
    }
}

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.CloseContainer))]
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

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
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
