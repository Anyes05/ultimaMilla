# ADR 0016: Historial documental con MongoDB Atlas

**Estado:** aceptada  
**Responsable:** Joel Leyes  
**Última revisión:** 14/11/2026  
**Requerimientos:** opcional 7.2 (NoSQL para historial y analítica), letra 6.5, 6.8

## Contexto

El opcional requiere una base documental para historial y consultas analíticas, con índices, retención y una API
de lectura paginada y filtrable, sin trasladar esa carga a la base transaccional. PostgreSQL sigue siendo el origen
de los datos operativos; no se reemplazan sus transacciones ni el historial relacional.

El [ADR 0011](0011-outbox.md) define la publicación mediante outbox. Este registro decide cómo producir y consultar
una proyección documental de esos eventos. La infraestructura AWS se describe en el
[ADR 0014](0014-infraestructura-y-autoescalado.md) y la entrega de configuración en el
[ADR 0015](0015-entrega-y-despliegue-continuo.md).

## Decisión

1. **MongoDB Atlas como servicio externo a EKS**, con conexión configurable, sin añadir un servidor MongoDB al
   clúster del laboratorio. El desarrollo local puede usar el contenedor MongoDB de Compose con el mismo repositorio
   de acceso a datos. Atlas no está administrado por el Terraform AWS actual.
2. **Separar recursos de Atlas y objetos de la base:**
   - El clúster, las reglas de acceso de red y el usuario técnico se preparan en Atlas.
   - La configuración operativa prevista usa un usuario `readWrite` limitado a la base `tallernet`, suficiente
     para insertar y crear los índices, sin privilegios administrativos globales.
   - El acceso de red debe limitarse a los orígenes necesarios; si se amplía por las restricciones del laboratorio,
     debe documentarse el riesgo. El reemplazo de nodos o el cambio de cuenta obliga a revisar sus IP de salida.
   - No se presupone peering ni PrivateLink. Tier, región, permisos y reglas efectivamente aplicados requieren
     evidencia de Atlas; los manifiestos de AWS no los demuestran.
3. **Proyección asíncrona con cola propia:** `ConsumidorHistorial` recibe los cambios de estado de envío desde
   `plataforma.historial-eventos`, separada de la cola de avisos a comercios. Inserta en MongoDB y confirma el mensaje;
   los errores de procesamiento se encaminan a colas de espera y, agotados los reintentos, a una cola de fallidos.
   La política de publicación original sigue siendo la del ADR de outbox.
4. **Documento por evento:** base `tallernet`, colección `eventos_envio`, configurables. Se guardan `_id`, operador,
   envío, tipo y versión del evento, estados anterior/nuevo, fecha del evento, fecha de registro y correlación.
   `_id` es el `EventId`: el índice único nativo evita duplicar el documento cuando se reentrega el mismo evento.
   No se modela MongoDB como fuente de verdad ni como event sourcing completo.
5. **Índices creados por la aplicación:** al inicializar el consumidor, `AsegurarIndicesAsync` solicita:

   | Índice | Propósito |
   |---|---|
   | `{ operadorId: 1, envioId: 1, fecha: 1 }` | Consultas por operador y envío |
   | `{ operadorId: 1, fecha: -1, _id: -1 }` | Listado temporal con cursor |
   | `{ operadorId: 1, estadoNuevo: 1, fecha: -1 }` | Filtrado por estado y período dentro del operador |
   | `{ registradoEn: 1 }`, TTL | Expiración de documentos |

   Los índices de consulta comienzan por operador; el índice TTL y el índice nativo `_id_` tienen otra finalidad.
6. **Retención configurable:** `Mongo:RetencionDias` vale 180 por defecto, con mínimo efectivo de un día.
   La expiración se calcula desde `registradoEn`, no desde la fecha de negocio. La eliminación TTL es asíncrona,
   no ocurre exactamente al segundo del vencimiento. No elimina el historial de PostgreSQL.
