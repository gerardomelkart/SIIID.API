# SIIID.API

API de SIIID para Consolidado mensual, Semanal, Federal, BANCI y administración. Proyecto ASP.NET Core con .NET 10 y acceso a SQL Server mediante Dapper.

## Desarrollo y compilación

Desde la raíz, con el SDK de .NET 10:

```bash
dotnet restore SIIID2.Api/SIIID2.Api.csproj
dotnet build SIIID2.Api/SIIID2.Api.csproj --configuration Release
dotnet run --project SIIID2.Api/SIIID2.Api.csproj
```

Revisar la configuración del ambiente antes de iniciar. Las credenciales y conexiones del servidor deben administrarse fuera del código mediante la configuración del despliegue. Compilar no instala ni ejecuta los scripts del repositorio de BD.

## Organización

| Ubicación en `SIIID2.Api/` | Responsabilidad |
|---|---|
| `Controllers/` | Contratos HTTP y controles de acceso de cada operación. |
| `Services/` | Coordinación de los flujos y generación de resultados. |
| `Repositories/` | Consultas y persistencia; conservar sus límites transaccionales. |
| `Validators/` | Validación de datos y reglas de carga. |
| `Models/` | Contratos y estructuras de datos. |
| `Program.cs` | Configuración y registro de dependencias. |

Los nombres `Semanal`, `Federal` y `Banci` identifican módulos específicos. No unificar sus reglas por similitud de nombres: los periodos, permisos y procesos de aprobación tienen diferencias funcionales.

## Repositorios parciales

Se mantiene una sola clase y el mismo registro de dependencias para cada repositorio. Los archivos parciales separan responsabilidades sin introducir servicios ni contratos nuevos:

- `InformeRepository.cs` conserva coordinación y consultas generales; `InformeRepository.Sabanas*.cs` separa delitos/víctimas y ámbito estatal/municipal.
- `SemanalEnviosRepository.cs` conserva las dependencias y operaciones generales. Sus parciales agrupan reporte de cargas, reporte preliminar, archivos confirmados y las cuatro variantes de sábanas.
- `ActualizacionRepository.cs` conserva la coordinación de aprobación y aplicación. Los parciales `Carpetas`, `Delitos` y `Victimas` contienen las operaciones de cada entidad.

En actualizaciones, todas las etapas reciben la misma transacción. Conservar el histórico antes de modificar datos y el orden de desactivación de hijos y padres. No agregar un COMMIT independiente a un parcial.

## Criterios de mantenimiento

Comentar el motivo de las decisiones, los límites de acceso y las dependencias transaccionales, evitando repetir lo que ya expresa el método. Antes de retirar una consulta o endpoint, revisar consumidores del frontend y posibles integraciones externas.

La compilación valida la integración de los parciales; no sustituye una prueba contra SQL Server. Para cambios de consultas o reglas, verificar en QA cargas, aprobación/rechazo y sábanas de los módulos afectados. No hay un proyecto de pruebas automatizadas de API en este repositorio.

## Estilo de código

Usar cuatro espacios y llaves en una línea independiente. Las firmas y condiciones que caben en 140 columnas se escriben en una línea; las más largas se dividen por parámetros o expresiones. Los comentarios explican decisiones, permisos y etapas de validación, sin repetir cada instrucción. Mantener intactos los textos SQL y los mensajes al aplicar formato. La configuración del editor está en `.editorconfig`.

## BANCI: ceros y omisiones en catálogos compartidos

Antes de validar y guardar un alta manual o masiva, `BanciCatalogosNormalizer` resuelve la opción activa «No identificado» o «No identificada» desde los catálogos devueltos por el repositorio. Aplica a sexo, género, POB, DISC, nacionalidad, forma de acción, instrumento y grado de consumación. No fija claves numéricas: cada catálogo aporta la suya. Sólo un cero numérico o un dato omitido/vacío activa esa sustitución; otras claves conservan las validaciones de formato y catálogo. Si no existe una opción activa inequívoca, se informa un error de configuración del campo antes de guardar la carga.

El cruce aplica la misma interpretación a copias de las filas de ambas fuentes, sin alterar el Consolidado ni sus snapshots. Los IDs de relación y los campos geográficos conservan sus reglas específicas. La normalización se realiza en la API, sin modificar los catálogos ni ejecutar una conversión masiva en BD.

Tras publicar esta API, rechazar y volver a validar las cargas antiguas que quedaron pendientes con ceros en staging; la publicación no modifica propuestas previamente almacenadas. Conservar el script 09 de BD ya aplicado.

Pruebas sin conexión a la base: `dotnet run --project SIIID2.Api.RegressionTests --configuration Release`.
