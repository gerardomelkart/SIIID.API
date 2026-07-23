using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalEnviosRepository
{
    Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int? idEntidadFederativaUsuario, int? idEntidadFederativa, int? anioSemana, int? numeroSemana, string? tipoCarga, string? estado);
    Task<SemanalEnvioReferenciaInfo?> ObtenerReferenciaAsync(string codigoReferencia);
}