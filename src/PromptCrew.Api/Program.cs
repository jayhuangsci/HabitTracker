using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using PromptCrew.Api;
using PromptCrew.Api.Data;
using PromptCrew.Api.Services;
using PromptCrew.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(sp => StorageSettings.From(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(sp => sp.GetRequiredService<StorageSettings>().ReadCredential(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<CrewDbContext>((sp, options) =>
    options.UseSqlite($"Data Source={sp.GetRequiredService<StorageSettings>().DatabasePath};Foreign Keys=True;Default Timeout=15"));
builder.Services.AddScoped<CrewService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CrewExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "PromptCrew · AI 员工提示词工作台", Version = "v1",
        Description = "输入不可变；结果追加版本。所有 /api 路由要求 X-Api-Key。首次签发的项目密钥仅返回一次。" });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, Name = "X-Api-Key", In = ParameterLocation.Header });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("ApiKey", document)] = [] });
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();
app.UseMiddleware<CrewAuthenticationMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "PromptCrew API");
}
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CrewDbContext>();
    await db.Database.MigrateAsync();
    _ = scope.ServiceProvider.GetRequiredService<AdminCredential>();
}
app.MapGet("/health", async (CrewDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy", product = "PromptCrew" }) : Results.StatusCode(503));
app.MapGet("/", () => Results.Redirect("/health"));
var api = app.MapGroup("/api");
api.MapGet("/employee-templates", () => RoleTemplates.All);
api.MapGet("/projects", (HttpContext h, CrewService s, CancellationToken ct) => s.ListProjects(h.Actor(), ct));
api.MapPost("/projects", async (ProjectInput input, HttpContext h, CrewService s, CancellationToken ct) =>
    Results.Created("/api/projects", await s.SaveProject(null, input, h.Actor(), ct)));
api.MapGet("/projects/{projectId:guid}", (Guid projectId, HttpContext h, CrewService s, CancellationToken ct) => s.GetProject(projectId, h.Actor(), ct));
api.MapPut("/projects/{projectId:guid}", (Guid projectId, ProjectInput input, HttpContext h, CrewService s, CancellationToken ct) => s.SaveProject(projectId, input, h.Actor(), ct));
api.MapGet("/projects/{projectId:guid}/employees", (Guid projectId, HttpContext h, CrewService s, CancellationToken ct) => s.ListEmployees(projectId, h.Actor(), ct));
api.MapPost("/projects/{projectId:guid}/employees", async (Guid projectId, EmployeeInput input, HttpContext h, CrewService s, CancellationToken ct) =>
    Results.Created($"/api/projects/{projectId}/employees", await s.SaveEmployee(projectId, null, input, h.Actor(), ct)));
api.MapGet("/projects/{projectId:guid}/employees/{id:guid}", (Guid projectId, Guid id, HttpContext h, CrewService s, CancellationToken ct) => s.GetEmployee(projectId, id, h.Actor(), ct));
api.MapPut("/projects/{projectId:guid}/employees/{id:guid}", (Guid projectId, Guid id, EmployeeInput input, HttpContext h, CrewService s, CancellationToken ct) => s.SaveEmployee(projectId, id, input, h.Actor(), ct));
api.MapGet("/projects/{projectId:guid}/chains", (Guid projectId, HttpContext h, CrewService s, CancellationToken ct) => s.ListChains(projectId, h.Actor(), ct));
api.MapPost("/projects/{projectId:guid}/chains", async (Guid projectId, ChainInput input, HttpContext h, CrewService s, CancellationToken ct) =>
    Results.Created($"/api/projects/{projectId}/chains", await s.SaveChain(projectId, null, input, h.Actor(), ct)));
api.MapGet("/projects/{projectId:guid}/chains/{id:guid}", (Guid projectId, Guid id, HttpContext h, CrewService s, CancellationToken ct) => s.GetChain(projectId, id, h.Actor(), ct));
api.MapPut("/projects/{projectId:guid}/chains/{id:guid}", (Guid projectId, Guid id, ChainInput input, HttpContext h, CrewService s, CancellationToken ct) => s.SaveChain(projectId, id, input, h.Actor(), ct));
api.MapGet("/access-keys", (HttpContext h, CrewService s, CancellationToken ct) => s.ListKeys(h.Actor(), ct));
api.MapPost("/access-keys", (AccessKeyInput input, HttpContext h, CrewService s, CancellationToken ct) => s.IssueKey(input, h.Actor(), ct));
api.MapPost("/access-keys/{id:guid}/revoke", async (Guid id, HttpContext h, CrewService s, CancellationToken ct) => { await s.RevokeKey(id, h.Actor(), ct); return Results.NoContent(); });
app.Run();

public partial class Program;
