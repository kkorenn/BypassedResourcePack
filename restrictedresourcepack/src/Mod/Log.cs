namespace RestrictedResourcePack
{
    internal static class Log
    {
        internal static void Info(string message)
        {
            Main.Mod?.Logger?.Log(message);
        }

        internal static void Verbose(string message)
        {
            if (Main.Settings != null && Main.Settings.VerboseLogging)
                Main.Mod?.Logger?.Log("[verbose] " + message);
        }
    }
}
