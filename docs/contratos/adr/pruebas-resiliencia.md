# Pruebas de resiliencia (opcional 7.4)

**Responsable:** Nadia Inthamoussu
**Fecha:** 08/11/2026
**Ambiente:** Docker Compose local con base nueva, una instancia de cada servicio, `Development`.

Cada dependencia se detuvo con `docker stop` mientras se operaba el sistema real: login, alta de envío en el Portal, cambio de estado en el Backoffice, seguimiento público y entrega de avisos a un receptor con suscripción v2. Los tiempos son de una sola corrida y sirven como referencia.

## Resumen

| Se interrumpe | Comportamiento esperado | Comportamiento observado | Resultado |
|---|---|---|---|
| **Redis** (caché) | Sigue contra la base. `/ready` 200 y `/health` `Degraded` | Login, alta, cambio de estado y avisos funcionan. El primer seguimiento tardó 11 s y un cambio de estado 6 s; después, 0,04 s. Al volver, `/health` `Healthy` sin reiniciar nada | Cumple |
| **RabbitMQ** (cola) | El cambio se guarda, el evento queda en el outbox y se publica al volver | Se guarda y el evento se publica a los 7 s de volver, sin duplicados. **Pero el usuario recibe un error 500 tras 6 s** | **Cumple a medias** |
| **PostgreSQL** (base) | `/live` 200, `/ready` y `/health` 503. Sin escrituras. Recuperación sola | Tal cual. El seguimiento ya cacheado responde 200. Monitoreo, login y Portal dan 500. Al volver, `/ready` 200 en 1 s y sin reiniciar procesos | Cumple |
| **MongoDB** (historial) | El flujo principal no se afecta. El evento se reintenta | Alta, cambio de estado y avisos sin cambios. La API de historial da 500 tras 30 s. El evento se reintentó y quedó guardado a los 3 s de volver | Cumple |
| **Receptor del comercio** (servicio externo) | Reintentos a 5, 15 y 45 s, luego `Fallido`, y reenvío manual | Intentos en t+0, 5, 20 y 65 s, todos 503, y `Fallido`. Con el receptor activo, "Reenviar" en el Portal lo entregó, sin crear otra entrega | Cumple |
| **Worker** | Los eventos esperan en la cola. La web no se afecta | La web sigue igual y no se crea la entrega. Al volver, aviso entregado en 2 s, sin duplicados | Cumple |

Al terminar no se perdió ningún evento: 12 mensajes en el outbox, todos publicados, y 12 documentos en el historial de MongoDB.

## Problemas encontrados

### 1. Con RabbitMQ caído, el usuario ve un error aunque el cambio se guardó

**Causa:** después de confirmar la transacción, `PublicarOutboxPendientes.EjecutarUnoAsync` intenta publicar y la excepción `BrokerUnreachableException` sube hasta la pantalla.

**Dónde:** lo llaman `CambiarEstadoEnvio`, `DespacharRuta`, `IniciarRuta` y `SincronizarEntregas`. En esta prueba se reprodujo en el cambio de estado.

**Efecto:** el dato queda correcto y el outbox lo recupera, pero la persona cree que falló y puede reintentar. Una sincronización de la app también recibiría un error, aunque al reintentar con el mismo `eventoId` no duplica nada.

**Corrección propuesta:** capturar el error de publicación en `EjecutarUnoAsync`, registrarlo y dejar que el publicador en segundo plano lo envíe.

### 2. Demoras mientras Redis deja de responder

El primer pedido después de la caída espera el vencimiento de la conexión (11 s en el seguimiento, 6 s en un cambio de estado). Después, el caché deja de consultarse durante 15 s y las respuestas vuelven a ser inmediatas.

### 3. La API de historial espera 30 s con MongoDB caído

Es el tiempo por defecto del driver para elegir un servidor, y termina en 500. Un límite menor daría una respuesta de error más rápida.

## Lo que esta prueba no cubre

- La caída de una instancia con dos réplicas y balanceador (escenario 1 de la defensa): necesita el ambiente en AWS.
- Una caída de MongoDB más larga que los reintentos (5, 15 y 45 s): el evento iría a la cola de fallidos del historial.
- Redis persistente: en Compose vuelve vacío.
