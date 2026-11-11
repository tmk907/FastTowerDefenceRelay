using FastTowerDefenceRelay;
using FastTowerDefenceRelay.Web;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Relay").Get<RelaySettings>() ?? new RelaySettings();
builder.Services.AddRelay(settings.ToOptions());

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapRoomListing();
app.Run();
