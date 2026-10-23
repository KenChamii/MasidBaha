using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace MasidBaha.WebAPI.Filters;

// Runs the FluentValidation validator (if one is registered) for every action
// argument and answers 400 with a ValidationProblemDetails listing each broken
// rule. It runs after model binding, so binding errors still short-circuit first.
public class FluentValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;
    private readonly ProblemDetailsFactory _problems;

    public FluentValidationFilter(IServiceProvider services, ProblemDetailsFactory problems)
    {
        _services = services;
        _problems = problems;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (_services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            if (result.IsValid) continue;

            foreach (var error in result.Errors)
                context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        if (!context.ModelState.IsValid)
        {
            // The factory adds the standard fields (type, traceId) like any other problem response
            context.Result = new BadRequestObjectResult(_problems.CreateValidationProblemDetails(
                context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest,
                title: "One or more validation errors occurred."));
            return;
        }

        await next();
    }
}
