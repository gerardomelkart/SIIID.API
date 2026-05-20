using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class CargaRepository : ICargaRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CargaRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<long> CrearCargaAsync(int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError)
    {
        // Crea el intento de carga.
        var sql = @"
            INSERT INTO carga (
                id_usuario_carga,
                codigo_referencia,
                mes_corte,
                anio_corte,
                total_carpetas_investigacion,
                total_delitos,
                total_victimas,
                estado,
                fecha_validacion,
                fecha_expiracion,
                mensaje_error,
                activo
            )
            VALUES (
                @IdUsuarioCarga,
                @CodigoReferencia,
                @MesCorte,
                @AnioCorte,
                @TotalCarpetas,
                @TotalDelitos,
                @TotalVictimas,
                @Estado,
                CURRENT_TIMESTAMP,
                DATE_ADD(CURRENT_TIMESTAMP, INTERVAL 48 HOUR),
                @MensajeError,
                1
            );

            SELECT LAST_INSERT_ID();
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var idCarga = await connection.ExecuteScalarAsync<long>(sql, new
        {
            IdUsuarioCarga = idUsuarioCarga,
            CodigoReferencia = codigoReferencia,
            MesCorte = mesCorte,
            AnioCorte = anioCorte,
            TotalCarpetas = totalCarpetas,
            TotalDelitos = totalDelitos,
            TotalVictimas = totalVictimas,
            Estado = estado,
            MensajeError = mensajeError
        });

        return idCarga;
    }

    public async Task GuardarTmpCarpetasAsync(long idCarga, List<ArchivoFila> filasCarpetas)
    {
        // Guarda las carpetas leídas en staging.
        var sql = @"
            INSERT INTO carga_tmp_carpeta (
                id_carga,
                numero_fila,
                id_ci,
                ntra_ci,
                fha_de_ini,
                hra_de_ini,
                rmen_de_hchos,
                estado,
                activo
            )
            VALUES (
                @IdCarga,
                @NumeroFila,
                @IdCi,
                @NtraCi,
                @FhaDeIni,
                @HraDeIni,
                @RmenDeHchos,
                'PENDIENTE',
                1
            );
        ";

        var parametros = filasCarpetas.Select(fila => new
        {
            IdCarga = idCarga,
            NumeroFila = fila.NumeroFila,
            IdCi = ObtenerValor(fila, "id_ci"),
            NtraCi = ObtenerValor(fila, "ntra_ci"),
            FhaDeIni = ObtenerValor(fila, "fha_de_ini"),
            HraDeIni = ObtenerValor(fila, "hra_de_ini"),
            RmenDeHchos = ObtenerValor(fila, "rmen_de_hchos")
        });

        using var connection = _dbConnectionFactory.CrearConexion();

        await connection.ExecuteAsync(sql, parametros);
    }

    public async Task GuardarTmpDelitosAsync(long idCarga, List<ArchivoFila> filasDelitos)
    {
        // Guarda los delitos leídos en staging.
        var sql = @"
            INSERT INTO carga_tmp_delito (
                id_carga,
                numero_fila,
                id_ci,
                id_delito,
                dto,
                moda_dto,
                forma_acc,
                fha_de_hchos,
                hra_de_hchos,
                emto_com_dto,
                grdo_cons,
                clasf_de_dto,
                id_ent_hchos,
                id_mun_hchos,
                id_loc_hchos,
                nom_loc_hchos,
                id_col_hchos,
                nom_col_hchos,
                cp,
                coord_x,
                coord_y,
                dom_hchos,
                estado,
                activo
            )
            VALUES (
                @IdCarga,
                @NumeroFila,
                @IdCi,
                @IdDelito,
                @Dto,
                @ModaDto,
                @FormaAcc,
                @FhaDeHchos,
                @HraDeHchos,
                @EmtoComDto,
                @GrdoCons,
                @ClasfDeDto,
                @IdEntHchos,
                @IdMunHchos,
                @IdLocHchos,
                @NomLocHchos,
                @IdColHchos,
                @NomColHchos,
                @Cp,
                @CoordX,
                @CoordY,
                @DomHchos,
                'PENDIENTE',
                1
            );
        ";

        var parametros = filasDelitos.Select(fila => new
        {
            IdCarga = idCarga,
            NumeroFila = fila.NumeroFila,
            IdCi = ObtenerValor(fila, "id_ci"),
            IdDelito = ObtenerValor(fila, "id_delito"),
            Dto = ObtenerValor(fila, "dto"),
            ModaDto = ObtenerValor(fila, "moda_dto"),
            FormaAcc = ObtenerValor(fila, "forma_acc"),
            FhaDeHchos = ObtenerValor(fila, "fha_de_hchos"),
            HraDeHchos = ObtenerValor(fila, "hra_de_hchos"),
            EmtoComDto = ObtenerValor(fila, "emto_com_dto"),
            GrdoCons = ObtenerValor(fila, "grdo_cons"),
            ClasfDeDto = ObtenerValor(fila, "clasf_de_dto"),
            IdEntHchos = ObtenerValor(fila, "id_ent_hchos"),
            IdMunHchos = ObtenerValor(fila, "id_mun_hchos"),
            IdLocHchos = ObtenerValor(fila, "id_loc_hchos"),
            NomLocHchos = ObtenerValor(fila, "nom_loc_hchos"),
            IdColHchos = ObtenerValor(fila, "id_col_hchos"),
            NomColHchos = ObtenerValor(fila, "nom_col_hchos"),
            Cp = ObtenerValor(fila, "cp"),
            CoordX = ObtenerValor(fila, "coord_x"),
            CoordY = ObtenerValor(fila, "coord_y"),
            DomHchos = ObtenerValor(fila, "dom_hchos")
        });

        using var connection = _dbConnectionFactory.CrearConexion();

        await connection.ExecuteAsync(sql, parametros);
    }

    public async Task GuardarTmpVictimasAsync(long idCarga, List<ArchivoFila> filasVictimas)
    {
        // Guarda las víctimas leídas en staging.
        var sql = @"
            INSERT INTO carga_tmp_victima (
                id_carga,
                numero_fila,
                id_ci,
                id_delito,
                id_vicf,
                id_tv,
                id_tpm,
                sexo,
                genero,
                pob,
                disc,
                fha_nac,
                edad,
                nacional,
                estado,
                activo
            )
            VALUES (
                @IdCarga,
                @NumeroFila,
                @IdCi,
                @IdDelito,
                @IdVicf,
                @IdTv,
                @IdTpm,
                @Sexo,
                @Genero,
                @Pob,
                @Disc,
                @FhaNac,
                @Edad,
                @Nacional,
                'PENDIENTE',
                1
            );
        ";

        var parametros = filasVictimas.Select(fila => new
        {
            IdCarga = idCarga,
            NumeroFila = fila.NumeroFila,
            IdCi = ObtenerValor(fila, "id_ci"),
            IdDelito = ObtenerValor(fila, "id_delito"),
            IdVicf = ObtenerValor(fila, "id_vicf"),
            IdTv = ObtenerValor(fila, "id_tv"),
            IdTpm = ObtenerValor(fila, "id_tpm"),
            Sexo = ObtenerValor(fila, "sexo"),
            Genero = ObtenerValor(fila, "genero"),
            Pob = ObtenerValor(fila, "pob"),
            Disc = ObtenerValor(fila, "disc"),
            FhaNac = ObtenerValor(fila, "fha_nac"),
            Edad = ObtenerValor(fila, "edad"),
            Nacional = ObtenerValor(fila, "nacional")
        });

        using var connection = _dbConnectionFactory.CrearConexion();

        await connection.ExecuteAsync(sql, parametros);
    }

    public async Task ActualizarEstadoCargaAsync(long idCarga, string estado, string? mensajeError)
    {
        // Actualiza el estado del intento de carga.
        var sql = @"
            UPDATE carga
            SET estado = @Estado,
                mensaje_error = @MensajeError
            WHERE id_carga = @IdCarga;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        await connection.ExecuteAsync(sql, new
        {
            IdCarga = idCarga,
            Estado = estado,
            MensajeError = mensajeError
        });
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }
}