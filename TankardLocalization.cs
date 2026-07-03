using System;
using System.Collections.Generic;
using HarmonyLib;

namespace UsefulTankards;

internal static class TankardLocalization
{
    internal const string OpenHintKey = "$ut_tankard_open_hint";
    internal const string CooldownReductionKey = "$ut_tankard_cooldown_reduction";
    internal const string BuffDurationBonusKey = "$ut_tankard_buff_duration_bonus";

    private const string OpenHintWord = "ut_tankard_open_hint";
    private const string CooldownReductionWord = "ut_tankard_cooldown_reduction";
    private const string BuffDurationBonusWord = "ut_tankard_buff_duration_bonus";
    private const string EnglishLanguage = "english";

    private static readonly Dictionary<string, string> OpenHintByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = "Press <b>$KEY_Use</b> to open tankard storage.",
        ["korean"] = "<b>$KEY_Use</b>를 눌러 탱커드 저장소를 엽니다.",
    };

    private static readonly Dictionary<string, string> CooldownReductionByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = "Potion cooldown reduction: {0}%",
        ["korean"] = "포션 재사용 대기시간 감소: {0}%",
    };

    private static readonly Dictionary<string, string> BuffDurationBonusByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = "Buff duration bonus: {0}%",
        ["korean"] = "버프 지속시간 증가: {0}%",
    };

    internal static void Register()
    {
        if (Localization.instance != null)
        {
            Register(Localization.instance);
        }
    }

    internal static void Register(Localization localization)
    {
        if (localization == null)
        {
            return;
        }

        string languageName = NormalizeLanguageName(localization.GetSelectedLanguage());
        localization.AddWord(OpenHintWord, Get(OpenHintByLanguage, languageName));
        localization.AddWord(CooldownReductionWord, Get(CooldownReductionByLanguage, languageName));
        localization.AddWord(BuffDurationBonusWord, Get(BuffDurationBonusByLanguage, languageName));
    }

    internal static string Localize(string key)
    {
        if (Localization.instance == null)
        {
            return key;
        }

        string localized = Localization.instance.Localize(key);
        return localized.Contains("$") ? Localization.instance.Localize(localized) : localized;
    }

    private static string NormalizeLanguageName(string languageName)
    {
        return string.IsNullOrWhiteSpace(languageName)
            ? EnglishLanguage
            : languageName.Trim().Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static string Get(Dictionary<string, string> translations, string languageName)
    {
        return translations.TryGetValue(languageName, out string translation)
            ? translation
            : translations[EnglishLanguage];
    }
}

[HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
internal static class UsefulTankardsLocalizationSetupLanguagePatch
{
    private static void Postfix(Localization __instance)
    {
        TankardLocalization.Register(__instance);
    }
}
