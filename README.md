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

Guía coordinada y prueba de QA: `SIIID2BD/documentacion/banci_operacion_mensual.md`. Conservar `ArchivosOriginalesBanci` al publicar API. El nuevo cruce y la reclasificación por víctima se describen en `SIIID2BD/documentacion/banci_reclasificacion_cruce.md`; requieren el script incremental 14.


### Ajustes BANCI: carga de pendientes, captura manual y acuse

Publicar API y Front de forma coordinada. Estos ajustes no requieren ningún cambio de BD ni scripts SQL nuevos.
Los motivos se consultan directamente en los catálogos existentes, con la misma consulta para manual y Excel.

- Masiva: la plantilla incluye todas las carpetas pendientes del mensual aceptado para la entidad y corte,
  con sus delitos y víctimas de desaparición. Excluye las aprobadas o enviadas a revisión en ambas modalidades.
  La validación exige la plantilla pendiente completa, sin faltantes, adicionales ni duplicados. Al completar
  el periodo ya no se permite otra alta; se utiliza Actualización para modificar lo aprobado.
- Manual: cada alta envía una carpeta elegida y sus hijos. Las aprobadas o enviadas a revisión dejan de
  aparecer como pendientes; se pueden enviar otras carpetas sin esperar la aprobación de las anteriores.
- Actualización manual y masiva: se integran solo víctimas modificadas y sus padres. La manual envía el
  subconjunto seleccionado; la masiva valida primero la coincidencia de la plantilla completa. El servidor
  protege identificadores y descarta registros sin cambios antes de preparar la operación.
- No localizada: condición de vida, fecha, condición y motivo de localización se ignoran y almacenan vacíos.
  La API aplica esta regla también al Excel y con validaciones de contenido desactivadas.
- Motivos constitutivos de delito: catálogo completo de modalidades con la jerarquía vigente del Semanal,
  conservando las claves generales anteriores. El catálogo de desaparición que delimita el mensual no cambia.
- Confirmación y visor BANCI en morado. El acuse no muestra la referencia interna de carga y distingue Ciudad de México de los estados. Acuse PDF con el mismo formato institucional del Consolidado (logos, encabezado, pie y tablas), con resumen y `no_banci` por víctima.
- Actualización masiva usa los mismos dos pasos de Carga: descargar plantilla precargada y cargar el Excel completado; el archivo solo se habilita después de descargar.
  El envío pendiente se distingue del confirmado; los folios nuevos se asignan al aprobar.

Validar los flujos contra la BD de desarrollo/QA antes de publicar en producción.


### Flujo mensual BANCI

Carga mensual y Actualización mensual ofrecen un selector Manual/Masiva y conservan sus permisos.
La descarga de plantilla oculta su bloque y desplaza al siguiente paso; validar desplaza al resultado.
El informe previo y el acuse final se muestran en el visor de PDF con Aceptar/Rechazar/Cerrar,
sin descarga automática. Los reportes permiten abrir ese mismo visor.
Las plantillas y formularios BANCI no incluyen las columnas heredadas de feminicidio.
Estos ajustes no modifican objetos de BD ni el flujo de Consolidado, Federal o Semanal.

### Descarga administrativa de diferencias completas

En Aprobación administrativa de Mensual, Federal, Semanal y BANCI se ofrece **Descargar todas las diferencias en Excel**. La muestra de pantalla permanece limitada a 100 registros por sección en los tres primeros módulos; la descarga consulta el detalle completo de forma independiente, incluso si no se pudo cargar la muestra.

La API exige rol SUPER_USUARIO y acceso al módulo correspondiente. El archivo contiene Carpetas, Delitos y Víctimas con movimiento, identificador, campo y valores anterior/propuesto, filtros y encabezados inmovilizados. Los textos extensos continúan en filas marcadas como continuación, sin una columna Parte, y las hojas se dividen al alcanzar el límite de filas de Excel. Se genera en un archivo temporal eliminado al terminar la descarga, sin enviar el detalle completo al navegador como JSON ni renderizarlo en tablas.

Publicar API y frontend de forma coordinada. No requiere scripts ni cambios en la estructura de la base de datos.

### Plantillas y colores por módulo

Las plantillas vacías de Mensual (guinda), Federal (azul) y Semanal (verde) se generan desde la API con sus encabezados oficiales, formato de texto y primera fila inmovilizada. Carga y Actualización solicitan el mismo generador; ya no dependen de archivos XLSX estáticos del frontend. BANCI conserva su plantilla morada precargada con información del periodo.

La exportación administrativa de diferencias usa el color del módulo en el Excel y el botón. Durante su generación y descarga se bloquea la interacción con un aviso de que puede tardar varios minutos cuando existen muchas modificaciones; se desbloquea tanto al terminar como ante un error.

### Consulta de envíos BANCI y originales masivos

BANCI utiliza una consulta de envíos independiente con el mismo esquema visual del informe mensual: cortes disponibles, búsqueda, ordenamiento, paginación, estados legibles, exportación de la tabla, vista PDF y ZIP de acuses del corte. Muestra los envíos remitidos o resueltos administrativamente; las validaciones sin enviar no forman parte del historial de envíos. El alcance de entidad y los permisos se verifican en la API.

Los originales corresponden únicamente a cargas y actualizaciones masivas. La captura manual mantiene su snapshot y datos propuestos para revisión y acuse, pero no genera ni almacena un XLSX artificial. Los originales manuales anteriores se excluyen del listado y se bloquea su descarga, sin eliminar registros ni ejecutar scripts de limpieza. Las operaciones masivas conservan exactamente el archivo recibido.

