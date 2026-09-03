using System;
using System.Globalization;
using UnityEngine;
using UnityModManagerNet;

namespace BypassedResourcePack
{
    internal static class SettingsGui
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static GUIStyle expandStyle;
        private static GUIStyle enableStyle;
        private static GUIStyle foldoutLabelStyle;
        private static string planet1HexInput;
        private static string planet2HexInput;

        internal static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Settings s = Main.Settings;
            if (s == null) return;

            DrawGeneralSettings(s);
            GUILayout.Space(10f);

            s.SecOverlayExpanded = Foldout(s.SecOverlayExpanded, "Overlayer");
            if (s.SecOverlayExpanded) DrawOverlayer(s);
            GUILayout.Space(6f);

            s.SecTweaksExpanded = Foldout(s.SecTweaksExpanded, "Game Tweaks");
            if (s.SecTweaksExpanded) DrawGameTweaks(s);
            GUILayout.Space(6f);

            s.SecResultsExpanded = Foldout(s.SecResultsExpanded, "Detailed Results");
            if (s.SecResultsExpanded) DrawResults(s);
            GUILayout.Space(6f);

            s.SecDiscordExpanded = Foldout(s.SecDiscordExpanded, "Discord Auto-Deafen");
            if (s.SecDiscordExpanded) DrawDiscordAutoDeafen(s);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static bool Foldout(bool expanded, string label)
        {
            EnsureFeatureStyles();
            GUILayout.BeginHorizontal();
            expanded = GUILayout.Toggle(expanded, expanded ? "◢" : "▶", expandStyle);
            if (GUILayout.Button(label, foldoutLabelStyle, GUILayout.ExpandWidth(false)))
                expanded = !expanded;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return expanded;
        }

        private static void EnsureFeatureStyles()
        {
            if (expandStyle == null)
            {
                expandStyle = new GUIStyle();
                expandStyle.fixedWidth = 10f;
                expandStyle.fontSize = 15;
                expandStyle.normal.textColor = Color.white;
                expandStyle.margin = new RectOffset(4, 2, 6, 6);
            }
            if (enableStyle == null)
            {
                enableStyle = new GUIStyle(GUI.skin.toggle);
                enableStyle.fontStyle = FontStyle.Normal;
                enableStyle.margin = new RectOffset(0, 4, 4, 4);
            }
            if (foldoutLabelStyle == null)
            {
                foldoutLabelStyle = new GUIStyle(GUI.skin.label);
                foldoutLabelStyle.fontStyle = FontStyle.Normal;
                foldoutLabelStyle.margin = new RectOffset(0, 4, 4, 4);
            }
        }

