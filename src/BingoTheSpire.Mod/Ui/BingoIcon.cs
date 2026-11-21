using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The bingo button: a mini-board on a parchment card, sized like the game's top bar icons, with
/// a badge in the corner (squares filled during a run, ON/OFF in the lobby). Drawn as vector
/// shapes, so it's sharp at any resolution.
/// In a run it can be dragged anywhere (the spot is saved) and right-clicked to go back next to
/// the timer. In the lobby it sits under the game's version text.
/// </summary>
internal sealed class BingoIcon
{
    public const int IconSize = 72;
    private const float DragThreshold = 6f;

    private readonly BingoSettings? _settings;
    private readonly Action _onClick;
    private readonly Func<Control, Vector2?> _defaultSpot;
    private readonly Control _art;
    private readonly Label _badge;

    private int _size = 5;
    private IReadOnlySet<int> _filled = new HashSet<int>();
    private IReadOnlySet<int> _inLines = new HashSet<int>();
    private bool _pressed;
    private bool _dragging;
    private Vector2 _grabOffset;
    private Vector2 _pressPosition;

    /// <param name="settings">Where to save a dragged position; null makes the icon stay put.</param>
    /// <param name="defaultSpot">Where the icon goes when it hasn't been moved (screen position), or null if unknown.</param>
    public BingoIcon(Control parent, BingoSettings? settings, Action onClick, Func<Control, Vector2?>? defaultSpot = null)
    {
        _settings = settings;
        _onClick = onClick;
        _defaultSpot = defaultSpot ?? NextToRunTimer;

        Root = new Control
        {
            Name = "BingoTheSpireIcon",
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            Size = new Vector2(IconSize, IconSize),
            MouseFilter = Control.MouseFilterEnum.Stop,
                        Theme = BingoFonts.Theme,
        };
        parent.AddChild(Root);

        _art = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _art.Draw += Paint;
        Root.AddChild(_art);

        _badge = BingoStyle.Text("0", 22, Colors.White, outline: 8, font: BingoFonts.Heading);
        _badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _badge.HorizontalAlignment = HorizontalAlignment.Right;
        _badge.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _badge.OffsetLeft = -64;
        _badge.OffsetTop = -30;
        _badge.OffsetRight = 8;
        _badge.OffsetBottom = 8;
        Root.AddChild(_badge);

        BingoStyle.CenterPivot(Root);
        Root.MouseEntered += () => Hover(1.1f);
        Root.MouseExited += () => Hover(1f);
        Root.GuiInput += OnInput;
        SetTooltip(null);
    }

    public Control Root { get; }

    public void SetTooltip(string? hotkey) =>
        Root.TooltipText = _settings == null
            ? "Bingo settings for this run"
            : "Bingo board" + (hotkey != null ? $" ({hotkey})" : "") + "\nDrag to move · right-click to reset";

    /// <summary>Redraws the mini-board: filled squares green, squares in a finished line gold.</summary>
    public void Draw(int size, IReadOnlySet<int> filled, IReadOnlySet<int> inLines)
    {
        _size = size;
        _filled = filled;
        _inLines = inLines;
        _badge.Text = filled.Count.ToString();
        _art.QueueRedraw();
    }

    /// <summary>Replaces the corner number with a word (e.g. ON or OFF in the lobby).</summary>
    public void SetBadge(string text, Color color)
    {
        _badge.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _badge.HorizontalAlignment = HorizontalAlignment.Center;
        _badge.OffsetLeft = -16;
        _badge.OffsetRight = 16;
        _badge.OffsetTop = -24;
        _badge.OffsetBottom = 12;
        _badge.Text = text;
        _badge.AddThemeColorOverride("font_color", color);
    }

    public void Pulse() => BingoStyle.Pulse(Root, 1.25f);

    private static readonly StyleBoxFlat CardStyle = BingoStyle.Box(BingoStyle.Parchment, 6, 3, BingoStyle.IconInk, 0);

    private void Paint()
    {
        // Card with a dark ink border.
        _art.DrawStyleBox(CardStyle, new Rect2(2, 2, IconSize - 4, IconSize - 4));

        const float gap = 2;
        var cell = (IconSize - 18 - gap * (_size - 1)) / _size;
        var start = (IconSize - (cell * _size + gap * (_size - 1))) / 2;
        for (var i = 0; i < _size * _size; i++)
        {
            var x = start + i % _size * (cell + gap);
            var y = start + i / _size * (cell + gap);
            var color = _inLines.Contains(i) ? BingoStyle.Gold : _filled.Contains(i) ? BingoStyle.Filled : BingoStyle.ParchmentDark;
            _art.DrawRect(new Rect2(x, y, cell, cell), color);
        }
    }

    /// <summary>Puts the icon at its saved spot, or its default one.</summary>
    public void Place()
    {
        var screen = Root.GetViewportRect().Size;
        Vector2 position;
        if (_settings?.IconPosition is { } saved)
        {
            position = new Vector2(saved.X * screen.X, saved.Y * screen.Y);
        }
        else if (_defaultSpot(Root) is { } spot)
        {
            position = spot;
        }
        else
        {
            position = new Vector2(screen.X - 520, 12);
        }
        Root.GlobalPosition = Clamp(position, screen);
    }

    /// <summary>In a run: left of the timer, or of the map button when the timer is hidden.</summary>
    private static Vector2? NextToRunTimer(Control _)
    {
        var topBar = NRun.Instance?.GlobalUi?.TopBar;
        if (topBar == null)
        {
            return null;
        }
        Control? anchor = topBar.Timer is { } timer && timer.IsVisibleInTree() ? timer : topBar.Map;
        if (anchor == null || !anchor.IsVisibleInTree())
        {
            return null;
        }
        var rect = anchor.GetGlobalRect();
        return new Vector2(rect.Position.X - IconSize - 18, rect.Position.Y + (rect.Size.Y - IconSize) / 2);
    }

    private void OnInput(InputEvent evt)
    {
        var movable = _settings != null;
        switch (evt)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                _pressed = true;
                _dragging = false;
                _pressPosition = press.GlobalPosition;
                _grabOffset = press.GlobalPosition - Root.GlobalPosition;
                Root.AcceptEvent();
                break;
            case InputEventMouseMotion motion when _pressed && movable:
                if (!_dragging && motion.GlobalPosition.DistanceTo(_pressPosition) > DragThreshold)
                {
                    _dragging = true;
                }
                if (_dragging)
                {
                    Root.GlobalPosition = Clamp(motion.GlobalPosition - _grabOffset, Root.GetViewportRect().Size);
                }
                Root.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (_pressed && !_dragging)
                {
                    _onClick();
                }
                else if (_dragging && _settings != null)
                {
                    var screen = Root.GetViewportRect().Size;
                    _settings.SetIconPosition((Root.GlobalPosition.X / screen.X, Root.GlobalPosition.Y / screen.Y));
                }
                _pressed = false;
                _dragging = false;
                Root.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when movable:
                _settings!.SetIconPosition(null);
                Place();
                Pulse();
                Root.AcceptEvent();
                break;
        }
    }

    private void Hover(float scale)
    {
        if (_dragging)
        {
            return;
        }
        var tween = Root.CreateTween();
        tween.TweenProperty(Root, "scale", new Vector2(scale, scale), 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    private static Vector2 Clamp(Vector2 position, Vector2 screen) =>
        new(Math.Clamp(position.X, 0, Math.Max(0, screen.X - IconSize)), Math.Clamp(position.Y, 0, Math.Max(0, screen.Y - IconSize)));
}
