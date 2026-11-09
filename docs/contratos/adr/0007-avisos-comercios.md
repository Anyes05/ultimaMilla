# ADR 0007: Avisos a comercios: garantías de entrega y versionado

**Estado:** aceptada
**Responsable:** Nadia Inthamoussu
**Última revisión:** 08/11/2026
**Requerimientos:** letra 6.1, 6.8 y 6.9; RF 28 y RF 36
**Contrato:** [contratos-api.md, sección 10](../../contratos-api.md#10-avisos-a-comercios-letra-69-rf-36)

## Contexto y restricciones

Ante cada cambio de estado, la plataforma notifica al sistema del comercio con una llamada HTTP saliente. La letra exige:

- suscripción por comercio y por tipo de evento;
- firma verificable por el receptor;
- reintentos con espera creciente y cola de fallidos;
- un panel donde el comercio consulte el historial y reenvíe los fallidos;
- un receptor de prueba para demostrar los escenarios de falla.

Además:

- El worker se comunica con el resto solo de forma asíncrona (6.1), y procesar dos veces un mensaje no puede duplicar efectos (6.8).
- El sistema del comercio es externo: puede estar caído o ser lento, y eso no puede afectar la operación.
- El cambio de requerimientos sumó el RF 36: "como los sistemas de los comercios ya reciben avisos con el formato actual, el cambio no puede afectarlos, por lo que el contrato de los avisos deberá versionarse".

## Decisión

Un módulo propio (`Plataforma.Avisos`, esquema `avisos`, con sus migraciones) y la entrega HTTP en el worker:

1. **Una entrega por evento y suscripción.** El worker consume el evento del outbox y crea una `EntregaAviso` por cada suscripción que acepta ese tipo. Un índice único sobre `(EventId, SuscripcionId)` hace idempotente el proceso.
2. **Firma HMAC-SHA256 con el secreto de cada suscripción**, sobre `timestamp.cuerpo`. Los encabezados son `X-Plataforma-Firma` y `X-Plataforma-Timestamp`. El receptor rechaza firmas con más de 5 minutos de diferencia, lo que limita la repetición de un aviso capturado.
3. **Reintentos con colas de espera con TTL** de 5, 15 y 45 s, sin plugins: al vencer, el mensaje vuelve a la cola de entregas. Agotados los reintentos, la entrega queda `Fallido` y el mensaje va a la cola de fallidos.
4. **Reenvío manual** desde el Portal y el Backoffice, con el mismo `EventId`.
5. **Red de seguridad:** un barrido periódico vuelve a encolar entregas pendientes que se quedaron sin mensaje.
6. **Versionado por suscripción.** v1 queda congelado; lo nuevo va en v2. Las suscripciones existentes quedan en v1 por defecto. El encabezado `X-Plataforma-Version` indica la versión, y cada entrega guarda el cuerpo que se firmó, así un reintento manda exactamente lo mismo aunque el comercio cambie de versión.
7. **URL segura:** las URL deben ser https, salvo los hosts permitidos por configuración. Evita usar el worker para llamar servicios internos.

## Alternativas consideradas

| Alternativa | Por qué se descartó |
|---|---|
| Hacer la llamada HTTP desde la API | Un comercio lento afectaría la operación, y la letra pide procesarlo en el worker |
| Reintentos en memoria con Polly | Se pierden si el worker se reinicia, y no dejan una cola de fallidos visible |
| Plugin de mensajes diferidos de RabbitMQ | Exige una imagen propia del broker. Las colas con TTL dan lo mismo con la imagen estándar |
| Una clave de firma global | Una filtración comprometería a todos los comercios, y no se podría rotar por comercio |
| Agregar campos al formato actual, sin versionar | Un receptor estricto se podría romper, y el RF 36 pide no afectarlos |
| Versión en la URL del comercio | Le exigiría al comercio publicar otro endpoint. Elegir la versión en la suscripción no requiere cambios de su lado |

## Consecuencias

**A favor:**

- Un comercio caído no afecta a los demás ni a la operación, y sus avisos se recuperan.
- Los comercios en v1 no notan ningún cambio. Un test compara el JSON exacto de v1.

**Lo que esta decisión dificulta a futuro:**

- Cada versión se mantiene mientras haya una suscripción que la use: un cambio de contrato implica otra rama en `ContratoAvisos`, sus tests y su documentación.
- Cambiar los tiempos de reintento implica cambiar la topología, porque cada espera es una cola.
- Los avisos pueden llegar fuera de orden si uno se reintenta: el comercio debe guiarse por `fecha` y tolerar duplicados por `eventId`.
- El secreto se guarda sin cifrar porque hace falta para firmar: quien acceda a la base podría falsificar avisos.
- Los consumidores deben ignorar los `tipo` que no conocen, porque v2 va a sumar eventos de bulto y de retiro.
- El evento todavía no trae `comercioId`, y el worker lo lee de la tabla `Envios`. Es una dependencia transitoria que hay que quitar cuando el evento lo incluya (6.1).
