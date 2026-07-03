using HarmonyLib;

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
        if (UsefulTankardsPlugin.EnableMod.Value
            && UsefulTankardsPlugin.MovementWhileDrinking.Value
            && __instance.InAttack()
            && TankardTweaks.TryGetProfile(__instance.GetCurrentWeapon(), out _))
        {
            __result = 1f;
        }
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackSpeedFactorRotation))]
internal static class UsefulTankardsAttackRotationSpeedPatch
{
    private static void Postfix(Humanoid __instance, ref float __result)
    {
        if (UsefulTankardsPlugin.EnableMod.Value
            && UsefulTankardsPlugin.MovementWhileDrinking.Value
            && __instance.InAttack()
            && TankardTweaks.TryGetProfile(__instance.GetCurrentWeapon(), out _))
        {
            __result = 1f;
        }
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

[HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.Setup))]
internal static class UsefulTankardsStatusEffectSetupPatch
{
    private static void Prefix(StatusEffect __instance)
    {
        TankardTweaks.ModifyEffectForCurrentTankard(__instance);
    }
}
