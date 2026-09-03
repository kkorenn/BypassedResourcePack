using HarmonyLib;

namespace BypassedResourcePack
{
    // Feeds OverlayerStats. The overlay itself never hides (Overlayer model: it swaps
    // PlayingText/NotPlayingText by IsPlaying), so these only drive run state:
    //  - combo (pure-perfect) from each hit
    //  - IsStarted / StartTile for RunsToHere + Tabub
    //  - the mod-owned judgement tally (OverlayerStats.liveCounts), which the reset
    //    hooks clear so judgements/accuracy reset on every attempt, including official
    //    ("main") levels the game does not zero on restart.
    internal static class OverlayerPatches
    {
        private static void OnRunReset()
        {
            OverlayerStats.OnRunStart();
            DiscordAutoDeafen.OnRunReset();
        }

        // Every judgement flows through here — used for the pure-perfect combo.
        [HarmonyPatch(typeof(scrMarginTracker), "AddHit")]
        private static class AddHitPatch
        {
            private static void Postfix(HitMargin hit) => OverlayerStats.OnHit(hit);
        }

        // Run (re)start. Official/main levels do not have scnGame.instance, so use the
        // controller/conductor game-world state instead.
        [HarmonyPatch(typeof(scnGame), "Play")]
        private static class ScnGamePlayPatch
        {
            private static void Postfix(bool __result)
            {
                if (__result) OnRunReset();
            }
        }

        [HarmonyPatch(typeof(scrPressToStart), "ShowText")]
        private static class PressToStartPatch
        {
            private static void Postfix() => OnRunReset();
        }

        [HarmonyPatch(typeof(scrController), "RestartProgress")]
        private static class RestartProgressPatch
        {
            private static void Postfix() => OnRunReset();
        }

        [HarmonyPatch(typeof(scrController), "Restart", typeof(bool))]
        private static class RestartPatch
        {
            private static void Postfix() => OnRunReset();
        }

        [HarmonyPatch(typeof(scrMistakesManager), "RevertToLastCheckpoint")]
        private static class RevertCheckpointPatch
        {
            private static void Postfix() => OnRunReset();
        }

        // Death — ends the run (RunsToHere/Tabub register it); overlay keeps showing.
        [HarmonyPatch(typeof(scrController), "FailAction")]
        private static class FailActionPatch
        {
            private static void Postfix()
            {
                OverlayerStats.OnDeath();
                DiscordAutoDeafen.OnRunEnded();
            }
        }

        // FailAction happens after the player's death animation begins.  Deafen state must be
        // restored at the real death boundary as well, including paths that bypass FailAction.
        [HarmonyPatch(typeof(scrPlayer), "Die")]
        private static class PlayerDeathPatch
        {
            private static void Postfix() => DiscordAutoDeafen.OnRunEnded();
        }

        [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
        private static class StartLoadingScenePatch
        {
            private static void Postfix()
            {
                OverlayerStats.OnSceneTransition();
                DiscordAutoDeafen.OnRunHide();
            }
        }

        [HarmonyPatch(typeof(scrUIController), "WipeToBlack")]
        private static class WipeToBlackPatch
        {
            private static void Postfix() => DiscordAutoDeafen.OnRunHide();
        }

        // Level clear (planet lands on the end portal) — same run-ended signal as death.
        // KorenResourcePack routes States.Won here via StateBehaviour; OnLandOnPortal is
        // the Assembly-CSharp trigger for the same event, so no extra assembly ref/reflection.
        [HarmonyPatch(typeof(scrController), "OnLandOnPortal")]
        private static class OnLandOnPortalPatch
        {
            private static void Postfix() => DiscordAutoDeafen.OnRunEnded();
        }
    }
}
