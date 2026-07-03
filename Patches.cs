using HarmonyLib;
using UnityEngine;

namespace UsefulTankards;

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
internal static class UsefulTankardsObjectDBAwakePatch
{
    private static void Postfix()
    {
        TankardTweaks.ApplyItemDefinitions();
    }
}

[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
internal static class UsefulTankardsZNetSceneAwakePatch
{
    private static void Postfix()
    {
        TankardTweaks.ApplyItemDefinitions();
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackSpeedFactorMovement))]
internal static class UsefulTankardsAttackMovementSpeedPatch
{
    private static void Postfix(Humanoid __instance, ref float __result)
    {
        float multiplier = UsefulTankardsPlugin.MovementWhileDrinkingMultiplier;
        if (multiplier > 0f
            && __instance.InAttack()
            && TankardTweaks.TryGetProfile(__instance.GetCurrentWeapon(), out _))
        {
            __result = Mathf.Max(__result, multiplier);
        }
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackSpeedFactorRotation))]
internal static class UsefulTankardsAttackRotationSpeedPatch
{
    private static void Postfix(Humanoid __instance, ref float __result)
    {
        float multiplier = UsefulTankardsPlugin.MovementWhileDrinkingMultiplier;
        if (multiplier > 0f
            && __instance.InAttack()
            && TankardTweaks.TryGetProfile(__instance.GetCurrentWeapon(), out _))
        {
            __result = Mathf.Max(__result, multiplier);
        }
    }
}

internal static class UsefulTankardsAttackAmmo
{
    internal static bool HasStoredDrink(Humanoid character, ItemDrop.ItemData weapon, out TankardProfile profile)
    {
        profile = null!;
        return TankardTweaks.TryGetProfile(weapon, out profile) &&
               character is Player player &&
               TankardStorageSystem.HasConsumableStoredDrink(player, weapon, profile);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.UseAmmo))]
internal static class UsefulTankardsUseAmmoPatch
{
    private static bool Prefix(Attack __instance, ref ItemDrop.ItemData ammoItem, ref bool __result, ref TankardProfile? __state)
    {
        __state = TankardTweaks.CurrentUseContext;
        if (TankardTweaks.TryGetProfile(__instance.GetWeapon(), out TankardProfile profile))
        {
            TankardTweaks.CurrentUseContext = profile;
            if (TankardStorageSystem.TryConsumeStoredDrinks(Player.m_localPlayer, __instance.GetWeapon(), profile, out ItemDrop.ItemData consumedAmmo))
            {
                ammoItem = consumedAmmo;
                __result = true;
                return false;
            }
        }

        return true;
    }

    private static void Postfix(TankardProfile? __state)
    {
        TankardTweaks.CurrentUseContext = __state;
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.EquipAmmoItem))]
internal static class UsefulTankardsEquipAmmoItemPatch
{
    private static bool Prefix(Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        if (UsefulTankardsAttackAmmo.HasStoredDrink(character, weapon, out _))
        {
            __result = true;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.HaveAmmo))]
internal static class UsefulTankardsHaveAmmoPatch
{
    private static bool Prefix(Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        if (!UsefulTankardsAttackAmmo.HasStoredDrink(character, weapon, out _))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.Setup))]
internal static class UsefulTankardsStatusEffectSetupPatch
{
    private static void Prefix(StatusEffect __instance)
    {
        TankardTweaks.ModifyEffectForCurrentTankard(__instance);
    }
}

[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int) })]
internal static class UsefulTankardsItemTooltipPatch
{
    private static void Postfix(ItemDrop.ItemData item, ref string __result)
    {
        TankardTweaks.AppendTankardTooltip(item, ref __result);
    }
}
