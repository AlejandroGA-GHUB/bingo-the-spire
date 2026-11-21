using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>One player's board at a glance, for the row of previews in Versus.</summary>
internal sealed record PlayerPreview(ulong Id, string Name, IReadOnlySet<int> Filled, IReadOnlySet<int> InLines, int Lines, bool Viewing, bool OneAway = false);

/// <summary>
/// Versus: a small card per player right of the nameplate, showing only which squares they've
/// filled. Clicking one opens that player's full board; clicking yours goes back to your own.
/// Fewer players get bigger cards, so the row always fits beside the nameplate.
/// </summary>
internal static class PlayerPreviews
{
    /// <summary>Card width, mini-board size, name and small text sizes, and the gap between cards.</summary>
    private readonly record struct Look(float Width, float Grid, int Name, int Small, int Gap);

    private static Look LookFor(int players) => players switch
    {
        <= 2 => new Look(86, 54, 13, 12, 10),
        3 => new Look(78, 48, 13, 12, 8),
        _ => new Look(66, 40, 12, 12, 6),
    };

    private static readonly StyleBoxFlat Card = Style(BingoStyle.Paper, BingoStyle.PageInkSoft, 2);
    private static readonly StyleBoxFlat CardViewing = Style(BingoStyle.Paper, BingoStyle.Gold.Darkened(0.25f), 4);

    public static void Fill(HBoxContainer row, int size, IReadOnlyList<PlayerPreview> players, Action<ulong> pick)
    {
        // Taken out of the row at once, so the row's size is right before the old cards are freed.
        foreach (var child in row.GetChildren())
        {
            row.RemoveChild(child);
            child.QueueFree();
        }
        var look = LookFor(players.Count);
        row.AddThemeConstantOverride("separation", look.Gap);
        foreach (var player in players)
        {
            row.AddChild(Build(size, player, pick, look));
        }
    }

    private static Control Build(int size, PlayerPreview player, Action<ulong> pick, Look look)
    {
        var card = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(look.Width, 0),
            TooltipText = player.Viewing ? "" : $"Show {player.Name}'s board",
        };
        card.AddThemeStyleboxOverride("panel", player.Viewing ? CardViewing : Card);
        card.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                card.AcceptEvent();
                pick(player.Id);
            }
        };
        Gamepad.Focusable(card, () => pick(player.Id));
        card.MouseEntered += () => card.Modulate = new Color(1.08f, 1.08f, 1.08f);
        card.MouseExited += () => card.Modulate = Colors.White;

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", -2);
        card.AddChild(column);

        var gridSize = look.Grid;
        var grid = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(gridSize, gridSize), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        grid.Draw += () =>
        {
            const float gap = 2;
            var cell = (gridSize - gap * (size - 1)) / size;
            for (var i = 0; i < size * size; i++)
            {
                var color = player.InLines.Contains(i) ? BingoStyle.MiniLine : player.Filled.Contains(i) ? BingoStyle.MiniFilled : BingoStyle.ParchmentDark;
                grid.DrawRect(new Rect2(i % size * (cell + gap), i / size * (cell + gap), cell, cell), color);
            }
        };
        column.AddChild(grid);
        // A little room between the mini-board and the text (the text rows sit close together).
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4), MouseFilter = Control.MouseFilterEnum.Ignore });

        var name = BingoStyle.Ink(player.Name, BingoFonts.NoteBold, look.Name, BingoStyle.PageInk);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.CustomMinimumSize = new Vector2(look.Width - 12, 0);
        column.AddChild(name);

        var score = BingoStyle.Ink(player.Filled.Count + (player.Lines > 0 ? $" · {player.Lines}★" : ""), BingoFonts.NoteBold, look.Small, BingoStyle.PageInkSoft);
        score.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(score);
        if (player.OneAway)
        {
            var away = BingoStyle.Ink("1 away!", BingoFonts.NoteBold, look.Small, BingoStyle.OneAwayInk);
            away.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(away);
        }
        return card;
    }

    private static StyleBoxFlat Style(Color fill, Color border, int width)
    {
        var box = BingoStyle.Box(fill, 8, width, border, 0, new Color(0, 0, 0, 0.25f), 4);
        box.ContentMarginLeft = box.ContentMarginRight = 6;
        box.ContentMarginTop = 6;
        box.ContentMarginBottom = 4;
        box.ShadowOffset = new Vector2(0, 2);
        box.AntiAliasing = true;
        return box;
    }
}
