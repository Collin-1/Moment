using Microsoft.AspNetCore.HttpOverrides;
using MomentApp.Hubs;
using MomentApp.Configuration;
using MomentApp.Infrastructure;
using MomentApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add SignalR
builder.Services.AddSignalR();

// Render (and any other reverse proxy) terminates TLS and forwards the original scheme in
// X-Forwarded-Proto. Without this, Request.Scheme is "http" inside the container, so the
// shareable link and QR code on the "room created" page are generated as http:// URLs.
// Anyone following one lands on an insecure origin, where getUserMedia does not exist and
// calls therefore cannot start at all. KnownNetworks/KnownProxies are cleared because the
// proxy's address is not known ahead of time on a PaaS.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// WebRTC connectivity. TURN credentials come from configuration/environment so they can be
// supplied on the host without a code change or a secret in the repository.
builder.Services.Configure<RtcOptions>(builder.Configuration.GetSection(RtcOptions.SectionName));
builder.Services.AddSingleton<IIceServerProvider, IceServerProvider>();

// Register application services
builder.Services.AddSingleton<IRoomService, RoomService>();
builder.Services.AddSingleton<IMessageService, MessageService>();
builder.Services.AddSingleton<IVotingService, VotingService>();
builder.Services.AddSingleton<ColorService>();
builder.Services.AddSingleton<MessageRateLimiter>();

// Add background timer service
builder.Services.AddHostedService<TimerService>();

// Backing store for the message rate limiter. Distinct from AddDistributedMemoryCache below,
// which is the session store.
builder.Services.AddMemoryCache();

// Add session support for storing participant info. The session cookie is the only thing
// identifying a participant, so it must survive a mobile browser killing the tab — hence
// MaxAge, which makes it persistent rather than session-scoped.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.MaxAge = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

var app = builder.Build();

// Must run before anything that reads the scheme, the client address, or sets a Secure cookie.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Only use HTTPS redirection in development; Render handles SSL termination.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

// Room pages carry the room code, participant identity and message history. Caching one
// would leave a readable copy of a "disappearing" conversation on disk after the room is
// gone, so they are marked no-store at the source. The service worker also refuses to cache
// them, but a privacy guarantee should not depend on client code being correct.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Room", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        context.Response.Headers.Pragma = "no-cache";
    }

    await next();
});

app.UseSession();

// Must sit between UseSession and the hub endpoints: it copies room membership out of the
// session and onto the request principal, which is the only form of identity a hub can read
// reliably on every transport.
app.UseHubSessionIdentity("/hubs");

app.UseAuthorization();

app.MapStaticAssets();

// One hub, one connection per client.
app.MapHub<RoomHub>("/hubs/room");

// Attribute-routed API endpoints (e.g. /api/ice/{roomCode}).
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