7. **Lectura autorizada y acotada:** `GET /api/historial/eventos` admite envío, estado y rango de fechas,
   con paginación por cursor `fecha|_id`. Devuelve `items` y `siguiente`, con 50 elementos por defecto y máximo 200.
   El operador se obtiene del usuario autenticado, no de un parámetro elegido por el cliente, y siempre integra
   el filtro. El endpoint se restringe a los perfiles de operación.
8. **Configuración fuera del código:** `Mongo__ConnectionString` llega al Secret Kubernetes de la aplicación.
   El despliegue inicial lo prepara y GitHub Actions lo actualiza desde `MONGODB_CONNECTION_STRING`.
   No se incluyen URI con credenciales en documentación, imágenes o manifiestos versionados.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Consultar solamente PostgreSQL | No cubre el opcional documental ni separa esas lecturas del almacenamiento transaccional |
| Escribir PostgreSQL y MongoDB sincrónicamente desde la solicitud | Introduce dos escrituras sin una transacción común y acopla la operación a Atlas |
| Ejecutar MongoDB dentro de EKS | Agrega operación, almacenamiento y recuperación al clúster temporal; se adopta un servicio externo |
| Compartir una única cola entre historial y avisos | Los consumidores competirían por los mensajes en lugar de recibir cada uno su copia |
| Paginación solo por desplazamiento (`skip`) | El trabajo crece al avanzar páginas; se elige cursor con orden estable por fecha e identificador |
| Retener documentos indefinidamente | Aumenta almacenamiento y costo sin una política explícita |

## Consecuencias

- **Consistencia eventual:** un cambio confirmado en PostgreSQL puede tardar en aparecer en MongoDB.
  El historial documental no debe usarse para decidir el estado transaccional vigente.
- **Disponibilidad independiente, con límites:** una caída de Atlas puede retrasar el historial sin impedir la
  transacción principal. Los reintentos son finitos; los mensajes que llegan a fallidos requieren atención operativa.
  La cola propia no elimina el riesgo de pérdida si se recrea RabbitMQ sin almacenamiento persistente.
- **Alcance real:** el consumidor revisado proyecta `EnvioEstadoCambiado` v1. No se afirma que almacene todos los
  eventos de negocio ni que exista un motor de analítica completo. Hay lectura filtrada por estado y período;
  los informes o agregaciones adicionales necesitan implementación y evidencia específica.
- **Seguridad:** el aislamiento depende del filtro aplicado por la API y de los permisos de acceso. Compartir una
  colección no proporciona aislamiento físico entre operadores.
- **Retención e idempotencia:** al vencer el documento también desaparece su `_id`; una reproducción posterior
  del evento puede insertarlo otra vez. La deduplicación documental no es una garantía de conservación indefinida.
- **Operación:** cambiar el valor de retención no demuestra por sí solo que un índice TTL existente se haya
  actualizado. Se debe verificar el índice efectivo y gestionar sus cambios; igualmente, se deben comprobar
  permisos de creación de índices y planes de consulta sobre los datos reales.
- **Cambio de laboratorio:** destruir AWS no destruye Atlas. Para reutilizarlo hay que actualizar acceso de red y
  conexión del nuevo ambiente; su conservación no implica que PostgreSQL haya sido restaurado.
- **Evidencia necesaria:** registrar, sin secretos, configuración de red/usuario, índices y TTL efectivos, consulta
  paginada y filtrada, aislamiento entre operadores y ausencia de duplicados al reentregar un evento.
  El código y la propuesta inicial no certifican por sí solos esa configuración remota.

## Referencias de implementación

- [Repositorio, documentos, índices y opciones MongoDB](../../../src/Plataforma.HistorialEventos/HistorialEventosMongo.cs).
- [Consumidor del historial](../../../src/Plataforma.Worker/ConsumidorHistorial.cs).
- [Topología RabbitMQ del historial](../../../src/Plataforma.Worker/TopologiaHistorial.cs).
- [API de lectura](../../../src/Plataforma.Backoffice/HistorialEventosApi.cs).
- [Propuesta inicial y preparación de Atlas](../../propuestas/mongodb-eventos.md).
