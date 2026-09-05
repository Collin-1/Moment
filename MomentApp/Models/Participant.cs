namespace MomentApp.Models;

/// <summary>
/// Represents a participant in a chat room
/// </summary>
public class Participant
{
    /// <summary>
    /// Unique identifier for the participant
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// SignalR connection ID for real-time communication
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown to other participants (e.g., "Blue", "Red")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Hex color code for the participant (e.g., "#3B82F6")
    /// </summary>
    public string ColorHex { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the participant joined the room
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the participant's last activity
    /// </summary>
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current status of the participant (Online, Away, Offline)
    /// </summary>
    public ParticipantStatus Status { get; set; } = ParticipantStatus.Online;

    /// <summary>
    /// Whether the participant has permanently left the room
    /// </summary>
    public bool HasLeft { get; set; } = false;

    /// <summary>
    /// Whether this is the participant's first connection (not a reconnect/refresh)
    /// </summary>
    public bool IsFirstConnection { get; set; } = true;

    /// <summary>
    /// Whether the participant is currently connected to the voice call
    /// </summary>
    public bool IsInVoice { get; set; } = false;

    /// <summary>
    /// Whether the participant is currently sending video
    /// </summary>
    public bool IsInVideo { get; set; } = false;

    /// <summary>
    /// Whether the participant's microphone is muted.
    /// </summary>
    /// <remarks>
    /// Broadcast rather than inferred. A receiver can observe that an incoming audio track is
    /// muted, but that signal is unreliable across browsers and lags by seconds, which makes
    /// for a mute indicator nobody trusts.
    /// </remarks>
    public bool IsMuted { get; set; } = false;

    /// <summary>
    /// When the participant's connection dropped, or null while they are connected.
    /// </summary>
    /// <remarks>
    /// Call membership is deliberately preserved for a grace period after a disconnect.
    /// SignalR reconnects routinely on a network blip, and peer-to-peer media survives the
    /// signalling outage — so tearing the call down immediately turns a recoverable hiccup
    /// into a dropped call.
    /// </remarks>
    public DateTime? DisconnectedAt { get; set; }
}
