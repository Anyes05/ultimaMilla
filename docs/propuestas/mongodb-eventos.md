# Propuesta: historial de eventos en MongoDB (opcional 7.2, NoSQL)

Estado: **propuesta para acordar** con Joel (Atlas) y Ana (contrato del evento).
Implementa: Nadia (worker y API de lectura). Recurso Atlas: Joel.

## Qué pide la letra

> Base documental para el historial de eventos y las consultas analíticas, sin afectar la base transaccional,
> con índices adecuados, política de retención y API de lectura paginada y filtrable.

## Cómo encaja

```
cambio de estado ─outbox─▶ plataforma.eventos ──envioestadocambiado.v1──▶ plataforma.avisos-comercio     (ya existe)
                                               └─envioestadocambiado.v1──▶ plataforma.historial-eventos  (nueva)
                                                                              │
                                                                     worker: ConsumidorHistorial
                                                                              │ insert idempotente
                                                                              ▼
                                                                MongoDB Atlas: eventos_envio
```

- **Cola propia** (`plataforma.historial-eventos`): si Mongo cae, los avisos a comercios siguen funcionando,
  y al volver Mongo se procesa lo acumulado. Misma estrategia de reintentos que avisos (esperas crecientes)
  y cola de fallidos `plataforma.historial-eventos.fallidos`.
- **No se toca Postgres**: el historial transaccional sigue en `HistorialEstados` (Ana); Mongo es la copia
  para consultas y analítica, alimentada solo por la cola (letra: "sin afectar la base transaccional").

## Base y colección

- Base: `tallernet` · Colección: `eventos_envio`

Documento (un evento de la cola = un documento):

```json
{
  "_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "operadorId": "11111111-1111-1111-1111-111111111111",
  "envioId": "40000000-0000-0000-0000-000000000001",
  "tipo": "EnvioEstadoCambiado",
  "version": 1,
  "estadoAnterior": "EnTransito",
  "estadoNuevo": "Entregado",
  "fecha": { "$date": "2026-10-23T00:00:00Z" },
  "registradoEn": { "$date": "2026-10-23T00:00:01Z" },
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

- `_id = eventId`: la **idempotencia** sale gratis. Si el mismo mensaje llega dos veces, el segundo insert
  falla por clave duplicada y el worker lo da por procesado (no duplica, letra 6.8).
- `operadorId` va en **todos** los documentos y en **todos** los índices: aislamiento por inquilino
  (documento de diseño, sección 5: "todo mensaje lleva OperadorId").
- Cuando Ana agregue campos al contrato (por ejemplo `comercioId`, `origen`, `usuario`, posición), se suman
  al documento sin migración. Se mantiene `version` para distinguir formas.

## Índices

| Índice | Para qué consulta |
|---|---|
| `{ operadorId: 1, envioId: 1, fecha: 1 }` | Historial de un envío, en orden (seguimiento, auditoría) |
| `{ operadorId: 1, fecha: -1, _id: -1 }` | Listado por período, paginado (más recientes primero) |
| `{ operadorId: 1, estadoNuevo: 1, fecha: -1 }` | Analítica: cuántos pasaron a X en un período (reportes de cumplimiento) |
| `{ registradoEn: 1 }` con `expireAfterSeconds` | **Retención** (TTL) |

- **Retención: 180 días**, configurable (`Mongo:RetencionDias`). Pasado ese plazo Mongo borra solo.
  El historial oficial no se pierde: está en Postgres.
- Los índices los **crea el worker al arrancar** (`createIndexes` es idempotente). Joel no tiene que crearlos
  a mano, pero quedan documentados acá.

## API de lectura (paginada y filtrable)

En el Backoffice (subdominio `api`), perfiles del operador, operador tomado del token (nunca del cliente):

```
GET /api/historial/eventos?envioId=&estado=&desde=&hasta=&despuesDe=&tamano=50
```

- Paginación por **cursor** (`despuesDe` = último `fecha|_id` recibido), no por `skip`: no se degrada con
  volúmenes grandes y aprovecha el índice `{operadorId, fecha, _id}`.
- Respuesta: `{ "items": [...], "siguiente": "<cursor o null>" }`.
- Tamaño máximo 200.

## Lo que necesito de Joel (Atlas)

1. Cluster (M0 gratuito alcanza) en `us-east-1`.
2. Usuario técnico con rol **`readWrite` solo sobre la base `tallernet`** (no `atlasAdmin`).
3. Acceso de red: la IP de salida de los nodos EKS (y la de quien pruebe), no `0.0.0.0/0` salvo que sea
   imposible en AWS Academy; si se usa, dejarlo documentado como riesgo.
4. Un Secret de Kubernetes con la clave `Mongo__ConnectionString` (cadena `mongodb+srv://...`), referenciado
   por `queue-worker` y `backoffice`. Fuera de Git y de chat.
5. Nada más: base, colección e índices los crea la aplicación.

Mientras Atlas no esté, desarrollo y pruebo con un `mongo:7` en compose; el código no cambia, solo la cadena.

## Lo que necesito de Ana

- Nada bloqueante: alcanza con el contrato `EnvioEstadoCambiado` v1 actual.
- Si en el cambio de contrato suma `comercioId`, `origen` o `usuario`, avisame para agregarlos al documento.

## Sin la variable `Mongo__ConnectionString`

El consumidor no arranca y lo informa en el log; el resto del worker sigue igual. Así un ambiente sin Atlas
no se rompe.
