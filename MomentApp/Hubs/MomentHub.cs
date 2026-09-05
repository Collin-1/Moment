using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR;
using MomentApp.Infrastructure;
using MomentApp.Models;
using MomentApp.Services;

namespace MomentApp.Hubs;

/// <summary>
/// Shared behaviour for hubs that act on behalf of a room participant.
/// </summary>
/// <remarks>
/// The important rule here is that <b>a caller's identity is never taken from method
/// arguments</b>. Participant ids are broadcast to every client in the room state, so a hub
/// method that accepts one and trusts it lets any participant act as any other — sending chat
/// under their name, and receiving the WebRTC signalling addressed to them. Identity comes
/// from the session cookie set during the join flow, which the client cannot forge.
/// </remarks>
public abstract class MomentHub : Hub
{

    protected IRoomService RoomService { get; }

    protected MomentHub(IRoomService roomService) => RoomService = roomService;

    /// <summary>
    /// The caller's participant id for a room, or null if they never joined it.
    /// </summary>
    /// <remarks>
    /// Read from the connection's principal, which <see cref="HubSessionIdentityMiddleware"/>
    /// populates from the session while the HTTP request is still alive. The session itself is
    /// unreachable from here on non-WebSocket transports.
    /// </remarks>
    protected string? GetSessionParticipantId(string roomId)
    {
        if (string.IsNullOrEmpty(roomId))
        {
            return null;
        }

        return Context.User?.FindFirst(HubSessionIdentityMiddleware.ClaimTypeForRoom(roomId))?.Value;
    }

    /// <summary>
    /// Resolves the room and the caller's participant record, or returns false if the caller
    /// has not joined this room through the normal flow.
    /// </summary>
    protected bool TryResolveCaller(
        string roomId,
        [NotNullWhen(true)] out Room? room,
        [NotNullWhen(true)] out Participant? participant)
    {
        room = null;
        participant = null;

        var candidateRoom = RoomService.GetRoom(roomId);
        if (candidateRoom == null)
        {
            return false;
        }

        var participantId = GetSessionParticipantId(roomId);
        if (string.IsNullOrEmpty(participantId))
        {
            return false;
        }

        var candidate = candidateRoom.FindParticipant(participantId);
        if (candidate == null || candidate.HasLeft)
        {
            return false;
        }

        room = candidateRoom;
        participant = candidate;
        return true;
    }
}
