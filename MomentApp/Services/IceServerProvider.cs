using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MomentApp.Configuration;

namespace MomentApp.Services;

/// <summary>One ICE server as the browser's RTCConfiguration expects it.</summary>
public sealed record IceServerDto(string[] Urls, string? Username = null, string? Credential = null);

/// <summary>
/// The ICE configuration handed to one participant.
/// </summary>
/// <param name="ExpiresAtUnix">
/// When the credentials stop working, so a long call can refresh before they lapse.
/// </param>
/// <param name="HasTurn">
/// Whether a relay is actually configured. Lets the client say something honest when a call
/// fails to connect, rather than leaving the user guessing.
/// </param>
public sealed record IceConfigDto(IceServerDto[] IceServers, long ExpiresAtUnix, bool HasTurn);

public interface IIceServerProvider
{
    IceConfigDto GetIceConfig(string participantId);
}

/// <summary>
/// Builds the ICE server list, minting short-lived TURN credentials where configured.
/// </summary>
public sealed class IceServerProvider : IIceServerProvider
{
    private readonly IOptionsMonitor<RtcOptions> _options;
    private readonly ILogger<IceServerProvider> _logger;

    public IceServerProvider(IOptionsMonitor<RtcOptions> options, ILogger<IceServerProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public IceConfigDto GetIceConfig(string participantId)
    {
        var options = _options.CurrentValue;
        var servers = new List<IceServerDto>();

        var configured = options.IceServers.Where(s => s.Urls.Count > 0).ToList();
        var stunServers = configured.Count > 0 ? configured : RtcOptions.DefaultStunServers;

        foreach (var stun in stunServers)
        {
            servers.Add(new IceServerDto(stun.Urls.ToArray(), stun.Username, stun.Credential));
        }

        var turn = options.Turn;
        var expiry = DateTimeOffset.UtcNow.AddSeconds(turn.CredentialTtlSeconds);

        if (turn.IsConfigured)
        {
            switch (turn.Provider)
            {
                case TurnProvider.Static:
                    servers.Add(new IceServerDto(turn.Urls.ToArray(), turn.Username, turn.Credential));
                    break;

                case TurnProvider.StaticSecret when !string.IsNullOrEmpty(turn.StaticAuthSecret):
                {
                    // coturn's REST scheme: the username is "<unix expiry>:<user>" and the
                    // credential is its HMAC-SHA1 under the shared secret. The secret stays on
                    // the server; the browser only ever sees a credential that expires.
                    var username = $"{expiry.ToUnixTimeSeconds()}:{participantId}";
                    using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(turn.StaticAuthSecret));
                    var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
                    servers.Add(new IceServerDto(turn.Urls.ToArray(), username, credential));
                    break;
                }

                case TurnProvider.StaticSecret:
                    _logger.LogWarning(
                        "TURN provider is StaticSecret but Rtc:Turn:StaticAuthSecret is empty; falling back to STUN only.");
                    break;
            }
        }

        return new IceConfigDto(servers.ToArray(), expiry.ToUnixTimeSeconds(), turn.IsConfigured);
    }
}
