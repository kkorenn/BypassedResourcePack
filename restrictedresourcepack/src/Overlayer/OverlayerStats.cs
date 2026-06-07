using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RestrictedResourcePack
{
    // Value brain, ported 1:1 from the customer's Overlayer tags + JS scripts.
    //
    // Modelled on Overlayer itself:
    //  - Judgement counts come straight from the game's margin trackers (they already reset
    //    per attempt / checkpoint revert), matching Overlayer's official tags.
    //  - IsPlaying mirrors Overlayer.Main.IsPlaying (ctrl && cdt && !paused && cdt.isGameWorld).
    //  - Stateful tags (RunsToHere/Tabub) are ticked every frame, exactly like the JS ran.
    internal static class OverlayerStats
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // =====================================================================
        // Judgement count fallback. Current ADOFAI stores counts in scrMarginTracker, but keeping
        // a local tally lets the overlay survive a null/early tracker during scene setup.
        // =====================================================================

        private static readonly int[] fallbackCounts = new int[16];

        private static int Get(HitMargin m)
        {
            try
            {
                scrMarginTracker[] trackers = scrMistakesManager.marginTrackers;
                if (trackers != null && trackers.Length > 0)
                {
                    int total = 0;
                    bool sawTracker = false;
                    for (int n = 0; n < trackers.Length; n++)
                    {
                        scrMarginTracker tracker = trackers[n];
                        if (tracker == null) continue;
                        total += tracker.GetHits(m);
                        sawTracker = true;
                    }
                    if (sawTracker) return total;
                }
            }
            catch { }

            int i = (int)m;
            return (i >= 0 && i < fallbackCounts.Length) ? fallbackCounts[i] : 0;
        }

        internal static int CTE => Get(HitMargin.TooEarly);
        internal static int CVE => Get(HitMargin.VeryEarly);
        internal static int CEP => Get(HitMargin.EarlyPerfect);
        internal static int CP => Get(HitMargin.Perfect);
        internal static int CLP => Get(HitMargin.LatePerfect);
        internal static int CVL => Get(HitMargin.VeryLate);
        internal static int CTL => Get(HitMargin.TooLate);
        internal static int Auto => Get(HitMargin.Auto);
        internal static int MissCount => Get(HitMargin.FailMiss);
        internal static int Overloads => Get(HitMargin.FailOverload);

        // =====================================================================
        // Run / tile state
        // =====================================================================

        internal static bool IsStarted { get; private set; }
        internal static int CurTile { get; private set; }
        internal static int TotalTile { get; private set; }
        internal static int LeftTile { get; private set; }
        internal static int StartTile { get; private set; }
        internal static int Combo { get; private set; }

        // bpm (polled)
        internal static double TileBpm { get; private set; }
        internal static double CurBpm { get; private set; }
        internal static double TileBpmWithoutPitch { get; private set; }
        internal static double CurBpmWithoutPitch { get; private set; }
        internal static double RecKPSWithoutPitch => CurBpmWithoutPitch / 60.0;

        // Overlayer.Main.IsPlaying: a real run is live (not paused, in the game world).
        internal static bool IsPlaying()
        {
            try
            {
                scrController c = scrController.instance;
                scrConductor cd = scrConductor.instance;
                return c != null && cd != null && !c.paused && cd.isGameWorld;
            }
            catch { return false; }
        }

        // =====================================================================
        // Lifecycle (driven by OverlayerPatches)
        // =====================================================================

        internal static void OnRunStart()
        {
            Array.Clear(fallbackCounts, 0, fallbackCounts.Length);
            Combo = 0;
            IsStarted = true;
            try { StartTile = scrController.instance != null ? scrController.instance.currentSeqID : 0; }
            catch { StartTile = 0; }
            ResetTabub(StartTile);
        }

        // Death — run no longer active so RunsToHere/Tabub register the end.
        internal static void OnDeath() => IsStarted = false;

        private static float comboPulseTime = -999f;

        internal static void OnHit(HitMargin hit)
        {
            int i = (int)hit;
            if (i >= 0 && i < fallbackCounts.Length) fallbackCounts[i]++;

            // Pure-perfect combo (Perfect/Auto keep it), matching Overlayer's {Combo}.
            int prev = Combo;
            if (hit == HitMargin.Perfect || hit == HitMargin.Auto) Combo++;
            else Combo = 0;
            if (Combo != prev) { try { comboPulseTime = Time.realtimeSinceStartup; } catch { } }
        }

        // Combo number pop: 110% on change, easing back to 100% over 0.15s (ease-out cubic).
        internal static float ComboPopPercent()
        {
            float now;
            try { now = Time.realtimeSinceStartup; } catch { return 100f; }
            float elapsed = now - comboPulseTime;
            const float dur = 0.15f;
            if (elapsed < 0f || elapsed >= dur) return 100f;
            float ease = 1f - Mathf.Pow(1f - elapsed / dur, 3f);
            return Mathf.Lerp(110f, 100f, ease);
        }

        // =====================================================================
        // Per-frame poll
        // =====================================================================

        internal static void Tick()
        {
            scrController controller = null;
            try { controller = scrController.instance; } catch { }

            UpdateTiles(controller);
            UpdateBpm(controller);
            RunsToHere_Tick();
            Tabub_Tick();
        }

        private static void UpdateTiles(scrController controller)
        {
            try
            {
                CurTile = controller != null ? controller.currentSeqID : 0;
                var lm = scrLevelMaker.instance;
                // listFloors[0] is the starting tile, not a played tile — exclude it so
                // tile counts read N/N and progress reaches 100% at the finish.
                int floors = (lm != null && lm.listFloors != null) ? lm.listFloors.Count : 0;
                TotalTile = floors > 0 ? floors - 1 : 0;
                LeftTile = TotalTile - CurTile;
            }
            catch { }
        }

        private static void UpdateBpm(scrController controller)
        {
            try
            {
                scrConductor conductor = scrConductor.instance;
                scrFloor floor = controller != null ? (controller.currFloor ?? controller.firstFloor) : null;
                if (controller == null || conductor == null || floor == null || conductor.song == null)
                    return;

                double speed = controller.planetarySystem != null ? controller.planetarySystem.speed : 1.0;
                double pitch = conductor.song.pitch;

                TileBpm = conductor.bpm * pitch * speed;
                TileBpmWithoutPitch = conductor.bpm * speed;

                if (floor.nextfloor != null)
                {
                    double dt = floor.nextfloor.entryTime - floor.entryTime;
                    double raw = dt != 0.0 ? 60.0 / dt : TileBpmWithoutPitch;
                    CurBpmWithoutPitch = raw;
                    CurBpm = raw * pitch;
                }
                else
                {
                    CurBpm = TileBpm;
                    CurBpmWithoutPitch = TileBpmWithoutPitch;
                }
            }
            catch { }
        }

        // =====================================================================
        // Derived values (ported from MyScripts.js)
        // =====================================================================

        internal static double XAccuracy()
        {
            double total = CP + Auto + CEP + CLP + CVE + CVL + CTE + CTL;
            if (total <= 0) return 100.0;
            double weighted = CP + Auto + 0.75 * (CEP + CLP) + 0.4 * (CVE + CVL) + 0.2 * (CTE + CTL);
            double absX = 100.0 * (weighted / total);
            int cp = 0;
            try { cp = scrController.checkpointsUsed; } catch { }
            return absX * Math.Pow(0.9875, cp);
        }

        internal static double MaxAcc()
        {
            double pp = TotalTile - CurTile + CP;
            double p = CEP + CLP;
            double e = CVE + CVL;
            double m = CTE + CTL - Overloads;
            double tot = LeftTile + CP + CEP + CLP + CVE + CVL + CTE + CTL + MissCount;
            if (tot <= 0) return 100.0;
            return 100.0 * ((pp + 0.75 * p + 0.4 * e + 0.2 * m) / tot);
        }

        private static double StartT() => TotalTile > 0 ? Clamp01x100((StartTile - 1) * 100.0 / TotalTile) : 0.0;
        private static double EndT() => TotalTile > 0 ? Clamp01x100(CurTile * 100.0 / TotalTile) : 0.0;
        private static double Clamp01x100(double v) => v < 0.0 ? 0.0 : (v > 100.0 ? 100.0 : v);

        // 0..1 fractions for the Image-based progress bar.
        internal static float ProgressStartFrac => (float)(StartT() / 100.0);
        internal static float ProgressEndFrac => (float)(EndT() / 100.0);
        internal static double ProgressPercent => EndT();

        internal static string BetterProgress()
        {
            return StartT().ToString("F2", Inv) + " - " + EndT().ToString("F2", Inv) + "%";
        }

        private static int pbStart = int.MinValue, pbEnd = int.MinValue;
        private static string pbCache = "";

        internal static string ProgressBar()
        {
            int start = Mathf.FloorToInt((float)StartT());
            int end = Mathf.FloorToInt((float)EndT());
            if (start == pbStart && end == pbEnd) return pbCache;
            pbStart = start; pbEnd = end;
            var sb = new System.Text.StringBuilder(100 * 40 + 8);
            sb.Append('[');
            for (int i = 0; i < 100; i++)
            {
                bool on = start <= i && end >= i;
                sb.Append("<size=40><color=#").Append(on ? "FF00FF" : "00FFFF").Append(">I</color></size>");
            }
            sb.Append(']');
            pbCache = sb.ToString();
            return pbCache;
        }

        // =====================================================================
        // RunsToHere (ported from RunsToHere.js)
        // =====================================================================

        private static readonly List<int> rthDeaths = new List<int>();
        private static bool rthStarted;
        private static int rthCurTile;
        private static object rthLevelRef;

        private static void RunsToHere_Tick()
        {
            object floors = null;
            try { floors = scrLevelMaker.instance != null ? scrLevelMaker.instance.listFloors : null; } catch { }
            if (!ReferenceEquals(floors, rthLevelRef))
            {
                rthLevelRef = floors;
                rthDeaths.Clear();
                rthStarted = false;
                rthCurTile = 0;
            }

            int cur = CurTile;
            bool started = IsStarted;

            if (started != rthStarted && cur <= 2) rthStarted = true;
            if (rthStarted && started != rthStarted)
            {
                rthStarted = false;
                rthDeaths.Add(rthCurTile);
            }
            if (cur != rthCurTile) rthCurTile = cur;
        }

        internal static int RunsToHere()
        {
            int c = 0;
            for (int i = 0; i < rthDeaths.Count; i++)
                if (rthCurTile < rthDeaths[i]) c++;
            return c;
        }

        // =====================================================================
        // Tabub (ported from Learner.js / TabubuLabubu)
        // =====================================================================

        private static double tabStorageVal;
        private static int tabCurTile = 1;
        private static readonly List<int> tabKeys = new List<int>();
        private static int tabCKeys;
        private static bool tabHasPlayableKey;
        private static int tabThreshold = 1;
        private static bool tabStarted;
        private static string tabCache = "";

        private static void ResetTabub(int startTile)
        {
            tabStorageVal = 0;
            tabCurTile = startTile;
            tabKeys.Clear();
            tabCKeys = 0;
            tabHasPlayableKey = false;
            tabThreshold = 1;
            tabStarted = false;
            tabCache = "";
        }

        private static void Tabub_Tick()
        {
            double tempBpm = TileBpmWithoutPitch;
            tabThreshold = 1;
            int maxIndex = Main.Settings != null ? Main.Settings.TabubMaxIndex : 850;
            if (maxIndex < 1) maxIndex = 1;
            while (tempBpm >= maxIndex)
            {
                tempBpm /= 2;
                tabThreshold *= 2;
            }

            if (!IsStarted) tabStarted = false;
            if (IsStarted && !tabStarted)
            {
                tabStorageVal = 0;
                tabCurTile = CurTile;
                tabKeys.Clear();
                tabCKeys = 0;
                tabHasPlayableKey = false;
                tabThreshold = 1;
                tabStarted = true;
            }

            double cur = CurBpmWithoutPitch;
            double tile = TileBpmWithoutPitch;

            if (CurTile != tabCurTile)
            {
                if (cur > 0) tabStorageVal += Math.Round(tile * 180.0 / cur, MidpointRounding.AwayFromZero);
                tabCurTile = CurTile;
                if (!IsAutoTile(CurTile))
                {
                    tabCKeys++;
                    tabHasPlayableKey = true;
                }
            }

            if (tabStorageVal >= 180.0 * tabThreshold)
            {
                tabStorageVal -= 180.0 * tabThreshold;
                if (tabHasPlayableKey)
                    tabKeys.Add(tabCKeys);
                tabCKeys = 0;
                tabHasPlayableKey = false;
                if (tabKeys.Count > 34)
                    tabKeys.RemoveRange(0, 8);
            }

            var sb = new System.Text.StringBuilder();
            int n = Math.Min(tabKeys.Count, 34);
            for (int i = 0; i < n; i++)
            {
                if (i % 2 == 0 && i > 0)
                {
                    int a = tabKeys[i - 2];
                    int b = tabKeys[i - 1];
                    if (a > 8 || b > 8) sb.Append('[').Append(a).Append(' ').Append(b).Append("] ");
                    else sb.Append(a).Append(b).Append(' ');
                }
                if (i % 8 == 0 && i > 0) sb.Append('\n');
            }
            tabCache = sb.ToString();
        }

        internal static string Tabub() => tabCache;

        private static bool IsAutoTile(int tile)
        {
            try
            {
                var lm = scrLevelMaker.instance;
                if (lm == null || lm.listFloors == null || tile < 0 || tile >= lm.listFloors.Count)
                    return false;

                scrFloor floor = lm.listFloors[tile];
                return floor != null && floor.auto;
            }
            catch { return false; }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        internal static string ColorRange(double value, double min, double max, string hexMin, string hexMax)
        {
            float t = max == min ? 0f : Mathf.Clamp01((float)((value - min) / (max - min)));
            Color c = Color.Lerp(HexToColor(hexMin), HexToColor(hexMax), t);
            return ColorToHex(c);
        }

        internal static string F(double v, int decimals) => v.ToString("F" + decimals, Inv);

        private static Color HexToColor(string hex)
        {
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        private static string ColorToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return r.ToString("X2", Inv) + g.ToString("X2", Inv) + b.ToString("X2", Inv);
        }
    }
}
