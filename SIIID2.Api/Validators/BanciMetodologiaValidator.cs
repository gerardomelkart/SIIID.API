using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public class BanciMetodologiaValidator
{
    private static readonly HashSet<int> FormaAccion = [1, 2, 3];
    private static readonly HashSet<int> ElementoComision = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly HashSet<int> GradoConsumacion = [1, 2];
    private static readonly HashSet<int> Sexo = [1, 2];
    private static readonly HashSet<int> Genero = [1, 2, 3];
    private static readonly HashSet<int> Pob = [0, 1];
    private static readonly HashSet<int> Disc = [0, 1];
    private static readonly HashSet<int> Localizado = [1, 2];
    private static readonly HashSet<int> CondicionVida = [1, 2];
    private static readonly HashSet<int> Voluntaria = [1, 2];
    private static readonly HashSet<int> FueDelito = [1, 2];

    public (List<BanciCargaValidacionError> Errores, List<BanciCargaValidacionError> Advertencias) Validar(BanciLecturaArchivosResultado lectura)
    {
        var errores = new List<BanciCargaValidacionError>();
        var advertencias = new List<BanciCargaValidacionError>();

        foreach (var fila in lectura.Carpetas) ValidarCarpeta(fila, errores, advertencias);
        foreach (var fila in lectura.Delitos) ValidarDelito(fila, errores, advertencias);
        foreach (var fila in lectura.Victimas) ValidarVictima(fila, errores, advertencias);

        return (errores, advertencias);
    }

    private static void ValidarCarpeta(ArchivoFila fila, List<BanciCargaValidacionError> errores, List<BanciCargaValidacionError> advertencias)
    {
        ValidarTextoObligatorio(fila, "carpetas", "ntra_ci", errores);
        ValidarFechaObligatoria(fila, "carpetas", "fha_de_ini", errores);
        ValidarHoraPendiente(fila, "carpetas", "hra_de_ini", errores, advertencias);

        var ordApreh = ValidarEnteroPendiente(fila, "carpetas", "ord_apreh", errores, advertencias);
        var fgran = ValidarEnteroPendiente(fila, "carpetas", "fgran", errores, advertencias);
        var ctaon = ValidarEnteroPendiente(fila, "carpetas", "ctaon", errores, advertencias);
        var tdVAp = ValidarEnteroPendiente(fila, "carpetas", "td_v_ap", errores, advertencias);
        var procAbrev = ValidarEnteroPendiente(fila, "carpetas", "proc_abrev", errores, advertencias);
        var jucOral = ValidarEnteroPendiente(fila, "carpetas", "juc_oral", errores, advertencias);
        var tdSenCon = ValidarEnteroPendiente(fila, "carpetas", "td_sen_con", errores, advertencias);

        ValidarEnteroPendiente(fila, "carpetas", "no_ejer_acc_pnal", errores, advertencias);
        ValidarEnteroPendiente(fila, "carpetas", "otra", errores, advertencias);
        ValidarEnteroOpcional(fila, "carpetas", "dic", errores);

        if (ordApreh.HasValue && fgran.HasValue && ctaon.HasValue && tdVAp.HasValue && tdVAp.Value != ordApreh.Value + fgran.Value + ctaon.Value)
        {
            AgregarError(errores, "carpetas", fila, "td_v_ap", "BANCI_TD_V_AP_INCONSISTENTE", $"TD_V_AP debe ser igual a ORD_APREH + FGRAN + CTAON ({ordApreh.Value + fgran.Value + ctaon.Value}).");
        }

        if (procAbrev.HasValue && jucOral.HasValue && tdSenCon.HasValue && tdSenCon.Value != procAbrev.Value + jucOral.Value)
        {
            AgregarError(errores, "carpetas", fila, "td_sen_con", "BANCI_TD_SEN_CON_INCONSISTENTE", $"TD_SEN_CON debe ser igual a PROC_ABREV + JUC_ORAL ({procAbrev.Value + jucOral.Value}).");
        }
    }

    private static void ValidarDelito(ArchivoFila fila, List<BanciCargaValidacionError> errores, List<BanciCargaValidacionError> advertencias)
    {
        ValidarTextoPendiente(fila, "delitos", "dto", advertencias);
        ValidarTextoPendiente(fila, "delitos", "moda_dto", advertencias);

        ValidarCatalogoPendiente(fila, "delitos", "forma_acc", FormaAccion, errores, advertencias);
        ValidarFechaPendiente(fila, "delitos", "fha_de_hchos", errores, advertencias);
        ValidarHoraPendiente(fila, "delitos", "hra_de_hchos", errores, advertencias);
        ValidarCatalogoPendiente(fila, "delitos", "emto_com_dto", ElementoComision, errores, advertencias);
        ValidarCatalogoPendiente(fila, "delitos", "grdo_cons", GradoConsumacion, errores, advertencias);

        ValidarTextoPendiente(fila, "delitos", "nom_ent_hchos", advertencias);
        ValidarEnteroPendiente(fila, "delitos", "id_ent_hchos", errores, advertencias);
        ValidarTextoPendiente(fila, "delitos", "nom_mun_hchos", advertencias);
        ValidarEnteroPendiente(fila, "delitos", "id_mun_hchos", errores, advertencias);

        ValidarEnteroOpcional(fila, "delitos", "id_loc_hchos", errores);

        ValidarTextoPendiente(fila, "delitos", "nom_col_hchos", advertencias);
        ValidarEnteroPendiente(fila, "delitos", "id_col_hchos", errores, advertencias);
        ValidarEnteroPendiente(fila, "delitos", "cp", errores, advertencias);

        ValidarCoordenada(fila, "coord_x", -118m, -86m, errores, advertencias);
        ValidarCoordenada(fila, "coord_y", 13m, 34m, errores, advertencias);

        ValidarTextoPendiente(fila, "delitos", "dom_hchos", advertencias);
    }

    private static void ValidarVictima(ArchivoFila fila, List<BanciCargaValidacionError> errores, List<BanciCargaValidacionError> advertencias)
    {
        ValidarCatalogoPendiente(fila, "victimas", "sexo", Sexo, errores, advertencias);
        ValidarCatalogoPendiente(fila, "victimas", "genero", Genero, errores, advertencias);
        ValidarCatalogoPendiente(fila, "victimas", "pob", Pob, errores, advertencias);
        ValidarCatalogoPendiente(fila, "victimas", "disc", Disc, errores, advertencias);

        ValidarFechaOpcional(fila, "victimas", "fha_nac", errores);
        ValidarEnteroPendiente(fila, "victimas", "edad", errores, advertencias);

        ValidarTextoPendiente(fila, "victimas", "nacional", advertencias);

        var noBanci = Valor(fila, "no_banci");

        if (!EsSinInformacion(noBanci) && (!long.TryParse(noBanci, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeroBanci) || numeroBanci <= 0))
        {
            AgregarError(errores, "victimas", fila, "no_banci", "BANCI_NO_BANCI_INVALIDO", "No_BANCI debe ser un número positivo cuando venga informado.");
        }

        ValidarTextoPendiente(fila, "victimas", "folio_fotovolante", advertencias);
        ValidarTextoPendiente(fila, "victimas", "folio_rnpdno", advertencias);
        ValidarTextoPendiente(fila, "victimas", "pro_apellido", advertencias);
        ValidarTextoPendiente(fila, "victimas", "sdo_apellido", advertencias);
        ValidarTextoPendiente(fila, "victimas", "nomb", advertencias);
        ValidarTextoPendiente(fila, "victimas", "entidad_nacimiento", advertencias);
        ValidarTextoPendiente(fila, "victimas", "estado_migratorio", advertencias);
        ValidarTextoPendiente(fila, "victimas", "curp", advertencias);
        ValidarTextoPendiente(fila, "victimas", "rfc", advertencias);

        ValidarFechaOpcional(fila, "victimas", "fecha_ultimo_contacto", errores);
        ValidarHoraOpcional(fila, "victimas", "hora_ultimo_contacto", errores);

        ValidarTextoPendiente(fila, "victimas", "entidad_visto", advertencias);
        ValidarTextoPendiente(fila, "victimas", "municipio_visto", advertencias);
        ValidarTextoPendiente(fila, "victimas", "lugar_ultimo_contacto", advertencias);
        ValidarTextoPendiente(fila, "victimas", "senas_tatuaje_datos_identificacion", advertencias);

        var localizado = ValidarCatalogoPendiente(fila, "victimas", "localizado_o_no_localizado", Localizado, errores, advertencias);
        var condicionVida = ValidarCatalogoOpcional(fila, "victimas", "con_o_sin_vida", CondicionVida, errores);
        var fechaLocalizacion = Valor(fila, "fecha_localizacion");

        if (localizado == 2)
        {
            if (!condicionVida.HasValue) AgregarAdvertencia(advertencias, "victimas", fila, "con_o_sin_vida", "BANCI_CONDICION_VIDA_PENDIENTE", "La persona está marcada como localizada y falta indicar si fue localizada con vida o sin vida.");
            if (EsSinInformacion(fechaLocalizacion)) AgregarAdvertencia(advertencias, "victimas", fila, "fecha_localizacion", "BANCI_FECHA_LOCALIZACION_PENDIENTE", "La persona está marcada como localizada y falta FECHA_LOCALIZACION.");
            else ValidarFechaOpcional(fila, "victimas", "fecha_localizacion", errores);
        }
        else if (localizado == 1)
        {
            if (condicionVida.HasValue) AgregarAdvertencia(advertencias, "victimas", fila, "con_o_sin_vida", "BANCI_CONDICION_VIDA_NO_APLICA", "La persona está marcada como no localizada pero CON_O_SIN_VIDA contiene información.");
            if (!EsSinInformacion(fechaLocalizacion)) AgregarAdvertencia(advertencias, "victimas", fila, "fecha_localizacion", "BANCI_FECHA_LOCALIZACION_NO_APLICA", "La persona está marcada como no localizada pero FECHA_LOCALIZACION contiene información.");
        }
        else if (!EsSinInformacion(fechaLocalizacion))
        {
            ValidarFechaOpcional(fila, "victimas", "fecha_localizacion", errores);
        }

        var voluntaria = ValidarCatalogoPendiente(fila, "victimas", "voluntaria", Voluntaria, errores, advertencias);
        var fueDelito = ValidarCatalogoOpcional(fila, "victimas", "fue_delito", FueDelito, errores);

        if (voluntaria == 2 && !fueDelito.HasValue) AgregarAdvertencia(advertencias, "victimas", fila, "fue_delito", "BANCI_FUE_DELITO_PENDIENTE", "La ausencia está marcada como no voluntaria y falta indicar si la persona fue víctima de un delito.");
        if (fueDelito == 1 && EsSinInformacion(Valor(fila, "delito"))) AgregarAdvertencia(advertencias, "victimas", fila, "delito", "BANCI_DELITO_VICTIMA_PENDIENTE", "Se indicó que la persona fue víctima de un delito y falta especificar DELITO.");
    }

    private static void ValidarTextoObligatorio(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> errores)
    {
        if (EsSinInformacion(Valor(fila, campo))) AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_REQUERIDO", $"El campo {campo} es obligatorio.");
    }

    private static void ValidarTextoPendiente(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> advertencias)
    {
        if (EsSinInformacion(Valor(fila, campo))) AgregarAdvertencia(advertencias, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_PENDIENTE", $"El campo {campo} está pendiente de información.");
    }

    private static int? ValidarEnteroPendiente(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> errores, List<BanciCargaValidacionError> advertencias)
    {
        var valor = Valor(fila, campo);

        if (EsSinInformacion(valor))
        {
            AgregarAdvertencia(advertencias, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_PENDIENTE", $"El campo {campo} está pendiente de información.");
            return null;
        }

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero) || numero < 0)
        {
            AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_INVALIDO", $"El campo {campo} debe contener un número entero mayor o igual a cero.");
            return null;
        }

        return numero;
    }

    private static int? ValidarEnteroOpcional(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> errores)
    {
        var valor = Valor(fila, campo);
        if (EsSinInformacion(valor)) return null;

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero) || numero < 0)
        {
            AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_INVALIDO", $"El campo {campo} debe contener un número entero mayor o igual a cero.");
            return null;
        }

        return numero;
    }

    private static int? ValidarCatalogoPendiente(ArchivoFila fila, string archivo, string campo, HashSet<int> catalogo, List<BanciCargaValidacionError> errores, List<BanciCargaValidacionError> advertencias)
    {
        var valor = Valor(fila, campo);

        if (EsSinInformacion(valor))
        {
            AgregarAdvertencia(advertencias, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_PENDIENTE", $"El campo {campo} está pendiente de información.");
            return null;
        }

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave) || !catalogo.Contains(clave))
        {
            AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_CATALOGO_INVALIDO", $"El valor \"{valor}\" no pertenece al catálogo permitido para {campo}.");
            return null;
        }

        return clave;
    }

    private static int? ValidarCatalogoOpcional(ArchivoFila fila, string archivo, string campo, HashSet<int> catalogo, List<BanciCargaValidacionError> errores)
    {
        var valor = Valor(fila, campo);
        if (EsSinInformacion(valor)) return null;

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave) || !catalogo.Contains(clave))
        {
            AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_CATALOGO_INVALIDO", $"El valor \"{valor}\" no pertenece al catálogo permitido para {campo}.");
            return null;
        }

        return clave;
    }

    private static void ValidarFechaObligatoria(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> errores)
    {
        var valor = Valor(fila, campo);

        if (EsSinInformacion(valor))
        {
            AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_REQUERIDO", $"El campo {campo} es obligatorio.");
            return;
        }

        if (!IntentarFecha(valor, out _)) AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_FECHA_INVALIDA", $"El campo {campo} no contiene una fecha válida.");
    }

    private static void ValidarFechaPendiente(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> errores, List<BanciCargaValidacionError> advertencias)
    {
        var valor = Valor(fila, campo);

        if (EsSinInformacion(valor))
        {
            AgregarAdvertencia(advertencias, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_PENDIENTE", $"El campo {campo} está pendiente de información.");
            return;
        }

        if (!IntentarFecha(valor, out _)) AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_FECHA_INVALIDA", $"El campo {campo} no contiene una fecha válida.");
    }

    private static void ValidarFechaOpcional(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> errores)
    {
        var valor = Valor(fila, campo);
        if (EsSinInformacion(valor)) return;
        if (!IntentarFecha(valor, out _)) AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_FECHA_INVALIDA", $"El campo {campo} no contiene una fecha válida.");
    }

    private static void ValidarHoraPendiente(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> errores, List<BanciCargaValidacionError> advertencias)
    {
        var valor = Valor(fila, campo);

        if (EsSinInformacion(valor))
        {
            AgregarAdvertencia(advertencias, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_PENDIENTE", $"El campo {campo} está pendiente de información.");
            return;
        }

        if (!IntentarHora(valor, out _)) AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_HORA_INVALIDA", $"El campo {campo} no contiene una hora válida.");
    }

    private static void ValidarHoraOpcional(ArchivoFila fila, string archivo, string campo, List<BanciCargaValidacionError> errores)
    {
        var valor = Valor(fila, campo);
        if (EsSinInformacion(valor)) return;
        if (!IntentarHora(valor, out _)) AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_HORA_INVALIDA", $"El campo {campo} no contiene una hora válida.");
    }

    private static void ValidarCoordenada(ArchivoFila fila, string campo, decimal minimo, decimal maximo, List<BanciCargaValidacionError> errores, List<BanciCargaValidacionError> advertencias)
    {
        var valor = Valor(fila, campo);

        if (EsSinInformacion(valor))
        {
            AgregarAdvertencia(advertencias, "delitos", fila, campo, $"BANCI_{campo.ToUpperInvariant()}_PENDIENTE", $"El campo {campo} está pendiente de información.");
            return;
        }

        valor = valor.Replace(',', '.');

        if (!decimal.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var coordenada))
        {
            AgregarError(errores, "delitos", fila, campo, $"BANCI_{campo.ToUpperInvariant()}_INVALIDA", $"El campo {campo} debe contener una coordenada numérica válida.");
            return;
        }

        if (coordenada < minimo || coordenada > maximo) AgregarError(errores, "delitos", fila, campo, $"BANCI_{campo.ToUpperInvariant()}_FUERA_RANGO", $"El campo {campo} debe encontrarse entre {minimo} y {maximo}.");
    }

    private static bool IntentarFecha(string valor, out DateTime fecha)
    {
        fecha = default;
        var formatos = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd" };

        foreach (var formato in formatos)
        {
            if (DateTime.TryParseExact(valor.Trim(), formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha)) return true;
        }

        return DateTime.TryParse(valor, new CultureInfo("es-MX"), DateTimeStyles.None, out fecha);
    }

    private static bool IntentarHora(string valor, out TimeSpan hora)
    {
        hora = default;
        valor = valor.Trim();

        if (TimeSpan.TryParse(valor, CultureInfo.InvariantCulture, out hora) && hora >= TimeSpan.Zero && hora < TimeSpan.FromDays(1)) return true;

        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numero) && numero >= 0 && numero < 1)
        {
            hora = TimeSpan.FromDays(numero);
            return true;
        }

        return false;
    }

    private static bool EsSinInformacion(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return true;

        var normalizado = valor.Trim().ToUpperInvariant();

        return normalizado is "NO DISPONIBLE" or "N/D" or "ND";
    }

    private static string Valor(ArchivoFila fila, string campo) => fila.Columnas.TryGetValue(campo, out var valor) ? valor?.Trim() ?? string.Empty : string.Empty;

    private static void AgregarError(List<BanciCargaValidacionError> errores, string archivo, ArchivoFila fila, string campo, string codigo, string mensaje)
    {
        errores.Add(new BanciCargaValidacionError { Archivo = archivo, NumeroFila = fila.NumeroFila, Campo = campo, Valor = Valor(fila, campo), Codigo = codigo, Mensaje = mensaje });
    }

    private static void AgregarAdvertencia(List<BanciCargaValidacionError> advertencias, string archivo, ArchivoFila fila, string campo, string codigo, string mensaje)
    {
        advertencias.Add(new BanciCargaValidacionError { Archivo = archivo, NumeroFila = fila.NumeroFila, Campo = campo, Valor = Valor(fila, campo), Codigo = codigo, Mensaje = mensaje });
    }
}