using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IRenapoCurpService
{
    Task<RenapoConsultaResultado> ConsultarAsync(string curp, CancellationToken cancellationToken = default);
}