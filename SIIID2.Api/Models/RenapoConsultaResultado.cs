namespace SIIID2.Api.Models;

public enum EstadoConsultaRenapo
{
    Encontrada,
    NoEncontrada,
    CurpInvalida,
    NoDisponible,
    ErrorValidacion
}

public sealed record RenapoConsultaResultado(
    EstadoConsultaRenapo Estado,
    string? TipoError = null,
    string? CodigoError = null
);