using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Recording;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Core.Tests.Recording;

public class RunJournalTests
{
    private static readonly JournalHeader Header = new(JournalHeader.CurrentFormat, "ABC123", 1763650000, "ABC123-1V5M", "hash");

    [Fact]
    public void UpToLastSave_KeepsEverythingThroughTheLastSave()
    {
        GameEvent[] events = [new RoomEntered("Monster", "M1"), new GameSaved(), new GoldGained(1, 10), new GameSaved(), new GoldGained(1, 15)];
        Assert.Equal(events[..4], RunJournal.UpToLastSave(events));
    }

    [Fact]
    public void UpToLastSave_IsEmptyWithoutAnySave()
    {
        Assert.Empty(RunJournal.UpToLastSave([new RoomEntered("Monster", "M1"), new GoldGained(1, 10)]));
    }

    [Fact]
    public void WriteThenRead_RoundTripsHeaderAndEvents()
    {
        var attached = new RunAttached("ABC123", "Standard", false, 0, [new PlayerInfo(1, "IRONCLAD")], 1, StartTime: 1763650000) { Seq = 1 };
        var writer = new StringWriter();
        using (var journal = RunJournal.Start(writer, Header, [attached]))
        {
            journal.Append(new GameSaved { Seq = 2 });
        }

        var (header, events) = RunJournal.Read(new StringReader(writer.ToString()));
        Assert.Equal(Header, header);
        Assert.Equal(2, events.Count);
        Assert.Equal(1763650000, Assert.IsType<RunAttached>(events[0]).StartTime);
        Assert.IsType<GameSaved>(events[1]);
    }

    [Fact]
    public void Read_SkipsALineCutOffByACrash()
    {
        var text = BingoJsonLine(Header) + "\n" + "{\"t\":\"GameSaved\",\"seq\":2}\n" + "{\"t\":\"GoldGai";
        var (_, events) = RunJournal.Read(new StringReader(text));
        Assert.IsType<GameSaved>(Assert.Single(events));
    }

    [Fact]
    public void OldRecordingsWithoutAStartTime_ReadAsZero()
    {
        var evt = Serialization.BingoJson.DeserializeEvent(
            "{\"t\":\"RunAttached\",\"seed\":\"X\",\"gameMode\":\"Standard\",\"isCustom\":false,\"ascension\":0,\"players\":[]}");
        Assert.Equal(0, Assert.IsType<RunAttached>(evt).StartTime);
    }

    /// <summary>
    /// Quitting halfway through any room and continuing must end with the same board as never
    /// quitting. The game saves before entering each room and after each won fight, and on
    /// Continue plays everything after its last save again; the journal mirrors that.
    /// </summary>
    [Fact]
    public void ContinueAfterAnySave_EndsWithTheSameBoardAsAnUnbrokenRun()
    {
        var events = WithGameSaves(LoadFixture("main-ironclad-act1.jsonl"));
        var saves = events.Select((e, i) => (e, i)).Where(x => x.e is GameSaved).Select(x => x.i).ToList();
        Assert.True(saves.Count > 10);

        var unbroken = Snapshot(Play(events));

        foreach (var save in saves)
        {
            var nextSave = saves.FirstOrDefault(s => s > save, events.Count);
            // Quit partway into what comes next, and again just before the next save.
            foreach (var quitAt in new[] { save + Math.Max(1, (nextSave - save) / 2), nextSave })
            {
                var journal = events.Take(quitAt).ToList();
                var resumed = Play(RunJournal.UpToLastSave(journal).Concat(events.Skip(save + 1)));
                Assert.Equal(unbroken, Snapshot(resumed));
            }
        }
    }

    private static List<GameEvent> LoadFixture(string name)
    {
        using var reader = new StreamReader(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return EventRecorder.Read(reader).Events;
    }

    /// <summary>Adds the saves the game makes: before entering a room, and after a won fight.</summary>
    private static List<GameEvent> WithGameSaves(List<GameEvent> events)
    {
        var result = new List<GameEvent>();
        foreach (var evt in events)
        {
            if (evt is RoomEntered)
            {
                result.Add(new GameSaved { At = evt.At });
            }
            result.Add(evt);
            if (evt is CombatWon)
            {
                result.Add(new GameSaved { At = evt.At });
            }
        }
        return result;
    }

    /// <summary>A session over every tier of every goal, so any difference in any detector shows.</summary>
    private static BingoSession Play(IEnumerable<GameEvent> events)
    {
        var squares = BuiltInGoals.List.Goals
            .SelectMany(g => g.Tiers.Select(t => (Goal: g, Tier: t)))
            .Select((x, i) => new BoardSquare(i, x.Goal.Id, x.Tier.Difficulty, x.Tier.Params ?? new Dictionary<string, int>(),
                GoalText.Render(x.Goal.Text, x.Tier.Params)))
            .ToList();
        var board = new BingoBoard(1, "TEST", BuiltInGoals.List.Version, BingoMode.Versus, BoardDifficulty.Medium, 0, squares);
        var session = new BingoSession(board, BuiltInGoals.List, (id, ex) => throw new Xunit.Sdk.XunitException($"{id} threw: {ex}"));
        foreach (var evt in events)
        {
            session.Handle(evt);
        }
        return session;
    }

    private static string Snapshot(BingoSession session)
    {
        var lines = new List<string>();
        foreach (var player in session.Tracker.Context.Players)
        {
            foreach (var square in session.FilledBy(player).Order())
            {
                lines.Add($"done {player} {square} {session.CompletionOf(square, player)?.Evidence}");
            }
            lines.AddRange(session.Tracker.ProgressOf(player).Select(p => $"progress {player} {p.Square} {p.Current}/{p.Target}"));
        }
        return string.Join("\n", lines);
    }

    private static string BingoJsonLine(JournalHeader header) => Serialization.BingoJson.Serialize(header);
}
