using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace UsefulTankards;

internal sealed class TankardProfile
{
    internal TankardProfile(
        string prefabName,
        ConfigEntry<int> durability,
        ConfigEntry<float> cooldownReduction,
        ConfigEntry<float> durationBonus,
        ConfigEntry<int> storageSlots)
    {
        PrefabName = prefabName;
        Durability = durability;
        CooldownReduction = cooldownReduction;
        DurationBonus = durationBonus;
        StorageSlots = storageSlots;
    }

    internal string PrefabName { get; }
    internal ConfigEntry<int> Durability { get; }
    internal ConfigEntry<float> CooldownReduction { get; }
    internal ConfigEntry<float> DurationBonus { get; }
    internal ConfigEntry<int> StorageSlots { get; }

    internal int DurabilityUses => Math.Max(0, Durability.Value);
    internal int TankardStorageSlots => Math.Max(0, StorageSlots.Value);
    internal float CooldownReductionMultiplier => Math.Max(0f, 1f - Clamp(CooldownReduction.Value, 0f, 0.95f));
    internal float DurationBonusMultiplier => 1f + Math.Max(0f, DurationBonus.Value);
    internal float CooldownReductionPercent => Clamp(CooldownReduction.Value, 0f, 0.95f) * 100f;
    internal float DurationBonusPercent => Math.Max(0f, DurationBonus.Value) * 100f;

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}

internal static class TankardTweaks
{
    private static readonly Dictionary<string, TankardProfile> Profiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DurabilityBaseline> DurabilityBaselines = new(StringComparer.OrdinalIgnoreCase);

    [ThreadStatic]
    private static TankardProfile? _currentUseContext;

    internal static TankardProfile? CurrentUseContext
    {
        get => _currentUseContext;
        set => _currentUseContext = value;
    }

    internal static void RegisterProfile(
        UsefulTankardsPlugin plugin,
        string section,
        string prefabName,
        int durability,
        float cooldownReduction,
        float durationBonus,
        int storageSlots)
    {
        TankardProfile profile = new TankardProfile(
            prefabName,
            plugin.ConfigEntry(
                section,
                "Durability Uses",
                durability,
                new ConfigDescription("Tankard uses before it can no longer be used. 0 disables durability changes for this tankard.", new AcceptableValueRange<int>(0, 1000)),
                order: 400),
            plugin.ConfigEntry(
                section,
                "Potion Cooldown Reduction",
                cooldownReduction,
                new ConfigDescription("Multiplier-style reduction for pure over-time potions drunk through this tankard. 0.10 means -10%.", new AcceptableValueRange<float>(0f, 0.95f)),
                order: 300),
            plugin.ConfigEntry(
                section,
                "Buff Duration Bonus",
                durationBonus,
                new ConfigDescription("Duration bonus for non-over-time buffs drunk through this tankard. 0.10 means +10%.", new AcceptableValueRange<float>(0f, 10f)),
                order: 200),
            plugin.ConfigEntry(
                section,
                "Storage Slots",
                storageSlots,
                new ConfigDescription("Number of mead storage slots in this tankard. 0 disables storage for this tankard.", new AcceptableValueRange<int>(0, 20)),
                order: 100));
        profile.Durability.SettingChanged += (_, _) => ApplyItemDefinitions();
        Profiles[prefabName] = profile;
    }

    internal static bool TryGetProfile(ItemDrop.ItemData? item, out TankardProfile profile)
    {
        profile = null!;
        if (!UsefulTankardsPlugin.ModEnabled || item == null)
        {
            return false;
        }

        string prefabName = GetPrefabName(item);
        return prefabName.Length > 0 && Profiles.TryGetValue(prefabName, out profile);
    }

    internal static bool TryGetProfile(GameObject? prefab, out TankardProfile profile)
    {
        profile = null!;
        if (!UsefulTankardsPlugin.ModEnabled || prefab == null || (UnityEngine.Object)(object)prefab == null)
        {
            return false;
        }

        string prefabName = CleanPrefabName(((UnityEngine.Object)prefab).name);
        return prefabName.Length > 0 && Profiles.TryGetValue(prefabName, out profile);
    }

