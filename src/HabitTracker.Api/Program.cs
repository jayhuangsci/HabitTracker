using System.Text.Json.Serialization;
using HabitTracker.Api.Data;
using HabitTracker.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<WorkoutService>();
var demo = builder.Configuration.GetValue<bool>("DemoMode");
var connection = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=habittracker.db";
if (demo && connection == "Data Source=habittracker.db")
    throw new InvalidOperationException("Demo mode requires a separate database. Use scripts/Start-Demo.ps1.");
var sqlite = new SqliteConnectionStringBuilder(connection);
if (sqlite.DataSource != ":memory:")
{
    sqlite.DataSource = Path.GetFullPath(sqlite.DataSource, builder.Environment.ContentRootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
}
builder.Services.AddDbContext<HabitTrackerDbContext>(options => options.UseSqlite(sqlite.ConnectionString));
var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "HabitTracker · API explorer");
}
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HabitTrackerDbContext>();
    await db.Database.MigrateAsync();
    if (demo) await DemoSeeder.SeedAsync(db, DateOnly.FromDateTime(DateTime.Today));
}
app.MapGet("/health", async (HabitTrackerDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy", demo }) : Results.StatusCode(503));
app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/swagger" : "/health"));
app.MapControllers();
app.Run();

public partial class Program;
