# StS2 Bingo: design spec

> **This is the source of truth for the design.** If code and this document disagree, one of them is wrong: fix the code or update this doc in the same change.
> The first draft is kept at [`archive/sts2_bingo_v0_2026-11-20.md`](archive/sts2_bingo_v0_2026-11-20.md) for history only. Where it disagrees with this doc, this doc wins.
> What we found in the game code is in [`game-internals.md`](game-internals.md).

| | |
|---|---|
| Status | Design agreed, repo skeleton set up, no gameplay code yet |
| Last updated | 2026-11-20 |
| Game versions checked | main `v0.111.1`, beta `v0.113.1` (the beta code was decompiled and read) |

---

## 1. What it is

A Slay the Spire 2 mod that puts a **bingo board in every run**. Squares fill in **by themselves** when the game sees you complete each goal. Nothing runs on the honor system.

- **No setup and no separate lobby.** Install the mod and start a run, solo or co-op, and a board is there with its own button to open it.
- **Solo:** try to complete a line before the run ends.
- **Co-op:** everyone is in **the same run** (the game's own multiplayer), and you play one of three bingo modes against or with your teammates (section 4).
- **The same board can be replayed:** it's built from the run seed (or a bingo seed you type in), so a board can be shared and played again.
- **Works next to other mods:** modded characters, events, cards and relics don't break it (section 8).
- **Works on main and beta:** one build of the mod runs on both Steam branches. Both are supported in v1 (section 9).

## 2. Why, and what already exists

Checked 2026-11-20 on Nexus (all 1,020 StS2 mods, full text), Steam Workshop, GitHub, Thunderstore, and the web in English and Chinese. **No StS2 mod plays bingo in the game with automatic marking.**

People already play StS2 bingo by hand on Bingosync, and on *Spire Lockout Bingo* (sweet115.github.io/spire-lockout-bingo), a web page where you mark squares yourself. That shows people want it. This mod adds:
- **detection by the game**, so there's no honor system,
- **the board inside the game**, so there's no second window,
- **exact-value goals** settled by code (e.g. exact lethal on an elite),
- **boards balanced by difficulty**.

Related projects, so this one stays different: Spire Race (same-seed racing, no bingo), SPIRECRAFT (single-player custom challenges), HollowKnight.BingoSync (auto-marking for another game).

Before release: ask once in the StS2 modding Discord whether anyone else is building this.

## 3. Goals and non-goals

**Goals for v1**
1. Solo and co-op bingo in any run, with no setup needed.
2. Every square is checked by the game. If the game can't check a goal, the goal isn't on the list.
3. The same seed, characters and versions always give the same board.
4. Co-op modes: Versus (default), Lockout, Team.
5. Works with other mods, including modded characters and modded events.
6. One build that runs on both **main and beta**.
7. Continuing a saved run brings back the board and its progress.
8. Local history of past boards and results, shown in the game.

**Not in v1** (see section 16 for later ideas)
- Other board sizes, difficulty settings, Blackout, solo Score mode.
- Custom or user-written boards.
- Team-only squares that scale with player count.
- Web spectator view, OBS overlay, online leaderboards.
- Anything that changes gameplay. The mod only watches.

## 4. How it plays

### 4.1 Starting a run

| Where | What the player sees |
|---|---|
| **Custom run screen** | A **Bingo** section above the run-seed field: a bingo-seed box, plus the mode picker in co-op |
| **Normal run (solo or co-op lobby)** | A small **Bingo** button on the character-select / lobby screen. It opens a panel with the bingo-seed box and, in co-op, the mode picker |
| **Co-op** | Only the **host** sets the bingo options. Other players see them, read-only, and they're sent to them over the game's own lobby connection |

If nothing is set, the bingo seed is empty: the board is built from the run seed and the mode is Versus. The board is built when the run starts and stays fixed for that run.

### 4.2 Solo
- One board. Squares fill as you complete goals: a toast appears and the square pulses.
- **Win: the first line** (row, column or diagonal) gets the "BINGO!" banner. After that, the board stays active: **the 2nd, 3rd and later lines are tracked**, with the floor and run time for each.
- When the run ends (win or death), the bingo results are recorded. Winning the run and winning the bingo are separate results.

### 4.3 Co-op: three modes, chosen by the host

| Mode | Board(s) | Who gets a square | How you win |
|---|---|---|---|
| **Versus** (default) | Every player has **their own copy of the same board** | Each player fills their own squares | **First line.** Play continues; later placements are tracked (Alex 1st on floor 18, Sam 2nd on floor 23, …) |
| **Lockout** | **One shared board.** A square takes the color of the player who got it | The **first** player to complete a goal gets the square, and it's **locked** for everyone else | **First line, or more than half the board** (13 of 25). If the run ends with neither, whoever has the most squares wins. Ties go to whoever reached that count first |
| **Team** | **One shared board.** A square takes the color of the player who completed it | **Any one player** completing the goal fills it for the team | Cooperative. The result is squares filled, lines made, and the floor where the first line was made |

Rules for every mode:
- **A goal is always completed by one player alone.** Numbers are never added up across players: "Gain 50+ Block in a turn" means *one* player gains 50, not the team combined.
- In Versus and Lockout, the board only uses goals that **every** player's character can complete. In Team, a character's goals can appear if **anyone** is playing that character. Modded characters only get the general goals (section 6.4).
- Lockout can be used to block other players (including taking the kill on an elite). That's part of the mode, and players choose it on purpose. Versus is the default for that reason.
- If a player leaves or disconnects, their squares stay theirs. When they come back, they get the current board (section 7).

### 4.4 In-run UI

- **Bingo button + hotkey** (default `B`, can be changed; works with mouse and controller) opens the full board.
- **Your board:** 5×5 grid showing each goal's text, filled squares, and **progress on goals in progress** (e.g. "7/10 cards this turn", "Block 38/50").
- **Teammate previews** (co-op): one small board per teammate showing only which cells are filled. **Clicking one opens that player's full board.** In Lockout and Team, the one shared board shows everyone's colors instead.
- **Hover details:** who completed the square, on which floor and turn, and the evidence (e.g. "53 Block on turn 4, Act 2 floor 22").
- **"One away" marker:** shown when any player (you or a teammate) needs one more square for a line.
- **Activity feed:** "Sam completed *50+ Block* (floor 22)", "BINGO: Alex (floor 18)", and so on.
- **Toasts** for your own completions and important events (a teammate's bingo, a locked square you were working on).
- **Mini-board** that can be pinned in a corner: about 120 px, colors only, hover a cell to see its goal.
- **Results** after the game's own run summary: final board(s), lines and their floors and times, placements, and the board code.
- **Settings:** hotkey, mini-board corner, colorblind-safe player colors.

## 5. Seeds and determinism

**Rule: the same inputs always give the same board, on every machine.**

```
seedText   = bingoSeed (if one was entered) ?? runSeed      // both are the game's 12-character seed format
boardSeed  = StableHash64("sts2bingo|" + seedText + "|" + goalListVersion + "|" + mode + "|" + sortedCharacterIds)
board      = Generate(boardSeed, goalList filtered by (characters, available detectors), mode)
```

- **The run seed is the default.** The game keeps the seed as a string (`RunRngSet.StringSeed`, e.g. `7K2M9QX4ZB1C`). Every co-op player already has it, so in the default case **nothing about the board needs to be sent**. Sharing a run seed shares the whole experience.
- **The bingo seed overrides it.** Uppercased and cleaned the same way the game does it (`SeedHelper.CanonicalizeSeed`: O→0, I→1). It lets you replay the same board on a different run seed, or a different board on the same run seed.
- **The mod uses its own random numbers.** The board builder never uses the game's random number streams, so building a board can't change the run (map, rewards, shuffles).
- **Stable hashing only.** We never use .NET's `string.GetHashCode()` (it changes every launch); we use our own hash (FNV-1a 64 or xxHash64) and our own seeded random number generator (SplitMix64 / xoshiro256**). Both are covered by tests.
- **The board depends on the characters in the run**, because character-specific goals are filtered. So "same seed + same characters + same versions = same board."
- **The board depends on which goals can be detected.** Goals whose detector can't run on this game version are left off before the board is built (section 9). In co-op every player is on the same game version (the game checks this when someone joins), so every player gets the same board.
- **Board codes:** a short string encoding `(seedText, goalListVersion, mode, size)`. Pasting one into the bingo-seed box reproduces the board, as long as the characters match. If the code's goal-list version isn't available, or its goals can't be detected on this version, the mod says so clearly instead of quietly building a different board.

## 6. Goals and the board builder

### 6.1 Goals are stored as data, not one hand-coded square each
Each goal in the goal list is a **template plus parameters**, so difficulty can scale by changing a number, and custom boards (section 16) become possible later:

```jsonc
{
  "id": "block_in_turn",
  "template": "Gain {n}+ Block in a single turn",
  "detector": "BlockInTurn",          // detector type name
  "params": { "n": [30, 50, 80] },    // values the generator can pick from
  "difficulty": { "30": 1, "50": 2, "80": 4 },
  "tags": ["combat", "block"],
  "exclusions": ["block_in_combat"],  // goals too similar, or that contradict this one
  "requires": { "events": ["BlockGained", "TurnStarted"], "character": null }
}
```

- The goal list is an embedded, **versioned** JSON file (`goalListVersion`).
- `requires.events` says which game events the detector needs. If any are missing on this game version, the goal is left out (section 9).
- `requires.character` is optional. Character-specific goals are matched by the game's character ID.

### 6.2 Board builder
- Works like Bingosync's "SRL" generator: every row, column and both diagonals add up to about the same total difficulty.
- No two goals with overlapping tags in the same line, exclusions are respected, and no goal appears twice (including the same template with different parameters).
- v1 default: **5×5, medium difficulty mix**. Other sizes and difficulty settings are built into the data model but not shown in the menus.

### 6.3 Detectors
- They're **plain C#** with no game types: small state machines that read game events (section 9.2) plus a read-only snapshot of the run.
- They output `Completed(goalId, playerId, evidence)` and optionally `Progress(goalId, playerId, current, target)` for the in-progress counters.
- **Each player is tracked separately.** Every event carries the player who caused it, and detectors never add values up across players.
- They can be unit-tested by replaying recorded event logs, without starting the game.

### 6.4 Rules that keep goals working with other mods
- **General goals count all content, modded included:** "10 different buffs" counts any power, modded ones too. "Play 10 cards in a turn" counts any card.
- **Goals never depend on a list of vanilla IDs, unless the goal is about specific vanilla content and is marked as character-specific** (e.g. "Channel all 4 orb types as Defect").
- Goals about rooms and events use **room types** (elite, event, shop, rest), not specific event IDs, so modded events still count.
- **Modded characters** get only the general goal pool. Any character the goal list doesn't know is treated as "general only."
- If another mod changes the game so much that a goal can never be completed, that's acceptable. That should be rare, and it's the player's choice to combine those mods.

### 6.5 First goal list (v1)
About 40–60 goals of the kinds that are easy to detect: event counters (cards per turn, Block, debuffs, potions), deck and run state (fully upgraded deck, unique cards, number of relics), exact values (overkill = 0; the game's `DamageResult.OverkillDamage` gives this directly), and time (act cleared within a time limit). The categories and examples in the [v0 draft §6](archive/sts2_bingo_v0_2026-11-20.md) are still the starting list.

## 7. Co-op syncing

**Everything goes over the game's own co-op connection.** No Steam lobby of our own, no relay server, no cost.

- **Messages:** the mod defines its own message types (`INetMessage` structs). The game finds message types in mods automatically (`MessageTypes.Initialize` → `ReflectionHelper.GetSubtypesInMods<INetMessage>()`). All bingo messages are sent reliably.
- **The host decides.** Every claim, the host's own included, goes through one function that applies the mode's rules:
  1. A player's game detects a goal and sends `BingoClaimRequest { square, goalId, player, floor, turn, evidence }` to the host.
  2. The host applies the mode's rules (Lockout: first valid claim wins; Versus and Team: accept) and assigns a sequence number.
  3. The host sends `BingoClaimResult { seq, square, player, accepted, … }` to everyone. Every player's game applies results in sequence order, so all boards stay the same.
- **Lobby options:** `BingoSettingsChanged { mode, bingoSeed }` goes from host to clients in the lobby. Before the run starts, everyone builds the board and checks it matches the host's (`BingoBoardHash`). If it doesn't, bingo is turned off for that run with a clear message, and the run itself is not affected.
- **Rejoin or late join:** the host sends `BingoStateSnapshot` (settings, board hash, the full list of claims in order).
- **If the host leaves:** the co-op run itself depends on the host, so bingo ends with the run. Nothing needs to take over.
- **Missing the mod or a different version:** the manifest sets `"affects_gameplay": true`. The game then puts the mod's id and version in the join handshake (`PeerVersionInfo.gameplayAffectingMods`) and blocks joins where the mod lists don't match. Our own message types also need matching mod lists to work. This takes care of "the other player needs the mod" without extra code (confirmed in spike B).

## 8. Saving, continuing and history

- **Run state:** settings, board hash, claims, and detectors' in-progress state (e.g. "7/10 cards this turn") are saved to a small file in the mod's data folder, keyed by the run's seed and start time, and saved after every claim and at every room change. **Continue** reloads it. If the file is missing or doesn't match, the board is rebuilt from the seed, with no claims.
- **History:** one tiny line per finished bingo run (date, mode, seed and board code, characters, result, floor and time of each line, squares filled), stored as JSON lines in `bingo_history.jsonl` in the mod's data folder.
- **History screen:** the in-game panel lists past runs and personal bests (fastest first line by floor and by time, most squares in Team) and **shows the folder path**, with a button to open it, for anyone curious.
- The data folder is a `sts2_bingo/` folder inside the game's user data folder (Godot `user://`). The exact path is confirmed in spike A.

## 9. Supporting main and beta, and how events reach the mod

### 9.1 One build, adapting at runtime
- The mod is **built against the main branch's `sts2.dll`** and the same DLL runs on main and beta.
- **The only code that touches game internals is the game adapter** (`StS2Bingo.Mod/GameAdapter`). Everything else (detectors, board builder, rules, UI state) is in `StS2Bingo.Core` and uses no game types.
- At startup the adapter connects each event source it can and **records which events are available**. The board builder only uses goals whose `requires.events` are all available. **So on any branch, any version, and alongside any other mods, every square on the board can be completed.** Anything that couldn't be connected is logged.
- When a branch has a method under a different name or signature, its hook lists **several possible targets** (main's name first, then beta's).
- Where a branch works **differently** (not just a different name), it gets its own small adapter class for each branch, picked by checking which methods exist, not by reading the version string.
- **Branch check:** a small tool compares the main and beta copies of `sts2.dll` for every type and method the adapter uses. It runs whenever a game update arrives. Reference DLLs live in the gitignored `refs/main/` and `refs/beta/` folders.

### 9.2 Where events come from (most to least preferred)
1. **The game's official mod hooks.** `ModHelper.SubscribeForCombatStateHooks` and `ModHelper.SubscribeForRunStateHooks` let a mod return its own listener models, and the game then calls about 190 `After*` / `Before*` hooks on them (`AfterCardPlayed`, `AfterBlockGained`, `AfterDamageGiven` with `DamageResult.OverkillDamage`, `AfterPowerAmountChanged`, `AfterRoomEntered`, `AfterCombatVictory`, `AfterItemPurchased`, `AfterPotionUsed`, …). These are a public API made for mods, so they're the most likely to stay the same across branches and patches.
   - **Split listeners into small groups by area** (combat, deck, map, economy, …), and register each group inside its own try/catch. If one hook's signature doesn't match on a branch, only that group fails to load.
   - Listeners **only watch**: they always return `Task.CompletedTask` and never change game state.
2. **Harmony postfix patches**, for anything the official hooks don't cover (e.g. UI screens, the run timer, save and continue). Targets are looked up by name with fallbacks (`TargetMethod()` + `Prepare()`, skipped when nothing matches). **Postfixes only. Never prefixes that skip the original method, and never transpilers.**
3. **Reading the run state**, for deck and run checks (deck contents, relics, gold, act, floor), done when relevant events fire.

The adapter turns everything into **events from `StS2Bingo.Core`** (`CardPlayed`, `BlockGained`, `DamageDealt`, `PowerApplied`, `TurnStarted`, `CombatEnded`, `RoomEntered`, `DeckChanged`, `ActCompleted`, …), each tagged with the player who caused it.

**Must be confirmed in spike A:**
- A listener model has to be an `AbstractModel` (its only abstract member is `ShouldReceiveCombatHooks`), and its constructor registers an ID with `ModelDb`. Check how mod model types get registered, and that adding one doesn't change saves.
- In co-op, check whether hooks run on every player's game for every player (the game simulates everyone), or only for the local player. Either way, **each game claims only for its own local player** so nothing is counted twice.

## 10. Project structure and tech

| | |
|---|---|
| Runtime | .NET 9 (`net9.0`, matching `sts2.runtimeconfig.json`), C# latest |
| Engine | Godot 4.5.1 (GodotSharp 4.5.1) |
| Patching | Harmony 2.4.2 (`0Harmony.dll`, comes with the game) |
| Mod loading | The game's built-in `ModManager`: `mods/<folder>/<id>.json` + `<id>.dll` (+ an optional `<id>.pck`, which a code-only mod doesn't need) |
| Entry point | A class marked `[ModInitializer("Initialize")]` |
| BaseLib | Allowed. Use it only where it saves real work (e.g. UI helpers), because every dependency also has to work on both branches |

```
StS2Bingo.sln
src/
  StS2Bingo.Core/         pure logic: events, detectors, goals, generator, rules, state, hashing (no game references)
  StS2Bingo.Mod/          the mod: entry point, game adapter (hooks/patches), network messages, Godot UI, saving
tests/
  StS2Bingo.Core.Tests/   unit, determinism and rule tests; replays of recorded event logs
docs/                     this spec, game-internals notes, archive
refs/  (gitignored)       copies of sts2.dll etc. for main and beta, used by the branch check
```

Manifest (`StS2Bingo.json`):
```json
{
  "id": "StS2Bingo",
  "name": "StS2 Bingo",
  "author": "…",
  "description": "In-game, auto-detected bingo boards for solo and co-op runs.",
  "version": "0.1.0",
  "has_dll": true,
  "has_pck": false,
  "affects_gameplay": true,
  "min_game_version": "0.111.1",
  "dependencies": []
}
```
The `id` must not change after release: the game uses it for its settings and co-op checks, and it names the DLL. The display name can change freely (section 15).

## 11. Milestones

0. **Repo skeleton.** Solution, projects, docs, build that copies the mod into the game's `mods` folder. *(done when the empty mod shows up in the game's mod list on both branches)*
1. **Spike A: events and recorder (solo).**
   - Official hook listener, plus Harmony fallback for one event.
   - A JSON-lines event recorder, plus a startup log of which events are available.
   - Find the data folder.
   - Run once on main and once on beta.
   - *Done when one Act 1 run on each branch produces a sensible event log.*
2. **Spike B: co-op messages and lobby UI.**
   - One custom message sent both ways in a co-op run.
   - The bingo button and panel added to the lobby, with the setting sent from host to client.
   - Confirm that a client without the mod is turned away when joining.
   - *Needs two players: the user and a friend or second PC.*
3. **Core: events, detectors and tests.** Core event model, the first 40–60 detectors, and replay tests that run against both branches' recordings.
4. **Board builder.** Goal-list JSON, the balanced builder, the stable hash and random number generator, determinism tests, board codes.
5. **Solo, complete.** Board UI, mini-board, toasts, progress counters, seed entry (custom run and normal run), save and continue, results, history.
6. **Co-op.** Messages, host-side rules for Versus, Lockout and Team, teammate previews and full-board view, feed, "one away" marker, rejoin snapshot.
7. **Polish.** Settings, colorblind palette, controller navigation, branch check, error messages.
8. **Release v1.** Main and beta, solo and co-op. Nexus and Workshop pages, a demo video, a pitch to bingo players.

## 12. Testing

- **Unit:** each detector against recorded and hand-made event logs, including edge cases:
  - overkill exactly 0 vs 1,
  - Block gained then lost in the same turn,
  - a goal completed during the combat that ends the run,
  - co-op events from two players interleaved, to confirm players are tracked separately and numbers are never added across players.
- **Determinism:** property tests checking that the same inputs always give the same board, that lines stay within their difficulty limits, and that exclusions are respected. A **known-answer test** pins exact boards for a few seeds, so changes to the hash or random number generator are caught.
- **Rules:** all three modes, plus simultaneous claims, duplicate claims, claims after a win, rejoin snapshots. All of it is plain C#, with no game or network needed.
- **Branches:** replay tests run against recordings from both branches. The branch check tool compares the adapter's targets in `refs/main` and `refs/beta`.
- **Save and continue:** quit in the middle of a goal in progress, continue, and check that the progress comes back.
- **Compatibility:** smoke-test runs with a popular character mod (e.g. Chaos Zero Nightmare / Tiphera) and a mod that adds events. The board has to build, general goals have to fire, and nothing can crash.
- **Manual:** a full solo run to a bingo on each branch, a co-op match in each mode, controller navigation.

## 13. Risks

| Risk | What to do about it |
|---|---|
| Early Access patches change hooks or signatures | Official hooks first; a thin adapter layer; listener groups loaded separately; goals left out when their events aren't available; the branch check tool; replaying recordings after every update |
| A listener model breaks saves or co-op (it registers a `ModelDb` ID) | Confirm in spike A. Fallback: Harmony postfixes on the game's `Hook.*` dispatchers |
| Main and beta drift apart while we're building | Build against main, check against beta often, use per-branch adapters only where needed |
| Another mod patches the same methods | Postfixes only; never skip the original method; never use transpilers; catch errors inside every patch |
| Co-op desync from the mod | The mod never changes game state. Bingo state is separate from the game state and is synced only through our own messages, with the host deciding |
| Lockout griefing | It's opt-in. Versus is the default |
| Testing co-op needs two people who own the game | Rules and detectors are tested offline. Solo testing covers most of the in-game work. Co-op checkpoints are planned with a friend |
| BaseLib or other dependencies lag behind beta | Use as few dependencies as possible. The core has none |

## 14. Licensing and repo

- The repo is **public, but not open source at first**. `LICENSE` says *all rights reserved*: people can read the code, but not copy it, change it or reuse it without permission. Note that GitHub's terms still let others view and fork public repos on GitHub.
- We'll revisit after release (e.g. switching to MIT or GPL later).
- **Never commit** game files (`sts2.dll`, decompiled source, game assets). They stay in the gitignored `refs/` folder or in temporary folders.

## 15. Open questions

- **Name:** "StS2 Bingo" for now (and the mod id `StS2Bingo`, which won't change). Alternatives: "Spire Bingo", "Lockout Spire".
- **Release order:** v1 = solo and co-op together, or solo first?
- **Lockout majority** with 3–4 players: is "more than half" right, or should it be "most squares when the run ends"?
- **When to pitch** to the bingo community: before or after the solo milestone?
- **Keeping a main install around for testing:** Steam only installs one branch at a time. Options: switch branches for each test session, or keep a copy of the game folder for the other branch.

## 16. Later ideas

- **Custom boards**, in three levels:
  1. **Hand-picked boards from built-in goals.** Still fully checked by the game. The board code lists goal IDs instead of a seed.
  2. **Built-in goal templates with your own numbers** (e.g. "Gain {N} Block"). Still fully checked.
  3. **Free-text goals.** These become **squares you mark yourself**, clearly labeled, and any board with them is marked *Custom* in history and kept out of personal bests.
- **Team-only squares** that scale with player count (e.g. "the team gains X Block in one turn"), Team mode only.
- **Blackout** (with a check that the whole board can be completed in one run) and solo **Score** mode, after v1, once there's player feedback.
- Board size and difficulty options; a "daily bingo" board code from the date; a web spectator view or OBS overlay; a community goal-list editor.
