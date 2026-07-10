using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UsefulTankards;

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
internal static class UsefulTankardsObjectDBAwakePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        TankardTweaks.ApplyItemDefinitions();
        TankardRecipes.ApplyRecipeDefinitions();
    }
}

[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
internal static class UsefulTankardsZNetSceneAwakePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        TankardTweaks.ApplyItemDefinitions();
        TankardRecipes.ApplyRecipeDefinitions();
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

[HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
internal static class UsefulTankardsCharacterAnimEventAnimationSpeedPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(Animator ___m_animator, Character ___m_character)
    {
        UsefulTankardsTankardAnimationSpeed.Apply(___m_animator, ___m_character);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Animator ___m_animator, Character ___m_character)
    {
        UsefulTankardsTankardAnimationSpeed.Apply(___m_animator, ___m_character);
    }
}

internal static class UsefulTankardsTankardAnimationSpeed
{
    private static readonly Dictionary<Humanoid, AnimationSpeedState> ActiveStates = new();

    internal static void Apply(Animator animator, Character character)
    {
        if ((Object)(object)animator == null)
        {
            return;
        }

        if (character is not Humanoid humanoid)
        {
            return;
        }

        Apply(humanoid, animator);
    }

    private static void Apply(Humanoid humanoid, Animator animator)
    {
        float speed = UsefulTankardsPlugin.TankardAnimationSpeedMultiplier;
        if (speed <= 1.0001f ||
            !humanoid.InAttack() ||
            !TankardTweaks.TryGetProfile(humanoid.GetCurrentWeapon(), out _))
        {
            Restore(humanoid);
            return;
        }

        ZSyncAnimation zAnim = humanoid.GetZAnim();
        if (!ActiveStates.TryGetValue(humanoid, out AnimationSpeedState state))
        {
            state = new AnimationSpeedState(animator, zAnim);
            ActiveStates[humanoid] = state;
        }
        else if (!ReferenceEquals(state.Animator, animator))
        {
            state.Restore();
            state = new AnimationSpeedState(animator, zAnim);
            ActiveStates[humanoid] = state;
        }

        state.Apply(speed);
    }

    internal static void Restore(Humanoid humanoid)
    {
        if (!ActiveStates.TryGetValue(humanoid, out AnimationSpeedState state))
        {
            return;
        }

        TryRestore(state);
        ActiveStates.Remove(humanoid);
    }

    internal static void RestoreAll()
    {
        foreach (AnimationSpeedState state in ActiveStates.Values)
        {
            TryRestore(state);
        }

        ActiveStates.Clear();
    }

    private static void TryRestore(AnimationSpeedState state)
    {
        try
        {
            state.Restore();
        }
        catch (System.Exception exception)
        {
            UsefulTankardsPlugin.Log.LogWarning($"Could not restore tankard animation speed: {exception.GetBaseException().Message}");
        }
    }

    private sealed class AnimationSpeedState
    {
        private readonly ZSyncAnimation _zAnim;
        private readonly float _originalSpeed;

        internal AnimationSpeedState(Animator animator, ZSyncAnimation zAnim)
        {
            Animator = animator;
            _zAnim = zAnim;
            _originalSpeed = animator.speed;
        }

        internal Animator Animator { get; }

        internal void Apply(float speed)
        {
            if ((Object)(object)Animator == null)
            {
                return;
            }

            if ((Object)(object)_zAnim != null)
            {
                _zAnim.SetSpeed(speed);
            }

            Animator.speed = speed;
        }

        internal void Restore()
        {
            if ((Object)(object)Animator == null)
            {
                return;
            }

            if ((Object)(object)_zAnim != null)
            {
                _zAnim.SetSpeed(_originalSpeed);
            }

            Animator.speed = _originalSpeed;
        }
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnDestroy))]
internal static class UsefulTankardsHumanoidDestroyPatch
{
    private static void Prefix(Humanoid __instance)
    {
        UsefulTankardsTankardAnimationSpeed.Restore(__instance);
    }
}

internal static class UsefulTankardsAttackAmmo
{
    internal static bool HasStoredDrink(Humanoid character, ItemDrop.ItemData weapon)
    {
        return TankardTweaks.TryGetProfile(weapon, out TankardProfile profile) &&
               character is Player player &&
               TankardStorageSystem.HasConsumableStoredDrink(player, weapon, profile);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.UseAmmo))]
internal static class UsefulTankardsUseAmmoPatch
{
    private static bool Prefix(Attack __instance, ref ItemDrop.ItemData ammoItem, ref bool __result, ref UseContextState __state)
    {
        __state = new UseContextState(TankardTweaks.CurrentUseContext);
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

    private static void Finalizer(UseContextState __state)
    {
        if (__state.Captured)
        {
            TankardTweaks.CurrentUseContext = __state.PreviousContext;
        }
    }

    private readonly struct UseContextState
    {
        internal UseContextState(TankardProfile? previousContext)
        {
            PreviousContext = previousContext;
            Captured = true;
        }

        internal TankardProfile? PreviousContext { get; }
        internal bool Captured { get; }
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.EquipAmmoItem))]
internal static class UsefulTankardsEquipAmmoItemPatch
{
    private static bool Prefix(Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        if (UsefulTankardsAttackAmmo.HasStoredDrink(character, weapon))
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
        if (!UsefulTankardsAttackAmmo.HasStoredDrink(character, weapon))
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

[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight), new[] { typeof(int) })]
internal static class UsefulTankardsItemWeightPatch
{
    private static void Postfix(ItemDrop.ItemData __instance, int stackOverride, ref float __result)
    {
        if (stackOverride == 0)
        {
            return;
        }

        __result += TankardStorageSystem.GetStoredDrinkWeight(__instance);
    }
}
