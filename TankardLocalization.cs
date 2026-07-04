using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace UsefulTankards;

internal static class TankardLocalization
{
    internal const string OpenHintKey = "$ut_tankard_open_hint";
    internal const string CooldownReductionKey = "$ut_tankard_cooldown_reduction";
    internal const string BuffDurationBonusKey = "$ut_tankard_buff_duration_bonus";
    internal const string StoredMeadsKey = "$ut_tankard_stored_meads";

    private const string OpenHintWord = "ut_tankard_open_hint";
    private const string CooldownReductionWord = "ut_tankard_cooldown_reduction";
    private const string BuffDurationBonusWord = "ut_tankard_buff_duration_bonus";
    private const string StoredMeadsWord = "ut_tankard_stored_meads";
    private const string EnglishLanguage = "english";
    private static readonly MethodInfo? AddWordMethod = AccessTools.Method(typeof(Localization), "AddWord", new[] { typeof(string), typeof(string) });

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

    private static readonly Dictionary<string, string> StoredMeadsByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = "Stored meads:",
        ["korean"] = "저장된 벌꿀주:",
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
        AddWord(localization, OpenHintWord, Get(OpenHintByLanguage, languageName));
        AddWord(localization, CooldownReductionWord, Get(CooldownReductionByLanguage, languageName));
        AddWord(localization, BuffDurationBonusWord, Get(BuffDurationBonusByLanguage, languageName));
        AddWord(localization, StoredMeadsWord, Get(StoredMeadsByLanguage, languageName));
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

    private static void AddWord(Localization localization, string key, string value)
    {
        AddWordMethod?.Invoke(localization, new object[] { key, value });
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