### Reporte de cargas y archivos originales BANCI

El reporte de cargas sigue el esquema mensual por corte: entidades con carga en pantalla y todas las entidades activas en la exportación Excel (incluidas las que no tienen carga), intentos manuales y masivos, último estado, fechas y ranking por primera carga enviada correctamente. Una resolución rechazada no se presenta como fecha de aprobación.

Archivos originales muestra el último archivo masivo recibido por entidad entre todos los periodos, con nombre original, tamaño y descarga íntegra XLSX o ZIP. Excluye capturas manuales y conserva los archivos anteriores almacenados. Ambos reportes requieren SUPER_USUARIO y ofrecen búsqueda, ordenamiento, paginación y exportación de la tabla.

Publicar API y frontend juntos. No requiere cambios de base de datos.

Los nuevos originales masivos BANCI se guardan con el nombre recibido dentro de una carpeta por envío, sin regenerar el XLSX. Se mantiene la lectura de archivos anteriores almacenados como GUID.xlsx. No requiere migración ni scripts.

### Aprobación administrativa BANCI

La bandeja de aprobación sigue la presentación del mensual con búsqueda, ordenamiento, paginación y revisión por envío, incluidos pendientes de años anteriores. Muestra entidad, corte, modalidad manual/masiva, remitente, fecha y totales de carpetas, delitos y víctimas.

La revisión presenta totales completos y muestras de hasta 100 registros/cambios; la descarga administrativa existente conserva todas las diferencias y su bloqueo durante la generación. Permite consultar el informe previo y descargar el original masivo con su nombre recibido. Las capturas manuales no ofrecen archivo original. Aprobar solicita confirmación; rechazar requiere un motivo de al menos cinco caracteres. La resolución bloquea la interacción hasta terminar y actualiza la bandeja al completarse.

Los nuevos endpoints de pendientes y revisión exigen SUPER_USUARIO y acceso administrativo BANCI. Publicar API y frontend juntos; no se requieren scripts ni cambios de estructura de base de datos.

### Configuración de permisos BANCI

La configuración de BANCI usa el mismo diseño que el consolidado: controles globales de carga y actualización, resumen por entidad, búsqueda, ordenamiento, paginación, Excel y panel de permisos individuales. Los estados globales se calculan desde las cuentas activas; los administradores generales quedan fuera de la gestión operativa y los usuarios CONSULTA no pueden cargar ni actualizar. Los cambios individuales utilizan el endpoint exclusivo de permisos BANCI, sin editar perfiles ni permisos de otros módulos.

Las reglas de validación y las cargas de cortes anteriores se administran únicamente en Configuración del sistema. Se retira el endpoint duplicado PUT /api/banci/mensual/configuracion; las reglas existentes siguen aplicándose en la carga y actualización. No requiere cambios en base de datos. Publicar API y frontend juntos.

### Avisos y distribución BANCI

BANCI consulta los rechazos propios al entrar y cada minuto, con el mismo aviso y acceso a envíos del consolidado. Registra su consulta una sola vez en la bitácora BANCI existente, sin tablas ni columnas adicionales. El inicio incorpora el recordatorio compartido de carga pendiente, calculado sobre carpetas del corte anterior todavía no aprobadas ni enviadas a revisión.

La revisión administrativa desplaza la resolución al borde inferior visible tras renderizar. La muestra de diferencias contiene su desplazamiento sin superponer descargas o resolución. La consulta de envíos amplía el selector de corte y distribuye acciones y botones con separación y ajuste al ancho disponible. Los cambios de estilo se limitan a BANCI.

### Bloqueo previo por envíos BANCI pendientes

La precarga y la plantilla de actualización se bloquean por completo cuando existe cualquier envío pendiente de aprobación para la misma entidad y corte, con BANCI_ENVIO_PENDIENTE (HTTP 409). La carga mantiene la exclusión de carpetas ya enviadas o aprobadas. La validación vuelve a comprobar superposición antes de procesar el contenido, además del control transaccional existente.

En captura manual y masiva, tanto de carga como de actualización, ese conflicto retira la información editable y el archivo seleccionado. Los demás errores conservan el trabajo y desplazan la vista al mensaje después de renderizarlo. Publicar API y frontend juntos. No requiere cambios de estructura de base de datos.

El bloqueo de actualización es por entidad y corte completo y se verifica antes de precargar, al validar y al confirmar el envío dentro de la transacción. No permite actualizar otras víctimas del mismo periodo mientras exista una aprobación pendiente. La carga inicial conserva su control por carpetas.

### Reclasificación por víctima y cruce mensual

BANCI mensual opera solo mediante archivos masivos. Sus campos existentes de localización permiten reclasificar o revertir cada víctima; el efecto comienza al aprobar. Al actualizar un corte del Consolidado que ya tiene BANCI, se exige concordancia con las reclasificaciones aprobadas y se solicitan complementos de nuevas desapariciones, incluso dentro de una carpeta existente. Staging, integración, rechazo y estados de ambos módulos se resuelven conjuntamente desde Mensual. Federal y Semanal quedan fuera.

Publicar BD/API/Front coordinados: ejecutar `SIIID2BD/desarrollo/14_banci_reclasificacion_cruce.sql` y seguir `SIIID2BD/documentacion/banci_reclasificacion_cruce.md`. El SQL no se ejecutó en el servidor desde este entorno; completar su prueba de integración en QA. Conservar los directorios de originales.
