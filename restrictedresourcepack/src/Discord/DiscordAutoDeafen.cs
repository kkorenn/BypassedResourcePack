using System;
using UnityEngine;
using UnityModManagerNet;

namespace RestrictedResourcePack
{
    internal static class DiscordAutoDeafen
    {
        private static DiscordRpc rpc;
        private static string configKey;
        private static bool desiredDeaf;
        private static string status = "off";
        private static bool suppressUntilRestart;
        private static bool runStartCaptured;
        private static bool startedFromFirstTile;
        private static int capturedStartSeqId = -1;

        internal static string Status
        {
            get
            {
                string rpcStatus = rpc != null ? rpc.Status : status;
                string oauthStatus = DiscordLocalOAuthServer.Status;
                Settings s = Main.Settings;
                if (s != null && !string.IsNullOrEmpty(Trim(s.DiscordAccessToken)) && !DiscordLocalOAuthServer.Running)
                    oauthStatus = "authorized";
                string combined = oauthStatus + " / " + rpcStatus;
                return desiredDeaf ? combined + " / deaf" : combined;
            }
        }

        internal static void Observe(float progress01)
        {
            Tick(progress01);
        }

        internal static void Tick()
        {
            Tick(CurrentProgress01());
        }

        internal static void Tick(float progress01)
        {
            Settings s = Main.Settings;
            if (s == null || !Main.Enabled || !s.AutoDeafenOn)
            {
                Stop();
                status = "off";
                return;
            }

            s.AutoDeafenAtPercent = Clamp(s.AutoDeafenAtPercent, 0f, 100f);

            if (string.IsNullOrEmpty(Trim(s.DiscordAccessToken)))
            {
                StopRpc();
                status = "waiting for authorization";
                return;
            }

            if (string.IsNullOrEmpty(DiscordLocalOAuthServer.ClientId))
            {
                StopRpc();
                status = "set client id first";
                return;
            }

            string nextConfigKey = BuildConfigKey(s);
            if (rpc == null || !string.Equals(configKey, nextConfigKey, StringComparison.Ordinal))
                Restart(s, nextConfigKey);

            if (progress01 >= 0f && !runStartCaptured)
                CaptureRunStart();

            bool eligibleStart = !s.AutoDeafenOnlyFromStart || (runStartCaptured && startedFromFirstTile);
            bool shouldDeaf = !suppressUntilRestart
                              && progress01 >= 0f
                              && InRealPlay()
                              && eligibleStart
                              && Mathf.Clamp01(progress01) * 100f >= s.AutoDeafenAtPercent;

            if (shouldDeaf != desiredDeaf)
            {
                desiredDeaf = shouldDeaf;
                rpc?.SetDeaf(shouldDeaf);
                Log.Verbose("[discord] desired deaf = " + shouldDeaf);
            }
        }

        internal static void OnRunReset()
        {
            suppressUntilRestart = false;
            runStartCaptured = false;
            startedFromFirstTile = false;
            capturedStartSeqId = -1;
            Undeafen();
        }

        internal static void OnRunEnded()
        {
            suppressUntilRestart = true;
            Undeafen();
        }

        internal static void OnRunHide()
        {
            Undeafen();
        }

        private static void Undeafen()
        {
            if (!desiredDeaf) return;
            desiredDeaf = false;
            try { rpc?.SetDeaf(false); } catch { }
        }

        internal static void Stop()
        {
            if (rpc != null)
            {
                StopRpc();
            }
            DiscordLocalOAuthServer.Stop();
            desiredDeaf = false;
            configKey = null;
            suppressUntilRestart = false;
            runStartCaptured = false;
            capturedStartSeqId = -1;
        }

        private const string TutorialUrl = "https://www.youtube.com/watch?v=1q4gB0ArypQ";

        internal static void OpenAuthorizeUrl()
        {
            Settings s = Main.Settings;
            if (s == null) return;
            DiscordLocalOAuthServer.OpenAuthorizeUrl(s);
        }

        internal static void OpenTutorial()
        {
            DiscordLocalOAuthServer.OpenUrl(TutorialUrl);
        }

        internal static string AuthorizeUrl()
        {
            Settings s = Main.Settings;
            return s != null ? DiscordLocalOAuthServer.AuthorizeUrl(s) : "";
        }

        internal static void Unlink()
        {
            Stop();
            Settings s = Main.Settings;
            UnityModManager.ModEntry mod = Main.Mod;
            if (s == null) return;
            s.DiscordAccessToken = "";
            if (mod != null) { try { s.Save(mod); } catch { } }
            status = "unlinked";
        }

        private static bool InRealPlay()
        {
            try
            {
                scrController controller = scrController.instance;
                scrConductor conductor = scrConductor.instance;
                return controller != null
                       && conductor != null
                       && conductor.isGameWorld
                       && !controller.paused;
            }
            catch { return false; }
        }

        private static void CaptureRunStart()
        {
            capturedStartSeqId = CurrentSeqId();
            // "Only from 0" means the chart's first floor, not merely "no saved checkpoint used".
            startedFromFirstTile = capturedStartSeqId == 0 && NoCheckpointStart();
            runStartCaptured = capturedStartSeqId >= 0;
        }

        private static int CurrentSeqId()
        {
            try
            {
                scrController controller = scrController.instance;
                if (controller != null) return controller.currentSeqID;
            }
            catch { }
            return -1;
        }

        private static bool NoCheckpointStart()
        {
            try
            {
                if (ADOBase.isScnGame && scnGame.instance != null)
                    return scnGame.instance.checkpointsUsed == 0 && GCS.checkpointNum == 0;
            }
            catch { }

            try { return scrController.checkpointsUsed == 0 && GCS.checkpointNum == 0; }
            catch { return false; }
        }

        // Mirrors KorenResourcePack's Main.GetLevelProgress: returns -1 (=> undeafen)
        // whenever a real run isn't actively progressing, so pause/menu/editor all
        // drop the deafen. 0..1 otherwise.
        private static float CurrentProgress01()
        {
            try
            {
                scrController c = scrController.instance;
                scrLevelMaker lm = scrLevelMaker.instance;
                if (c == null || lm == null || lm.listFloors == null) return -1f;
                if (c.paused) return -1f;
                if (lm.listFloors.Count <= 1) return -1f;
                return Mathf.Clamp01(c.percentComplete);
            }
            catch { return -1f; }
        }

        private static void Restart(Settings s, string nextConfigKey)
        {
            StopRpc();
            configKey = nextConfigKey;
            status = "starting";
            rpc = new DiscordRpc(
                DiscordLocalOAuthServer.ClientId,
                Trim(s.DiscordAccessToken));
            rpc.Start();
        }

        private static void StopRpc()
        {
            if (rpc == null) return;
            try { rpc.SetDeaf(false); } catch { }
            try { rpc.Stop(); } catch { }
            rpc = null;
            desiredDeaf = false;
            configKey = null;
        }

        private static string BuildConfigKey(Settings s)
        {
            return DiscordLocalOAuthServer.ClientId + "\n" +
                   Trim(s.DiscordAccessToken);
        }

        internal static void SaveAccessToken(string token)
        {
            Settings s = Main.Settings;
            UnityModManager.ModEntry mod = Main.Mod;
            if (s == null || mod == null || string.IsNullOrEmpty(token)) return;
            if (string.Equals(s.DiscordAccessToken, token, StringComparison.Ordinal)) return;

            s.DiscordAccessToken = token;
            try { s.Save(mod); } catch { }
        }

        private static string Trim(string value) => (value ?? "").Trim();

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
