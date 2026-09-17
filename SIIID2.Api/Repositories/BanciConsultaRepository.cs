using System.Globalization;
using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class BanciConsultaRepository : IBanciConsultaRepository
{
    private readonly IDbConnectionFactory _factory;
    public BanciConsultaRepository(IDbConnectionFactory factory) { _factory = factory; }

    public async Task<BanciConsultaOpciones> ObtenerOpcionesAsync(int? idEntidadAlcance)
    {
        using var connection = _factory.CrearConexion();
        using var resultados = await connection.QueryMultipleAsync("""
            SELECT DISTINCT YEAR(c.fha_de_ini)
            FROM dbo.banci_carpeta_investigacion c
            WHERE c.activo = 1 AND (@Alcance IS NULL OR c.id_entidad_federativa = @Alcance)
            ORDER BY 1 DESC;

            SELECT CONVERT(int, e.id_entidad_federativa) AS IdEntidadFederativa, e.nombre AS Nombre
            FROM dbo.catalogo_entidad_federativa e
            WHERE e.id_entidad_federativa BETWEEN 1 AND 32
              AND (@Alcance IS NULL OR e.id_entidad_federativa = @Alcance)
            ORDER BY e.nombre;
            """, new { Alcance = idEntidadAlcance });
        return new BanciConsultaOpciones
        {
            AlcanceNacional = !idEntidadAlcance.HasValue,
            Anios = (await resultados.ReadAsync<int>()).ToList(),
            Entidades = (await resultados.ReadAsync<BanciConsultaEntidad>()).ToList()
        };
    }

    public async Task<BanciConsultaResultado> ConsultarAsync(BanciConsultaFiltro filtro, int? idEntidadAlcance)
    {
        var desde = new DateTime(filtro.Anio, filtro.Mes ?? 1, 1);
        var hasta = filtro.Mes.HasValue ? desde.AddMonths(1) : desde.AddYears(1);
        var busqueda = filtro.Busqueda?.Trim();
        // LIKE literal: los comodines escritos por el usuario no amplían la consulta.
        var patron = string.IsNullOrEmpty(busqueda) ? null : "%" + busqueda
            .Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        using var connection = _factory.CrearConexion();
        using var resultados = await connection.QueryMultipleAsync("""
            SELECT c.id_banci_carpeta_investigacion, c.id_entidad_federativa, c.id_ci, c.ntra_ci, c.fha_de_ini
            INTO #BanciConsultaFiltrada
            FROM dbo.banci_carpeta_investigacion c
            WHERE c.activo = 1
              AND (@Alcance IS NULL OR c.id_entidad_federativa = @Alcance)
              AND (@Entidad IS NULL OR c.id_entidad_federativa = @Entidad)
              AND c.fha_de_ini >= @Desde AND c.fha_de_ini < @Hasta
              AND (@Busqueda IS NULL OR c.id_ci LIKE @Busqueda ESCAPE N'~' OR c.ntra_ci LIKE @Busqueda ESCAPE N'~')
            OPTION (RECOMPILE);

            CREATE UNIQUE CLUSTERED INDEX IX_BanciConsultaFiltrada ON #BanciConsultaFiltrada(id_banci_carpeta_investigacion);

            SELECT
              (SELECT COUNT_BIG(*) FROM #BanciConsultaFiltrada) AS TotalCarpetas,
              (SELECT COUNT_BIG(*) FROM dbo.banci_delito d
               JOIN #BanciConsultaFiltrada c ON c.id_banci_carpeta_investigacion = d.id_banci_carpeta_investigacion
               WHERE d.activo = 1) AS TotalDelitos,
              (SELECT COUNT_BIG(*) FROM dbo.banci_victima v
               JOIN dbo.banci_delito d ON d.id_banci_delito = v.id_banci_delito AND d.activo = 1
               JOIN #BanciConsultaFiltrada c ON c.id_banci_carpeta_investigacion = d.id_banci_carpeta_investigacion
               WHERE v.activo = 1) AS TotalVictimas;

            SELECT * INTO #BanciConsultaPagina FROM #BanciConsultaFiltrada
            ORDER BY fha_de_ini DESC, id_banci_carpeta_investigacion DESC
            OFFSET @Omitir ROWS FETCH NEXT @Tamano ROWS ONLY;

            SELECT p.id_banci_carpeta_investigacion AS IdBanciCarpetaInvestigacion,
                   CONVERT(int, p.id_entidad_federativa) AS IdEntidadFederativa,
                   e.nombre AS Entidad, p.id_ci AS IdCi, p.ntra_ci AS NtraCi, p.fha_de_ini AS FechaInicio,
                   (SELECT COUNT_BIG(*) FROM dbo.banci_delito d
                    WHERE d.id_banci_carpeta_investigacion = p.id_banci_carpeta_investigacion AND d.activo = 1) AS TotalDelitos,
                   (SELECT COUNT_BIG(*) FROM dbo.banci_victima v
                    JOIN dbo.banci_delito d ON d.id_banci_delito = v.id_banci_delito AND d.activo = 1
                    WHERE d.id_banci_carpeta_investigacion = p.id_banci_carpeta_investigacion AND v.activo = 1) AS TotalVictimas
            FROM #BanciConsultaPagina p
            JOIN dbo.catalogo_entidad_federativa e ON e.id_entidad_federativa = p.id_entidad_federativa
            ORDER BY p.fha_de_ini DESC, p.id_banci_carpeta_investigacion DESC;

            DROP TABLE #BanciConsultaPagina;
            DROP TABLE #BanciConsultaFiltrada;
            """, new
        {
            Alcance = idEntidadAlcance,
            Entidad = filtro.IdEntidadFederativa,
            Desde = desde,
            Hasta = hasta,
            Busqueda = patron,
            Omitir = (filtro.Pagina - 1) * filtro.TamanoPagina,
            Tamano = filtro.TamanoPagina
        }, commandTimeout: 120);
        var respuesta = await resultados.ReadSingleAsync<BanciConsultaResultado>();
        respuesta.Pagina = filtro.Pagina;
        respuesta.TamanoPagina = filtro.TamanoPagina;
        respuesta.Carpetas = (await resultados.ReadAsync<BanciConsultaCarpeta>()).ToList();
        return respuesta;
    }

    public async Task<BanciConsultaDetalle?> ObtenerDetalleAsync(long idCarpeta, int? idEntidadAlcance)
    {
        using var connection = _factory.CrearConexion();
        // Los tres resultados aplican el alcance a la entidad propietaria de la carpeta,
        // no a la entidad de hechos ni al usuario que realizó la última modificación.
        using var resultados = await connection.QueryMultipleAsync("""
            SELECT e.nombre AS [Entidad], c.id_ci AS [ID_CI], c.ntra_ci AS [NTRA_CI],
                   CONVERT(nvarchar(10), c.fha_de_ini, 23) AS [Fecha de inicio],
                   CONVERT(nvarchar(8), c.hra_de_ini, 108) AS [Hora de inicio],
                   c.rmen_de_hchos AS [Resumen de hechos], c.ord_apreh AS [Órdenes de aprehensión],
                   c.fgran AS [FGRAN], c.ctaon AS [CTAON], c.td_v_ap AS [TD_V_AP],
                   c.proc_abrev AS [Procedimiento abreviado], c.juc_oral AS [Juicio oral],
                   c.td_sen_con AS [TD_SEN_CON], c.no_ejer_acc_pnal AS [No ejercicio de acción penal],
                   c.otra AS [Otra], c.dic AS [DIC]
            FROM dbo.banci_carpeta_investigacion c
            JOIN dbo.catalogo_entidad_federativa e ON e.id_entidad_federativa = c.id_entidad_federativa
            WHERE c.id_banci_carpeta_investigacion = @IdCarpeta AND c.activo = 1
              AND (@Alcance IS NULL OR c.id_entidad_federativa = @Alcance);

            SELECT d.id_banci_delito AS _Id, d.id_delito AS [ID_DELITO], d.dto AS [Delito],
                   d.moda_dto AS [Modalidad], d.forma_acc AS [Forma de acción (clave)],
                   CONVERT(nvarchar(10), d.fha_de_hchos, 23) AS [Fecha de hechos],
                   CONVERT(nvarchar(8), d.hra_de_hchos, 108) AS [Hora de hechos],
                   d.emto_com_dto AS [Instrumento de comisión (clave)], d.grdo_cons AS [Grado de consumación (clave)],
                   d.clasf_de_dto AS [Clasificación], d.nom_ent_hchos AS [Entidad de hechos],
                   d.id_ent_hchos AS [Entidad de hechos (clave)], d.nom_mun_hchos AS [Municipio de hechos],
                   d.id_mun_hchos AS [Municipio de hechos (clave)], d.nom_loc_hchos AS [Localidad de hechos],
                   d.id_loc_hchos AS [Localidad de hechos (clave)], d.nom_col_hchos AS [Colonia de hechos],
                   d.id_col_hchos AS [Colonia de hechos (clave)], d.cp AS [Código postal],
                   d.coord_x AS [Coordenada X], d.coord_y AS [Coordenada Y], d.dom_hchos AS [Domicilio de hechos]
            FROM dbo.banci_delito d
            JOIN dbo.banci_carpeta_investigacion c ON c.id_banci_carpeta_investigacion = d.id_banci_carpeta_investigacion
            WHERE c.id_banci_carpeta_investigacion = @IdCarpeta AND c.activo = 1 AND d.activo = 1
              AND (@Alcance IS NULL OR c.id_entidad_federativa = @Alcance)
            ORDER BY d.id_banci_delito;

            SELECT v.id_banci_delito AS _IdDelito, v.id_vicf AS [ID_VICF],
                   v.id_tv AS [Tipo de víctima (clave)], v.id_tpm AS [Tipo de persona moral (clave)],
                   v.sexo AS [Sexo (clave)], v.genero AS [Género (clave)], v.pob AS [POB], v.disc AS [DISC],
                   CONVERT(nvarchar(10), v.fha_nac, 23) AS [Fecha de nacimiento], v.edad AS [Edad],
                   v.nacional AS [Nacionalidad (clave)], v.no_banci AS [No_BANCI],
                   v.folio_fotovolante AS [Folio fotovolante], v.folio_rnpdno AS [Folio RNPDNO],
                   v.pro_apellido AS [Primer apellido], v.sdo_apellido AS [Segundo apellido], v.nomb AS [Nombre],
                   v.entidad_nacimiento AS [Entidad de nacimiento], v.estado_migratorio AS [Estado migratorio],
                   v.curp AS [CURP], v.rfc AS [RFC],
                   CONVERT(nvarchar(10), v.fecha_ultimo_contacto, 23) AS [Fecha de último contacto],
                   CONVERT(nvarchar(8), v.hora_ultimo_contacto, 108) AS [Hora de último contacto],
                   v.entidad_visto AS [Entidad del último contacto], v.municipio_visto AS [Municipio del último contacto],
                   v.lugar_ultimo_contacto AS [Lugar del último contacto],
                   v.senas_tatuaje_datos_identificacion AS [Señas, tatuajes y datos de identificación],
                   v.localizado_o_no_localizado AS [Localización (clave)], v.con_o_sin_vida AS [Condición de vida (clave)],
                   CONVERT(nvarchar(10), v.fecha_localizacion, 23) AS [Fecha de localización],
                   v.voluntaria AS [Voluntaria (clave)], v.fue_delito AS [Fue delito (clave)],
                   v.delito AS [Delito relacionado], v.obs AS [Observaciones]
            FROM dbo.banci_victima v
            JOIN dbo.banci_delito d ON d.id_banci_delito = v.id_banci_delito
            JOIN dbo.banci_carpeta_investigacion c ON c.id_banci_carpeta_investigacion = d.id_banci_carpeta_investigacion
            WHERE c.id_banci_carpeta_investigacion = @IdCarpeta AND c.activo = 1 AND d.activo = 1 AND v.activo = 1
              AND (@Alcance IS NULL OR c.id_entidad_federativa = @Alcance)
            ORDER BY v.id_banci_delito, v.id_banci_victima;
            """, new { IdCarpeta = idCarpeta, Alcance = idEntidadAlcance }, commandTimeout: 120);

        var carpeta = (await resultados.ReadAsync()).SingleOrDefault();
        if (carpeta == null) return null;
        var detalle = new BanciConsultaDetalle { Carpeta = Campos((IDictionary<string, object>)carpeta) };
        var delitos = new Dictionary<long, BanciConsultaDelito>();
        foreach (IDictionary<string, object> fila in await resultados.ReadAsync())
        {
            var delito = new BanciConsultaDelito { Id = Convert.ToInt64(fila["_Id"]), Campos = Campos(fila) };
            delitos.Add(delito.Id, delito);
            detalle.Delitos.Add(delito);
        }
        foreach (IDictionary<string, object> fila in await resultados.ReadAsync())
        {
            if (delitos.TryGetValue(Convert.ToInt64(fila["_IdDelito"]), out var delito))
                delito.Victimas.Add(Campos(fila));
        }
        return detalle;
    }

    private static List<BanciCampoConsulta> Campos(IDictionary<string, object> fila) => fila
        .Where(x => !x.Key.StartsWith('_'))
        .Select(x => new BanciCampoConsulta
        {
            Nombre = x.Key,
            Valor = x.Value is null or DBNull ? null : Convert.ToString(x.Value, CultureInfo.InvariantCulture)
        }).ToList();
}
