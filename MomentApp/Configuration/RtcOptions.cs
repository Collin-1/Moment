namespace MomentApp.Configuration;

/// <summary>
/// How TURN credentials are obtained, if at all.
/// </summary>
public enum TurnProvider
{
    /// <summary>No TURN. Calls work only where STUN alone can traverse the NAT.</summary>
    None,

    /// <summary>A fixed username and password, as configured on the TURN server.</summary>
    Static,

    /// <summary>
    /// coturn's <c>use-auth-secret</c> scheme: the server holds a long-lived shared secret and
    /// mints a short-lived credential per participant. Preferred, because the secret itself
    /// never reaches a browser.
    /// </summary>
    StaticSecret,
}

/// <summary>
/// WebRTC connectivity settings.
/// </summary>
/// <remarks>
/// Bound from the <c>Rtc</c> configuration section, so everything here can be set through
/// environment variables on the host (<c>Rtc__Turn__StaticAuthSecret</c> and so on) without a
/// code change or a secret in the repository.
/// </remarks>
public sealed class RtcOptions
{
    public const string SectionName = "Rtc";

    /// <summary>
    /// STUN servers, which are enough for most home networks.
    /// </summary>
    /// <remarks>
    /// Left empty rather than pre-populated: the configuration binder <i>appends</i> to a list
    /// that already has entries instead of replacing it, so defaults here would be duplicated
    /// by whatever appsettings.json supplies. <see cref="DefaultStunServers"/> fills the gap
    /// when configuration provides nothing.
    /// </remarks>
    public List<IceServerOptions> IceServers { get; set; } = new();

    /// <summary>Public STUN, used only when no ICE servers are configured at all.</summary>
    public static IReadOnlyList<IceServerOptions> DefaultStunServers { get; } = new[]
    {
        new IceServerOptions { Urls = { "stun:stun.l.google.com:19302" } },
        new IceServerOptions { Urls = { "stun:stun1.l.google.com:19302" } },
    };

    public TurnOptions Turn { get; set; } = new();
}

public sealed class IceServerOptions
{
    public List<string> Urls { get; set; } = new();
    public string? Username { get; set; }
    public string? Credential { get; set; }
}

public sealed class TurnOptions
{
    public TurnProvider Provider { get; set; } = TurnProvider.None;

    /// <summary>
    /// TURN endpoints. Include a <c>turns:</c> URL on 443 as well as UDP and TCP: networks
    /// that block plain TURN will usually still allow TLS on 443, and that is the case that
    /// decides whether calls work from inside a corporate office.
    /// </summary>
    public List<string> Urls { get; set; } = new();

    public string? Username { get; set; }
    public string? Credential { get; set; }

    /// <summary>Shared secret for <see cref="TurnProvider.StaticSecret"/>.</summary>
    public string? StaticAuthSecret { get; set; }

    /// <summary>How long a minted credential stays valid.</summary>
    public int CredentialTtlSeconds { get; set; } = 3600;

    public bool IsConfigured =>
        Provider != TurnProvider.None && Urls.Count > 0;
}