    internal static void ApplyItemDefinitions()
    {
        if (ObjectDB.instance != null)
        {
            foreach (GameObject itemPrefab in ObjectDB.instance.m_items)
            {
                ApplyItemDefinition(itemPrefab);
            }
        }

        if (ZNetScene.instance == null)
        {
            return;
        }

        foreach (TankardProfile profile in Profiles.Values)
        {
            ApplyItemDefinition(ZNetScene.instance.GetPrefab(profile.PrefabName));
        }
    }

    internal static void ModifyEffectForCurrentTankard(StatusEffect effect)
    {
        TankardProfile? profile = CurrentUseContext;
        if (!UsefulTankardsPlugin.ModEnabled || profile == null || effect.m_ttl <= 0f)
        {
            return;
        }

        float multiplier = IsPureOverTimeEffect(effect)
            ? profile.CooldownReductionMultiplier
            : profile.DurationBonusMultiplier;
        effect.m_ttl *= multiplier;
    }

    internal static void AppendTankardTooltip(ItemDrop.ItemData item, ref string tooltip)
    {
        if (!TryGetProfile(item, out TankardProfile profile))
        {
            return;
        }

        List<string> lines = new();
        if (UsefulTankardsPlugin.TankardStorageEnabled && profile.TankardStorageSlots > 0)
        {
            lines.Add(TankardLocalization.Localize(TankardLocalization.OpenHintKey));
        }

        if (profile.CooldownReductionPercent > 0f)
        {
            lines.Add(FormatTooltip(TankardLocalization.CooldownReductionKey, profile.CooldownReductionPercent));
        }

        if (profile.DurationBonusPercent > 0f)
        {
            lines.Add(FormatTooltip(TankardLocalization.BuffDurationBonusKey, profile.DurationBonusPercent));
        }

        if (lines.Count > 0)
        {
            tooltip += "\n\n<color=orange>" + string.Join("\n", lines.Where(line => !string.IsNullOrWhiteSpace(line))) + "</color>";
        }
    }

    private static string FormatTooltip(string key, float percentage)
    {
        string template = TankardLocalization.Localize(key);
        string value = percentage.ToString("0.#", CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.InvariantCulture, template, value);
    }

    private static void ApplyItemDefinition(GameObject? prefab)
    {
        if (prefab == null || (UnityEngine.Object)(object)prefab == null)
        {
            return;
        }

        GameObject prefabObject = prefab;
        string prefabName = CleanPrefabName(((UnityEngine.Object)prefabObject).name);
        if (!Profiles.TryGetValue(prefabName, out TankardProfile profile))
        {
            return;
        }

        ItemDrop itemDrop = prefabObject.GetComponent<ItemDrop>();
        if ((UnityEngine.Object)(object)itemDrop == null)
        {
            return;
        }

        ItemDrop.ItemData.SharedData shared = itemDrop.m_itemData.m_shared;
        if (!DurabilityBaselines.ContainsKey(prefabName))
        {
            DurabilityBaselines[prefabName] = DurabilityBaseline.From(shared);
        }

        if (!UsefulTankardsPlugin.ModEnabled || profile.DurabilityUses <= 0)
        {
            DurabilityBaselines[prefabName].Restore(shared);
            return;
        }

        shared.m_useDurability = true;
        shared.m_maxDurability = profile.DurabilityUses;
        shared.m_durabilityPerLevel = 0f;
        shared.m_useDurabilityDrain = 1f;
        shared.m_durabilityDrain = 0f;
        shared.m_canBeReparied = false;
        shared.m_destroyBroken = false;
    }

