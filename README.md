# Blue Chain — DLL Mod Loader

Blue Chain is an independent DLL mod loader for Sea Power with Anchor Chain (AC) compatibility. It is designed to let you use existing AC-compatible mods without modifying or recompiling them.

## Features

- **Minimal reliance on game internals.** Blue Chain limits its reliance on Sea Power's internal mod management and initialization order. Future game updates may still affect compatibility.
- **Open source.** Blue Chain is released under the [MIT License](LICENSE).

## Installation

Close Sea Power. Install BepInEx 5 for Unity Mono if it is not already installed. On Proton, follow the [BepInEx Proton setup](https://docs.bepinex.dev/master/articles/advanced/steam_interop.html).

- If switching from Anchor Chain, remove `BepInEx/plugins/AnchorChain.Preloader.dll` and unsubscribe from Anchor Chain on the Workshop.
- If upgrading from Blue Chain 0.1.0, remove the old `BepInEx/plugins/BlueChain/BlueChain.dll` and `BepInEx/plugins/BlueChain/AnchorChain.dll`.

### Local installation from GitHub

1. Extract `BlueChain-<version>.zip` into the folder containing `Sea Power.exe`. This places the Preloader under `BepInEx/plugins` and the runtime under `Sea Power_Data/StreamingAssets/BlueChain`.
2. Launch Sea Power, enable Blue Chain in the Mod Manager, and restart the game.

### Workshop installation

1. Extract `BlueChain-<version>-Preloader.zip` into the folder containing `Sea Power.exe`. The ZIP includes the Workshop configuration.
2. Subscribe to [Blue Chain on the Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3842265825).
3. Launch Sea Power, enable Blue Chain in the Mod Manager, and restart the game.

### Runtime configuration

The BepInEx Preloader starts the runtime from the selected mod folder. The runtime package includes Blue Chain's compatibility assembly for existing Anchor Chain plug-ins. By default, the Preloader looks for a folder named `BlueChain`.

```ini
[Runtime]
ModFolder = BlueChain
```

The Preloader looks for that folder name under `StreamingAssets` first, then among subscribed and installed Workshop folders. An existing local folder takes precedence, and its own checkbox determines whether Blue Chain starts.

### Troubleshooting

Read `BepInEx/LogOutput.log` for initialization results, skipped plug-ins, and errors.

Steam Workshop requirements are configured by each mod author. A Workshop page or the game may still display a dependency on Anchor Chain.

## Initial compatibility

- The Preloader resolves the selected folder's saved checkbox from `usersettings.ini` in Sea Power's load order. It looks up each saved row by Workshop ID, then by folder name when that item is unavailable. A mod without a matching saved ON selection starts disabled.
- The runtime discovers local folders in `StreamingAssets` and uses Steam to locate installed Workshop items across Steam libraries. It reads the DLL metadata of folders selected by Sea Power after the game's startup compatibility check.
- Blue Chain resolves managed library dependencies from enabled mod folders. A duplicate assembly name or plug-in ID is resolved by the game's mod order and a stable order within the folder.
- The compatibility assembly provides the published Anchor Chain plug-in entry point and declarations for dependencies, `before` and `after` ordering, and incompatible plug-ins. `ACConfig` and `AnchorChainLoader.FindFile` use the selected resource folders and `StreamingAssets/ACConfigs`.
- Configuration loading creates missing user INIs, fills in keys from the reference INI, and honors declared reset values. Blue Chain records applied resets under `ACConfigs/BlueChain` so the reference INI can stay read-only.
- Harmony Startup Optimizer (HSO) remains a separate mod in this release. Its optional early-startup plug-in has its own loading path, which needs a separate in-game compatibility check. Proton support also needs in-game verification.

## Build and test

Run these in Windows PowerShell. Use a Sea Power installation with BepInEx 5 for `$gameDirectory`.

```powershell
.\build.ps1 -GameDirectory $gameDirectory
.\tools\test-mono.ps1 -GameDirectory $gameDirectory
```

The build writes ZIPs for local installation, the Preloader with its Workshop ID configured, and Workshop content to `dist`, each with a SHA-256 file. Extract the Workshop content ZIP as the upload directory. Pass `-SourceRevision` with the source commit ID for a release from a committed tree.

The tests run under Sea Power's bundled Mono without launching the game. Steam startup and INI changes made through Unity need in-game verification.

Anchor Chain compatibility API attribution is in [ThirdPartyNotices/AnchorChain.txt](ThirdPartyNotices/AnchorChain.txt).
