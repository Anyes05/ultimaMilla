# Pruebas de resiliencia (opcional 7.4)

**Responsable:** Nadia Inthamoussu
**Fecha:** 10/11/2026 (repetición completa; la primera corrida fue el 08/11)
**Ambiente:** Docker Compose local con base nueva, una instancia de cada servicio, `Development`. Código de `main` en `fe6a25d` más los cambios de banderas en todas las capas.

Cada dependencia se detuvo con `docker stop` mientras se operaba el sistema real: login, alta de envío en el Portal, cambio de estado en el Backoffice, seguimiento público y entrega de avisos a un receptor con suscripción v2. Los tiempos son de una sola corrida y sirven como referencia.

## Resumen

| Se interrumpe | Comportamiento esperado | Comportamiento observado | Resultado |
|---|---|---|---|
| **Redis** (caché) | Sigue contra la base. `/ready` 200 y `/health` `Degraded` | Login, alta, cambio de estado, tablero y avisos funcionan. El primer seguimiento tardó 12 s y el primer cambio de estado 5 s; después, 0,1 s. Al volver, `/health` `Healthy` sin reiniciar nada | Cumple |
| **RabbitMQ** (cola) | El cambio se guarda, el usuario ve el resultado, el evento queda en el outbox y se publica al volver | Dos cambios de estado guardados con mensaje de éxito, en 0,2 s y 0,1 s. Quedaron 4 eventos pendientes en el outbox y se publicaron solos: el aviso llegó al comercio 15 s después de volver RabbitMQ, sin duplicados | Cumple |
| **PostgreSQL** (base) | `/live` 200, `/ready` y `/health` 503. Sin escrituras. Recuperación sola | Tal cual. El seguimiento ya cacheado responde 200. Monitoreo, login, Portal y la API de la app dan 500 en unos 4 s. Ningún proceso se cae. Al volver, `/ready` 200 en menos de 1 s y el flujo completo funciona sin reiniciar | Cumple |
| **MongoDB** (historial) | El flujo principal no se afecta. El evento se reintenta | Alta, cambio de estado y avisos sin cambios. La API de historial da 500 tras 30 s. El evento pasó a la cola de espera y quedó guardado a los 4 s de volver MongoDB | Cumple |
| **Receptor del comercio** (servicio externo) | Reintentos a 5, 15 y 45 s, luego `Fallido`, y reenvío manual | Intentos en t+0, 5, 20 y 66 s, todos 503, y `Fallido`. Con el receptor activo, "Reenviar" en el Portal lo entregó, sin crear otra entrega | Cumple |
| **Worker** | Los eventos esperan en la cola. La web no se afecta | La web sigue igual y no se crea la entrega. Al volver, aviso entregado en 4 s, sin duplicados | Cumple |

Al terminar no se perdió ningún evento: 20 mensajes en el outbox (10 altas y 10 cambios de estado), todos publicados, y los 10 cambios de estado guardados en el historial de MongoDB, que solo registra cambios de estado. En la cola de fallidos de avisos quedó un único mensaje: el del receptor caído, que es el comportamiento esperado y se resolvió con el reenvío manual.

## Problemas encontrados

### 1. Con RabbitMQ caído, el usuario veía un error aunque el cambio se guardó (resuelto)

**Antes (08/11):** después de confirmar la transacción, la publicación inmediata del outbox lanzaba `BrokerUnreachableException` y la pantalla mostraba un error 500 a los 6 s, aunque el cambio estaba guardado.

**Corrección:** si la publicación falla, se registra el error y el publicador queda en pausa 15 s; el evento sigue en el outbox y lo publica el proceso en segundo plano cuando vuelve la cola.

**Ahora (10/11):** el usuario ve "Estado actualizado" en 0,2 s y los eventos se publican solos al volver RabbitMQ, sin duplicados.

### 2. Demoras mientras Redis deja de responder

El primer pedido después de la caída espera el vencimiento de la conexión (12 s en el seguimiento, 5 s en un cambio de estado). Después, el caché deja de consultarse durante 15 s y las respuestas vuelven a ser inmediatas. Bajar el tiempo de conexión de Redis acortaría esa primera espera.

### 3. La API de historial espera 30 s con MongoDB caído

Es el tiempo por defecto del driver para elegir un servidor, y termina en 500. Un límite menor daría una respuesta de error más rápida. El historial no se pierde: el worker reintenta y lo guarda al volver.

## Lo que esta prueba no cubre

- La caída de una instancia con dos réplicas y balanceador (escenario 1 de la defensa): necesita el ambiente en AWS.
- Una caída de MongoDB más larga que los reintentos (5, 15 y 45 s): el evento iría a la cola de fallidos del historial.
- Redis persistente: en Compose vuelve vacío.
