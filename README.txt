XBOX MOD BRIDGE - CurseForge mods for Xbox app / Game Pass PC games

The CurseForge app only installs mods into Steam and Epic copies of a game. Xbox app (Game Pass /
Microsoft Store) copies are laid out differently, so CurseForge either can't find them or puts mods
where the Xbox game never looks. This program fixes that, so you can install, update and remove mods
in CurseForge as normal.

WHAT YOU NEED
- The game installed by the Xbox app into a normal folder such as C:\XboxGames.
  (Xbox app > Settings > Install options. Games inside C:\Program Files\WindowsApps can't be modded.)
- The CurseForge app (curseforge.com/download/app), opened at least once.

HOW TO USE IT
1. Run XboxModBridge.exe. It lists your Xbox app games and the CurseForge game each one matches.
2. Pick your game and click "Connect to CurseForge". The game's folder is copied for you.
3. In the CurseForge app, open the game and click "Locate Game" (or right-click the game >
   "Manually add a game"), then paste the folder with Ctrl+V.
4. Install mods in CurseForge as usual.
5. After installing or updating mods, open XboxModBridge again and click "Repair now".
   While its window is open it repairs by itself.

WHAT IT CHANGES (all inside the game's own folder, all undone by Disconnect)
- Unreal Engine games: Steam copies run from <Game>\Binaries\Win64, Xbox copies from
  <Game>\Binaries\WinGDK. Win64 is added as a folder link (junction) to WinGDK, so whatever
  CurseForge installs there - UE4SS, UE4SS mods - lands where the Xbox game reads it.
- CurseForge recognises a game folder by a file it expects to find (usually the Steam launcher,
  e.g. RSDragonwilds.exe). The Xbox copy starts through gamelaunchhelper.exe instead, so that file
  is added under the expected name as a hard link to the Xbox launcher (no extra disk space;
  double-clicking it still starts the game).
- Known Xbox fixes for a game's mod loader are written in and put back whenever CurseForge
  replaces them. Built in: RuneScape: Dragonwilds (UE4SS signature for the Xbox build).
It never edits CurseForge's own files and needs no admin rights.

GAMES THAT NEED MORE
Some Unreal games' Xbox builds need extra UE4SS signatures before UE4SS mods load (check
ue4ss\UE4SS.log). Add them in a fixpacks.json next to XboxModBridge.exe - same format as the
built-in one included here. Fixes are matched by the Unreal project folder name ("ueProject") or
the Xbox package identity ("identityName").

DISCONNECT
Remove the game's mods in CurseForge first, remove the game from CurseForge (right-click > Remove),
then select the game here and click Disconnect.

COMMAND LINE
  XboxModBridge.exe --scan            list games and their state
  XboxModBridge.exe --repair          one repair pass
  XboxModBridge.exe --link "<Content folder>" [CurseForge game id]
  XboxModBridge.exe --unlink "<Content folder>"
