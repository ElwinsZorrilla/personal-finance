using Margen.Budget;

namespace Margen.Api.Endpoints;

/// <summary>Respuestas comunes, todas en formato ProblemDetails.</summary>
public static class ApiResults
{
    public static IResult Problem(int status, string detail) =>
        Results.Problem(detail: detail, statusCode: status);

    public static IResult BadRequest(string detail) =>
        Problem(StatusCodes.Status400BadRequest, detail);

    public static IResult NotFound(string detail) =>
        Problem(StatusCodes.Status404NotFound, detail);

    public static IResult Unauthorized(string detail) =>
        Problem(StatusCodes.Status401Unauthorized, detail);

    public static IResult Conflict(string detail) =>
        Problem(StatusCodes.Status409Conflict, detail);

    /// <summary>
    /// Traduce un <see cref="Outcome{T}"/> a una respuesta HTTP.
    /// </summary>
    /// <remarks>
    /// «Faltan datos» es 409 y no 200 con ceros: el servidor entendió la
    /// petición y no puede responderla con una cifra, y decirlo con un cuerpo
    /// lleno de ceros sería devolver cifras inventadas con código de éxito.
    /// «Los datos se contradicen» es 500 porque el defecto es del servidor: el
    /// cliente no puede hacer nada distinto para arreglarlo.
    /// </remarks>
    public static IResult FromOutcome<T>(Outcome<T> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome.Kind switch
        {
            OutcomeKind.Computed => Results.Ok(outcome.Value),
            OutcomeKind.Insufficient => Problem(
                StatusCodes.Status409Conflict,
                outcome.Reason ?? "Faltan datos para calcular esta cifra."),
            _ => Problem(
                StatusCodes.Status500InternalServerError,
                outcome.Reason ?? "Los datos guardados se contradicen."),
        };
    }
}
