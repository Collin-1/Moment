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
}
