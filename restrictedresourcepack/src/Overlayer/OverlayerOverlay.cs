using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestrictedResourcePack
{
    // ScreenSpace overlay canvas (1920x1080 reference). Modelled on Overlayer's OverlayerText:
    // objects stay active and each frame Text.text = IsPlaying ? PlayingText : NotPlayingText.
    // Position / scale / visibility / not-playing text are read live from per-panel settings.
    internal static class OverlayerOverlay
    {
        private static GameObject root;
        private static bool built;
        private static bool buildFailed;
        private static readonly List<Panel> panels = new List<Panel>();

        private sealed class Panel
        {
            public TextMeshProUGUI Text;
            public TextMeshProUGUI Shadow;
            public Func<OverlayPanel> Cfg;   // live settings for this panel
            public Func<string> Playing;     // content while a run is live
            public bool Animated;
            public bool StateInitialized;
            public bool LayoutInitialized;
            public bool LastEnabled;
            public bool LastPlaying;
            public float LastX;
            public float LastY;
            public float LastScale;
            public int LastRevision = int.MinValue;
            public int LastSettingsKey = int.MinValue;
            public string LastNotPlaying;
        }

        private static readonly Color ShadowColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Vector2 ShadowOffset = new Vector2(2.5f, -2.5f);
        private static readonly Regex ColorTagRegex = new Regex(@"</?color[^>]*>", RegexOptions.Compiled);
        private static GameObject titleObject;
        private static bool titleHidden;
        private static string StripColor(string s) =>
            string.IsNullOrEmpty(s) || s.IndexOf('<') < 0 ? s : ColorTagRegex.Replace(s, string.Empty);

        internal static void Tick()
        {
            Settings s = Main.Settings;
            if (s == null || !Main.Enabled)
            {
                Hide();
                RestoreTitle();
                return;
            }

            try
            {
                bool playing = OverlayerStats.IsPlaying();
                SetTitleHidden(playing && s.HideTitle);

                if (buildFailed || !s.OverlayerOn)
                {
                    Hide();
                    return;
                }

                OverlayerStats.Tick();
                Build();
                if (root != null && !root.activeSelf) root.SetActive(true);

                int revision = OverlayerStats.Revision;
                int settingsKey = ContentSettingsKey(s);
                bool animate = playing && OverlayerStats.ComboAnimating;

                for (int i = 0; i < panels.Count; i++)
                {
                    Panel p = panels[i];
                    OverlayPanel cfg = p.Cfg();
                    if (cfg == null)
                    {
                        p.Text.enabled = false;
                        p.Shadow.enabled = false;
                        p.StateInitialized = false;
                        continue;
                    }
                    bool on = cfg.Enabled;

                    if (!p.StateInitialized || p.LastEnabled != on)
                    {
                        p.Text.enabled = on;
                        p.Shadow.enabled = on;
                        p.LastEnabled = on;
                    }
                    if (!on)
                    {
                        p.StateInitialized = true;
                        continue;
                    }

                    if (!p.LayoutInitialized || p.LastX != cfg.X || p.LastY != cfg.Y || p.LastScale != cfg.Scale)
                    {
                        Vector2 pos = new Vector2((cfg.X - 0.5f) * 1920f, (cfg.Y - 0.5f) * 1080f);
                        ApplyPosScale(p.Text.rectTransform, pos, cfg.Scale);
                        ApplyPosScale(p.Shadow.rectTransform, pos + ShadowOffset, cfg.Scale);
                        p.LastX = cfg.X;
                        p.LastY = cfg.Y;
                        p.LastScale = cfg.Scale;
                        p.LayoutInitialized = true;
                    }

                    string notPlaying = cfg.NotPlaying ?? "";
                    bool contentDirty = !p.StateInitialized || p.LastPlaying != playing;
                    if (playing)
                    {
                        contentDirty |= p.LastRevision != revision || p.LastSettingsKey != settingsKey;
                        contentDirty |= p.Animated && animate;
                    }
                    else
                    {
                        contentDirty |= !string.Equals(p.LastNotPlaying, notPlaying, StringComparison.Ordinal);
                    }

                    if (contentDirty)
                    {
                        string content = playing ? p.Playing() : notPlaying;
                        if (!string.Equals(p.Text.text, content, StringComparison.Ordinal)) p.Text.text = content;
                        string shadow = StripColor(content);
                        if (!string.Equals(p.Shadow.text, shadow, StringComparison.Ordinal)) p.Shadow.text = shadow;
                        p.LastPlaying = playing;
                        p.LastRevision = revision;
                        p.LastSettingsKey = settingsKey;
                        p.LastNotPlaying = notPlaying;
                    }

                    p.StateInitialized = true;
                }
            }
            catch (Exception ex)
            {
                buildFailed = true;
                Destroy();
                Log.Info("[overlayer] render failed: " + ex);
            }
        }

        private static void ApplyPosScale(RectTransform rt, Vector2 pos, float scale)
        {
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            Vector3 sc = new Vector3(scale, scale, 1f);
            if (rt.localScale != sc) rt.localScale = sc;
        }

        internal static void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        internal static void Destroy()
        {
            try { if (root != null) UnityEngine.Object.Destroy(root); } catch { }
            root = null;
            built = false;
            panels.Clear();
            RestoreTitle();
        }

        // =====================================================================
        // Build
        // =====================================================================

        private static void Build()
        {
            if (built && root != null) return;

            root = new GameObject("RestrictedResourcePack.Overlayer");
            UnityEngine.Object.DontDestroyOnLoad(root);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>().enabled = false;

            panels.Clear();

            Add("Judgements", 0.5f, 0.525f, 35f, 0f, TextAlignmentOptions.Center, true,
                () => Main.Settings.PanelJudgements, BuildJudgements);
            Add("Tabub", 0.48f, 0.5f, 44f, 0f, TextAlignmentOptions.Center, false,
                () => Main.Settings.PanelTabub, BuildTabub);
            Add("TopInfo", 0.05f, 0.5f, 38f, 0f, TextAlignmentOptions.Left, false,
                () => Main.Settings.PanelTopInfo, BuildTopInfo);
            Add("RightInfo", 0f, 0.5f, 34f, 0f, TextAlignmentOptions.Left, false,
                () => Main.Settings.PanelRightInfo, BuildRightInfo);
            Add("Combo", 0.5f, 0.45f, 130f, -30f, TextAlignmentOptions.Center, false,
                () => Main.Settings.PanelCombo, BuildCombo, true);
            Add("ProgressBar", 0.5f, 0.5f, 44f, 0f, TextAlignmentOptions.Center, false,
                () => Main.Settings.PanelProgressBar, OverlayerStats.ProgressBar);

            built = true;
        }

        private static void Add(string name, float pivotX, float pivotY, float fontSize, float lineSpacing,
            TextAlignmentOptions align, bool bold, Func<OverlayPanel> cfg, Func<string> playing,
            bool animated = false)
        {
            TMP_FontAsset font = (bold ? OverlayerFont.Bold : OverlayerFont.SemiBold) ?? FallbackFont();
            Vector2 pivot = new Vector2(pivotX, pivotY);

            TextMeshProUGUI shadow = MakeText(name + "_Shadow", font, align, fontSize, lineSpacing, ShadowColor, pivot);
            TextMeshProUGUI t = MakeText(name, font, align, fontSize, lineSpacing, Color.black, pivot);

            panels.Add(new Panel { Text = t, Shadow = shadow, Cfg = cfg, Playing = playing, Animated = animated });
        }

        private static TextMeshProUGUI MakeText(string name, TMP_FontAsset font, TextAlignmentOptions align,
            float fontSize, float lineSpacing, Color color, Vector2 pivot)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.richText = true;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = align;
            t.color = color;
            t.fontSize = fontSize;
            t.lineSpacing = lineSpacing;
            t.text = string.Empty;
            DisableWrap(t);
            if (font != null) t.font = font;

            RectTransform rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(1400f, 700f);
            return t;
        }

        private static TMP_FontAsset FallbackFont()
        {
            try { return TMP_Settings.defaultFontAsset; } catch { return null; }
        }

        private static void DisableWrap(TextMeshProUGUI t)
        {
            try
            {
                PropertyInfo p = t.GetType().GetProperty("enableWordWrapping",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanWrite) { p.SetValue(t, false, null); return; }
                FieldInfo f = t.GetType().GetField("m_enableWordWrapping",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) f.SetValue(t, false);
            }
            catch { }
        }

        // =====================================================================
        // Song title hiding (scrUIController.txtLevelName)
        // =====================================================================

        private static void SetTitleHidden(bool hide)
        {
            if (titleObject != null && titleHidden == hide)
            {
                if (titleObject.activeSelf == hide) titleObject.SetActive(!hide);
                return;
            }

            try
            {
                scrUIController ui = scrUIController.instance;
                if (ui == null || ui.txtLevelName == null) return;
                GameObject go = ui.txtLevelName.gameObject;
                if (go.activeSelf == hide) go.SetActive(!hide);
                titleObject = go;
                titleHidden = hide;
            }
            catch { }
        }

        internal static void RestoreTitle() => SetTitleHidden(false);

        // =====================================================================
        // Content builders (PlayingText)
        // =====================================================================

        private const string JFail = "38A600";
        private const string JTooEarly = "00C7C7";
        private const string JVeryEarly = "008FB0";
        private const string JEarlyPerf = "5E00B0";
        private const string JPerfect = "9E00B0";
        private const string Gold = "0028FF";

        private static string BuildJudgements()
        {
            return "<color=#" + JFail + ">" + OverlayerStats.Overloads + "</color> " +
                   "<color=#" + JTooEarly + ">" + OverlayerStats.CTE + "</color> " +
                   "<color=#" + JVeryEarly + ">" + OverlayerStats.CVE + "</color> " +
                   "<color=#" + JEarlyPerf + ">" + OverlayerStats.CEP + "</color> " +
                   "<color=#" + JPerfect + ">" + OverlayerStats.CP + "</color> " +
                   "<color=#" + JEarlyPerf + ">" + OverlayerStats.CLP + "</color> " +
                   "<color=#" + JVeryEarly + ">" + OverlayerStats.CVL + "</color> " +
                   "<color=#" + JTooEarly + ">" + OverlayerStats.CTL + "</color> " +
                   "<color=#" + JFail + ">" + OverlayerStats.MissCount + "</color>";
        }

        // White->green as accuracy climbs to 100, gold at 100.000.
        private static string AccColor(double value)
        {
            string f = OverlayerStats.F(value, 3);
            return f == "100.000" ? Gold : OverlayerStats.ColorRange(value, 99.9995, 100, "000000", "CC44CC");
        }

        private static string BuildTopInfo()
        {
            Settings s = Main.Settings;
            double xacc = OverlayerStats.XAccuracy();
            double maxacc = OverlayerStats.MaxAcc();
            List<string> lines = new List<string>(4);
            if (s.LineXAcc)
                lines.Add("<color=#660066>XAcc</color> | <color=#" + AccColor(xacc) + ">" + OverlayerStats.F(xacc, 3) + "</color>");
            if (s.LineMaxAcc)
                lines.Add("<color=#880088>MaxAcc</color> | <color=#" + AccColor(maxacc) + ">" + OverlayerStats.F(maxacc, 3) + "</color>");
            if (s.LineProgress)
                lines.Add("<color=#CC00CC>Progress</color> | " + OverlayerStats.BetterProgress());
            if (s.LineTile)
                lines.Add("<color=#EE00EE>" + OverlayerStats.CurTile + "</color> / " + OverlayerStats.TotalTile);
            return string.Join("\n", lines.ToArray());
        }

        private static string BuildRightInfo()
        {
            Settings s = Main.Settings;
            List<string> lines = new List<string>(4);
            if (s.LineRuns)
                lines.Add("<color=#666600>Runs To Here</color> | " + OverlayerStats.RunsToHere());
            if (s.LineTileBpm)
                lines.Add("<color=#999900>TileBPM</color> | " + OverlayerStats.F(OverlayerStats.TileBpm, 2));
            if (s.LineCurBpm)
                lines.Add("<color=#CCCC00>CurBPM</color> | " + OverlayerStats.F(OverlayerStats.CurBpm, 2));
            if (s.LineKps)
                lines.Add("<color=#FFFF00>KPS</color> | " + Mathf.CeilToInt((float)OverlayerStats.RecKPSWithoutPitch).ToString(CultureInfo.InvariantCulture));
            return string.Join("\n", lines.ToArray());
        }

        private static string BuildTabub()
        {
            return "<color=#88FF00>Tabub :D</color>\n\n" + OverlayerStats.Tabub();
        }

        private static string BuildCombo()
        {
            float pop = OverlayerStats.ComboPopPercent();
            string cr = OverlayerStats.ColorRange(OverlayerStats.Combo, 0, 500, "00AAAA", "AA00AA");
            return "<size=" + pop.ToString("0", CultureInfo.InvariantCulture) +
                   "%><color=#" + cr + ">" + OverlayerStats.Combo + "</color></size>\n<size=60>Combo</size>";
        }

        private static int ContentSettingsKey(Settings s)
        {
            int key = 0;
            if (s.LineXAcc) key |= 1 << 0;
            if (s.LineMaxAcc) key |= 1 << 1;
            if (s.LineProgress) key |= 1 << 2;
            if (s.LineTile) key |= 1 << 3;
            if (s.LineRuns) key |= 1 << 4;
            if (s.LineTileBpm) key |= 1 << 5;
            if (s.LineCurBpm) key |= 1 << 6;
            if (s.LineKps) key |= 1 << 7;
            return key;
        }
    }
}
