using System.Text;
using HabitTracker.Api.Services;
using HabitTracker.Core;
using Microsoft.AspNetCore.Mvc;

namespace HabitTracker.Api.Controllers;

[ApiController]
[Route("api/workouts")]
public sealed class WorkoutsController(WorkoutService workouts) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkoutResponse>>> List(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        Ok(await workouts.ListAsync(from, to, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkoutResponse>> Get(int id, CancellationToken ct) => Ok(await workouts.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<WorkoutResponse>> Create(CreateWorkoutRequest request, CancellationToken ct)
    {
        var result = await workouts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/sets")]
    public async Task<ActionResult<WorkoutResponse>> AddSet(int id, LogSetRequest request, CancellationToken ct) =>
        Ok(await workouts.AddSetAsync(id, request, ct));

    [HttpDelete("{id:int}/sets/{setId:int}")]
    public async Task<ActionResult<WorkoutResponse>> DeleteSet(int id, int setId, CancellationToken ct) =>
        Ok(await workouts.DeleteSetAsync(id, setId, ct));

    [HttpPost("{id:int}/complete")]
    public async Task<ActionResult<WorkoutResponse>> Complete(int id, CancellationToken ct) => Ok(await workouts.CompleteAsync(id, ct));

    [HttpGet("export.csv")]
    public async Task<FileContentResult> Export(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(WorkoutCsvExporter.Export(await workouts.ListAsync(from, to, ct)))).ToArray(),
            "text/csv; charset=utf-8", "habittracker-workouts.csv");
}

[ApiController]
[Route("api/training")]
public sealed class TrainingController : ControllerBase
{
    [HttpGet("exercises")] public IActionResult Exercises() => Ok(TrainingCatalog.Exercises);
    [HttpGet("routines")] public IActionResult Routines() => Ok(TrainingCatalog.Routines);
}
