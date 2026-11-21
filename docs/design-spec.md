# Bingo the Spire: design spec

> **This is the source of truth for the design.** If code and this document disagree, one of them is wrong: fix the code or update this doc in the same change.

| | |
|---|---|
| Status | v1: solo and co-op complete |
| Last updated | 2026-11-21 |
| Game versions checked | main `v0.111.1`, beta `v0.113.1` |

---

## 1. What it is

A Slay the Spire 2 mod that puts a **bingo board in every run**. Squares fill in **by themselves** when the game sees you complete each goal. Nothing runs on the honor system.

- **No setup and no separate lobby.** Install the mod and start a run, solo or co-op, and a board is there with its own button to open it.
- **Solo:** try to complete a line before the run ends.
- **Co-op:** everyone is in **the same run** (the game's own multiplayer), and you play one of three bingo modes against or with your teammates (section 4).
- **The same board can be replayed:** it's built from the run seed (or a bingo seed you type in), so a board can be shared and played again.
- **Works next to other mods:** modded characters, events, cards and relics don't break it (section 6.4).
- **Works on main and beta:** one build of the mod runs on both Steam branches. Both are supported in v1 (section 9).

## 2. Compared with playing bingo by hand

StS2 bingo can be played on web boards like Bingosync, where players mark squares themselves. This mod adds:
- **detection by the game**, so there's no honor system,
- **the board inside the game**, so there's no second window,
- **exact-value goals** settled by code (e.g. exact lethal on an elite),
- **boards balanced by difficulty**.

## 3. Goals and non-goals

**v1 includes**
1. Solo and co-op bingo in any run, with no setup needed.
2. Every square is checked by the game. If the game can't check a goal, the goal isn't on the list.
3. The same seed, characters and versions always give the same board.
4. Co-op modes: Versus (default), Lockout, Team.
5. Works with other mods, including modded characters and modded events.
6. One build that runs on both **main and beta**.
7. Continuing a saved run brings back the board and its progress.
8. Local history of past boards and results, shown in the game.

**Not in v1** (see section 16 for later ideas)
- Board size settings (beyond the automatic 4×4 for Blitz), an Extreme difficulty (a 5 in every line), Blackout, solo Score mode.
- Custom or user-written boards.
- Web spectator view, OBS overlay, online leaderboards.
- Anything that changes gameplay. The mod only watches.

## 4. How it plays

### 4.1 Starting a run

| Where | What the player sees |
|---|---|
| **Every screen where a run is set up** (character select solo or co-op, custom run, and beta's extra modes) | The **bingo icon** (the same one as in a run) under the game's version text in the top right, or just above the seed box on the custom run screen, with an ON or OFF badge. It opens the Bingo panel |
| **The Bingo panel** | An open book (section 4.4) with an **ON / OFF switch** (a colored pill slides behind the picked word; remembered between runs, on by default), the **board difficulty** as three small notes (Easy / Medium / Hard, explained under them), the **bingo-seed box**, and in co-op the three **modes as notes to pick from**, with the picked mode explained in plain words on the right-hand page |
| **Co-op** | Only the **host** sets the bingo options. Other players see them, read-only, and they're sent to them over the game's own co-op connection |

- **The bingo-seed box** (labelled "Board seed" in the game) takes nothing (the board comes from the run seed), a bingo seed, or a full board code (section 5). The hint leads with the common case: leave it empty for the run's seed, or paste the board seed shown at the top of any board ("Board: ...") to play that board instead. It then says which one it read; a bad entry is shown in red and turns bingo off for that run.
- The seed entry is **cleared once a run starts with it**, so the next run doesn't quietly reuse the same board. The on/off choice, the difficulty and the co-op mode are remembered.
- **Board difficulty:** Easy (the default) has no difficulty 5 squares, Medium has one in the middle, Hard has three (two on 4×4 Blitz boards). In co-op the host picks it. A pasted board code brings its own difficulty.
- If nothing is set, the board is built from the run seed and the mode is Versus. The board is built when the run starts and stays fixed for that run. With bingo off, the run has no board, no icon, and nothing is tracked.

### 4.2 Solo
- One board. Squares fill as you complete goals: a toast appears and the square pulses.
- **Win: the first line** (row, column or diagonal) gets the BINGO! pop-up at the top of the screen: a white box with a drifting grey checkerboard, the letters stamping down one by one, then stars popping around it while the letters bob in a wave, before it floats away. Every later line gets the same. The board marks the line itself with a yellow highlighter swipe through its notes, which keeps the text readable (animated the next time the board is open, if it was closed). After that, the board stays active: **the 2nd, 3rd and later lines are tracked**, with the floor and run time for each.
- When the run ends (win or death), the bingo results are recorded. Winning the run and winning the bingo are separate results.

### 4.3 Co-op: three modes, chosen by the host

| Mode | Board(s) | Who gets a square | How you win |
|---|---|---|---|
| **Versus** (default) | Every player has **their own copy of the same board** | Each player fills their own squares | **First line.** Play continues; later placements are tracked (Alex 1st on floor 18, Sam 2nd on floor 23, …). If the run ends with no line, whoever has the most squares wins. Ties go to whoever reached that count first |
| **Lockout** | **One shared board.** A square takes the color of the player who got it | The **first** player to complete a goal gets the square, and it's **locked** for everyone else | **First line, or a clinch** (section 4.3.1). If the run ends with neither, whoever has the most squares wins. Ties go to whoever reached that count first |
| **Team** | **One shared board.** A filled square is simply DONE, whoever filled it | **The team works together.** Goals that count or add up amounts pool everyone's share (see below); goals about one moment need one player | Cooperative. The result is squares filled, lines made, and the floor where the first line was made |

Rules for every mode:
- **Versus and Lockout: a goal is always completed by one player alone.** Numbers are never added up across players: "Gain 50+ Block in a turn" means *one* player gains 50.
- **Team: counts and amounts add up across the team,** within the goal's own turn, fight or run. "Draw 25+ cards in a single turn" counts both players' draws in the same turn; "Use 9 potions" counts everyone's potions; "Have 1050+ gold" adds every purse.
  - Pooled goals get bigger numbers: each player after the first adds 75% of the solo number, rounded to a clean value (2 players: 5 → 9, 10 → 18, 20 → 35, 400 → 700).
  - A tier can set its 2-player number by hand instead (`"team": { "n": 6 }`), for fine-tuning; each further player adds the same step again.
  - **Flawless fights pool too:** every player who wins a normal fight without losing HP adds one, so a fight where both stay clean counts twice.
  - The square goes to whoever's share reached the target.
  - **Not pooled** (one moment for one person): single hits, overkill, card cost, surviving one big hit, quick wins, low-HP wins, Strength, buff stacks and different buffs at once. Elites and bosses defeated already count for everyone in the fight.
  - **Progress is the team's:** every player's board shows the same bars. For goals that don't pool, that's the best any player has so far (a teammate's 4/10 shows on your board too).
- In Versus and Lockout, the board only uses goals that **every** player's character can complete. In Team, a character's goals can appear if **anyone** is playing that character. Modded characters only get the general goals (section 6.4).
- Lockout can be used to block other players (including taking the kill on an elite). That's part of the mode, and players choose it on purpose. Versus is the default for that reason.
- If a player leaves or disconnects, their squares stay theirs. When they come back, they get the current board (section 7).

#### 4.3.1 Lockout: the clinch rule

**Why it's needed:** in Lockout, any opponent's square in a line kills that line for you. Partway through a match, every line can be blocked for everyone, and then nobody can ever win by a line. So Lockout also needs a way to win on squares. The classic 2-player rule is "13 of 25", which is just the point where you can't be caught. A clinch is that same idea, made general so it works with any number of players and any board size.

**The rule:** a player **P** clinches, and wins at once, when **no other player can still win**. That means, for **every** other player **Q**, both of these are true:

1. **Q can't catch up on squares:** `Q's squares + unclaimed squares ≤ P's squares`. Equal counts go to whoever got there first, which is P, so Q needs strictly more to be a threat.
2. **Q has no live line left.** A line is live for Q if every square in it is Q's or unclaimed. **A big lead in squares is never a clinch while an opponent can still complete a line.**

**Examples:**
- **2 players, 5×5, nothing blocking lines:** this works out to the classic 13 of 25. With 13 squares, the other player can reach 12 at most.
- **Lead, but a line is still open:** Alex has 13, Sam has 6, and 6 squares are unclaimed. Sam can reach 12 at most, so Sam can't catch up. But 5 of the unclaimed squares make up a row Sam could still complete, so it's **not** a clinch yet. Alex wins once one of those squares is claimed by Alex or a third player.
- **4 players:** Alex 9, Sam 5, Kim 4, Lee 4, and 3 unclaimed. If no one but Alex has a live line, Alex clinches at 9 (Sam can reach 8 at most).
- **Blitz 4×4, 2 players:** the same rule gives **8 of 16, as long as you reached 8 first**. The other player can then only tie at 8, and the tie goes to whoever got there first. Nothing about the 4×4 board is special-cased.

**Details:**
- The host checks for a clinch after every accepted claim, at the same moment it checks for lines.
- Unclaimed squares count as still possible even when they can no longer be done in this run (e.g. an Act 1 goal after Act 1). That makes the rule safe: it can be slow to call a clinch, but it can never call one wrongly. Spotting squares that can no longer be done is a later idea (section 16).
- After a win, play continues and later placements are tracked, as in the other modes.

### 4.4 In-run UI

- **Bingo button + hotkey** opens the full board. The button is always there and works with mouse and controller. It lives in the game's top bar, so screens and menus the game opens over the top bar cover it like the game's own buttons.
  - The hotkey defaults to `B`, but **only if no other binding (the game's or another mod's) already uses that key**. If it does, the hotkey starts off and the log says why.
  - Players can set their own key, or turn the hotkey off, in the bingo settings.
- **Controller:** with Steam Input (the usual case on Steam) every button is one of the game's actions, so the board has no button of its own. Instead:
  - **In a run,** the bingo icon is added to both ends of the top bar's controller row: press the top bar button (□ / X) and move along the top bar to it, then Accept.
  - **Before a run,** R2 opens the bingo panel (an "R2" hint shows under the lobby icon while the game's input is a controller, the same rule the game uses for its button glyphs). The game only uses the triggers in fights, so nothing is taken; the Daily screen, where they page the leaderboard, is left alone.
  - Without Steam Input, a free raw button (the first of Select / View, R3, L3, Share that no game action uses) also opens the board.
  - Every bingo book can be moved through with the stick or d-pad (a gold ring shows where you are; inside the ON / OFF switch, so it isn't cut off), Accept picks (the game's own Accept action, which is what Steam Input sends), and Back / Esc closes the book. While a book is open, the game's own hotkeys are held back, so Back doesn't also open the pause menu. Mouse clicks never leave a ring behind.
- **One book at a time:** a book with a backdrop opens in front of everything on the screen, the bingo icon included. A book opened from another (Settings or Past runs from the lobby) hides the one below until it's closed, then the first comes back. Back / Esc only closes the book on top.
- **Sounds:** our buttons and notes use the game's own menu sounds: hover, click, back on the close button, and the tickbox on / off sounds on the ON / OFF switch.
- **Look:** the board and the Bingo panel are an **open book** with curved pages, drawn in code so they're sharp at any resolution. Each square is a **paper note** taped to the page, with its difficulty as a colored corner. Headings use Mochiy Pop One and notes use Mali (both SIL OFL, embedded in the mod).
- **Your board:** 5×5 grid of notes showing each goal's text, a **DONE** stamp on filled squares (gold when part of a finished line), and **progress on goals in progress** (e.g. "7/10 cards this turn", "Block 38/50"). A brass nameplate (a cream "BINGO!" with a thick near-black outline) shows the score, "Board: code" with a **paperclip that copies the code**, and the mode ("Solo" in a one-player run, which plays by Versus rules); the difficulty key runs along the bottom of the page.
  - **Versus:** only your own squares. **Lockout:** squares someone else took are greyed out with "Taken by Sam". **Team:** every filled square is just DONE, whoever filled it.
  - **Progress only shows while it can still count.** Turn, fight and one-moment goals show it during the fight and clear when the fight ends. Goals that build over the run (rest sites, potions used, cards removed, relics bought, elites, exact kills, gold held, flawless normal fights) keep their bars. Gold held shows the gold you (or your team) hold right now, not the best so far: it drops when gold is spent, and every player's gold is checked again at each new room, since the game doesn't report gold lost in events.
  - Long goal text shrinks to fit its note.
- **Teammate previews** (Versus): a small card per player (you first), centered in the space right of the nameplate, showing only which cells are filled, with their square count, lines and "1 away!".
  - Fewer players get bigger cards: 86 wide with 2 players, 78 with 3, 66 with 4. In Versus with other players the nameplate is a little smaller to leave room, and if it would still touch the cards it scales itself down; nothing in the header overlaps. The activity feed only uses the space left of the nameplate.
  - **Clicking a card opens that player's full board**, with their notes, details and progress; clicking yours goes back. **LB / RB** step through the players while the board is open. The board always opens on your own.
  - On someone else's board, the nameplate shows a tag with their name in their color ("SAM'S BOARD") and the board gets a frame in that color, so it can't be mistaken for yours. Each player's color comes from their place in the row (colorblind-safe colors when that setting is on).
  - In Lockout and Team, the one shared board shows everyone's colors instead.
- **Hover details** (filled squares only; the goal text is already on the note): who completed the square (if it wasn't you), on which floor and turn, and the evidence (e.g. "53 Block on turn 4, Act 2 floor 22").
- **The game's own cursor** stays on screen over the bingo UI: no hand or text cursors, since the game only draws its own arrow.
- **"One away" marker:** a gently pulsing orange "1 AWAY!" stamp on every square that would finish a line for you (your team in Team). In Lockout, squares that would finish a line for a rival get a red "SAM 1 AWAY" stamp; a line with someone else's square can't be finished, so it never counts. In Versus, each player preview says "1 away!" when that player is.
- **Activity feed** (co-op): the three newest happenings at the left of the nameplate, e.g. "Sam: Gain 45+ Block in a single turn (floor 22)", "BINGO! Alex, row 2 (floor 18)"; hovering shows the last 12. It's built from the board's results, so it comes back after a Continue.
- **Toasts** for your own completions and important events (a teammate's bingo, a locked square you were working on). A finished square shows a green card with a white "SQUARE COMPLETE!" label sitting on its top edge.
- **Problems are shown, not just logged:** an amber toast in the run says what happened and what it means, e.g. "BINGO IS OFF: the host turned bingo off for this run", "BOARD STARTED FRESH", "PROGRESS NOT SAVED: if you quit and Continue, the board starts fresh", "A SQUARE IS STUCK" (a goal hit an error and can't be completed this run), "DIFFERENT VERSIONS" between host and player, and "NOT IN HISTORY". Ones raised before the run screen is up wait for it.
- **Fonts stay ours** when another mod swaps the game's fonts: our text controls carry the marker CznStyleUI checks to leave a control alone.
- **The bingo button is a mini-board:** filled squares green, squares in a finished line gold. In the lobby it carries an ON or OFF badge. It can be dragged anywhere; right-click (or Settings) puts it back by the run timer.
- **Results** a moment after the game's own end-of-run screen appears, as an open book over it, with just "BINGO!" on the nameplate. Left page: the board code with its copy clip, the run's facts (mode, difficulty, character in solo, run result, act and floor reached, play time, date) and, in co-op, placements (or your team) with each player's character, squares and lines. Right page: every line with its act, floor and play time, then squares as a bar per player (one team bar in Team) and squares done per difficulty. A **Past runs** button sits underneath; it opens over the results, and closing it brings the results back.
- **Settings** (a book opened from **Settings** in the lobby's bingo panel, saved to `settings.json` at once):
  - **Board hotkey:** click, then press any plain key; keys the game already uses are refused with the reason. Or turn it off. Applies from the next run.
  - **Colorblind-safe colors:** difficulty tags, mini-boards and 1 AWAY stamps switch to Okabe-Ito colors that run light to dark as well as by hue. Tags always show their number, Lockout squares always name who took them.
  - **Bingo button:** put it back in its default spot by the run timer (it's moved by dragging it).

## 5. Seeds and determinism

**Rule: the same inputs always give the same board, on every machine.**

```
seedText   = bingoSeed (if one was entered) ?? runSeed      // both are the game's 12-character seed format
boardSeed  = StableHash64("bingothespire|" + seedText + "|" + goalListVersion + "|" + bingoMode + "|" + gameModeClass + "|" + size + "|" + difficulty + "|" + sortedDistinctCharacterIds)
board      = Generate(boardSeed, goalList filtered by (characters, game mode class, available detectors), bingoMode, size, difficulty)
```

- **Game mode class:** only Blitz changes the board (4×4 and fewer goals), so the class is `Blitz` or `Standard`. Standard, Custom, Daily and Dread all count as `Standard`, so **replaying a run's seed in a Custom run gives the same board.**

- **The run seed is the default.** It's the same seed the game shows (e.g. `7K2M9QX4ZB1C`). Every co-op player already has it, so in the default case **nothing about the board needs to be sent**. Sharing a run seed shares the whole experience.
- **The bingo seed overrides it.** It uses the same format as game seeds and is cleaned the same way (uppercased; O is read as 0 and I as 1). It lets you replay the same board on a different run seed, or a different board on the same run seed.
- **The mod uses its own random numbers.** The board builder never uses the game's random number streams, so building a board can't change the run (map, rewards, shuffles).
- **Stable hashing only.** We never use .NET's `string.GetHashCode()` (it changes every launch); we use our own hash (FNV-1a 64) and our own seeded random number generator (xoshiro256** seeded through SplitMix64). Both are checked against reference outputs in the tests, and a known-answer test pins exact boards.
- **The board depends on the characters in the run**, because character-specific goals are filtered. So "same seed + same characters + same versions = same board."
- **The board depends on which goals can be detected.** Goals whose detector can't run on this game version are left off before the board is built (section 9). In co-op every player is on the same game version (the game checks this when someone joins), so every player gets the same board.
- **Board codes:** a short string encoding `(seedText, goalListVersion, mode, size, difficulty)`, e.g. `7K2M9QX4ZB1C-1V5M` (seed, goal-list version 1, Versus, 5×5, Medium). Pasting one into the bingo-seed box reproduces the board, as long as the characters match. If the code's goal-list version isn't available, or its goals can't be detected on this version, the mod says so clearly instead of quietly building a different board.

## 6. Goals and the board builder

### 6.1 Goals are stored as data, not one hand-coded square each
Each goal in the goal list is a **template plus parameters**, so difficulty can scale by changing a number, and custom boards (section 16) become possible later:

```jsonc
{
  "id": "block_in_turn",
  "text": "Gain {n}+ Block in a single turn",
  "detector": "BlockInTurn",          // detector type name
  "tiers": [                          // parameter values the builder can pick from, and how hard each is (1-5)
    { "difficulty": 1, "params": { "n": 30 } },
    { "difficulty": 2, "params": { "n": 50 } },
    { "difficulty": 4, "params": { "n": 80 } }
  ],
  "tags": ["block"],                  // specific topics; kept out of the same line when possible
  "exclusions": ["block_in_combat"],  // goals too similar, or that contradict this one
  "requires": { "events": ["BlockGained", "SideTurnStarted"], "character": null },
  "excludeGameModes": ["Blitz"]       // optional: game mode classes this goal is left out of
}
```

- The goal list is an embedded, **versioned** JSON file (`goalListVersion`).
- `{s}` in a goal's text is a plural ending that follows `{n}`: "Remove {n} card{s} at shops" reads "Remove 1 card" or "Remove 2 cards".
- `requires.events` is optional: the detector catalog already knows which game events each detector needs. A goal is left out if its detector, or any event listed here, is missing on this game version (section 9).
- `requires.character` is optional. Character-specific goals are matched by the game's character ID.

### 6.2 Board builder
- Works like Bingosync's "SRL" generator: every row, column and both diagonals add up to **exactly** the same total difficulty.
  - A seeded layout puts one cell of each **difficulty band** in every line (a diagonal Latin square).
  - The bands are goal difficulties 1,2,3,3,4 on 5×5 and 1,2,3,3 on 4×4, so on Easy every line has the same total.
  - Medium and Hard raise cells of the top band to difficulty 5. Every line holds exactly one top-band cell, so no line gets two 5s. On 5×5 the bands are swapped so the middle cell is top-band and always the first 5; Hard adds two more at random (one more on 4×4, where the middle four cells are preferred).
  - Cells are filled hardest first. Each gets a random goal tier at the target difficulty, or the closest available.
- Goals sharing a tag are kept out of the same line whenever the pool allows it. This is why tags should be specific ("block", "potions"), not broad ("combat").
- Exclusions apply to the whole board, and no goal appears twice (including the same goal with different parameters).
- The goal list's file order never matters: goals are sorted by id first.
- v1 default: **5×5, Easy**. Other sizes are built into the data model but not shown in the menus.
- **Game modes:** bingo is on in every game mode.
  - Standard, Custom, Daily and **Dread** (beta) use the normal 5×5 board.
  - **Blitz** (beta) uses a **4×4** board, to fit its shorter runs. Goals that need more of the run than Blitz has are tagged and left out.
  - The game mode is part of the board inputs (section 5), so the same seed in Blitz and in Standard gives different boards.

### 6.3 Detectors
- They're **plain C#** with no game types: small state machines that read game events (section 9.2) plus a read-only snapshot of the run.
- They output `Completed(goalId, playerId, evidence)` and optionally `Progress(goalId, playerId, current, target)` for the in-progress counters.
- **Each player is tracked separately.** Every event carries the player who caused it, and detectors never add values up across players, except pooled goals in Team mode (section 4.3).
- They can be unit-tested by replaying recorded event logs, without starting the game.
- **Most goals are one of two shapes:** a *counter* ("do X {n} times within a turn / fight / shop visit / run", restarting from 0 when that scope ends) or a *threshold* ("reach {n} in one go", e.g. one hit, gold held). A few have their own logic (flawless fights, hits from one card, debuffs on one enemy).
- **What counts:**
  - A **turn** is one combat round: the player's turn plus the enemy turn after it. "Draw {n} cards in a turn" includes the opening hand.
  - **Damage** is the full hit as shown in game: HP lost, Block removed and overkill.
  - **Pets** (e.g. Necrobinder's Osty) count for their owner.
  - **Kills by Poison or Doom** count for the player who last added Poison or Doom to that enemy, including exact kills (209 Doom on an enemy with 209 HP is exactly lethal).
  - **Evidence** uses names as the game shows them ("12 Weak on Fuzzy Wurm Crawler"), in the game's language.
  - **Without losing HP** means the player's HP never went down during the fight, including HP paid for their own cards.
  - **Fights won** (elites, bosses, quick wins) count for every player in the fight. Skipping a fight with the dev console counts as winning it; the mod doesn't police cheats.
  - When a co-op player's share can't be told apart in the game's numbers (two players stacking Poison on one enemy), detectors track each player's own share instead.

### 6.4 Rules that keep goals working with other mods
- **General goals count all content, modded included:** "10 different buffs" counts any power, modded ones too. "Play 10 cards in a turn" counts any card.
- **Goals never depend on a list of vanilla IDs, unless the goal is about specific vanilla content and is marked as character-specific** (e.g. "Channel all 4 orb types as Defect").
- Goals about rooms and events use **room types** (elite, event, shop, rest), not specific event IDs, so modded events still count.
- **Modded characters** get only the general goal pool. Any character the goal list doesn't know is treated as "general only."
- If another mod changes the game so much that a goal can never be completed, that's acceptable. That should be rare, and it's the player's choice to combine those mods.

### 6.5 First goal list (v1)
The list lives in `src/BingoTheSpire.Core/Goals/goals.json` (goal list version 6, 44 goals):
- **39 general goals**, which count modded content: cards, Attacks and Skills per turn; Powers per fight; draw, energy, expensive cards and 3+ cost cards per fight; exhaust, shuffles and created cards; damage per turn, single hits, overkill, exact-lethal kills, kills on the first turn of a fight (counted over the run), hits and kills from one card; Block per turn; flawless elites, bosses and normal fights; quick wins, low-HP wins and surviving big hits; elites and bosses defeated; debuff variety and stacks, buff variety and stacks; potions per fight and per run; shop sprees, relics bought and card removals; gold held; rest-site upgrades and rests.
- **5 character goals:** Ironclad Strength, Silent Poison, Defect orbs, Necrobinder pet damage and Regent stars.
- **Not yet possible,** because they need more events from the game: deck and run state (fully upgraded deck, unique cards, number of relics) and time (act cleared within a time limit).

## 7. Co-op syncing

**Everything goes over the game's own co-op connection.** No Steam lobby of our own, no relay server, no cost.

- **Messages:** the mod adds its own small message types to the game's co-op connection. They're always sent reliably.
- **The host decides.** Each player's game detects goals for **its own player only** and sends a **claim** (square, goal, player, floor, turn, evidence) to the host. The host's own player claims locally.
  1. The host applies the mode's rules (Lockout and Team: the first claim for a square wins it; Versus: accept) and gives the result an **order number**. A claim for a square that player already has gets no result at all.
  2. The host sends the **result** to everyone, and every game applies results in order, so all boards stay the same. Results also go into each game's journal (section 8).
  3. Claims for squares that are already taken aren't sent. A race that's lost shows "Too late: Sam got it first".
- **Lobby options:** the host's bingo settings (on/off, difficulty, bingo seed, mode) are sent to everyone in the lobby: when the host changes them, when a player joins, and when a client's Bingo panel asks for them.
- **One sync for start, Continue and rejoin:** whenever a client's run starts, it sends the host a **hello** with its board's hash. The host answers with a **snapshot**: on/off, its board code, its board hash, and every result so far.
  - If the client built a different board (e.g. it missed the lobby settings), it rebuilds the host's board from the code, and its own progress so far is claimed again.
  - The client then resets its board to exactly the snapshot. This also repairs a client whose saved results drifted from the host's after a Continue.
  - If the client can't build the host's board at all (the hashes still differ), bingo is turned off for that client with a clear message, and the run itself is not affected.
- **If the host leaves:** the co-op run itself depends on the host, so bingo ends with the run. Nothing needs to take over.
- **Missing the mod or a different version:** the mod is marked as affecting gameplay, so the game itself compares mod lists when someone joins, and blocks the join if they don't match. This takes care of "the other player needs the mod" without extra code. If versions still differ, the "DIFFERENT VERSIONS" notice says so (section 4.4).

## 8. Saving, continuing and history

- **Run state:** each run in progress has a **journal**: the run's events, written as they happen to a small file in the mod's data folder (`runs/`), named by the run's seed and start time and tagged with the board's hash.
  - The mod marks every point where the game saves the run. **Continue** resumes from the game's last save and plays everything after it again, so the mod replays the journal **up to that last save** into a fresh board. That restores every square, line and in-progress counter (e.g. "7/10 cards this turn") exactly, without saving each goal's state separately.
  - If the journal is missing, unreadable or for a different board, the board starts fresh from the seed.
  - The journal is deleted when the run ends. Leftover journals (e.g. a run abandoned from the main menu) are removed after 30 days untouched.
- **History:** one tiny line per finished bingo run (date, mode, seed and board code, characters, result, floor and time of each line, squares filled, placements and winner), stored as JSON lines in `bingo_history.jsonl` in the mod's data folder. Each game writes its own, from its own player's point of view. Times are play time since the run started, carried across Continues. Abandoned runs are kept and marked as such. Lines that can't be read are skipped, so a damaged file never stops the mod.
- **History screen:** opened from **Past runs** on the results or in the lobby's bingo panel. It lists the 8 most recent runs and personal bests (fewest floors to a first line, fastest first line by play time, most squares in Team) and **shows the file's path**, with a button to open the folder, for anyone curious.
- The data folder is `bingo_the_spire/` inside the game's user data folder (Godot `user://`; on Windows `%APPDATA%\SlayTheSpire2\bingo_the_spire`).

## 9. Supporting main and beta, and how events reach the mod

### 9.1 One build, adapting at runtime
- The mod is **built against the main branch** and the same file runs on main and beta.
- **Only the game adapter talks to the game.** Everything else (detectors, board builder, rules, UI state) is in `BingoTheSpire.Core` and has no game dependencies.
- At startup the adapter connects each event source it can and **records which events are available**. The board builder only uses goals whose detector has every event it needs. **So on any branch, any version, and alongside any other mods, every square on the board can be completed.** Anything that couldn't be connected is logged.
- When something has a different name on each branch, the adapter tries **each known name**, main's first.
- Where a branch works **differently** (not just under a different name), it gets its own small adapter for that branch, picked by checking what the game actually has, not by reading the version string.
- **Branch check:** a small tool compares the main and beta game files for everything the adapter uses. It runs whenever a game update arrives. Reference copies live in the gitignored `refs/` folder.

### 9.2 Where events come from (most to least preferred)
1. **The game's official mod hooks.** The game lets mods listen to its own events (card played, Block gained, damage dealt, power applied, room entered, combat won, item bought, potion used, and many more). These exist for mods, so they're the most likely to stay the same across branches and patches.
   - Listeners are **split into small groups by area** (combat, deck, map, economy, …), each connected on its own. If one doesn't fit a branch, only that group is lost.
   - Listeners **only watch** and never change game state.
2. **Harmony patches**, for anything the official hooks don't cover (e.g. UI screens, the run timer, save and continue). They're **postfixes only**, meaning they run after the game's own code. They never skip or rewrite the game's code, and they're skipped quietly when their target isn't found.
3. **Reading the run state**, for deck and run checks (deck contents, relics, gold, act, floor), done when relevant events fire.

The adapter turns everything into **Core events** (`CardPlayed`, `BlockGained`, `DamageDealt`, `PowerApplied`, `TurnStarted`, `CombatEnded`, `RoomEntered`, `DeckChanged`, `ActCompleted`, …), each tagged with the player who caused it.

**Each player's game claims only for its own local player**, so nothing is counted twice in co-op.

## 10. Project structure and tech

| | |
|---|---|
| Runtime | .NET 9 (`net9.0`, matching `sts2.runtimeconfig.json`), C# latest |
| Engine | Godot 4.5.1 (GodotSharp 4.5.1) |
| Patching | Harmony 2.4.2 (`0Harmony.dll`, comes with the game) |
| Mod loading | The game's built-in mod loader: `mods/<folder>/<id>.json` + `<id>.dll` |
| Entry point | A class marked `[ModInitializer("Initialize")]` |
| Dependencies | None beyond the game. Fonts (SIL OFL) are embedded |

```
BingoTheSpire.sln
src/
  BingoTheSpire.Core/     pure logic: events, detectors, goals, generator, rules, state, hashing (no game references)
  BingoTheSpire.Mod/      the mod: entry point, game adapter (hooks/patches), network messages, Godot UI, saving
tests/
  BingoTheSpire.Core.Tests/  unit, determinism and rule tests; replays of recorded event logs
tools/
  BranchCheck/            checks the built mod against the main and beta game files
docs/                     this spec and the README's screenshots
refs/  (gitignored)       reference copies of game files for main and beta, used to build and for the branch check
```

Manifest (`BingoTheSpire.json`):
```json
{
  "id": "BingoTheSpire",
  "name": "Bingo the Spire",
  "author": "Texin",
  "description": "In-game bingo boards for solo and co-op runs. Squares complete automatically as the game detects each goal.",
  "version": "0.4.0",
  "has_dll": true,
  "has_pck": false,
  "affects_gameplay": true,
  "min_game_version": "0.111.1",
  "dependencies": []
}
```
The `id` must never change: the game uses it for its settings and co-op checks, and it names the DLL. The display name can change freely. The manifest's `version` is the only version number: the DLL takes its version from it.

## 11. Releases

- **Release builds** are made with `dotnet build -c Release`. They leave out the dev console commands (section 12), which only exist in debug builds.
- The mod is the `BingoTheSpire` folder in the game's `mods` folder: `BingoTheSpire.dll`, `BingoTheSpire.json` and the two font licenses.
- **Versions:** the manifest's `version` is raised for every build that's shared. Co-op players need the same version; the game compares mod lists when someone joins.
- **Goal list version:** raised whenever goals change in a way that changes boards, so old board codes are refused clearly instead of building a different board (section 5).

## 12. Testing

- **Unit:** each detector against recorded and hand-made event logs, including edge cases:
  - overkill exactly 0 vs 1,
  - Block gained then lost in the same turn,
  - a goal completed during the combat that ends the run,
  - co-op events from two players interleaved, to confirm players are tracked separately and numbers are never added across players.
- **Determinism:** property tests checking that the same inputs always give the same board, that lines stay within their difficulty limits, and that exclusions are respected. A **known-answer test** pins exact boards for a few seeds, so changes to the hash or random number generator are caught.
- **Rules:** all three modes, plus simultaneous claims, duplicate claims, claims after a win, rejoin snapshots. All of it is plain C#, with no game or network needed.
- **Clinch:** each example in section 4.3.1 becomes a test. In particular, a big lead in squares while an opponent still has a live line must **not** count as a clinch. Also covered: ties, 3–4 players, the 4×4 board, and the run ending before anyone has won.
- **Branches:** replay tests run against recordings from both branches. The branch check tool loads the built mod against `refs/main` and `refs/beta`: every hook listener and patch target must exist, and every game type, method and field the mod uses must resolve (an optional parameter added on one branch changes a method's signature, and would otherwise only fail in game).
- **Save and continue:** quit in the middle of a goal in progress, continue, and check that the progress comes back.
- **Compatibility:** smoke-test runs with a popular character mod (e.g. Chaos Zero Nightmare / Tiphera) and a mod that adds events. The board has to build, general goals have to fire, and nothing can crash.
- **Manual:** a full solo run to a bingo on each branch, a co-op match in each mode, controller navigation.
- **Dev console commands** for trying the UI, compiled into **debug builds only**. Release builds never contain them, because the game turns its debug commands on for anyone running a mod. They fill squares through the normal claim rules, so they're saved like real completions:
  - `bingo <row> [delay]` fills a whole row (1 = top) to show the BINGO! and the line stroke, without the square pop-ups.
  - `fill <row> <col> [delay]` fills one square, with its pop-up.
  - Both wait 1 second by default (or the delay given, in seconds) so the console can be closed first.
  - `bingo reset` empties the board, in solo only. A Continue brings the squares back.
  - `bingo results` previews the end-of-run results as if the run ended now (nothing is saved); `bingo history` opens past runs.

## 13. Risks

| Risk | What to do about it |
|---|---|
| Early Access patches change hooks or signatures | Official hooks first; a thin adapter layer; listener groups loaded separately; goals left out when their events aren't available; the branch check tool; replaying recordings after every update |
| Listening to the official hooks has side effects on saves or co-op | Listeners only read; Harmony postfixes are the fallback for anything a hook can't cover |
| Main and beta drift apart | Build against main, run the branch check against beta after every update, use per-branch adapters only where needed |
| Another mod patches the same methods | Postfixes only; never skip the original method; never use transpilers; catch errors inside every patch |
| Co-op desync from the mod | The mod never changes game state. Bingo state is separate from the game state and is synced only through our own messages, with the host deciding |
| Lockout griefing | It's opt-in. Versus is the default |
| Dependencies lag behind beta | The mod has none beyond the game |

## 14. Licensing and repo

- The repo is **public, but not open source**. `LICENSE` says *all rights reserved*: people can read the code, but not copy it, change it or reuse it without permission. GitHub's terms still let others view and fork public repos on GitHub.
- **Never commit** game files or game code. Reference copies stay in the gitignored `refs/` folder.

## 15. Names

- **Bingo the Spire**: repo `bingo-the-spire`, mod id `BingoTheSpire`. The id must never change (section 10).

## 16. Later ideas

- **Custom boards**, in three levels:
  1. **Hand-picked boards from built-in goals.** Still fully checked by the game. The board code lists goal IDs instead of a seed.
  2. **Built-in goal templates with your own numbers** (e.g. "Gain {N} Block"). Still fully checked.
  3. **Free-text goals.** These become **squares you mark yourself**, clearly labeled, and any board with them is marked *Custom* in history and kept out of personal bests.
- **Blackout** (with a check that the whole board can be completed in one run) and solo **Score** mode.
- An **Extreme** difficulty (a difficulty 5 square in every line) and board size options.
- Spotting squares that can no longer be done this run, so Lockout clinches are called sooner (section 4.3.1).
- Board notes reachable with a controller, so their details don't need a mouse hover; settings reachable during a run.
- A "daily bingo" board code from the date; a web spectator view or OBS overlay; a community goal-list editor.
