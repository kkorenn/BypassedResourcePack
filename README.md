# BypassedResourcePack

Minimal ADOFAI Unity Mod Manager skeleton, based on the structure used by `KorenResourcePack`.

- **Overlayer** — native C# overlay (no JS runtime) porting the customer's Overlayer tags + scripts. Six TMP panels laid out from `Texts.json`, rendered in Linotte, each toggleable in settings.
- **Discord auto-deafen** — optional Discord desktop RPC integration with an in-mod `127.0.0.1` OAuth callback server. It self-deafens the authorized account once run progress reaches the configured threshold.
- **i18n** — flat `key -> string` JSON tables (`en`/`kr`/`cn`), English fallback, first-run auto-detect from the game language. Call `I18n.Tr("key")`.
- **Settings** — `UnityModManager.ModSettings` with an in-panel language selector, per-element overlay toggles, and a `Verbose logging` toggle, persisted via `OnSaveGUI`.

## Overlayer

Polled once per frame from live game state (mirrors how the original `registerTag` callbacks ran). Panels and the values they show:

| Panel | Shows | Ported from |
|-------|-------|-------------|
| Progress bar (very top) | start→current run range, 100 segments | `MyScripts.progBar` |
| Combo (very top) | pure-perfect combo, color gradient | `Texts.json` #3 |
| Top | XAcc (3dp), MaxAcc (3dp), Progress (2dp), Tile cur/total | `MyScripts.MaxAcc/xAcc/betterProgress` |
| Right | RunsToHere, TileBPM (2dp), CurBPM (2dp), KPS (ceil) | `RunsToHere.js`, BPM tags |
| Judgements (below bar) | Overload / TE VE EP P LP VL TL / Miss | `Texts.json` #1 |
| Tabub (left of bar) | grouped per-tile key estimate | `Learner.js` |

Judgement counts come from the `scrMarginTracker.AddHit` hook; everything else is read from `scrController` / `scrConductor` / `scrLevelMaker` each frame. See [OverlayerStats.cs](src/Overlayer/OverlayerStats.cs).

Notes:
- `Score` (in `Texts.json` #2) is omitted — it isn't in the requested panel list.
- The combo size animation (`MovingMan`) is simplified to a static size + color gradient.
- Positions/scales/font sizes are taken from `Texts.json`; tweak in [OverlayerOverlay.cs](src/Overlayer/OverlayerOverlay.cs).

## Discord auto-deafen

Open the UMM settings panel, expand **Discord Auto-Deafen**, then set:

- `Threshold %` — default `15`; once a live run reaches this progress, the mod sends `SET_VOICE_SETTINGS { deaf: true }`; when stopping/unloading, it sends `deaf: false`.

Enable the feature, then press **Open Discord** or **Copy URL** in the settings panel. Client id and redirect URI are hardcoded:

```text
client_id: 1512405963490197664
redirect_uri: http://127.0.0.1:5672
copy_url: https://discord.com/oauth2/authorize?client_id=1512405963490197664&response_type=token&redirect_uri=http%3A%2F%2F127.0.0.1%3A5672&scope=identify+rpc+rpc.voice.write
```

The mod starts a tiny loopback-only HTTP listener inside ADOFAI on port `5672`, opens/copies that OAuth URL, and Discord redirects back to `http://127.0.0.1:5672#access_token=...`. The callback page passes that token back to ADOFAI, then the mod uses it for Discord desktop RPC `AUTHENTICATE`.

Discord's `rpc` and `rpc.voice.write` scopes require approved partner access or tester access for the app.

## File tree

```text
BypassedResourcePack/
├── BypassedResourcePack.csproj
├── meta/
│   └── Info.json
├── Fonts/
│   ├── LinotteBold.otf
│   └── LinotteSemiBold.otf
├── localization/
│   ├── en.json
│   ├── kr.json
│   └── cn.json
└── src/
    ├── Mod/
    │   ├── Main.cs          # entry point, lifecycle, Harmony, update loop
    │   ├── Settings.cs      # persisted ModSettings
    │   ├── SettingsGui.cs   # UMM settings panel
    │   ├── Localization.cs  # JSON loader + lookup
    │   ├── I18n.cs          # terse Tr() alias
    │   └── Log.cs           # logging (honors Verbose logging)
    ├── Overlayer/
        ├── OverlayerStats.cs    # value brain + JS-tag ports
        ├── OverlayerPatches.cs  # Harmony hooks (hit / run lifecycle)
        ├── OverlayerOverlay.cs  # canvas + TMP panels
        └── OverlayerFont.cs     # Linotte runtime font loading
    └── Discord/
        ├── DiscordRpc.cs              # Discord desktop IPC protocol client
        ├── DiscordLocalOAuthServer.cs # 127.0.0.1:5672 OAuth callback
        └── DiscordAutoDeafen.cs       # threshold/lifecycle glue
```

## Build

```bash
dotnet build -c Release
```

Outputs:

```text
build/bin/Release/netstandard2.1/BypassedResourcePack.dll
dist/BypassedResourcePack.zip
```

The zip and install both include the `localization/` folder next to the dll.

## Install

```bash
dotnet build -c Release -p:Install=true
```

If ADOFAI is not in a default Steam path, pass the game path:

```bash
dotnet build -c Release -p:Game="/path/to/A Dance of Fire and Ice"
```

## Adding a localized string

1. Add the key to every file in `localization/`.
2. Use it: `GUILayout.Label(I18n.Tr("my.key"));`

Missing keys fall back to English, then to the raw key, so partial translations are safe.
