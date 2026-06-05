namespace BypassedResourcePack
{
    // Thin alias over Localization so feature code stays terse: I18n.Tr("key").
    internal static class I18n
    {
        internal static string Lang => Localization.CurrentLanguage;

        internal static string Tr(string key) => Localization.Text(key);

        internal static string Tr(string key, params object[] args) => Localization.Format(key, args);
    }
}
