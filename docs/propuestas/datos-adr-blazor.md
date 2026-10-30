# Datos para el ADR "Modo de renderizado de Blazor" (letra 6.2 y 8.2)

Para Ana, que redacta el ADR. Estos son **hechos del código**, verificados el 30/10/2026; las decisiones
y alternativas quedan para el ADR.

## Cómo está hoy

| Aspecto | Situación | Dónde |
|---|---|---|
| Tipo de app | Blazor Web App (.NET 10) | `src/Plataforma.PortalComercio` |
| Modo registrado | `AddInteractiveServerComponents()` y `AddInteractiveServerRenderMode()` | `Program.cs` |
| Modo usado por las páginas | **SSR estático**: ninguna página ni componente declara `@rendermode` | `Components/Pages/*.razor` |
| Formularios | `EditForm` con `FormName` y `[SupplyParameterFromForm]`: POST normal y respuesta HTML del servidor (PRG con `NavigateTo(forceLoad: true)`) | `Home.razor`, `Avisos.razor` |
| Antiforgery | `UseAntiforgery()` con claves de Data Protection compartidas | `Program.cs` |
| Claves de Data Protection | En **Redis** (`plataforma:portal:data-protection`), compartidas entre las 2 réplicas | `Program.cs` |
| Afinidad de sesión (sticky) | **No hay** en el Ingress NGINX ni en el ALB | `infra/terraform/k8s/base/ingress.yaml` |
| Estado en memoria del servidor | Ninguno por usuario: sin circuitos SignalR de Blazor | — |
| Tiempo real | Lo da el Backoffice (Razor Pages) con un hub SignalR propio y backplane Redis; clientes solo WebSockets (`skipNegotiation`), así no necesita sticky | `HubAvisosOperador`, `Tablero.cshtml` |

## Consecuencias medibles de usar SSR estático

- **Escalado horizontal:** cualquier réplica atiende cualquier request (no hay circuitos que fijen usuario y pod).
  Se puede bajar una réplica sin cortar a nadie (escenario 1 de la defensa).
- **Estado:** nada en memoria; la sesión está en la cookie JWT y las claves en Redis.
- **Latencia:** un viaje por request (POST/GET); no hay ida y vuelta por cada clic como en Interactive Server.
- **Recursos del servidor:** sin memoria por circuito abierto. Esto importa sobre todo en el **seguimiento
  público** (letra 4: "el componente con mayor volumen de tráfico"), que también se hace SSR estático en el
  Portal y se puede cachear.

## Qué cambiaría con las otras opciones (para "alternativas descartadas")

| Modo | Lo que exigiría acá |
|---|---|
| Interactive Server | Afinidad de sesión en el ALB o NGINX (o Azure SignalR); memoria por usuario conectado; al caer un pod, los usuarios pierden el circuito y reconectan |
| WebAssembly | Descarga inicial grande; la lógica del cliente necesita API HTTP (hoy las pantallas usan los casos de uso en memoria, como pide el diseño); el token queda en el navegador |
| Auto | Las dos cosas anteriores juntas, más complejidad para el equipo |

## Lo que la decisión dificulta a futuro

- Una interacción sin recargar (por ejemplo, autocompletar) requiere convertir ese componente a Interactive
  Server, y en ese momento aparece el requisito de afinidad de sesión.
- La interactividad local se hace con JavaScript mínimo, como en el Backoffice.
