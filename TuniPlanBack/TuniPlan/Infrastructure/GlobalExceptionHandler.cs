using Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TuniPlan.Infrastructure;

/// <summary>Turns exceptions into RFC 7807 ProblemDetails. Internal details are never sent to clients.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case ValidationException ve:
                problem = new ValidationProblemDetails(ve.Errors) { Status = 400, Title = ve.Message };
                problem.Extensions["code"] = ve.Code;
                break;
            case AppException ae:
                problem = new ProblemDetails { Status = ae.StatusCode, Title = ae.Message };
                problem.Extensions["code"] = ae.Code;
                if (ae is AccountLockedException le) problem.Extensions["lockoutEndUtc"] = le.LockoutEndUtc;
                break;
            case DbUpdateConcurrencyException:
                problem = new ProblemDetails { Status = 409, Title = "Les données ont été modifiées entre-temps. Rechargez et réessayez." };
                problem.Extensions["code"] = "concurrency_conflict";
                break;
            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                httpContext.Response.StatusCode = 499;
                return true;
            default:
                logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                problem = new ProblemDetails { Status = 500, Title = "Une erreur inattendue est survenue." };
                problem.Extensions["code"] = "server_error";
                break;
        }

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status ?? 500;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
