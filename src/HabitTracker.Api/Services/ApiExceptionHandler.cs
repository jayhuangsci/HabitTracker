using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HabitTracker.Api.Services;

public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            ResourceNotFoundException => StatusCodes.Status404NotFound,
            WorkoutConflictException or DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            DbUpdateException e when e.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } => StatusCodes.Status409Conflict,
            _ => 0
        };
        if (status == 0) return false;
        context.Response.StatusCode = status;
        var detail = exception is DbUpdateException
            ? "The data changed or an entry already exists. Refresh and try again." : exception.Message;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = "The request could not be completed", Detail = detail } });
    }
}
