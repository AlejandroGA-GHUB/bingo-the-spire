using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Mod.Net;

internal enum NetRole
{
    /// <summary>Solo, or no connection: this game decides its own claims.</summary>
    Solo,

    /// <summary>Co-op host: decides everyone's claims.</summary>
    Host,

    /// <summary>Co-op client: sends claims to the host and applies its results.</summary>
    Client,
}

/// <summary>
/// Bingo on the game's co-op connection (spec section 7). Registers our message handlers on the
/// game's net service (the same one carries the lobby and then the run) and passes messages to
/// the runtime. Every handler catches its own errors, so a bad message can't reach the game.
/// </summary>
internal static class BingoNet
{
    private static readonly MessageHandlerDelegate<BingoTheSpireSettingsMessage> OnSettings = (m, s) => Safe("settings", () => ReceiveSettings(m, s));
    private static readonly MessageHandlerDelegate<BingoTheSpireHelloMessage> OnHello = (m, s) => Safe("hello", () => ReceiveHello(m, s));
    private static readonly MessageHandlerDelegate<BingoTheSpireSnapshotMessage> OnSnapshot = (m, s) => Safe("snapshot", () => ModEntry.Bingo?.OnSnapshot(m.Enabled, m.BoardCode, m.BoardHash, m.Results ?? []));
    private static readonly MessageHandlerDelegate<BingoTheSpireClaimMessage> OnClaim = (m, s) => Safe("claim", () => ModEntry.Bingo?.OnRemoteClaim(s, new Claim(m.Square, m.GoalId, m.PlayerId, m.Evidence, m.At)));
    private static readonly MessageHandlerDelegate<BingoTheSpireResultMessage> OnResult = (m, s) => Safe("result", () => ModEntry.Bingo?.OnRemoteResult(m.Result));

    private static INetGameService? _service;

    public static NetRole Role => _service == null ? NetRole.Solo : RoleOf(_service);

    /// <summary>Co-op client: the bingo settings the host last sent, if any.</summary>
    public static BingoSetup? HostSetup { get; private set; }

    /// <summary>Raised when the host's settings arrive, so the lobby panel can show them.</summary>
    public static event Action? HostSetupChanged;

    /// <summary>Starts listening on the game's current net service (lobby or run). Safe to call repeatedly.</summary>
    public static void Use(INetGameService? service)
    {
        if (service == null || ReferenceEquals(service, _service))
        {
            return;
        }
        Stop();
        _service = service;
        HostSetup = null;
        service.RegisterMessageHandler(OnSettings);
        service.RegisterMessageHandler(OnHello);
        service.RegisterMessageHandler(OnSnapshot);
        service.RegisterMessageHandler(OnClaim);
        service.RegisterMessageHandler(OnResult);
        BingoLog.Info($"listening for bingo messages ({Role})");
    }

    public static void SendSettings(BingoSetup setup)
    {
        if (Role == NetRole.Host)
        {
            Send(new BingoTheSpireSettingsMessage
            {
                Enabled = setup.Enabled,
                SeedEntry = setup.SeedEntry ?? "",
                BingoMode = (int)setup.Mode,
                ModVersion = ModEntry.Version,
            });
        }
    }

    public static void SendHello(string boardHash)
    {
        if (Role == NetRole.Client)
        {
            Send(new BingoTheSpireHelloMessage { BoardHash = boardHash, ModVersion = ModEntry.Version });
        }
    }

    /// <summary>Client, in the lobby: asks the host for its bingo settings.</summary>
    public static void RequestSettings()
    {
        if (Role == NetRole.Client)
        {
            Send(new BingoTheSpireHelloMessage { InLobby = true, BoardHash = "", ModVersion = ModEntry.Version });
        }
    }

    public static void SendSnapshot(ulong playerId, bool enabled, string boardCode, string boardHash, List<ClaimResolved> results)
    {
        if (Role == NetRole.Host && _service is { IsConnected: true } service)
        {
            service.SendMessage(new BingoTheSpireSnapshotMessage
            {
                Enabled = enabled,
                BoardCode = boardCode,
                BoardHash = boardHash,
                Results = results,
            }, playerId);
        }
    }

    public static void SendClaim(Claim claim)
    {
        if (Role == NetRole.Client)
        {
            Send(new BingoTheSpireClaimMessage
            {
                Square = claim.Square,
                GoalId = claim.GoalId,
                PlayerId = claim.PlayerId,
                Evidence = claim.Evidence,
                At = claim.At,
            });
        }
    }

    public static void SendResult(ClaimResolved result)
    {
        if (Role == NetRole.Host)
        {
            Send(new BingoTheSpireResultMessage { Result = result });
        }
    }

    private static void ReceiveHello(BingoTheSpireHelloMessage message, ulong sender)
    {
        if (!message.InLobby)
        {
            ModEntry.Bingo?.OnHello(sender, message.BoardHash, message.ModVersion);
        }
        else if (Role == NetRole.Host && ModEntry.Settings is { } settings)
        {
            SendSettings(settings.Setup);
        }
    }

    private static void ReceiveSettings(BingoTheSpireSettingsMessage message, ulong sender)
    {
        if (Role != NetRole.Client)
        {
            return;
        }
        if (message.ModVersion != ModEntry.Version)
        {
            BingoLog.Warn($"the host has Bingo the Spire v{message.ModVersion}, this game has v{ModEntry.Version}");
            var text = $"The host has Bingo the Spire v{message.ModVersion}, you have v{ModEntry.Version}. Boards may not match.";
            Godot.Callable.From(() => Ui.BingoOverlay.Notify("DIFFERENT VERSIONS", text)).CallDeferred();
        }
        var mode = Enum.IsDefined(typeof(BingoMode), message.BingoMode) ? (BingoMode)message.BingoMode : BingoMode.Versus;
        HostSetup = new BingoSetup(message.Enabled, string.IsNullOrEmpty(message.SeedEntry) ? null : message.SeedEntry, mode);
        HostSetupChanged?.Invoke();
    }

    private static void Send<T>(T message) where T : INetMessage
    {
        if (_service is { IsConnected: true } service)
        {
            service.SendMessage(message);
        }
    }

    private static void Stop()
    {
        if (_service == null)
        {
            return;
        }
        try
        {
            _service.UnregisterMessageHandler(OnSettings);
            _service.UnregisterMessageHandler(OnHello);
            _service.UnregisterMessageHandler(OnSnapshot);
            _service.UnregisterMessageHandler(OnClaim);
            _service.UnregisterMessageHandler(OnResult);
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't stop listening on the old connection: {ex.Message}");
        }
        _service = null;
    }

    /// <summary>The game's net type, read by name (enum numbers can differ between branches).</summary>
    private static NetRole RoleOf(INetGameService service) => service.Type.ToString() switch
    {
        "Host" => NetRole.Host,
        "Client" => NetRole.Client,
        _ => NetRole.Solo,
    };

    private static void Safe(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            BingoLog.Error($"handling a bingo {what} message failed: {ex}");
        }
    }
}
