# Building

This source builds against the Colony Survival server assemblies.

Default Windows build:

```powershell
dotnet build .\BetterColonyCommands.sln -c Release
```

If Colony Survival is installed somewhere else, pass the install directory:

```powershell
dotnet build .\BetterColonyCommands.sln -c Release -p:ColonySurvivalDir="D:\SteamLibrary\steamapps\common\Colony Survival"
```

The compiled mod DLL is `ColonyCommands.dll`. Release ZIPs should include the DLL, `modInfo.json`, `README.md`, `LICENSE`, `preview.png`, and the example JSON config files.

Release files: the DLL plus `modInfo.json`, `README.md`, `CHANGELOG.md`, `LICENSE`, `preview.png`, `ui-logo.png` and the example JSON files.

Sources live in `src/` and its subfolders (`src/Api`, `src/UI`, `src/Economy`, `src/Settings`, `src/Util`); the project compiles `src\**\*.cs`.

Testing without a game client: see "Self Test" in the README (a dedicated server started with `colonyserver.exe -batchmode -nographics +server.config <launch.json>` runs the commands from `bcc-selftest.txt`).
