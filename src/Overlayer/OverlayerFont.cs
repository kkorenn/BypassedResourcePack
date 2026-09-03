using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace BypassedResourcePack
{
    // Loads the bundled Linotte OTFs into runtime TMP font assets.
    // Files ship in the mod's Fonts/ folder (see csproj packaging).
    internal static class OverlayerFont
    {
        private static readonly Dictionary<string, TMP_FontAsset> cache =
            new Dictionary<string, TMP_FontAsset>(System.StringComparer.OrdinalIgnoreCase);

        internal static TMP_FontAsset SemiBold => Get("LinotteSemiBold");
        internal static TMP_FontAsset Bold => Get("LinotteBold");

        internal static TMP_FontAsset Get(string fileNameNoExt)
        {
            if (cache.TryGetValue(fileNameNoExt, out TMP_FontAsset fa)) return fa;

            try
            {
                string path = Resolve(fileNameNoExt);
                if (path == null)
                {
                    Log.Info("[overlayer] font not found: " + fileNameNoExt);
                    cache[fileNameNoExt] = null;
                    return null;
                }

                Font font = new Font(path);
                fa = TMP_FontAsset.CreateFontAsset(font);
                cache[fileNameNoExt] = fa;
                if (fa != null) return fa;
                else Log.Info("[overlayer] failed to build TMP font from " + path);
                return fa;
            }
            catch (System.Exception ex)
            {
                Log.Info("[overlayer] font load error (" + fileNameNoExt + "): " + ex.Message);
                cache[fileNameNoExt] = null;
                return null;
            }
        }

        private static string Resolve(string fileNameNoExt)
        {
            string file = fileNameNoExt + ".otf";

            if (Main.Mod != null && !string.IsNullOrEmpty(Main.Mod.Path))
            {
                string packaged = Path.Combine(Path.Combine(Main.Mod.Path, "Fonts"), file);
                if (File.Exists(packaged)) return packaged;
            }

            string cwd = Directory.GetCurrentDirectory();
            string dev = Path.Combine(Path.Combine(cwd, "Fonts"), file);
            if (File.Exists(dev)) return dev;

            string modsDev = Path.Combine(cwd, Path.Combine("Mods", Path.Combine("BypassedResourcePack", Path.Combine("Fonts", file))));
            if (File.Exists(modsDev)) return modsDev;

            return null;
        }
    }
}
