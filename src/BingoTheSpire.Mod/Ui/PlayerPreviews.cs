using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>One player's board at a glance, for the row of previews in Versus.</summary>
internal sealed record PlayerPreview(ulong Id, string Name, IReadOnlySet<int> Filled, IReadOnlySet<int> InLines, int Lines, bool Viewing, bool OneAway = false);

/// <summary>
/// Versus: a small card per player next to the nameplate, showing only which squares they've
/// filled. Clicking one opens that player's full board; clicking yours goes back to your own.
/// </summary>
internal static class PlayerPreviews
{
    private const float Grid = 40;
    private const float CardWidth = 70;

    private static readonly StyleBoxFlat Card = Style(BingoStyle.Paper, BingoStyle.PageInkSoft, 2);
    private static readonly StyleBoxFlat CardViewing = Style(BingoStyle.Paper, BingoStyle.Gold.Darkened(0.25f), 4);

    public static void Fill(HBoxContainer row, int size, IReadOnlyList<PlayerPreview> players, Action<ulong> pick)
    {
        foreach (var child in row.GetChildren())
        {
            child.QueueFree();
        }
        foreach (var player in players)
        {
            row.AddChild(Build(size, player, pick));
        }
    }

    private static Control Build(int size, PlayerPreview player, Action<ulong> pick)
    {
        var card = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(CardWidth, 0),
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
        column.AddThemeConstantOverride("separation", 2);
        card.AddChild(column);

        var grid = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(Grid, Grid), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        grid.Draw += () =>
        {
            const float gap = 2;
            var cell = (Grid - gap * (size - 1)) / size;
            for (var i = 0; i < size * size; i++)
            {
                var color = player.InLines.Contains(i) ? BingoStyle.MiniLine : player.Filled.Contains(i) ? BingoStyle.MiniFilled : BingoStyle.ParchmentDark;
                grid.DrawRect(new Rect2(i % size * (cell + gap), i / size * (cell + gap), cell, cell), color);
            }
        };
        column.AddChild(grid);

        var name = BingoStyle.Ink(player.Name, BingoFonts.NoteBold, 13, BingoStyle.PageInk);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.CustomMinimumSize = new Vector2(CardWidth - 12, 0);
        column.AddChild(name);

        var score = BingoStyle.Ink(player.Filled.Count + (player.Lines > 0 ? $" · {player.Lines}★" : ""), BingoFonts.NoteBold, 12, BingoStyle.PageInkSoft);
        score.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(score);
        if (player.OneAway)
        {
            var away = BingoStyle.Ink("1 away!", BingoFonts.NoteBold, 12, BingoStyle.OneAwayInk);
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
