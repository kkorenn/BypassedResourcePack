using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BypassedResourcePack
{
    // Game-visual tweaks ported from AdofaiTweaks (HideUiElements + PlanetColor):
    //  - hide judgement text popups, hide miss indicators
    //  - recolor red/blue planet bodies, hide tail + ring
    // All gated by settings; applied via Harmony like AdofaiTweaks does.
    internal static class GameTweaksPatches
    {
        private static readonly Vector3 OffScreen = new Vector3(123456f, 123456f, 123456f);
        private static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
        private static string cachedPlanet1Hex;
        private static string cachedPlanet2Hex;
        private static Color cachedPlanet1Color;
        private static Color cachedPlanet2Color;
        private static bool cachedPlanet1Valid;
        private static bool cachedPlanet2Valid;

        private static bool TryHex(string hex, out Color color)
        {
            color = Color.white;
            return Settings.TryNormalizePlanetHex(hex, out string normalized) &&
                   ColorUtility.TryParseHtmlString("#" + normalized, out color);
        }

        private static void RefreshPlanetColors(Settings settings)
        {
            if (string.Equals(cachedPlanet1Hex, settings.Planet1Hex, System.StringComparison.Ordinal) &&
                string.Equals(cachedPlanet2Hex, settings.Planet2Hex, System.StringComparison.Ordinal)) return;

            cachedPlanet1Hex = settings.Planet1Hex;
            cachedPlanet2Hex = settings.Planet2Hex;
            cachedPlanet1Valid = TryHex(cachedPlanet1Hex, out cachedPlanet1Color);
            cachedPlanet2Valid = TryHex(cachedPlanet2Hex, out cachedPlanet2Color);
        }

        // 1 = red gameplay planet, 2 = blue gameplay planet, 0 = decoration/unknown.
        private static int GameplayPlanetRole(PlanetRenderer renderer)
        {
            try
            {
                var controller = scrController.instance;
                if (controller == null) return 0;
                if (controller.planetRed != null && renderer == controller.planetRed.planetRenderer) return 1;
                if (controller.planetBlue != null && renderer == controller.planetBlue.planetRenderer) return 2;
            }
            catch { }
            return 0;
        }

        // Menus / title (no active level or editor) — where decorative planets like the
        // continue-button pair live and should be recoloured. Inside a level/editor only the
        // real gameplay pair is touched, never level decorations.
        private static bool IsMenuContext()
        {
            try
            {
                scrController controller = scrController.instance;
                if (controller != null && controller.gameworld) return false;
                return scnGame.instance == null && scnEditor.instance == null;
            }
            catch { return false; }
        }

        // Gameplay pair -> mapped by identity. Decorative planets -> mapped by hue, but ONLY
        // in a menu context, so custom-/main-level decorations are left alone.
        private static void ApplyBody(PlanetRenderer r, ref Color color)
        {
            ApplyBody(r, GameplayPlanetRole(r), ref color);
        }

        private static void ApplyBody(PlanetRenderer r, int role, ref Color color)
        {
            Settings s = Main.Settings;
            if (s == null || !s.PlanetColorOn) return;
            RefreshPlanetColors(s);

            if (role == 1 && cachedPlanet1Valid) color = cachedPlanet1Color;
            else if (role == 2 && cachedPlanet2Valid) color = cachedPlanet2Color;
            else if (role == 0 && IsMenuContext())
            {
                if (color.r >= color.b && cachedPlanet1Valid) color = cachedPlanet1Color;
                else if (cachedPlanet2Valid) color = cachedPlanet2Color;
            }
        }

        // ---- judgement text ("Perfect!" etc.) ----
        [HarmonyPatch(typeof(scrHitTextMesh), "Show")]
        private static class JudgmentTextPatch
        {
            private static void Prefix(ref Vector3 position)
            {
                if (Main.Settings != null && Main.Settings.HideJudgmentText) position = OffScreen;
            }
        }

        // ---- miss indicators ----
        [HarmonyPatch(typeof(scrMissIndicator), "Awake")]
        private static class MissIndicatorPatch
        {
            private static void Postfix(scrMissIndicator __instance)
            {
                if (Main.Settings != null && Main.Settings.HideMissIndicators)
                    __instance.transform.position = OffScreen;
            }
        }

        // ---- planet body color ----
        [HarmonyPatch(typeof(PlanetRenderer), "SetPlanetColor")]
        private static class SetPlanetColorPatch
        {
            private static void Prefix(PlanetRenderer __instance, ref Color color) => ApplyBody(__instance, ref color);
        }

        [HarmonyPatch(typeof(PlanetRenderer), "SetCoreColor")]
        private static class SetCoreColorPatch
        {
            private static void Prefix(PlanetRenderer __instance, ref Color color) => ApplyBody(__instance, ref color);
        }

        [HarmonyPatch(typeof(PlanetRenderer), "SetFaceColor")]
        private static class SetFaceColorPatch
        {
            private static void Prefix(PlanetRenderer __instance, ref Color color) => ApplyBody(__instance, ref color);
        }

        // ---- tail (gameplay pair only) ----
        [HarmonyPatch(typeof(PlanetRenderer), "SetTailColor")]
        private static class SetTailColorPatch
        {
            private static void Prefix(PlanetRenderer __instance, ref Color color)
            {
                Settings s = Main.Settings;
                if (s == null) return;
                int role = GameplayPlanetRole(__instance);
                if (s.HideTail && role != 0) { color = Transparent; return; }
                ApplyBody(__instance, role, ref color);
            }
        }

        // ---- ring (gameplay pair only) ----
        [HarmonyPatch(typeof(PlanetRenderer), "SetRingColor")]
        private static class SetRingColorPatch
        {
            private static void Prefix(PlanetRenderer __instance, ref Color color)
            {
                Settings s = Main.Settings;
                if (s == null) return;
                int role = GameplayPlanetRole(__instance);
                if (s.HideRing && role != 0) { color = Transparent; return; }
                ApplyBody(__instance, role, ref color);
            }
        }

        // ---- main-menu logo "FIRE" / "ICE" text → planet colours ----
        [HarmonyPatch(typeof(scrLogoText), "LateUpdate")]
        private static class LogoColorPatch
        {
            private static void Postfix(scrLogoText __instance)
            {
                Settings s = Main.Settings;
                if (s == null || !s.PlanetColorOn) return;
                RefreshPlanetColors(s);
                // Only change RGB; keep each image's current alpha so the game's
                // fade-in/out (when the planet leaves the centre tile) still works.
                if (cachedPlanet1Valid)
                {
                    Tint(__instance.fireImage, cachedPlanet1Color);
                    Tint(__instance.fireLight, cachedPlanet1Color);
                }
                if (cachedPlanet2Valid)
                {
                    Tint(__instance.iceImage, cachedPlanet2Color);
                    Tint(__instance.iceLight, cachedPlanet2Color);
                }
            }

            private static void Tint(UnityEngine.UI.Graphic g, Color rgb)
            {
                if (!g) return;
                Color c = g.color;
                if (c.r == rgb.r && c.g == rgb.g && c.b == rgb.b) return;
                g.color = new Color(rgb.r, rgb.g, rgb.b, c.a);
            }
        }

        // ---- detailed results screen: drop X-Acc / Checkpoints / Max-keys rows ----
        [HarmonyPatch(typeof(DetailedResults), "GenerateResults")]
        private static class DetailedResultsPatch
        {
            private static void Postfix(ref string __result)
            {
                Settings s = Main.Settings;
                if (s == null || string.IsNullOrEmpty(__result)) return;

                List<string> dropList = new List<string>(4);
                if (s.HideResultXAcc) dropList.Add(ResLabel("xAccuracy"));
                if (s.HideResultAccuracy) dropList.Add(ResLabel("accuracy"));
                if (s.HideResultCheckpoints) dropList.Add(ResLabel("checkpoints"));
                if (s.HideResultMaxKeys) dropList.Add(ResLabel("maximumUsedKeys"));
                if (dropList.Count == 0) return;
                string[] drop = dropList.ToArray();

                string[] rows = __result.Split('\n');
                List<string> kept = new List<string>(rows.Length);
                foreach (string row in rows)
                {
                    bool remove = false;
                    foreach (string lbl in drop)
                        if (!string.IsNullOrEmpty(lbl) && row.Contains(lbl)) { remove = true; break; }
                    if (!remove) kept.Add(row);
                }
                __result = string.Join("\n", kept.ToArray());
            }

            private static string ResLabel(string key)
            {
                try { return RDString.Get("status.results." + key); }
                catch { return null; }
            }
        }
    }
}
