using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RoomBooking.Api.Http;

/// <summary>Fabryka odpowiedzi ProblemDetails (RFC 9457) z polskimi komunikatami wg CONTRACT.md.</summary>
public static class Problems
{
    public const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    public const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    public const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
    public const string ValidationTitle = "Błąd walidacji.";

    public static IResult Validation(IReadOnlyDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, title: ValidationTitle, type: BadRequestType);

    public static IResult NotFound(string detail) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Nie znaleziono.",
            detail: detail,
            type: NotFoundType);

    public static IResult Conflict(string detail) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Konflikt.",
            detail: detail,
            type: ConflictType);

    public static IResult BookingCollision(Guid conflictingBookingId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Kolizja rezerwacji.",
            detail: "Termin koliduje z istniejącą rezerwacją.",
            type: ConflictType,
            extensions: new Dictionary<string, object?> { ["conflictingBookingId"] = conflictingBookingId });
}

/// <summary>
/// Zamienia błędy wiązania żądania (zły JSON, zły typ pola, brak body) na 400 ValidationProblemDetails,
/// a pozostałe wyjątki na ProblemDetails 500 z polskim tytułem.
/// </summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        ProblemDetails problem;
        if (exception is BadHttpRequestException bad)
        {
            if (bad.StatusCode == StatusCodes.Status400BadRequest)
            {
                var (key, message) = Describe(bad);
                problem = new HttpValidationProblemDetails(new Dictionary<string, string[]> { [key] = [message] })
                {
                    Title = Problems.ValidationTitle,
                    Type = Problems.BadRequestType,
                    Status = StatusCodes.Status400BadRequest,
                };
            }
            else
            {
                problem = new ProblemDetails
                {
                    Title = "Nieprawidłowe żądanie.",
                    Detail = bad.StatusCode == StatusCodes.Status415UnsupportedMediaType
                        ? "Nieobsługiwany typ treści; wymagany application/json."
                        : null,
                    Status = bad.StatusCode,
                };
            }
        }
        else
        {
            problem = new ProblemDetails
            {
                Title = "Wewnętrzny błąd serwera.",
                Status = StatusCodes.Status500InternalServerError,
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static (string Key, string Message) Describe(BadHttpRequestException exception)
    {
        if (exception.InnerException is JsonException json)
        {
            var path = json.Path;
            var key = string.IsNullOrEmpty(path) || path == "$"
                ? "body"
                : path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
            return key == "body"
                ? (key, "Nieprawidłowy format JSON.")
                : (key, "Nieprawidłowa wartość pola.");
        }

        return ("body", "Nieprawidłowe lub puste body żądania.");
    }
}
