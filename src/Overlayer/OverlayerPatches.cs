using HarmonyLib;

namespace BypassedResourcePack
{
    // Feeds OverlayerStats. The overlay itself never hides (Overlayer model: it swaps
    // PlayingText/NotPlayingText by IsPlaying), so these only drive run state:
    //  - combo (pure-perfect) from each hit
    //  - IsStarted / StartTile for RunsToHere + Tabub
    // Judgement counts are read live from mistakesManager, so no reset hook is needed.
    internal static class OverlayerPatches
    {
        // Every judgement flows through here — used for the pure-perfect combo.
        [HarmonyPatch(typeof(scrMarginTracker), "AddHit")]
        private static class AddHitPatch
        {
            private static void Postfix(HitMargin hit) => OverlayerStats.OnHit(hit);
        }

        // Run (re)start. Guard on scnGame so the editor preview (scnGame == null) is treated
        // as not-started, while real play and practice mode start the run.
        [HarmonyPatch(typeof(scnGame), "Play")]
        private static class ScnGamePlayPatch
        {
            private static void Postfix()
            {
                if (scnGame.instance != null) OverlayerStats.OnRunStart();
            }
        }

        [HarmonyPatch(typeof(scrPressToStart), "ShowText")]
        private static class PressToStartPatch
        {
            private static void Postfix()
            {
                if (scnGame.instance != null) OverlayerStats.OnRunStart();
            }
        }

        // Death — ends the run (RunsToHere/Tabub register it); overlay keeps showing.
        [HarmonyPatch(typeof(scrController), "FailAction")]
        private static class FailActionPatch
        {
            private static void Postfix() => OverlayerStats.OnDeath();
        }
    }
}
