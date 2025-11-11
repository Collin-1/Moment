using MomentApp.Hubs;
using MomentApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add SignalR
builder.Services.AddSignalR();

// Register application services
builder.Services.AddSingleton<IRoomService, RoomService>();
builder.Services.AddSingleton<IMessageService, MessageService>();
builder.Services.AddSingleton<IVotingService, VotingService>();
builder.Services.AddSingleton<ColorService>();

// Add background timer service
builder.Services.AddHostedService<TimerService>();

// Add session support for storing participant info
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();
app.UseSession();

app.MapStaticAssets();

// Map SignalR hubs
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<RoomHub>("/hubs/room");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
