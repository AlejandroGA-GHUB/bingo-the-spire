using System.Globalization;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.History;
using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The two history books (spec 4.5, 8): the results of a run that just ended, and the list of
/// past runs with personal bests. Both are open books on a dimmed screen, freed when closed.
/// </summary>
internal static class HistoryBook
{
    private const float ColumnWidth = 360;
    private const float SpineGap = 100;
    private const int RecentRuns = 8;

    /// <summary>The run that just ended: placements, every line with its floor and time, the board code.</summary>
    public static void ShowResults(Control parent, HistoryEntry entry)
    {
        var (book, left, right) = Open(parent, "BingoTheSpireResults", "BINGO!", Summary(entry));

        left.AddChild(Heading(entry.Solo ? "Your run" : entry.Mode == "Team" ? "Your team" : "Placements"));
        var players = entry.Players
            .OrderBy(p => p.Place ?? int.MaxValue)
            .ThenByDescending(p => p.Squares)
            .ToList();
        foreach (var player in players)
        {
            var name = player.Id == entry.You ? (entry.Solo ? "You" : $"{player.Name} (you)") : player.Name;
            var place = player.Place is { } p ? $"{Ordinal(p)}  " : entry.Winner == player.Id ? "Winner  " : "";
            left.AddChild(Body($"{place}{name}", 19, BingoStyle.PageInk, bold: true));
            left.AddChild(Body($"{Character(player.Character)}  ·  {player.Squares} squares  ·  {Plural(player.Lines, "line")}", 16, BingoStyle.PageInkSoft));
        }
        if (entry.Winner is { } winner && entry.WinReason == "MostSquares")
        {
            left.AddChild(Body($"No line this run: {Who(entry, winner)} won on most squares.", 15, BingoStyle.PageInkSoft));
        }
        left.AddChild(Spacer(10));
        left.AddChild(Body($"Board: {entry.BoardCode}", 15, BingoStyle.PageInkSoft, bold: true));
        left.AddChild(Body("Paste it in the bingo seed box to play this board again.", 14, BingoStyle.PageInkSoft));

        right.AddChild(Heading("Lines"));
        if (entry.Lines.Count == 0)
        {
            right.AddChild(Body("No lines this time.", 17, BingoStyle.PageInkSoft));
        }
        foreach (var line in entry.Lines.Take(10))
        {
            var who = entry.Solo || entry.Mode == "Team" ? "" : $"{Who(entry, line.Player)}: ";
            var title = line.Ordinal == 1 && (entry.Solo || entry.Mode == "Team") ? "BINGO!" : $"{Ordinal(line.Ordinal)} line";
            right.AddChild(Body($"{who}{title}  {BoardRules.LineName(entry.Size, line.Line)}", 18, BingoStyle.PageInk, bold: true));
            right.AddChild(Body(Where(line), 15, BingoStyle.PageInkSoft));
        }
        if (entry.Lines.Count > 10)
        {
            right.AddChild(Body($"…and {entry.Lines.Count - 10} more", 15, BingoStyle.PageInkSoft));
        }

        book.Content.AddChild(Footer(Button("Past runs", () =>
        {
            book.Hide();
            ShowHistory(parent);
        })));
        book.Show();
    }

