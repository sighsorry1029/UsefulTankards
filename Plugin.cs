using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace UsefulTankards;

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInDependency(ValheimCuisineGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class UsefulTankardsPlugin : BaseUnityPlugin
{
    public const string ModName = "UsefulTankards";
    public const string ModVersion = "1.0.2";
    public const string Author = "sighsorry";
    public const string ModGuid = Author + "." + ModName;
    private const string ValheimCuisineGuid = "XutzBR.ValheimCuisine";

    internal static ManualLogSource Log = null!;

    private static readonly ConfigSync ConfigSync = new(ModGuid)
    {
        DisplayName = ModName,
        CurrentVersion = ModVersion,
        MinimumRequiredVersion = ModVersion
    };

    internal enum Toggle
    {
        On = 1,
        Off = 0
    }

    private static ConfigEntry<Toggle> ServerConfigLocked = null!;
    private static ConfigEntry<float> MovementWhileDrinking = null!;
    private static ConfigEntry<float> TankardAnimationSpeed = null!;

    internal static float MovementWhileDrinkingMultiplier => Math.Min(1f, Math.Max(0f, MovementWhileDrinking.Value));
    internal static float TankardAnimationSpeedMultiplier => Math.Min(3f, Math.Max(1f, TankardAnimationSpeed.Value));

    private readonly Harmony _harmony = new(ModGuid);
    private static bool _roundingMovementWhileDrinking;
    private static bool _roundingTankardAnimationSpeed;

    private void Awake()
    {
        Log = Logger;
        ValheimAccess.Validate();

        ServerConfigLocked = ConfigEntry(
            "1 - General",
            "Lock Configuration",
            Toggle.On,
            "If on, the configuration is locked and can be changed by server admins only.",
            order: 1000);
        _ = ConfigSync.AddLockingConfigEntry(ServerConfigLocked);

        MovementWhileDrinking = ConfigEntry(
            "1 - General",
            "Movement While Drinking",
            0.5f,
            new ConfigDescription(
                "Movement and rotation speed multiplier while drinking through a tankard. 0 keeps vanilla movement lock; 1 allows normal movement.",
                new AcceptableValueRange<float>(0f, 1f)),
            order: 900);
        TankardAnimationSpeed = ConfigEntry(
            "1 - General",
            "Tankard Animation Speed",
            2f,
            new ConfigDescription(
                "Drinking animation speed multiplier for tankards. 1 keeps vanilla speed; 2 is twice as fast; 3 is three times as fast.",
                new AcceptableValueRange<float>(1f, 3f)),
            order: 850);

        NormalizeFloatConfig(MovementWhileDrinking, 0f, 1f, ref _roundingMovementWhileDrinking);
        NormalizeFloatConfig(TankardAnimationSpeed, 1f, 3f, ref _roundingTankardAnimationSpeed);
        MovementWhileDrinking.SettingChanged += OnMovementWhileDrinkingChanged;
        TankardAnimationSpeed.SettingChanged += OnTankardAnimationSpeedChanged;

        TankardLocalization.Register();

        TankardTweaks.RegisterProfile(
            this,
            "02 - Tankard",
            "Tankard",
            durability: 10,
            cooldownReduction: 0.10f,
            durationBonus: 0.10f,
            storageSlots: 3);
        TankardRecipes.RegisterRecipe(
            this,
            "02 - Tankard",
            "Tankard",
            "piece_workbench, 1",
            "FineWood:5, Resin:2");

        TankardTweaks.RegisterProfile(
            this,
            "03 - Anniversary Tankard",
            "TankardAnniversary",
            durability: 15,
            cooldownReduction: 0.20f,
            durationBonus: 0.20f,
            storageSlots: 4);
        TankardRecipes.RegisterRecipe(
            this,
            "03 - Anniversary Tankard",
            "TankardAnniversary",
            "piece_workbench, 1",
            "Bronze:2, TrollHide:2, Iron:2");

        TankardTweaks.RegisterProfile(
            this,
            "04 - Dvergr Tankard",
            "Tankard_dvergr",
            durability: 20,
            cooldownReduction: 0.30f,
            durationBonus: 0.30f,
            storageSlots: 5);
        if (Chainloader.PluginInfos.ContainsKey(ValheimCuisineGuid))
        {
            TankardTweaks.RegisterProfile(
                this,
                "05 - Goblet of Kings",
                "VC_GoK",
                durability: 20,
                cooldownReduction: 0.30f,
                durationBonus: 0.30f,
                storageSlots: 5);
        }

        _harmony.PatchAll();
    }

    private static void OnMovementWhileDrinkingChanged(object sender, EventArgs args)
    {
        NormalizeFloatConfig(MovementWhileDrinking, 0f, 1f, ref _roundingMovementWhileDrinking);
    }

    private static void OnTankardAnimationSpeedChanged(object sender, EventArgs args)
    {
        NormalizeFloatConfig(TankardAnimationSpeed, 1f, 3f, ref _roundingTankardAnimationSpeed);
    }

    private static void NormalizeFloatConfig(
        ConfigEntry<float> configEntry,
        float minimum,
        float maximum,
        ref bool normalizing)
    {
        if (normalizing)
        {
            return;
        }

        float clamped = Math.Min(maximum, Math.Max(minimum, configEntry.Value));
        float rounded = (float)Math.Round(clamped, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(configEntry.Value - rounded) <= 0.0001f)
        {
            return;
        }

        try
        {
            normalizing = true;
            configEntry.Value = rounded;
        }
        finally
        {
            normalizing = false;
        }
    }

    private void OnDestroy()
    {
        try
        {
            TankardStorageSystem.CloseOpenTankardStorage();
        }
        finally
        {
            try
            {
                UsefulTankardsTankardAnimationSpeed.RestoreAll();
            }
            finally
            {
                _harmony.UnpatchSelf();
            }
        }
    }

    internal ConfigEntry<T> ConfigEntry<T>(
        string group,
        string name,
        T value,
        ConfigDescription description,
        bool synchronizedSetting = true,
        int? order = null)
    {
        object[] tags = description.Tags ?? Array.Empty<object>();
        if (order != null)
        {
            object[] orderedTags = new object[tags.Length + 1];
            Array.Copy(tags, orderedTags, tags.Length);
            orderedTags[tags.Length] = new ConfigurationManagerAttributes { Order = order.Value };
            tags = orderedTags;
        }

        ConfigDescription extendedDescription = new(
            description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
            description.AcceptableValues,
            tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
        return configEntry;
    }

    internal ConfigEntry<T> ConfigEntry<T>(
        string group,
        string name,
        T value,
        string description,
        bool synchronizedSetting = true,
        int? order = null)
    {
        return ConfigEntry(group, name, value, new ConfigDescription(description), synchronizedSetting, order);
    }

    private sealed class ConfigurationManagerAttributes
    {
        public int? Order = null;
    }
}
