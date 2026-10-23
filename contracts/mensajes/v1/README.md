# Contratos de mensajes — v1

Definidos por Ana (backend). Consumen Nadia (worker / notificaciones) y, más
adelante, Álvaro si el móvil necesita reaccionar a eventos publicados.

Los JSON de ejemplo son el contrato de intercambio sobre la cola (RabbitMQ).

Flujo actual:
1. El cambio de estado escribe `MensajesOutbox` en la **misma transacción**.
2. Tras el commit, Backoffice intenta publicar a RabbitMQ (`plataforma.eventos`,
   routing key `envioestadocambiado.v1`) y marca `publicadoEn`.
3. Un background service reintenta pendientes cada pocos segundos si RabbitMQ
   estaba caído en el momento del cambio.

Sin `RabbitMq:Host` configurado, los mensajes quedan pendientes en outbox.

## Catálogo v1

| Tipo | Versión | Ejemplo |
|---|---|---|
| `EnvioEstadoCambiado` | `1` | [envio-estado-cambiado.json](envio-estado-cambiado.json) |

## Convenciones

- JSON **camelCase**
- UUID como string
- Instantes ISO 8601 con offset
- Estados de envío como **texto** (`EnTransito`, no números)
- `eventId` es la clave de idempotencia del mensaje (también PK de outbox)

## `EnvioEstadoCambiado`

Evento mínimo emitido cada vez que un envío cambia de estado (backoffice,
despacho, inicio de ruta o sincronización del repartidor).

| Campo | Tipo | Notas |
|---|---|---|
| `eventId` | UUID | Único; el consumidor debe tratarlo como idempotente |
| `fecha` | DateTimeOffset | Momento del cambio (UTC con offset) |
| `operadorId` | UUID | Tenant |
| `envioId` | UUID | Envío afectado |
| `estadoAnterior` | texto | Estado antes del cambio |
| `estadoNuevo` | texto | Estado después del cambio |

Headers / metadatos de cola sugeridos (fuera del cuerpo JSON):

| Clave | Valor |
|---|---|
| `tipo` | `EnvioEstadoCambiado` |
| `version` | `1` |

## Alcance fuera de v1

Publicación real a RabbitMQ, reintentos/DLQ, SignalR y webhooks: iteraciones
siguientes del worker.
