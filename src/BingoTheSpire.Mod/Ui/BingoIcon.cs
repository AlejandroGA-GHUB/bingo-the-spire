using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The bingo button: a live mini-board drawn on a parchment card, sized like the game's top bar
/// icons, with the filled-square count in the corner. Click to open the board, drag to move it
/// anywhere (the spot is saved), right-click to put it back next to the run timer.
/// The art is drawn in code, so the mod needs no asset files.
/// </summary>
internal sealed class BingoIcon
{
    private const int IconSize = 72;
    private const float DragThreshold = 6f;

    private readonly BingoSettings _settings;
    private readonly Action _onClick;
    private readonly TextureRect _art;
    private readonly Label _count;
    private readonly Image _image;
    private readonly ImageTexture _texture;

    private bool _pressed;
    private bool _dragging;
    private Vector2 _grabOffset;
    private Vector2 _pressPosition;

    public BingoIcon(Control parent, BingoSettings settings, Action onClick)
    {
        _settings = settings;
        _onClick = onClick;

        Root = new Control
        {
            Name = "BingoIcon",
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            Size = new Vector2(IconSize, IconSize),
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        parent.AddChild(Root);

        _image = Image.CreateEmpty(IconSize, IconSize, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        _art = new TextureRect
        {
            Texture = _texture,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(_art);

        _count = BingoStyle.Text("0", 24, Colors.White, outline: 8);
        _count.MouseFilter = Control.MouseFilterEnum.Ignore;
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        _count.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _count.OffsetLeft = -48;
        _count.OffsetTop = -30;
        _count.OffsetRight = 6;
        _count.OffsetBottom = 6;
        Root.AddChild(_count);

        BingoStyle.CenterPivot(Root);
        Root.MouseEntered += () => Hover(1.1f);
        Root.MouseExited += () => Hover(1f);
        Root.GuiInput += OnInput;
        SetTooltip(null);

        Draw(5, new HashSet<int>(), new HashSet<int>());
    }

    public Control Root { get; }

    public void SetTooltip(string? hotkey) =>
        Root.TooltipText = "Bingo board" + (hotkey != null ? $" ({hotkey})" : "")
            + "\nDrag to move · right-click to reset";

    /// <summary>Redraws the mini-board: filled squares green, squares in a finished line gold.</summary>
    public void Draw(int size, IReadOnlySet<int> filled, IReadOnlySet<int> inLines)
    {
        _image.Fill(Colors.Transparent);
        // Card with a dark ink border and clipped corners.
        _image.FillRect(new Rect2I(2, 2, IconSize - 4, IconSize - 4), BingoStyle.Ink);
        _image.FillRect(new Rect2I(5, 5, IconSize - 10, IconSize - 10), BingoStyle.Parchment);
        foreach (var (x, y) in new[] { (2, 2), (IconSize - 4, 2), (2, IconSize - 4), (IconSize - 4, IconSize - 4) })
        {
            _image.FillRect(new Rect2I(x, y, 2, 2), Colors.Transparent);
        }

        const int gap = 2;
        var cell = (IconSize - 18 - gap * (size - 1)) / size;
        var start = (IconSize - (cell * size + gap * (size - 1))) / 2;
        for (var i = 0; i < size * size; i++)
        {
            var x = start + i % size * (cell + gap);
            var y = start + i / size * (cell + gap);
            var color = inLines.Contains(i) ? BingoStyle.Gold : filled.Contains(i) ? BingoStyle.Filled : BingoStyle.ParchmentDark;
            _image.FillRect(new Rect2I(x, y, cell, cell), color);
        }
        _texture.Update(_image);
        _count.Text = filled.Count.ToString();
    }

    public void Pulse() => BingoStyle.Pulse(Root, 1.25f);

    /// <summary>Puts the icon at the saved spot, or next to the run timer.</summary>
    public void Place()
    {
        var screen = Root.GetViewportRect().Size;
        Vector2 position;
        if (_settings.IconPosition is { } saved)
        {
            position = new Vector2(saved.X * screen.X, saved.Y * screen.Y);
        }
        else if (DefaultAnchor() is { } anchor)
        {
            position = new Vector2(anchor.Position.X - IconSize - 18, anchor.Position.Y + (anchor.Size.Y - IconSize) / 2);
        }
        else
        {
            position = new Vector2(screen.X - 520, 12);
        }
        Root.Position = Clamp(position, screen);
    }

    /// <summary>The run timer's spot on screen, or the map button's when the timer is hidden.</summary>
    private static Rect2? DefaultAnchor()
    {
        var topBar = NRun.Instance?.GlobalUi?.TopBar;
        if (topBar == null)
        {
            return null;
        }
        Control? anchor = topBar.Timer is { } timer && timer.IsVisibleInTree() ? timer : topBar.Map;
        return anchor != null && anchor.IsVisibleInTree() ? anchor.GetGlobalRect() : null;
    }

    private void OnInput(InputEvent evt)
    {
        switch (evt)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                _pressed = true;
                _dragging = false;
                _pressPosition = press.GlobalPosition;
                _grabOffset = press.GlobalPosition - Root.GlobalPosition;
                Root.AcceptEvent();
                break;
            case InputEventMouseMotion motion when _pressed:
                if (!_dragging && motion.GlobalPosition.DistanceTo(_pressPosition) > DragThreshold)
                {
                    _dragging = true;
                    Root.MouseDefaultCursorShape = Control.CursorShape.Drag;
                }
                if (_dragging)
                {
                    Root.Position = Clamp(motion.GlobalPosition - _grabOffset, Root.GetViewportRect().Size);
                }
                Root.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (_pressed && !_dragging)
                {
                    _onClick();
                }
                else if (_dragging)
                {
                    var screen = Root.GetViewportRect().Size;
                    _settings.SetIconPosition((Root.Position.X / screen.X, Root.Position.Y / screen.Y));
                    Root.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
                }
                _pressed = false;
                _dragging = false;
                Root.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                _settings.SetIconPosition(null);
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
