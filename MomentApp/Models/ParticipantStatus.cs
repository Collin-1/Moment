namespace MomentApp.Models;

/// <summary>
/// Represents the current status of a participant in a room
/// </summary>
public enum ParticipantStatus
{
    /// <summary>
    /// Participant is actively using the chat
    /// </summary>
    Online,

    /// <summary>
    /// Participant is inactive for more than 5 minutes
    /// </summary>
    Away,

    /// <summary>
    /// Participant has disconnected
    /// </summary>
    Offline
}