    private static bool IsPureOverTimeEffect(StatusEffect effect)
    {
        if (effect is not SE_Stats stats)
        {
            return false;
        }

        bool hasOverTime = stats.m_healthOverTime > 0f || stats.m_staminaOverTime != 0f || stats.m_eitrOverTime != 0f;
        if (!hasOverTime)
        {
            return false;
        }

        return stats.m_tickInterval == 0f
               && stats.m_healthPerTick == 0f
               && stats.m_healthPerTickMinHealthPercentage == 0f
               && stats.m_healthUpFront == 0f
               && stats.m_staminaUpFront == 0f
               && stats.m_staminaDrainPerSec == 0f
               && stats.m_runStaminaDrainModifier == 0f
               && stats.m_jumpStaminaUseModifier == 0f
               && stats.m_attackStaminaUseModifier == 0f
               && stats.m_blockStaminaUseModifier == 0f
               && stats.m_blockStaminaUseFlatValue == 0f
               && stats.m_dodgeStaminaUseModifier == 0f
               && stats.m_swimStaminaUseModifier == 0f
               && stats.m_homeItemStaminaUseModifier == 0f
               && stats.m_sneakStaminaUseModifier == 0f
               && stats.m_runStaminaUseModifier == 0f
               && stats.m_adrenalineUpFront == 0f
               && stats.m_adrenalineModifier == 0f
               && stats.m_staggerModifier == 0f
               && stats.m_timedBlockBonus == 0f
               && stats.m_eitrUpFront == 0f
               && stats.m_healthRegenMultiplier == 1f
               && stats.m_staminaRegenMultiplier == 1f
               && stats.m_eitrRegenMultiplier == 1f
               && stats.m_addArmor == 0f
               && stats.m_armorMultiplier == 0f
               && stats.m_raiseSkillModifier == 0f
               && stats.m_skillLevelModifier == 0f
               && stats.m_skillLevelModifier2 == 0f
               && (stats.m_mods == null || stats.m_mods.Count == 0)
               && stats.m_damageModifier == 1f
               && !stats.m_percentigeDamageModifiers.HaveDamage()
               && stats.m_noiseModifier == 0f
               && stats.m_stealthModifier == 0f
               && stats.m_addMaxCarryWeight == 0f
               && stats.m_speedModifier == 0f
               && stats.m_swimSpeedModifier == 0f
               && stats.m_jumpModifier == Vector3.zero
               && stats.m_maxMaxFallSpeed == 0f
               && stats.m_fallDamageModifier == 0f
               && stats.m_windMovementModifier == 0f
               && stats.m_windRunStaminaModifier == 0f
               && (UnityEngine.Object)(object)stats.m_pheromoneTarget == null
               && stats.m_pheromoneSpawnChanceOverride == 0f
               && stats.m_pheromoneSpawnMinLevel == 0
               && stats.m_pheromoneLevelUpMultiplier == 1f
               && stats.m_pheromoneMaxInstanceOverride == 0
               && !stats.m_pheromoneFlee;
    }

    private static string GetPrefabName(ItemDrop.ItemData item)
    {
        return (UnityEngine.Object)(object)item.m_dropPrefab != null
            ? CleanPrefabName(((UnityEngine.Object)item.m_dropPrefab).name)
            : "";
    }

    internal static string GetCleanPrefabName(ItemDrop.ItemData item)
    {
        return GetPrefabName(item);
    }

    private static string CleanPrefabName(string name)
    {
        const string cloneSuffix = "(Clone)";
        return name.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - cloneSuffix.Length)
            : name;
    }

    private readonly struct DurabilityBaseline
    {
        private readonly bool _useDurability;
        private readonly bool _destroyBroken;
        private readonly bool _canBeRepaired;
        private readonly float _maxDurability;
        private readonly float _durabilityPerLevel;
        private readonly float _useDurabilityDrain;
        private readonly float _durabilityDrain;

        private DurabilityBaseline(ItemDrop.ItemData.SharedData shared)
        {
            _useDurability = shared.m_useDurability;
            _destroyBroken = shared.m_destroyBroken;
            _canBeRepaired = shared.m_canBeReparied;
            _maxDurability = shared.m_maxDurability;
            _durabilityPerLevel = shared.m_durabilityPerLevel;
            _useDurabilityDrain = shared.m_useDurabilityDrain;
            _durabilityDrain = shared.m_durabilityDrain;
        }

        internal static DurabilityBaseline From(ItemDrop.ItemData.SharedData shared)
        {
            return new DurabilityBaseline(shared);
        }

        internal void Restore(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_useDurability = _useDurability;
            shared.m_destroyBroken = _destroyBroken;
            shared.m_canBeReparied = _canBeRepaired;
            shared.m_maxDurability = _maxDurability;
            shared.m_durabilityPerLevel = _durabilityPerLevel;
            shared.m_useDurabilityDrain = _useDurabilityDrain;
            shared.m_durabilityDrain = _durabilityDrain;
        }
    }
}
