namespace MomentApp.Models;

/// <summary>
/// Defines the type of message being sent
/// </summary>
public enum MessageType
{
    /// <summary>
    /// Regular user-generated message
    /// </summary>
    User,

    /// <summary>
    /// System-generated notification (e.g., "User joined")
    /// </summary>
    System
}
