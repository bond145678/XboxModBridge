# Xbox Mod Bridge

**Use the CurseForge app to mod your Xbox app / PC Game Pass games.**

The CurseForge app only installs mods into Steam and Epic copies of a game. Xbox app (Game Pass /
Microsoft Store) copies are laid out differently, so CurseForge either can't find them or puts mods
where the Xbox game never looks. Xbox Mod Bridge sets up the Xbox copy so CurseForge treats it like
any other install. Then you install, update and remove mods in CurseForge as usual.

## Download

Get `XboxModBridge-vX.Y.Z.zip` from [Releases](../../releases), extract it, and run `XboxModBridge.exe`.
No install and no admin rights are needed.

> **"Windows protected your PC"?** The program isn't code-signed yet. Click **More info → Run anyway**.
> The full source is in this repository.

## Requirements

- Windows 10 or 11.
- The game installed by the Xbox app into a normal folder such as `C:\XboxGames`
  (Xbox app → Settings → Install options). Games in `C:\Program Files\WindowsApps` can't be modded.
- The [CurseForge app](https://www.curseforge.com/download/app), opened at least once.

## How to use it

1. Run **XboxModBridge.exe**. It lists your Xbox app games and the CurseForge game each one matches.
2. Pick your game and click **Connect to CurseForge**. The game's folder is copied to your clipboard.
3. In the CurseForge app, open the game and click **Locate Game** (or right-click the game →
   **Manually add a game**), then paste the folder with Ctrl+V.
4. Install mods in CurseForge as usual.
5. After installing or updating mods, open Xbox Mod Bridge and click **Repair now**.
   While its window is open, it repairs by itself.

## What it changes

Everything happens inside the game's own folder, and **Disconnect** undoes all of it.
It never edits CurseForge's files.

| Problem on Xbox | What Xbox Mod Bridge does |
|---|---|
| Unreal Engine games run from `Binaries\WinGDK` on Xbox, but CurseForge installs UE4SS and UE4SS mods into the Steam folder `Binaries\Win64` | Makes `Win64` a folder link (junction) to `WinGDK`, so everything CurseForge installs lands where the Xbox game reads it |
| CurseForge only accepts a game folder holding a file it expects, usually the Steam launcher (e.g. `RSDragonwilds.exe`); Xbox copies start through `gamelaunchhelper.exe` | Adds the expected name as a hard link to the Xbox launcher. It uses no extra disk space, and double-clicking it still starts the game |
| Some games' Xbox builds need extra fixes before their mod loader works | Writes known fixes and puts them back whenever CurseForge replaces them |

## Supported games

The folder link and CurseForge detection work for any Xbox app game that CurseForge supports.

Some Unreal games' Xbox builds also need UE4SS signatures before UE4SS mods load. Built-in fixes:

- **RuneScape: Dragonwilds**: UE4SS `StaticConstructObject` signature for the Xbox build

If UE4SS mods don't load for another game, check `ue4ss\UE4SS.log` in the game's
`Binaries\WinGDK` folder. You can add fixes in a `fixpacks.json` next to `XboxModBridge.exe`
(see `fixpacks-example.json`). Pull requests with fixes for more games are welcome.

## Disconnecting a game

1. Remove the game's mods in CurseForge.
2. Remove the game from CurseForge (right-click it → Remove).
3. In Xbox Mod Bridge, select the game and click **Disconnect**.

## Command line

```
XboxModBridge.exe --scan                                  list games and their state
XboxModBridge.exe --repair                                one repair pass
XboxModBridge.exe --link "<Content folder>" [game id]     connect a game (CurseForge game id optional)
XboxModBridge.exe --unlink "<Content folder>"             disconnect a game
```

## Building

Requires the .NET 10 SDK.

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

## Disclaimer

This is a community tool. It isn't made by or affiliated with CurseForge, Overwolf, Microsoft
or any game's developer. Back up anything you care about, and use it at your own risk.
