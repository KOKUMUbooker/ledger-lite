using System.Text.Json;
using LedgerLite.Application.Exceptions;
using LedgerLite.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LedgerLite.Api.ErrorHandling;

public class LedgerExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            AccountNotFoundException or EntryNotFoundException
                => (StatusCodes.Status404NotFound, "Not found"),
            DuplicateAccountCodeException or EntryAlreadyReversedException
                => (StatusCodes.Status409Conflict, "Conflict"),
            AccountInactiveException or InvalidParentAccountException
                or AccountHasBalanceException or AccountHasActiveChildrenException
                => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            DomainException
                => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (0, string.Empty)
        };

        // Not one of ours: return false so ASP.NET produces its standard 500 (and logs it)
        if (status == 0) return false;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message
        };

        // Machine-readable code for the Blazor client, e.g. "UnbalancedEntry", "AccountNotFound"
        problem.Extensions["code"] = exception.GetType().Name.Replace("Exception", "");

        if (exception is UnbalancedEntryException unbalanced)
        {
            problem.Extensions["totalDebits"] = unbalanced.TotalDebits;
            problem.Extensions["totalCredits"] = unbalanced.TotalCredits;
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            problem, (JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }
}