using System;
using UnityModManagerNet;

namespace BypassedResourcePack
{
    internal static class DiscordAutoDeafen
    {
        private static DiscordRpc rpc;
        private static string configKey;
        private static bool desiredDeaf;
        private static string status = "off";

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

        internal static void Tick()
        {
            Settings s = Main.Settings;
            if (s == null || !Main.Enabled || !s.DiscordAutoDeafenOn)
            {
                Stop();
                status = "off";
                return;
            }

            s.DiscordDeafenThresholdPercent = Clamp(s.DiscordDeafenThresholdPercent, 0f, 100f);

            if (string.IsNullOrEmpty(Trim(s.DiscordAccessToken)))
            {
                StopRpc();
                status = "waiting for authorization";
                return;
            }

            string nextConfigKey = BuildConfigKey(s);
            if (rpc == null || !string.Equals(configKey, nextConfigKey, StringComparison.Ordinal))
                Restart(s, nextConfigKey);

            bool shouldDeaf = OverlayerStats.IsStarted
                              && OverlayerStats.IsPlaying()
                              && OverlayerStats.ProgressPercent >= s.DiscordDeafenThresholdPercent;

            if (shouldDeaf != desiredDeaf)
            {
                desiredDeaf = shouldDeaf;
                rpc?.SetDeaf(shouldDeaf);
                Log.Verbose("[discord] desired deaf = " + shouldDeaf);
            }
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
        }

        internal static void OpenAuthorizeUrl()
        {
            Settings s = Main.Settings;
            if (s == null) return;
            DiscordLocalOAuthServer.OpenAuthorizeUrl(s);
        }

        internal static string AuthorizeUrl()
        {
            Settings s = Main.Settings;
            return s != null ? DiscordLocalOAuthServer.AuthorizeUrl(s) : "";
        }

        private static void Restart(Settings s, string nextConfigKey)
        {
            StopRpc();
            configKey = nextConfigKey;
            status = "starting";
            rpc = new DiscordRpc(
                DiscordLocalOAuthServer.ClientId,
                Trim(s.DiscordAccessToken),
                SaveAccessToken);
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
