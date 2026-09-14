using SIIID2.Api.Models;

namespace SIIID2.Api.Readers;

public interface IBanciArchivoReader
{
    Task<BanciLecturaArchivosResultado> LeerAsync(
        BanciCargaArchivosRequest request);
}