# Blue Chain — DLL Mod Loader

Blue Chain is an independent code mod loader for Sea Power. A small BepInEx Preloader starts the runtime from a local mod folder or an installed Workshop item. The runtime package includes Blue Chain's compatibility assembly for existing Anchor Chain plug-ins.

## Installation

1. Install BepInEx 5 for a Windows Unity Mono game. On Proton, follow the [BepInEx Proton setup](https://docs.bepinex.dev/master/articles/advanced/steam_interop.html).
2. Choose Blue Chain as the code mod loader. If upgrading from Blue Chain 0.1.0, remove the previous `BepInEx/plugins/BlueChain/BlueChain.dll` and `BepInEx/plugins/BlueChain/AnchorChain.dll`. If switching from Anchor Chain, remove `AnchorChain.Preloader.dll` from the BepInEx plug-ins directory.
3. For local installation, extract `BlueChain-<version>.zip` into your Sea Power installation. This places the Preloader under `BepInEx/plugins` and the runtime under `Sea Power_Data/StreamingAssets/BlueChain`.
4. Enable Blue Chain in the Mod Manager, confirm the changes, and restart Sea Power.
5. Read `BepInEx/LogOutput.log` for initialization results, skipped plug-ins and errors.

For Workshop installation, install the Preloader ZIP, subscribe to the Blue Chain Workshop item, and let Steam finish downloading it. Set `ModFolder` in `BepInEx/config/bluechain.preloader.cfg` to the item's actual folder name. Steam normally names that folder with the item ID. BepInEx creates the configuration when the Preloader first starts.

```ini
[Runtime]
ModFolder = BlueChain
```

The Preloader looks for that folder name under `StreamingAssets` first, then among subscribed and installed Workshop folders. An existing local folder takes precedence, and its own checkbox determines whether Blue Chain starts.


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

The build writes ZIPs for local installation, the standalone Preloader, and Workshop content to `dist`, each with a SHA-256 file. Extract the Workshop content ZIP as the upload directory. Pass `-SourceRevision` with the source commit ID for a release from a committed tree.

The tests run under Sea Power's bundled Mono without launching the game. Steam startup and INI changes made through Unity need in-game verification.

Anchor Chain compatibility API attribution is in [ThirdPartyNotices/AnchorChain.txt](ThirdPartyNotices/AnchorChain.txt).
