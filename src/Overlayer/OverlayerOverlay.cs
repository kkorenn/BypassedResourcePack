using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BypassedResourcePack
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

        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Vector2 ShadowOffset = new Vector2(2.5f, -2.5f);
        private static readonly Regex ColorTagRegex = new Regex(@"</?color[^>]*>", RegexOptions.Compiled);
        private static readonly StringBuilder ContentBuilder = new StringBuilder(256);
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

            root = new GameObject("BypassedResourcePack.Overlayer");
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
            TextMeshProUGUI t = MakeText(name, font, align, fontSize, lineSpacing, Color.white, pivot);

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

        private const string JFail = "C759FF";
        private const string JTooEarly = "FF3838";
        private const string JVeryEarly = "FF704F";
        private const string JEarlyPerf = "A1FF4F";
        private const string JPerfect = "61FF4F";
        private const string Gold = "FFD700";

        private static string BuildJudgements()
        {
            ContentBuilder.Clear();
            AppendJudgement(JFail, OverlayerStats.Overloads);
            AppendJudgement(JTooEarly, OverlayerStats.CTE);
            AppendJudgement(JVeryEarly, OverlayerStats.CVE);
            AppendJudgement(JEarlyPerf, OverlayerStats.CEP);
            AppendJudgement(JPerfect, OverlayerStats.CP);
            AppendJudgement(JEarlyPerf, OverlayerStats.CLP);
            AppendJudgement(JVeryEarly, OverlayerStats.CVL);
            AppendJudgement(JTooEarly, OverlayerStats.CTL);
            AppendJudgement(JFail, OverlayerStats.MissCount, false);
            return ContentBuilder.ToString();
        }

        private static void AppendJudgement(string color, int count, bool trailingSpace = true)
        {
            ContentBuilder.Append("<color=#").Append(color).Append('>').Append(count).Append("</color>");
            if (trailingSpace) ContentBuilder.Append(' ');
        }

        // White->green as accuracy climbs to 100, gold at 100.000.
        private static string AccColor(double value)
        {
            string f = OverlayerStats.F(value, 3);
            return f == "100.000" ? Gold : OverlayerStats.ColorRange(value, 99.9995, 100, "FFFFFF", "33BB33");
        }

        private static string BuildTopInfo()
        {
            Settings s = Main.Settings;
            double xacc = OverlayerStats.XAccuracy();
            double maxacc = OverlayerStats.MaxAcc();
            ContentBuilder.Clear();
            bool hasLine = false;
            if (s.LineXAcc)
            {
                ContentBuilder.Append("<color=#99FF99>XAcc</color> | <color=#").Append(AccColor(xacc))
                    .Append('>').Append(OverlayerStats.F(xacc, 3)).Append("</color>");
                hasLine = true;
            }
            if (s.LineMaxAcc)
            {
                AppendNewline(hasLine);
                ContentBuilder.Append("<color=#77FF77>MaxAcc</color> | <color=#").Append(AccColor(maxacc))
                    .Append('>').Append(OverlayerStats.F(maxacc, 3)).Append("</color>");
                hasLine = true;
            }
            if (s.LineProgress)
            {
                AppendNewline(hasLine);
                ContentBuilder.Append("<color=#33FF33>Progress</color> | ").Append(OverlayerStats.BetterProgress());
                hasLine = true;
            }
            if (s.LineTile)
            {
                AppendNewline(hasLine);
                ContentBuilder.Append("<color=#11FF11>").Append(OverlayerStats.CurTile)
                    .Append("</color> / ").Append(OverlayerStats.TotalTile);
            }
            return ContentBuilder.ToString();
        }

        private static string BuildRightInfo()
        {
            Settings s = Main.Settings;
            ContentBuilder.Clear();
            bool hasLine = false;
            if (s.LineRuns)
            {
                ContentBuilder.Append("<color=#9999FF>Runs To Here</color> | ").Append(OverlayerStats.RunsToHere());
                hasLine = true;
            }
            if (s.LineTileBpm)
            {
                AppendNewline(hasLine);
                ContentBuilder.Append("<color=#6666FF>TileBPM</color> | ").Append(OverlayerStats.F(OverlayerStats.TileBpm, 2));
                hasLine = true;
            }
            if (s.LineCurBpm)
            {
                AppendNewline(hasLine);
                ContentBuilder.Append("<color=#3333FF>CurBPM</color> | ").Append(OverlayerStats.F(OverlayerStats.CurBpm, 2));
                hasLine = true;
            }
            if (s.LineKps)
            {
                AppendNewline(hasLine);
                ContentBuilder.Append("<color=#0000FF>KPS</color> | ")
                    .Append(Mathf.CeilToInt((float)OverlayerStats.RecKPSWithoutPitch));
            }
            return ContentBuilder.ToString();
        }

        private static string BuildTabub()
        {
            return "<color=#7700FF>Tabub :D</color>\n\n" + OverlayerStats.Tabub();
        }

        private static string BuildCombo()
        {
            float pop = OverlayerStats.ComboPopPercent();
            string cr = OverlayerStats.ColorRange(OverlayerStats.Combo, 0, 500, "FF5555", "55FF55");
            ContentBuilder.Clear();
            ContentBuilder.Append("<size=").Append(pop.ToString("0", CultureInfo.InvariantCulture))
                .Append("%><color=#").Append(cr).Append('>').Append(OverlayerStats.Combo)
                .Append("</color></size>\n<size=60>Combo</size>");
            return ContentBuilder.ToString();
        }

        private static void AppendNewline(bool hasLine)
        {
            if (hasLine) ContentBuilder.Append('\n');
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
