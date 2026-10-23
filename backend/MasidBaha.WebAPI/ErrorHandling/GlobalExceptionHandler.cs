using MasidBaha.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MasidBaha.WebAPI.ErrorHandling;

// Turns exceptions into RFC 7807 problem responses. Known application
// exceptions map to their HTTP status; anything else is a 500 with a generic
// message, so internals never leak to clients (the details are in the logs).
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem = exception switch
        {
            NotFoundException e => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = e.Message },
            ConflictException e => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = e.Message },
            ForbiddenException e => new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Forbidden", Detail = e.Message },
            ValidationFailedException e => new ValidationProblemDetails(e.Errors.ToDictionary(x => x.Key, x => x.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Detail = "Something went wrong. Please try again."
            }
        };

        if (problem.Status >= 500)
            _logger.LogError(exception, "Unhandled exception");

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }
}
