# ADR 0011: Outbox transaccional

**Estado:** aceptada  
**Responsable:** Ana  
**Última revisión:** 09/11/2026  
**Requerimientos:** letra 6.1, 6.8

## Contexto

Hay que publicar cambios de estado (y otros eventos) a RabbitMQ sin perder mensajes ni acoplar la UX a la disponibilidad del broker.

## Decisión

1. En la misma transacción del cambio se escribe `MensajeOutbox` pendiente.
2. Publicación **best-effort** inmediata (`PublicarOutboxPendientes.EjecutarUnoAsync`); si RabbitMQ falla, se **traga** el error y el dato queda guardado.
3. `OutboxPublicadorBackground` drena pendientes.
4. Consumidores son idempotentes por `EventId`.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Publicar dentro de la misma transacción DB+MQ | 2PC / no disponible |
| Fallar la API si RabbitMQ cae | Produce 500 engañoso (prueba de resiliencia) |

## Consecuencias

Con RabbitMQ caído la operación web/sync responde OK; los eventos salen al volver el broker. El historial y los avisos se alimentan del mismo outbox.
