namespace MomentApp.Models;

/// <summary>
/// What a room opens into. Every room supports text, voice and video regardless — this
/// chooses the surface a participant lands on, so a room created for a call does not open
/// into a chat window they then have to leave.
/// </summary>
public enum RoomType
{
    Chat,
    Video,
    Voice
}
