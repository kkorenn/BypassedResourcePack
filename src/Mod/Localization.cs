using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace BypassedResourcePack
{
    // Flat key -> string tables loaded from localization/<lang>.json next to the mod.
    // Missing keys fall back to English, then to the raw key.
    internal static class Localization
    {
        private const string DefaultLanguage = "en";
        private static readonly string[] BundledLanguages = { "en", "kr", "cn" };

        private static readonly Dictionary<string, Dictionary<string, string>> tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static UnityModManager.ModEntry entry;

        internal static string CurrentLanguage { get; private set; } = DefaultLanguage;

        internal static void Initialize(UnityModManager.ModEntry modEntry, string language)
        {
            entry = modEntry;
            foreach (string lang in BundledLanguages)
                LoadLanguage(lang);

            // First run: seed from the game's language instead of the "en" default.
            if (Main.Settings != null && !Main.Settings.languageInitialized)
            {
                language = DefaultFromGameLanguage();
                Main.Settings.languageInitialized = true;
            }

            SetLanguage(language);
        }

        private static string DefaultFromGameLanguage()
        {
            try
            {
                switch (RDString.language)
                {
                    case UnityEngine.SystemLanguage.Korean:
                        return "kr";
                    case UnityEngine.SystemLanguage.Chinese:
                    case UnityEngine.SystemLanguage.ChineseSimplified:
                    case UnityEngine.SystemLanguage.ChineseTraditional:
                        return "cn";
                    default:
                        return DefaultLanguage;
                }
            }
            catch
            {
                return DefaultLanguage;
            }
        }

        internal static bool SetLanguage(string language)
        {
            string normalized = NormalizeLanguage(language);
            if (!tables.ContainsKey(normalized))
                LoadLanguage(normalized);
            if (!tables.ContainsKey(normalized))
                normalized = DefaultLanguage;

            bool changed = !string.Equals(CurrentLanguage, normalized, StringComparison.OrdinalIgnoreCase);
            CurrentLanguage = normalized;
            if (Main.Settings != null)
                Main.Settings.language = normalized;
            return changed;
        }

        internal static string Text(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";

            Dictionary<string, string> table;
            string value;
            if (tables.TryGetValue(CurrentLanguage, out table) && table.TryGetValue(key, out value))
                return value;
            if (tables.TryGetValue(DefaultLanguage, out table) && table.TryGetValue(key, out value))
                return value;
            return key;
        }

        internal static string Format(string key, params object[] args)
        {
            try { return string.Format(Text(key), args); }
            catch { return Text(key); }
        }

        private static string NormalizeLanguage(string language)
        {
            if (string.IsNullOrEmpty(language)) return DefaultLanguage;
            switch (language.ToLowerInvariant())
            {
                case "ko":
                case "ko-kr":
                case "korean":
                    return "kr";
                case "zh":
                case "zh-cn":
                case "chinese":
                    return "cn";
                case "english":
                    return "en";
                default:
                    return language;
            }
        }

        private static void LoadLanguage(string language)
        {
            string normalized = NormalizeLanguage(language);
            if (tables.ContainsKey(normalized)) return;

            string path = GetLocalizationPath(normalized);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                entry?.Logger?.Log("[localization] missing " + normalized + ".json");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var table = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                            ?? new Dictionary<string, string>();
                tables[normalized] = new Dictionary<string, string>(table, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                entry?.Logger?.Log("[localization] failed to load " + path + ": " + ex.Message);
            }
        }

        private static string GetLocalizationPath(string language)
        {
            string fileName = language + ".json";

            // Packaged next to the dll (UMM sets entry.Path to the mod folder).
            if (entry != null && !string.IsNullOrEmpty(entry.Path))
            {
                string packaged = Path.Combine(entry.Path, "localization", fileName);
                if (File.Exists(packaged)) return packaged;
            }

            // Dev fallbacks: running from the repo, or from the game's working dir.
            string cwd = Directory.GetCurrentDirectory();
            string dev = Path.Combine(cwd, "localization", fileName);
            if (File.Exists(dev)) return dev;

            return Path.Combine(cwd, "Mods", "BypassedResourcePack", "localization", fileName);
        }
    }
}
