using SIIID2.Api.Data;

namespace SIIID2.Api.Services;

public static class CodigoPostalCatalogoHelper
{
    public sealed class Resultado
    {
        public long IdCarga { get; set; }
        public int CodigosReactivados { get; set; }
        public int CodigosInsertados { get; set; }
        public int CodigosSinPlantilla { get; set; }
        public int DelitosRemapeados { get; set; }
    }

    public static Task<bool> EsSuperUsuarioAsync(IDbConnectionFactory dbConnectionFactory, int idUsuario) => Task.FromResult(false);

    public static Task<Resultado?> AsegurarAsync(IDbConnectionFactory dbConnectionFactory, string codigoReferencia, bool remapearFinal = false, bool permitirConfirmada = false) => Task.FromResult<Resultado?>(null);
}