        // Slider + editable number box (KRP-style).
        private static float NumField(string label, float val, float min, float max, string fmt = "0.###")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120f));
            val = GUILayout.HorizontalSlider(val, min, max, GUILayout.Width(200f));
            string str = GUILayout.TextField(val.ToString(fmt, Inv), GUILayout.Width(70f));
            float parsed;
            if (float.TryParse(str, NumberStyles.Float, Inv, out parsed))
                val = Mathf.Clamp(parsed, min, max);
            GUILayout.EndHorizontal();
            return val;
        }

        private static string TextField(string label, string val, float width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120f));
            val = GUILayout.TextField(val ?? "", GUILayout.Width(width));
            GUILayout.EndHorizontal();
            return val;
        }

        private static bool PlanetHexField(string label, ref string input, ref string value)
        {
            if (input == null) input = value ?? "";

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120f));
            input = GUILayout.TextField(input, GUILayout.Width(90f));

            if (Settings.TryNormalizePlanetHex(input, out string normalized))
            {
                input = normalized;
                value = normalized;
                GUILayout.Label("#RRGGBB", GUILayout.Width(80f));
                GUILayout.EndHorizontal();
                return true;
            }

            GUILayout.Label("Use #RRGGBB", GUILayout.Width(100f));
            GUILayout.EndHorizontal();
            return false;
        }

        // =====================================================================
        // Overlayer (collapsible, per-panel)
        // =====================================================================

        private static void DrawOverlayer(Settings s)
        {
            s.OverlayerOn = GUILayout.Toggle(s.OverlayerOn, "  Enable overlay");
            if (!s.OverlayerOn) return;

            DrawPanel("Progress bar", s.PanelProgressBar, null);
            DrawPanel("Combo", s.PanelCombo, null);
            DrawPanel("Top info", s.PanelTopInfo, () =>
            {
                s.LineXAcc = GUILayout.Toggle(s.LineXAcc, "XAcc");
                s.LineMaxAcc = GUILayout.Toggle(s.LineMaxAcc, "MaxAcc");
                s.LineProgress = GUILayout.Toggle(s.LineProgress, "Progress");
                s.LineTile = GUILayout.Toggle(s.LineTile, "Tile");
            });
            DrawPanel("Right info", s.PanelRightInfo, () =>
            {
                s.LineRuns = GUILayout.Toggle(s.LineRuns, "Runs To Here");
                s.LineTileBpm = GUILayout.Toggle(s.LineTileBpm, "TileBPM");
                s.LineCurBpm = GUILayout.Toggle(s.LineCurBpm, "CurBPM");
                s.LineKps = GUILayout.Toggle(s.LineKps, "KPS");
            });
            DrawPanel("Judgements", s.PanelJudgements, null);
            DrawPanel("Tabub", s.PanelTabub, () =>
            {
                s.TabubMaxIndex = Mathf.RoundToInt(NumField("BPM index", s.TabubMaxIndex, 100f, 4000f, "0"));
            });
        }

        private static void DrawPanel(string label, OverlayPanel p, Action lines)
        {
            EnsureFeatureStyles();
            GUILayout.BeginHorizontal();
            p.Expanded = GUILayout.Toggle(p.Expanded, p.Enabled ? (p.Expanded ? "◢" : "▶") : "", expandStyle);
            p.Enabled = GUILayout.Toggle(p.Enabled, label, enableStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!p.Expanded || !p.Enabled) return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(24f);
            GUILayout.BeginVertical();

            // Do not clamp coordinates to the visible 0..1 rectangle: a small off-screen
            // offset is part of the shipped layout (for example, Judgements.Y is -0.01).
            p.X = NumField("X", p.X, OverlayPanel.PositionMin, OverlayPanel.PositionMax);
            p.Y = NumField("Y", p.Y, OverlayPanel.PositionMin, OverlayPanel.PositionMax);
            p.Scale = NumField("Scale", p.Scale, 0.2f, 2f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Not playing", GUILayout.Width(120f));
            p.NotPlaying = GUILayout.TextField(p.NotPlaying ?? "", GUILayout.Width(220f));
            GUILayout.EndHorizontal();

            if (lines != null) lines();

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        // =====================================================================
        // Game tweaks
        // =====================================================================

        private static void DrawGameTweaks(Settings s)
        {
            s.HideTitle = GUILayout.Toggle(s.HideTitle, "  Hide song title");
            s.HideJudgmentText = GUILayout.Toggle(s.HideJudgmentText, "  Hide judgement text");
            s.HideMissIndicators = GUILayout.Toggle(s.HideMissIndicators, "  Hide miss indicators");
            s.HideTail = GUILayout.Toggle(s.HideTail, "  Hide planet tail");
            s.HideRing = GUILayout.Toggle(s.HideRing, "  Hide planet ring");
            s.PlanetColorOn = GUILayout.Toggle(s.PlanetColorOn, "  Recolor planets");

            GUILayout.BeginHorizontal();
            GUILayout.Space(24f);
            GUILayout.BeginVertical();
            bool planet1Valid = PlanetHexField("Planet 1 color", ref planet1HexInput, ref s.Planet1Hex);
            bool planet2Valid = PlanetHexField("Planet 2 color", ref planet2HexInput, ref s.Planet2Hex);
            if (!planet1Valid || !planet2Valid)
                GUILayout.Label("Invalid edits keep the last valid color.");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        // =====================================================================
        // Detailed results (which rows to drop)
        // =====================================================================

        private static void DrawResults(Settings s)
        {
            GUILayout.Label("  Hide these rows from the detailed results screen:");
            s.HideResultXAcc = GUILayout.Toggle(s.HideResultXAcc, "  X-Accuracy");
            s.HideResultAccuracy = GUILayout.Toggle(s.HideResultAccuracy, "  Accuracy");
            s.HideResultCheckpoints = GUILayout.Toggle(s.HideResultCheckpoints, "  Checkpoints used");
            s.HideResultMaxKeys = GUILayout.Toggle(s.HideResultMaxKeys, "  Maximum used keys");
        }

        // =====================================================================
        // Discord RPC auto-deafen
        // =====================================================================

        private static void DrawDiscordAutoDeafen(Settings s)
        {
            s.AutoDeafenOn = GUILayout.Toggle(s.AutoDeafenOn, "  Enable auto-deafen");
            s.AutoDeafenOnlyFromStart = GUILayout.Toggle(s.AutoDeafenOnlyFromStart, "  Only when starting from 0%");
            s.AutoDeafenAtPercent = NumField("Deafen at (%)", s.AutoDeafenAtPercent, 0f, 100f, "0.##");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Client ID", GUILayout.Width(120f));
            string newClientId = (GUILayout.TextField(s.DiscordClientId ?? "", GUILayout.Width(220f)) ?? "").Trim();
            if (!string.Equals(newClientId, s.DiscordClientId, StringComparison.Ordinal))
            {
                s.DiscordClientId = newClientId;
                s.DiscordAccessToken = ""; // token is per-app; force re-auth
            }
            if (GUILayout.Button("Watch Tutorial", GUILayout.Width(120f)))
                DiscordAutoDeafen.OpenTutorial();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Authorize", GUILayout.Width(120f));
            if (GUILayout.Button("Open Discord", GUILayout.Width(120f)))
                DiscordAutoDeafen.OpenAuthorizeUrl();
            if (GUILayout.Button("Copy URL", GUILayout.Width(90f)))
                GUIUtility.systemCopyBuffer = DiscordAutoDeafen.AuthorizeUrl();
            if (GUILayout.Button("Unlink", GUILayout.Width(90f)))
                DiscordAutoDeafen.Unlink();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Status", GUILayout.Width(120f));
            GUILayout.Label(DiscordAutoDeafen.Status);
            GUILayout.EndHorizontal();
        }

        // =====================================================================

        private static void DrawGeneralSettings(Settings settings)
        {
            bool verbose = GUILayout.Toggle(settings.VerboseLogging, "Verbose logging");
            if (verbose != settings.VerboseLogging)
            {
                settings.VerboseLogging = verbose;
                Log.Verbose("verbose logging enabled.");
            }
        }

        internal static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Main.Settings?.Save(modEntry);
        }
    }
}
