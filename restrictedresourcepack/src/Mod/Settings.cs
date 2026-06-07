using System;
using UnityModManagerNet;

namespace RestrictedResourcePack
{
    // Per-panel overlay customization (position/scale/visibility/not-playing text).
    [Serializable]
    public class OverlayPanel
    {
        public bool Enabled = true;
        public bool Expanded = false;
        public float X = 0.5f;       // 0..1 screen fraction
        public float Y = 0.5f;       // 0..1 screen fraction (from bottom)
        public float Scale = 1f;
        public string NotPlaying = "";

        public OverlayPanel() { }
        public OverlayPanel(float x, float y, float scale, string notPlaying)
        {
            X = x; Y = y; Scale = scale; NotPlaying = notPlaying;
        }
    }

    public class Settings : UnityModManager.ModSettings
    {
        // general
        public bool VerboseLogging = false;

        // overlayer
        public bool OverlayerOn = true;
        public bool HideTitle = true;        // hide the game's song title/artist while playing

        // GUI section expand state
        public bool SecOverlayExpanded = true;
        public bool SecTweaksExpanded = false;
        public bool SecResultsExpanded = false;
        public bool SecDiscordExpanded = false;

        // per-panel position/scale/visibility/not-playing text (defaults = current layout)
        public OverlayPanel PanelJudgements  = new OverlayPanel(0.5f, -0.01f, 0.85f, "hi ;)");
        public OverlayPanel PanelTabub       = new OverlayPanel(0.34f, 0.09f, 0.6f, "");
        public OverlayPanel PanelTopInfo     = new OverlayPanel(0.43f, 0.13f, 0.66f, "hi ;)");
        public OverlayPanel PanelRightInfo   = new OverlayPanel(0.6f, 0.045f, 0.7f, "");
        public OverlayPanel PanelCombo       = new OverlayPanel(0.5f, 0.88f, 0.7f, "hi ;)");
        public OverlayPanel PanelProgressBar = new OverlayPanel(0.5f, 1f, 0.7f, "");

        // individual status lines (TopInfo) and bpm lines (RightInfo)
        public bool LineXAcc = true, LineMaxAcc = true, LineProgress = true, LineTile = true;
        public bool LineRuns = true, LineTileBpm = true, LineCurBpm = true, LineKps = true;

        public int TabubMaxIndex = 850;      // Tabub bpm halving threshold

        // game visual tweaks (AdofaiTweaks-style)
        public bool HideJudgmentText = true;     // "Perfect!" / "Pure Perfect!" popups
        public bool HideMissIndicators = true;
        public bool PlanetColorOn = true;
        public string Planet1Hex = "AA00AA";     // red planet body
        public string Planet2Hex = "BBBBBB";     // blue planet body
        public bool HideTail = true;
        public bool HideRing = true;

        // detailed-results screen: drop individual rows
        public bool HideResultXAcc = true;
        public bool HideResultAccuracy = true;
        public bool HideResultCheckpoints = true;
        public bool HideResultMaxKeys = true;

        // Discord RPC auto-deafen (ported 1:1 from KorenResourcePack's wired AutoDeafen)
        public bool AutoDeafenOn = false;
        public bool AutoDeafenOnlyFromStart = true;   // only deafen on a run begun at 0%
        public float AutoDeafenAtPercent = 5f;        // deafen once progress >= this %
        public string DiscordClientId = "";           // user's own Discord app id (rpc.voice.write is owner+50-testers only)
        public string DiscordAccessToken = "";

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
