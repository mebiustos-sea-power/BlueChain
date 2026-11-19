# Blue Chain — DLL Mod Loader

Blue Chain is an independent code mod loader for Sea Power. Players choose to install it and can keep using compatible existing Anchor Chain plug-ins. The package includes Blue Chain's own compatibility assembly, which supplies the type names used by those plug-ins.

## Installation

1. Install BepInEx 5 for a Windows Unity Mono game. On Proton, follow the [BepInEx Proton setup](https://docs.bepinex.dev/master/articles/advanced/steam_interop.html).
2. Choose Blue Chain as the only code mod loader. For an Anchor Chain installation, remove `AnchorChain.Preloader.dll` from the BepInEx plug-ins directory. Keep the shared BepInEx installation and the mods you want to use.
3. Copy the ZIP's `BepInEx` folder into the Sea Power installation. Both DLLs belong in `BepInEx/plugins/BlueChain`. The `AnchorChain.dll` in this directory is part of Blue Chain.
4. Select the mods in Sea Power's mod manager and restart the game. Read `BepInEx/LogOutput.log` for initialization results, skipped plug-ins and errors.

Steam Workshop requirements are configured by each mod author. A Workshop page or the game may still display a dependency on Anchor Chain.

## Initial compatibility

- Blue Chain discovers local folders in `StreamingAssets` and uses Steam to locate installed Workshop items across Steam libraries. It reads the DLL metadata of folders selected by Sea Power after the game's startup compatibility check.
- Blue Chain resolves managed library dependencies from enabled mod folders. A duplicate assembly name or plug-in ID is resolved by the game's mod order and a stable order within the folder.
- The compatibility assembly provides the published Anchor Chain plug-in entry point and declarations for dependencies, `before` and `after` ordering, and incompatible plug-ins. `ACConfig` and `AnchorChainLoader.FindFile` use the selected resource folders and `StreamingAssets/ACConfigs`.
- Configuration loading creates missing user INIs, fills in keys from the reference INI, and honors declared reset values. Blue Chain records applied resets under `ACConfigs/BlueChain` so the reference INI can stay read-only.
- Harmony Startup Optimizer (HSO) remains a separate mod in this release. Its optional early-startup plug-in has its own loading path, which needs a separate in-game compatibility check. Proton support also needs in-game verification.

## Build and test

Run these in a Windows PowerShell session. Set `$gameDirectory` to the path of a Sea Power installation with BepInEx 5.

```powershell
.\build.ps1 -GameDirectory $gameDirectory
.\tools\test-mono.ps1 -GameDirectory $gameDirectory
```

The build writes an installation ZIP and SHA-256 file to `dist`. Pass `-SourceRevision` with the source commit ID for a release from a committed tree. The test runs generated plug-ins under Sea Power's bundled Mono without launching the game. Steam startup and reading or writing INIs through Unity need in-game verification.

Anchor Chain compatibility API attribution is in [ThirdPartyNotices/AnchorChain.txt](ThirdPartyNotices/AnchorChain.txt).
