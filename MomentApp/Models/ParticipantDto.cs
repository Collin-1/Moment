namespace MomentApp.Models;

/// <summary>
/// The participant fields that are safe to send to other clients.
/// </summary>
/// <remarks>
/// Hubs must project through this rather than serialising <see cref="Participant"/> directly.
/// The entity carries <see cref="Participant.ConnectionId"/>, which is the server's routing
/// token for that person — broadcasting it hands every client the means to address messages
/// as though they were the server. It also pins the wire format, so adding a server-side
/// field can no longer leak into every client payload by accident.
/// </remarks>
public sealed record ParticipantDto(
    string Id,
    string DisplayName,
    string ColorHex,
    DateTime JoinedAt,
    ParticipantStatus Status,
    bool IsInVoice,
    bool IsInVideo,
    bool IsMuted)
{
    public static ParticipantDto From(Participant participant) => new(
        participant.Id,
        participant.DisplayName,
        participant.ColorHex,
        participant.JoinedAt,
        participant.Status,
        participant.IsInVoice,
        participant.IsInVideo,
        participant.IsMuted);
}
