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

La compilación valida la integración de los parciales; no sustituye una prueba contra SQL Server. Para cambios de consultas o reglas, verificar en QA cargas, aprobación/rechazo y sábanas de los módulos afectados. El proyecto `SIIID2.Api.RegressionTests` verifica reglas compartidas y el retiro de contratos anteriores sin conectar a BD.

## Estilo de código

Usar cuatro espacios y llaves en una línea independiente. Las firmas y condiciones que caben en 140 columnas se escriben en una línea; las más largas se dividen por parámetros o expresiones. Los comentarios explican decisiones, permisos y etapas de validación, sin repetir cada instrucción. Mantener intactos los textos SQL y los mensajes al aplicar formato. La configuración del editor está en `.editorconfig`.

## BANCI mensual: vistas y operación

Se recuperó el diseño institucional original y se conectaron carga/actualización masiva y manual, informes, originales, aprobación administrativa y configuración al modelo mensual. Requiere el script `desarrollo/12_banci_mensual_operacion.sql` de SIIID2BD después del bloque 1.

Guía coordinada y prueba de QA: `SIIID2BD/documentacion/banci_operacion_mensual.md`. Conservar `ArchivosOriginalesBanci` al publicar API. El nuevo cruce del consolidado y las reclasificaciones permanecen en el bloque 5, desactivado.


### Ajustes BANCI: masiva completa, captura manual y acuse

Publicar API y Front de forma coordinada. Estos ajustes no requieren ningún cambio de BD ni scripts SQL nuevos.
Los motivos se consultan directamente en los catálogos existentes, con la misma consulta para manual y Excel.

- Masiva: la plantilla y la validación incluyen todas las carpetas, delitos y víctimas de desaparición
  del mensual aceptado para la entidad y corte, sin registros faltantes, adicionales o duplicados.
  Si el periodo ya tiene registros BANCI o un envío en revisión, se bloquean descarga, validación e integración de una nueva carga masiva; se utiliza Actualización.
- Manual: cada alta envía una carpeta elegida y sus hijos. Las aprobadas o enviadas a revisión dejan de
  aparecer como pendientes; se pueden enviar otras carpetas sin esperar la aprobación de las anteriores.
- Actualización manual y masiva: se integran solo víctimas modificadas y sus padres. La manual envía el
  subconjunto seleccionado; la masiva valida primero la coincidencia de la plantilla completa. El servidor
  protege identificadores y descarta registros sin cambios antes de preparar la operación.
- No localizada: condición de vida, fecha, condición y motivo de localización se ignoran y almacenan vacíos.
  La API aplica esta regla también al Excel y con validaciones de contenido desactivadas.
- Motivos constitutivos de delito: catálogo completo de modalidades con la jerarquía vigente del Semanal,
  conservando las claves generales anteriores. El catálogo de desaparición que delimita el mensual no cambia.
- Confirmación BANCI en morado. Acuse PDF con el mismo formato institucional del Consolidado (logos, encabezado, pie y tablas), con resumen y `no_banci` por víctima.
- Actualización masiva usa los mismos dos pasos de Carga: descargar plantilla precargada y cargar el Excel completado; el archivo solo se habilita después de descargar.
  El envío pendiente se distingue del confirmado; los folios nuevos se asignan al aprobar.

Validar los flujos contra la BD de desarrollo/QA antes de publicar en producción.
