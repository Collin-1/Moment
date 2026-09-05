using System.Security.Claims;

namespace MomentApp.Infrastructure;

/// <summary>
/// Projects a visitor's room memberships out of their session and onto the request principal,
/// so hubs can identify callers without trusting client-supplied ids.
/// </summary>
/// <remarks>
/// Hubs cannot read the session directly. <c>Context.GetHttpContext()</c> hands back the
/// request that opened the connection, and on every transport except WebSockets that request
/// has already completed — its <c>HttpContext</c> is returned to a pool and its session
/// feature removed, so touching <c>.Session</c> from a hub throws
/// "Session has not been configured for this application or request".
///
/// <c>HubCallerContext.User</c>, by contrast, is a snapshot SignalR takes at connection time
/// and holds for the life of the connection. Copying the session's membership entries into
/// claims here is therefore the one channel that works on all transports.
/// </remarks>
public static class HubSessionIdentityMiddleware
{
    private const string SessionKeyPrefix = "ParticipantId_";

    /// <summary>Claim type holding the caller's participant id for a given room.</summary>
    public static string ClaimTypeForRoom(string roomId) => $"moment:room:{roomId}";

    public static IApplicationBuilder UseHubSessionIdentity(this IApplicationBuilder app, PathString hubPrefix)
    {
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(hubPrefix, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Keys is empty until the session is actually loaded.
            await context.Session.LoadAsync(context.RequestAborted);

            var claims = context.Session.Keys
                .Where(key => key.StartsWith(SessionKeyPrefix, StringComparison.Ordinal))
                .Select(key => (Room: key[SessionKeyPrefix.Length..], Id: context.Session.GetString(key)))
                .Where(entry => !string.IsNullOrEmpty(entry.Id))
                .Select(entry => new Claim(ClaimTypeForRoom(entry.Room), entry.Id!))
                .ToList();

            if (claims.Count > 0)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "MomentSession"));
            }

            await next();
        });
    }
}
