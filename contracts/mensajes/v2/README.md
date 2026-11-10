# Contratos de mensajes / avisos — v2

**Estado:** cerrado (Ana, 10/11/2026).  
**Consumidores:** Nadia (avisos HTTP v2), Joel (historial Mongo si aplica), Álvaro (solo referencia; sync de bultos vive en `contracts/repartidor/v2`).

v1 permanece **congelado**. Lo nuevo del cambio de requerimientos va acá o en repartidor v2.

## Catálogo

| Tipo | Dónde | Ejemplo |
|---|---|---|
| `EnvioEstadoCambiado` con `EntregadoParcialmente` | Aviso HTTP v2 | [aviso-parcial.json](aviso-parcial.json) |
| `BultoEstadoCambiado` | Aviso HTTP v2 + outbox | [aviso-bulto-estado-cambiado.json](aviso-bulto-estado-cambiado.json) |
| `RetiroSolicitado` | Aviso HTTP v2 + outbox | [aviso-retiro-solicitado.json](aviso-retiro-solicitado.json) |
| `RetiroCompletado` | Aviso HTTP v2 + outbox | [aviso-retiro-completado.json](aviso-retiro-completado.json) |
| `EnvioReasignado` | Cola (mismo cuerpo v1) | [../v1/envio-reasignado.json](../v1/envio-reasignado.json) |
| Sync parcial `bultos[]` | API repartidor | [../../repartidor/v2/sincronizacion.request.json](../../repartidor/v2/sincronizacion.request.json) |

## Convenciones

- JSON **camelCase**
- Sobre HTTP v2: `version: 2`, encabezado `X-Plataforma-Version: 2`
- Instantes ISO 8601 con offset (−03:00 en ejemplos de contrato)
- Una suscripción v1 **nunca** recibe tipos solo-v2
- Comercios v2 deben **ignorar** `tipo` desconocidos

## Routing keys (outbox → RabbitMQ)

| Tipo | Routing key |
|---|---|
| `BultoEstadoCambiado` | `bultoestadocambiado.v1` |
| `RetiroSolicitado` | `retirosolicitado.v1` |
| `RetiroCompletado` | `retirocompletado.v1` |
| `EnvioReasignado` | `envioreasignado.v1` |

La versión del **contrato de cola** del payload interno sigue en `1`; la versión **HTTP al comercio** es independiente (suscripción v1/v2).
