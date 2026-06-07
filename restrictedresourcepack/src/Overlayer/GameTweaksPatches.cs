using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RestrictedResourcePack
{
    // Game-visual tweaks ported from AdofaiTweaks (HideUiElements + PlanetColor):
    //  - hide judgement text popups, hide miss indicators
    //  - recolor red/blue planet bodies, hide tail + ring
    // All gated by settings; applied via Harmony like AdofaiTweaks does.
    internal static class GameTweaksPatches
    {
        private static readonly Vector3 OffScreen = new Vector3(123456f, 123456f, 123456f);
        private static readonly Color Transparent = new Color(1f, 1f, 1f, 0f);

        private static bool TryHex(string hex, out Color color)
        {
            color = Color.black;
            if (string.IsNullOrEmpty(hex)) return false;
            return ColorUtility.TryParseHtmlString(hex[0] == '#' ? hex : "#" + hex, out color);
        }

        private static bool IsRed(PlanetRenderer r)
        {
            try { var c = scrController.instance; return c != null && c.planetRed != null && r == c.planetRed.planetRenderer; }
            catch { return false; }
        }

        private static bool IsBlue(PlanetRenderer r)
        {
            try { var c = scrController.instance; return c != null && c.planetBlue != null && r == c.planetBlue.planetRenderer; }
            catch { return false; }
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
            Settings s = Main.Settings;
            if (s == null || !s.PlanetColorOn) return;

            string hex;
            if (IsRed(r)) hex = s.Planet1Hex;
            else if (IsBlue(r)) hex = s.Planet2Hex;
            else if (IsMenuContext()) hex = color.r >= color.b ? s.Planet1Hex : s.Planet2Hex;
            else return;

            if (TryHex(hex, out Color c)) color = c;
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
                if (s.HideTail && (IsRed(__instance) || IsBlue(__instance))) { color = Transparent; return; }
                ApplyBody(__instance, ref color);
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
                if (s.HideRing && (IsRed(__instance) || IsBlue(__instance))) { color = Transparent; return; }
                ApplyBody(__instance, ref color);
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
                // Only change RGB; keep each image's current alpha so the game's
                // fade-in/out (when the planet leaves the centre tile) still works.
                if (TryHex(s.Planet1Hex, out Color fire))
                {
                    Tint(__instance.fireImage, fire);
                    Tint(__instance.fireLight, fire);
                }
                if (TryHex(s.Planet2Hex, out Color ice))
                {
                    Tint(__instance.iceImage, ice);
                    Tint(__instance.iceLight, ice);
                }
            }

            private static void Tint(UnityEngine.UI.Graphic g, Color rgb)
            {
                if (!g) return;
                Color c = g.color;
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
