# ADR 0003: Modo de renderizado de Blazor

**Estado:** aceptada
**Responsable:** Nadia Inthamoussu
**Última revisión:** 08/11/2026
**Requerimientos:** letra 6.2, 6.13 y 8.2

## Contexto y restricciones

El Portal del comercio y el seguimiento público se desarrollan con Blazor, y la letra deja elegir el modo de renderizado analizando sus consecuencias sobre el escalado horizontal, el manejo de estado, la latencia y el consumo de recursos del servidor.

Restricciones que pesan en la decisión:

- La API y las webs corren en al menos dos instancias detrás de un balanceador, y la caída de una no puede cortar el servicio (6.13). Hoy el ingress y el ALB no tienen afinidad de sesión.
- El seguimiento público es "el componente con mayor volumen de tráfico" y el único sin autenticación (letra 4).
- Por diseño, las pantallas web llaman a los casos de uso en memoria, sin HTTP interno.
- Las pantallas del Portal son formularios y listados: no necesitan interacción en cada tecla.

## Decisión

Usar **SSR estático** en todas las páginas del Portal y del seguimiento público: ninguna página ni componente declara `@rendermode`.

- Los formularios son `EditForm` con `FormName` y `[SupplyParameterFromForm]`: un POST normal, con antiforgery, y redirección después de guardar.
- La navegación usa la navegación mejorada de `blazor.web.js`, sin circuito.
- La sesión viaja en una cookie JWT. Las claves de Data Protection se comparten entre réplicas en Redis.
- El tiempo real no lo da Blazor: lo da el hub SignalR del Backoffice, con backplane Redis y solo WebSockets.

`Program.cs` todavía registra los servicios de Interactive Server, pero ninguna página los usa.

## Alternativas consideradas

| Modo | Por qué se descartó |
|---|---|
| **Interactive Server** | Cada usuario conectado mantiene un circuito en una réplica. Exigiría afinidad de sesión en el ALB o el ingress, consumiría memoria por usuario y, al caer una instancia, sus usuarios perderían el circuito. Va en contra de 6.13 y es especialmente caro en el seguimiento público |
| **WebAssembly** | Descarga inicial grande para quien solo quiere ver un envío. Obliga a exponer una API HTTP para cada pantalla y deja el token en el navegador |
| **Auto** | Suma los requisitos de las dos anteriores, con más complejidad para el equipo y sin una necesidad concreta de interactividad |

## Consecuencias

| Aspecto | Efecto |
|---|---|
| **Escalado horizontal** | Cualquier réplica atiende cualquier pedido, sin afinidad de sesión. Se puede bajar una instancia sin cortar una conexión de usuario |
| **Manejo de estado** | No hay estado de usuario en memoria: la sesión está en la cookie y las claves en Redis. El estado del formulario vive en el pedido, así que un error de validación vuelve a dibujar la página |
| **Latencia** | Un viaje por pedido, en vez de un viaje por cada interacción como en Interactive Server. La respuesta llega como HTML completo y se puede cachear el dato de seguimiento |
| **Recursos del servidor** | No hay memoria por usuario conectado: el consumo depende de los pedidos, no de la cantidad de usuarios con la página abierta |

**Lo que esta decisión dificulta a futuro:**

- Cualquier interacción sin recargar (autocompletar, validar mientras se escribe) requiere JavaScript propio o pasar ese componente a Interactive Server, y en ese momento aparece el requisito de afinidad de sesión.
- Actualizar en vivo el estado de un envío en el Portal no se resuelve con Blazor: habría que sumar SignalR.
- Las claves de Data Protection dependen de Redis: si se pierden y las réplicas regeneran claves distintas, los tokens antiforgery de una no los valida otra. Redis necesita persistencia, o las claves otro almacén, si se reinicia con frecuencia.
