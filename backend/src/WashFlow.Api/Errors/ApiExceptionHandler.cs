using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WashFlow.Application.Scheduling;

namespace WashFlow.Api.Errors;

public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Only explicit typed application faults carry public codes. Never serialize exception messages.
        var fault = exception as SchedulingFault;
        var status = fault?.Status is 400 or 401 or 403 or 404 or 409 ? fault.Status
            : exception is BadHttpRequestException { StatusCode: 400 } ? 400 : 500;
        var problem = new ProblemDetails { Status = status };
        if (fault != null && status != 500) problem.Extensions["code"] = fault.Code;
        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        });
    }
}
