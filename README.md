# Bingo the Spire

A **Slay the Spire 2** mod that puts a bingo board in every run. Squares complete **automatically** when the game detects each goal, so nobody marks squares by hand.

- **Solo:** start any run and the board is there. Complete a line before the run ends.
- **Co-op:** everyone is in the same run. Three modes, chosen by the host:
  - **Versus:** everyone gets their own copy of the same board, and the first line wins.
  - **Lockout:** one shared board. Claiming a square locks it for everyone else.
  - **Team:** one shared board, filled together.
- **Boards can be replayed and shared:** each board is built from the run seed, or from a bingo seed you enter.
- **Works with other mods:** modded characters and events don't break it.
- **Works on main and beta** with a single build.

> **Status:** early development, no playable build yet. The design is in [`docs/design-spec.md`](docs/design-spec.md).

## Building

Requirements:
- the .NET SDK (9 or later; the tests run on .NET 10),
- Slay the Spire 2 installed.

```sh
dotnet build                          # builds and copies the mod to <game>/mods/BingoTheSpire/
dotnet build -p:DeployToGame=false    # builds without touching the game folder
dotnet test                           # runs the Core unit tests (no game needed)
dotnet run --project tools/BranchCheck   # checks the built mod against each branch's game files in refs/
```

If the game isn't installed at the default Steam path, create a `local.props` file in the repo root (it's gitignored):

```xml
<Project>
  <PropertyGroup>
    <GameDir>D:\SteamLibrary\steamapps\common\Slay the Spire 2</GameDir>
  </PropertyGroup>
</Project>
```

To compile against a specific branch's game files, put copies of `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` in `refs/main/` (used automatically when present) or set `Sts2RefDir`. Game files are never committed.

## Repository layout

| Path | Contents |
|---|---|
| `src/BingoTheSpire.Core` | Bingo logic with no game dependencies: events, detectors, goals, board generator, rules |
| `src/BingoTheSpire.Mod` | The mod: entry point, game adapter (hooks), network messages, UI, save files |
| `tests/BingoTheSpire.Core.Tests` | Unit and determinism tests |
| `docs/` | Design spec, guides, archive |

## License

All rights reserved. The source is public to read, but it isn't open source. See [`LICENSE`](LICENSE).
Not affiliated with Mega Crit.
