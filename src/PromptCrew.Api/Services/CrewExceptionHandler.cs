using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PromptCrew.Core;

namespace PromptCrew.Api.Services;

public sealed class CrewExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var known = exception as CrewException;
        if (known == null && exception is DbUpdateConcurrencyException)
            known = new(409, "version_conflict", "记录已改变，请刷新后重试。");
        if (known == null && exception is DbUpdateException { InnerException: Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } })
            known = new(409, "record_conflict", "记录已存在，或关联关系与已有数据冲突。");
        if (known == null) return false;
        context.Response.StatusCode = known.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = known.StatusCode, Title = known.Code, Detail = known.Message,
                Extensions = { ["code"] = known.Code } }
        });
    }
}
