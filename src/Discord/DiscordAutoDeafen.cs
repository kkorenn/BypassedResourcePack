using System;
using UnityEngine;
using UnityModManagerNet;

namespace BypassedResourcePack
{
    internal static class DiscordAutoDeafen
    {
        private static DiscordRpc rpc;
        private static string configClientId;
        private static string configAccessToken;
        private static bool desiredDeaf;
        private static string status = "off";
        private static bool suppressUntilRestart;
        private static bool runStartCaptured;
        private static bool startedFromFirstTile;
        private static int capturedStartSeqId = -1;
        private static long nextRpcRetryAtUtcTicks;
        private const int RpcRetryDelayMs = 5000;

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
            Settings s = Main.Settings;
            if (s == null || !Main.Enabled || !s.AutoDeafenOn)
            {
                SetInactive();
                return;
            }

            TickEnabled(s, CurrentProgress01());
        }

        internal static void Tick(float progress01)
        {
            Settings s = Main.Settings;
            if (s == null || !Main.Enabled || !s.AutoDeafenOn)
            {
                SetInactive();
                return;
            }

            TickEnabled(s, progress01);
        }

        private static void TickEnabled(Settings s, float progress01)
        {
            s.AutoDeafenAtPercent = Clamp(s.AutoDeafenAtPercent, 0f, 100f);
            string accessToken = Trim(s.DiscordAccessToken);
            string clientId = DiscordLocalOAuthServer.ClientId;

            if (string.IsNullOrEmpty(accessToken))
            {
                StopRpc();
                status = "waiting for authorization";
                return;
            }

            if (string.IsNullOrEmpty(clientId))
            {
                StopRpc();
                status = "set client id first";
                return;
            }

            bool configChanged = !string.Equals(configClientId, clientId, StringComparison.Ordinal) ||
                                 !string.Equals(configAccessToken, accessToken, StringComparison.Ordinal);
            if (configChanged || ((rpc == null || !rpc.Running) && CanRetryRpc()))
                Restart(clientId, accessToken);

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

        private static void SetInactive()
        {
            if (rpc != null || DiscordLocalOAuthServer.Running || desiredDeaf ||
                configClientId != null || configAccessToken != null || nextRpcRetryAtUtcTicks != 0)
                Stop();
            status = "off";
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
            OnRunEnded();
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
            configClientId = null;
            configAccessToken = null;
            suppressUntilRestart = false;
            runStartCaptured = false;
            startedFromFirstTile = false;
            capturedStartSeqId = -1;
            nextRpcRetryAtUtcTicks = 0;
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

        private static void Restart(string clientId, string accessToken)
        {
            StopRpc();
            configClientId = clientId;
            configAccessToken = accessToken;
            status = "starting";
            nextRpcRetryAtUtcTicks = DateTime.UtcNow.AddMilliseconds(RpcRetryDelayMs).Ticks;
            rpc = new DiscordRpc(clientId, accessToken);
            rpc.Start();
        }

        private static void StopRpc()
        {
            if (rpc == null) return;
            try { rpc.SetDeaf(false); } catch { }
            try { rpc.Stop(); } catch { }
            rpc = null;
            desiredDeaf = false;
            configClientId = null;
            configAccessToken = null;
        }

        private static bool CanRetryRpc()
        {
            return DateTime.UtcNow.Ticks >= nextRpcRetryAtUtcTicks;
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
