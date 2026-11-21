# CLAUDE.md

Guidance for AI agents working on this repo.

## Source of truth
- **`docs/design-spec.md` is the design.** Read the relevant section before changing behavior. If a change departs from the spec, update the spec in the same commit, or ask first.
- **Notes on the game's internals live in `notes/game-internals.md`, which is local and gitignored.** Read it before working on the game adapter, and add to it when you discover something. The public repo holds only project code and docs about how the mod works, so never move these notes into `docs/` or commit them.

## Architecture rules (do not break)
1. **`BingoTheSpire.Core` has no game references.** No `sts2.dll`, Godot or Harmony types. Everything in Core must be testable with plain `dotnet test`.
2. **Only the game adapter in `BingoTheSpire.Mod` touches game internals.** It turns game hooks into Core events, tagged with the player who caused them.
3. **The mod only watches the game.** Patches are postfixes or official hook listeners that never change game state. No prefixes that skip the original method, no transpilers. Catch exceptions inside every hook so nothing reaches the game.
4. **Determinism:** never use `string.GetHashCode()`, `System.Random`, the game's random number streams, or the clock for board generation. Use `StableHash` and our own seeded random number generator.
5. **A goal is completed by one player alone,** except pooled goals in Team mode (spec 4.3). Never add values up across players anywhere else; a detector only pools when the catalog marks it `Pools` and the board is Team.
6. **One build runs on main and beta.** Build against main's `sts2.dll`. Look up anything that may differ between branches by name with fallbacks, and turn off the goals that depend on it if it's missing. Don't branch on version strings.
   - **Never compare game enums by their compiled value.** They're compiled into our DLL as numbers, and the numbers change between branches. For example, `GameMode.Custom` = 3 on main, but 3 means `Dread` on beta. Read enums by name through the adapter.
   - Hooks whose signature differs between branches (e.g. `AfterDeath`) need a handler for each branch. See `notes/game-internals.md` (local).
7. **Compatibility with other mods:** general goals must count modded content. Don't hard-code vanilla IDs except in goals tied to a specific character.
8. **Never commit game files** (`sts2.dll`, decompiled source, assets). `refs/` is gitignored for this reason.

## Layout
- `src/BingoTheSpire.Core/`: bingo logic only, no game dependencies. Its source files are **compiled into** the mod's DLL through a linked `<Compile Include>`. The game loads exactly one `<id>.dll` per mod.
- `src/BingoTheSpire.Mod/`: entry point `ModEntry` (`[ModInitializer]`), manifest `BingoTheSpire.json` (the `id` must equal the AssemblyName and must never change).
- `tests/BingoTheSpire.Core.Tests/`: xUnit tests on net10.0. Core and the mod target net9.0 to match the game.
- `refs/main/`, `refs/beta/` (gitignored): copies of the game DLLs for each branch, for compiling and comparing branches.

## Commands
```sh
dotnet build -p:DeployToGame=false   # build only
dotnet build                         # build + copy the mod into <GameDir>/mods/BingoTheSpire
dotnet test                          # Core tests
dotnet run --project tools/BranchCheck   # after a build: check the mod against every branch in refs/
```
Game path: `GameDir` in `Directory.Build.props`, overridden by the gitignored `local.props`.

## Reading game code
- Decompile with `ilspycmd` into a temporary or scratch folder, never inside the repo.
- `sts2.xml` next to `sts2.dll` has the game's own documentation comments for many types.
- The game is Godot 4.5.1 + .NET 9 + Harmony 2.4.2.