    /// <summary>Past runs, newest first, with personal bests and where the file lives.</summary>
    public static void ShowHistory(Control parent)
    {
        var entries = BingoRuntime.ReadHistory();
        var bests = RunHistory.Bests(entries);
        var (book, left, right) = Open(parent, "BingoTheSpireHistory", "HISTORY",
            entries.Count == 0 ? "No finished runs yet" : $"{Plural(bests.Runs, "run")}  ·  {Plural(bests.Bingos, "bingo")}");

        left.AddChild(Heading("Personal bests"));
        Best(left, "Fewest floors to a bingo", bests.FewestFloors, e => $"Floor {e.FirstLine!.Floor}");
        Best(left, "Fastest bingo", bests.FastestTime, e => RunHistory.FormatTime(e.FirstLine!.Ms));
        if (bests.MostTeamSquares is { } team)
        {
            Best(left, "Most squares as a team", team, e => $"{e.Players.Sum(p => p.Squares)} / {e.Squares}");
        }
        left.AddChild(Spacer(10));
        left.AddChild(Body("Saved in:", 14, BingoStyle.PageInkSoft, bold: true));
        var path = Body(DataPaths.History, 13, BingoStyle.PageInkSoft);
        path.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        left.AddChild(path);
        left.AddChild(Button("Open folder", () => OS.ShellOpen(DataPaths.Root)));

        right.AddChild(Heading("Recent runs"));
        if (entries.Count == 0)
        {
            right.AddChild(Body("Finish a run with bingo on and it shows up here.", 17, BingoStyle.PageInkSoft));
        }
        foreach (var entry in Enumerable.Reverse(entries).Take(RecentRuns))
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds(entry.Date).ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
            var mode = entry.Solo ? "Solo" : entry.Mode;
            right.AddChild(Body($"{date}  ·  {mode}  ·  {Outcome(entry)}", 17, BingoStyle.PageInk, bold: true));
            right.AddChild(Body(RunLine(entry), 14, BingoStyle.PageInkSoft));
        }
        book.Show();
    }

    private static (BookFrame Book, VBoxContainer Left, VBoxContainer Right) Open(Control parent, string name, string title, string info)
    {
        // Only one of these open at a time.
        foreach (var old in parent.GetChildren().Where(c => c.Name == "BingoTheSpireResults" || c.Name == "BingoTheSpireHistory"))
        {
            old.QueueFree();
        }
        BookFrame? book = null;
        book = new BookFrame(parent, name, ColumnWidth * 2 + SpineGap, backdrop: true, onClosed: () => book?.Root.QueueFree());
        var content = book.Content;
        content.AddThemeConstantOverride("separation", 14);

        var stats = BingoStyle.Ink(info, BingoFonts.NoteBold, 18, BingoStyle.NameplateText);
        stats.VerticalAlignment = VerticalAlignment.Center;
        content.AddChild(BoardPanel.Nameplate(title, stats));

        var spread = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        spread.AddThemeConstantOverride("separation", (int)SpineGap);
        content.AddChild(spread);
        var left = Column();
        var right = Column();
        spread.AddChild(left);
        spread.AddChild(right);
        return (book, left, right);
    }

    private static string Summary(HistoryEntry entry)
    {
        var run = entry.Result switch { "won" => "Run won", "abandoned" => "Run abandoned", _ => "Run lost" };
        var squares = entry.Mode == "Team" ? entry.Players.Sum(p => p.Squares) : entry.Me?.Squares ?? 0;
        return $"{run}  ·  {squares} / {entry.Squares} squares  ·  {RunHistory.FormatTime(entry.PlayMs)}";
    }

    /// <summary>The bingo result from your point of view, e.g. "Bingo on floor 9" or "2nd".</summary>
    private static string Outcome(HistoryEntry entry)
    {
        if (!entry.Solo && entry.Mode != "Team" && entry.Me?.Place is { } place)
        {
            return entry.Winner == entry.You ? "Won the bingo" : $"{Ordinal(place)} place";
        }
        if (!entry.Solo && entry.Mode != "Team" && entry.Winner is { } winner)
        {
            return winner == entry.You ? "Won on squares" : $"{Who(entry, winner)} won";
        }
        return entry.FirstLine is { } line ? $"Bingo on floor {line.Floor}" : "No bingo";
    }

    private static string RunLine(HistoryEntry entry)
    {
        var squares = entry.Mode == "Team" ? entry.Players.Sum(p => p.Squares) : entry.Me?.Squares ?? 0;
        var characters = string.Join(", ", entry.Players.Select(p => Character(p.Character)));
        var run = entry.Result switch { "won" => "run won", "abandoned" => "abandoned", _ => "run lost" };
        return $"{characters}  ·  {squares}/{entry.Squares} squares  ·  {run}  ·  {RunHistory.FormatTime(entry.PlayMs)}";
    }

    private static void Best(VBoxContainer column, string title, HistoryEntry? entry, Func<HistoryEntry, string> value)
    {
        column.AddChild(Body(title, 15, BingoStyle.PageInkSoft, bold: true));
        if (entry == null)
        {
            column.AddChild(Body("Not yet", 18, BingoStyle.PageInkSoft));
            return;
        }
        var date = DateTimeOffset.FromUnixTimeMilliseconds(entry.Date).ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
        column.AddChild(Body($"{value(entry)}  ·  {(entry.Solo ? "Solo" : entry.Mode)}, {date}", 18, BingoStyle.PageInk, bold: true));
    }

    private static string Where(HistoryLine line) =>
        (line.Floor is { } floor ? $"Act {(line.Act ?? 0) + 1}, floor {floor}" : "") + $"  ·  {RunHistory.FormatTime(line.Ms)}";

    private static string Who(HistoryEntry entry, ulong player) =>
        player == entry.You ? "You" : entry.Players.FirstOrDefault(p => p.Id == player)?.Name ?? "Someone";

    /// <summary>"IRONCLAD" → "Ironclad"; modded IDs like "MYMOD-WITCH" → "Witch".</summary>
    private static string Character(string id)
    {
        var name = id.Contains('-') ? id[(id.LastIndexOf('-') + 1)..] : id;
        name = name.Replace('_', ' ').ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private static VBoxContainer Column()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(ColumnWidth, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        return column;
    }

    private static Label Heading(string text)
    {
        var label = BingoStyle.Ink(text, BingoFonts.Heading, 20, BingoStyle.PageInk);
        label.CustomMinimumSize = new Vector2(0, 34);
        return label;
    }

    private static Label Body(string text, int size, Color color, bool bold = false)
    {
        var label = BingoStyle.Ink(text, bold ? BingoFonts.NoteBold : BingoFonts.Note, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(ColumnWidth, 0);
        return label;
    }

    private static Control Spacer(int height) => new() { CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    private static Control Footer(Control child)
    {
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.AddChild(child);
        return center;
    }

    /// <summary>A brass button in the heading font.</summary>
    public static Button Button(string text, Action pressed)
    {
        var button = BingoFonts.Keep(new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(150, 42),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        });
        button.AddThemeFontOverride("font", BingoFonts.Heading);
        button.AddThemeFontSizeOverride("font_size", 17);
        StyleBoxFlat Box(Color fill)
        {
            var box = BingoStyle.Box(fill, 9, 0, BingoStyle.BrassDark, 0);
            box.BorderWidthBottom = 3;
            box.ContentMarginLeft = box.ContentMarginRight = 16;
            box.AntiAliasing = true;
            return box;
        }
        button.AddThemeStyleboxOverride("normal", Box(BingoStyle.Brass));
        button.AddThemeStyleboxOverride("hover", Box(BingoStyle.Brass.Lightened(0.12f)));
        button.AddThemeStyleboxOverride("pressed", Box(BingoStyle.Brass.Darkened(0.08f)));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            button.AddThemeColorOverride(state, BingoStyle.NameplateText);
        }
        button.Pressed += pressed;
        return button;
    }
}
