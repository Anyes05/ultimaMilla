# ADR 0013: Resolución de conflictos de sincronización

**Estado:** aceptada  
**Responsable:** Álvaro  
**Última revisión:** 11/11/2026  
**Requerimientos:** RF 18, 20, 21, 24, 32; letra 6.11; escenarios 2 y 3 de la defensa

## Contexto

El repartidor trabaja horas sin señal. Mientras tanto, en el servidor pueden pasar cosas que la app no ve:
el despachador reasigna un envío a otra ruta, el operador lo marca extraviado o en devolución, cambia la regla
operativa (por ejemplo, empieza a exigir foto) o apaga la bandera de entrega parcial. Cuando la app recupera
conexión, lo que registró en el teléfono puede ser incompatible con el estado del servidor.

Restricciones:

- La evidencia de una entrega (foto, firma, posición y hora del dispositivo) no se puede perder ni sobrescribir.
- El estado del envío solo cambia por la máquina de estados declarativa (RF 11): ningún cliente lo escribe directo.
- La conexión puede cortarse a mitad de un envío: el mismo lote puede llegar dos veces o la respuesta puede perderse.
- Desde el cambio de requerimientos, el resultado es por bulto (RF 31 y 32), así que hay conflictos a ese nivel.

## Decisión

**El servidor es la fuente de verdad; el teléfono propone eventos y el servidor los aplica o los rechaza con
motivo.** Nunca se sobrescribe ni se descarta nada en silencio.

1. **Eventos inmutables con identificador propio.** Cada entrega, intento fallido o carga se guarda en el teléfono
   (SQLite cifrado) como un comprobante con su `eventoId`, la hora del dispositivo con offset, la posición y la
   evidencia. Un envío tiene como máximo un evento pendiente; la entrega siguiente es otro evento.
2. **Idempotencia durable** (`SincronizarEntregas` + `EventosSincronizacionProcesados`). Si llega un `eventoId` ya
   procesado, se devuelve el mismo resultado sin volver a aplicarlo; así una confirmación perdida se reintenta sin
   duplicar. Un `eventoId` reutilizado para otro envío u otro estado se rechaza.
3. **Validación contra el estado actual del servidor, evento por evento.** Cada evento del lote (hasta 100) se aplica
   o se rechaza de forma independiente; el lote puede volver con aplicados y rechazados a la vez.
4. **Conflictos y cómo se resuelven:**

   | Conflicto | Resolución |
   |---|---|
   | Misma sincronización enviada dos veces (respuesta perdida) | Mismo resultado, sin efectos duplicados |
   | El envío fue reasignado a otra ruta o repartidor mientras estaba sin señal | Rechazado: "El envío no está en tu ruta de hoy". Gana la decisión del despachador |
   | El operador cambió el estado (extraviado, en devolución, ya entregado) | Rechazado por transición inválida de la máquina de estados |
   | "Pedir otra visita" con los intentos agotados | Rechazado: máximo de intentos del operador |
   | La prueba no cumple la regla vigente (firma, foto o documento exigidos) | Rechazado con el motivo; el comprobante queda en el teléfono |
   | Código de carga que no corresponde al envío | Rechazado |
   | **Bulto** ya en estado terminal distinto (entregado, devuelto, extraviado) | Rechazado: no se pisa el resultado de otro bulto ya cerrado |
   | Bulto que no pertenece al envío o repetido en el evento | Rechazado |
   | Entrega parcial con la bandera o la regla del operador apagada | Rechazado; una entrega completa por bultos sí se acepta |
   | Motivo de no entrega fuera del catálogo del operador | Rechazado |
   | Ruta de un día anterior que quedó sin sincronizar | La app la archiva y la sincroniza con su fecha original |
   | El envío cambió en el servidor durante la misma sincronización (versión del envío) | No se guarda nada de ese lote; la app reintenta con los mismos `eventoId` y se vuelve a validar |

5. **En el teléfono, los rechazos no se pierden.** El comprobante queda en estado *Rechazado* con el motivo del
   servidor y su evidencia. No se reenvía en bucle: "Reintentar rechazados" es una acción manual que reenvía el
  mismo comprobante y el mismo `eventoId`, sin editarlo. Cerrar sesión elimina el token, pero conserva
  pendientes y rechazos ligados a la cuenta original; otra cuenta no puede reemplazarlos.
   Después de una sincronización con algo aplicado, la app vuelve a bajar la ruta para quedar igual al servidor.
6. **Trazabilidad.** Todo lo aplicado deja historial con responsable, prueba de entrega con posición y hora del
   dispositivo, y evento en el outbox (incluidos los eventos por bulto), con el mismo identificador de correlación.

## Alternativas descartadas

| Alternativa | Motivo del descarte |
|---|---|
| Gana el último que escribe (el teléfono pisa al servidor) | Desharía decisiones del operador, como una reasignación urgente o un extravío, sin que nadie se entere |
| Gana siempre el servidor y se descarta lo del teléfono | Se perdería evidencia real de una entrega; es justo el reclamo que la letra quiere evitar |
| Fusión automática campo por campo | Los estados no se pueden combinar: "entregado" y "en devolución" no tienen un punto medio válido |
| Bloquear el envío en el servidor mientras está en el teléfono | Sin conexión nunca se libera, y deja al despachador sin poder reasignar |
| Estructuras convergentes (CRDT) | Excesivo para el problema: los conflictos son pocos y necesitan una persona que decida |

## Consecuencias

- **Fácil de defender:** la evidencia nunca se pierde, las reglas de negocio siguen en un solo lugar (la máquina de
  estados y las reglas del operador) y los reintentos por cortes de red son seguros.
- **Los conflictos llegan a una persona:** un rechazo requiere que el repartidor o el operador lo resuelvan; el
  sistema no decide por ellos. El motivo se muestra en cada envío de la app.
- **Lo que dificulta:**
  - La regla operativa se evalúa al sincronizar, no al registrar: una entrega hecha sin foto antes de que el operador
    empezara a exigirla puede rechazarse. Si se quisiera lo contrario, habría que validar contra la regla vigente a
    la hora del dispositivo.
  - Un choque de versión del envío hace fallar el lote entero (hoy responde 500). Se resuelve solo con el reintento,
    pero un `409` explícito con los envíos afectados sería más claro para la app.
  - Un comprobante rechazado no se puede corregir en el teléfono (por ejemplo, agregar la foto que faltaba): hay que
    registrar un evento nuevo.

## Verificación

- Pruebas de la app (`Plataforma.Mobile.Tests`): `RechazoConservaComprobanteYEstadoServidor`,
  `RespuestaIncompletaODuplicadaNoConfirmaEntregas`, `ReloginConservaColaYRechazaOtroOperador` y las de lote limitado a 100.
- Pruebas del servidor (`Plataforma.Infrastructure.Tests`): `SincronizarEntregas_MismoEventoId_EsIdempotente` y
  `EntregaParcialE2ETests`. Pendiente: una prueba dedicada a los rechazos por bulto (bulto terminal, ajeno o repetido).
- Escenario 3 de la defensa: con la app sin señal, el despachador reasigna un envío; el repartidor lo entrega en el
  teléfono; al recuperar conexión la entrega vuelve rechazada con el motivo y la evidencia sigue en el teléfono.
