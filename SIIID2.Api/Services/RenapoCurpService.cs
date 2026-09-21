using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public sealed class RenapoCurpService : IRenapoCurpService
{
    private const string NamespaceSoap = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string NamespaceServicio = "http://services.wserv.ecurp.dgti.segob.gob.mx";
    private const string NamespaceDatos = "http://services.wserv.ecurp.dgti.segob.gob.mx/xsd";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RenapoCurpService> _logger;
    private readonly IHostEnvironment _environment;

    public RenapoCurpService(HttpClient httpClient, IConfiguration configuration, IHostEnvironment environment, ILogger<RenapoCurpService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<RenapoConsultaResultado> ConsultarAsync(string curp, CancellationToken cancellationToken = default)
    {
        var ambiente = _environment.IsDevelopment() ? "Development" : "Production";
        var endpoint = _configuration[$"Renapo:Ambientes:{ambiente}:Endpoint"];
        var usuario = _configuration["Renapo:Usuario"];
        var password = _configuration["Renapo:Password"];
        var direccionIp = _configuration[$"Renapo:Ambientes:{ambiente}:DireccionIp"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(direccionIp) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogError("La configuración de RENAPO está incompleta o es incorrecta.");
            return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
        }

        curp = curp.Trim().ToUpperInvariant();

        try
        {
            using var request = CrearPeticion(uri, "urn:consultarPorCurp", CrearConsulta(curp, usuario, password, direccionIp));
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogError("RENAPO rechazó la autorización. HTTP {CodigoHttp}.", (int)response.StatusCode);
                return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
            }

            if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                _logger.LogWarning("RENAPO no está disponible. HTTP {CodigoHttp}.", (int)response.StatusCode);
                return new RenapoConsultaResultado(EstadoConsultaRenapo.NoDisponible);
            }

            var contenido = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                if (ContieneSoapFault(contenido))
                {
                    _logger.LogError("RENAPO devolvió un SOAP Fault.");
                    return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
                }

                _logger.LogWarning("RENAPO devolvió HTTP 500 sin SOAP Fault.");
                return new RenapoConsultaResultado(EstadoConsultaRenapo.NoDisponible);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("RENAPO devolvió HTTP {CodigoHttp}.", (int)response.StatusCode);
                return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
            }

            var soap = LeerXmlSeguro(contenido);

            if (soap.Descendants().Any(x => x.Name.LocalName == "Fault"))
            {
                _logger.LogError("RENAPO devolvió un SOAP Fault con HTTP 200.");
                return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
            }

            var retorno = soap.Descendants().FirstOrDefault(x => x.Name.LocalName == "return")?.Value;

            if (string.IsNullOrWhiteSpace(retorno))
            {
                _logger.LogError("La respuesta de RENAPO no contiene el elemento return.");
                return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
            }

            var raiz = LeerXmlSeguro(retorno).Root;

            if (raiz == null || raiz.Name.LocalName != "CURPStruct")
            {
                _logger.LogError("RENAPO devolvió un resultado con estructura desconocida.");
                return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
            }

            var statusOper = (string?)raiz.Attribute("statusOper");
            var tipoError = (string?)raiz.Attribute("TipoError");
            var codigoError = (string?)raiz.Attribute("CodigoError");
            var sessionId = (string?)raiz.Attribute("SessionID");
            var curpDevuelta = raiz.Elements().FirstOrDefault(x => x.Name.LocalName == "CURP")?.Value.Trim();

            if (!string.IsNullOrWhiteSpace(sessionId)) await ConfirmarAsync(uri, sessionId, cancellationToken);

            if (string.Equals(statusOper, "EXITOSO", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(curpDevuelta, curp, StringComparison.OrdinalIgnoreCase)) return new RenapoConsultaResultado(EstadoConsultaRenapo.Encontrada);

                _logger.LogError("RENAPO informó una operación exitosa, pero la CURP devuelta no coincide.");
                return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
            }

            if (tipoError == "01" && codigoError == "06") return new RenapoConsultaResultado(EstadoConsultaRenapo.NoEncontrada, tipoError, codigoError);
            if (tipoError == "01" && codigoError == "09") return new RenapoConsultaResultado(EstadoConsultaRenapo.CurpInvalida, tipoError, codigoError);

            _logger.LogError("RENAPO respondió con un resultado no reconocido. TipoError={TipoError}, CodigoError={CodigoError}.", tipoError, codigoError);
            return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion, tipoError, codigoError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Se agotó el tiempo de espera de RENAPO.");
            return new RenapoConsultaResultado(EstadoConsultaRenapo.NoDisponible);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("No fue posible conectar con RENAPO. Tipo de error: {TipoError}.", ex.GetType().Name);
            return new RenapoConsultaResultado(EstadoConsultaRenapo.NoDisponible);
        }
        catch (XmlException ex)
        {
            _logger.LogError("RENAPO devolvió un XML no válido. Tipo de error: {TipoError}.", ex.GetType().Name);
            return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error inesperado en el cliente RENAPO. Tipo de error: {TipoError}.", ex.GetType().Name);
            return new RenapoConsultaResultado(EstadoConsultaRenapo.ErrorValidacion);
        }
    }

    private async Task ConfirmarAsync(Uri endpoint, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = CrearPeticion(endpoint, "urn:getConfirm", CrearConfirmacion(sessionId));
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode) _logger.LogWarning("RENAPO no aceptó getConfirm. HTTP {CodigoHttp}.", (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("No fue posible completar getConfirm. Tipo de error: {TipoError}.", ex.GetType().Name);
        }
    }

    private static HttpRequestMessage CrearPeticion(Uri endpoint, string soapAction, XDocument xml)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");
        request.Content = new StringContent(xml.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        return request;
    }

    private static XDocument CrearConsulta(string curp, string usuario, string password, string direccionIp)
    {
        XNamespace soap = NamespaceSoap;
        XNamespace servicio = NamespaceServicio;
        XNamespace datos = NamespaceDatos;

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soap),
                new XAttribute(XNamespace.Xmlns + "ser", servicio),
                new XAttribute(XNamespace.Xmlns + "curp", datos),
                new XElement(soap + "Header"),
                new XElement(soap + "Body",
                    new XElement(servicio + "consultarPorCurp",
                        new XElement(servicio + "datos",
                            new XElement(datos + "cveCurp", curp),
                            new XElement(datos + "direccionIp", direccionIp),
                            new XElement(datos + "password", password),
                            new XElement(datos + "tipoTransaccion", 5),
                            new XElement(datos + "usuario", usuario))))));
    }

    private static XDocument CrearConfirmacion(string sessionId)
    {
        XNamespace soap = NamespaceSoap;
        XNamespace servicio = NamespaceServicio;

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soap),
                new XAttribute(XNamespace.Xmlns + "ser", servicio),
                new XElement(soap + "Header"),
                new XElement(soap + "Body",
                    new XElement(servicio + "getConfirm",
                        new XElement(servicio + "sessionID", sessionId),
                        new XElement(servicio + "Mssg", "OK")))));
    }

    private static XDocument LeerXmlSeguro(string contenido)
    {
        var configuracion = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var lectorTexto = new StringReader(contenido);
        using var lectorXml = XmlReader.Create(lectorTexto, configuracion);
        return XDocument.Load(lectorXml);
    }

    private static bool ContieneSoapFault(string contenido)
    {
        try { return LeerXmlSeguro(contenido).Descendants().Any(x => x.Name.LocalName == "Fault"); }
        catch (XmlException) { return false; }
    }
}