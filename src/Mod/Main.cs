using System;
using HarmonyLib;
using UnityModManagerNet;

namespace BypassedResourcePack
{
    public static class Main
    {
        private const string HarmonyId = "koren.bypassedresourcepack";

        internal static UnityModManager.ModEntry Mod;
        internal static Settings Settings;
        internal static bool Enabled = true;

        private static Harmony harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Enabled = true;

            try
            {
                Settings = UnityModManager.ModSettings.Load<Settings>(modEntry) ?? new Settings();
            }
            catch (Exception ex)
            {
                modEntry.Logger.Log("[warning] settings load failed, using defaults: " + ex.Message);
                Settings = new Settings();
            }

            Localization.Initialize(modEntry, Settings.language);

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = SettingsGui.OnGUI;
            modEntry.OnSaveGUI = SettingsGui.OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;
            modEntry.OnUnload = OnUnload;

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(Main).Assembly);

            Log.Info("BypassedResourcePack loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            if (!value)
            {
                DiscordAutoDeafen.Stop();
                OverlayerOverlay.Hide();
                OverlayerOverlay.RestoreTitle();
            }
            Log.Info(value ? "BypassedResourcePack enabled." : "BypassedResourcePack disabled.");
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            OverlayerOverlay.Tick();
            DiscordAutoDeafen.Tick();
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            try
            {
                Settings?.Save(modEntry);
                DiscordAutoDeafen.Stop();
                OverlayerOverlay.Destroy();
                harmony?.UnpatchAll(HarmonyId);
                Log.Info("BypassedResourcePack unloaded.");
            }
            catch (Exception ex)
            {
                modEntry.Logger.Log("unload warning: " + ex.Message);
            }

            return true;
        }
    }
}
