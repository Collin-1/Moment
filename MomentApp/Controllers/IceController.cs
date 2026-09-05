using Microsoft.AspNetCore.Mvc;
using MomentApp.Services;

namespace MomentApp.Controllers;

/// <summary>
/// Serves per-participant ICE configuration, including short-lived TURN credentials.
/// </summary>
/// <remarks>
/// Deliberately an endpoint rather than something baked into the room page. Credentials in
/// markup would be cached by the browser and the back/forward cache, would be readable by any
/// script that ever got onto the page, and could never be refreshed for a call that outlives
/// its TTL.
///
/// The session check is what stops this being an open relay: only somebody already admitted to
/// the room can mint credentials against your TURN bandwidth.
/// </remarks>
[ApiController]
[Route("api/ice")]
public sealed class IceController : ControllerBase
{
    private readonly IRoomService _roomService;
    private readonly IIceServerProvider _iceServerProvider;

    public IceController(IRoomService roomService, IIceServerProvider iceServerProvider)
    {
        _roomService = roomService;
        _iceServerProvider = iceServerProvider;
    }

    [HttpGet("{roomCode}")]
    public IActionResult Get(string roomCode)
    {
        if (_roomService.GetRoom(roomCode) == null)
        {
            return NotFound();
        }

        var participantId = HttpContext.Session.GetString($"ParticipantId_{roomCode}");
        if (string.IsNullOrEmpty(participantId))
        {
            return Unauthorized();
        }

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";

        return Ok(_iceServerProvider.GetIceConfig(participantId));
    }
}
