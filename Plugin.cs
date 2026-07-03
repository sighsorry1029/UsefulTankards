using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace UsefulTankards;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class UsefulTankardsPlugin : BaseUnityPlugin
{
    public const string ModName = "UsefulTankards";
    public const string ModVersion = "0.1.0";
    public const string Author = "sighsorry";
    public const string ModGuid = Author + "." + ModName;

    internal static ManualLogSource Log = null!;

    internal static ConfigEntry<bool> EnableMod = null!;
    internal static ConfigEntry<bool> MovementWhileDrinking = null!;

    private readonly Harmony _harmony = new(ModGuid);

    private void Awake()
    {
        Log = Logger;

        EnableMod = Config.Bind("01 - General", "Enable", true, "Enable UsefulTankards tweaks.");
        MovementWhileDrinking = Config.Bind("01 - General", "Movement While Drinking", true, "Allow movement and rotation while drinking through a tankard.");

        EnableMod.SettingChanged += OnConfigChanged;
        MovementWhileDrinking.SettingChanged += OnConfigChanged;

        TankardTweaks.RegisterProfile(
            Config,
            "02 - Tankard",
            "Tankard",
            durability: 10,
            cooldownReduction: 0.10f,
            durationBonus: 0.10f);
        TankardTweaks.RegisterProfile(
            Config,
            "03 - Anniversary Tankard",
            "TankardAnniversary",
            durability: 15,
            cooldownReduction: 0.20f,
            durationBonus: 0.20f);
        TankardTweaks.RegisterProfile(
            Config,
            "04 - Dvergr Tankard",
            "Tankard_dvergr",
            durability: 20,
            cooldownReduction: 0.30f,
            durationBonus: 0.30f);

        _harmony.PatchAll();
    }

    private static void OnConfigChanged(object sender, EventArgs args)
    {
        TankardTweaks.ApplyItemDefinitions();
    }

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
    }
}
