# ADR 0001: Arquitectura de cebolla

**Estado:** aceptada (ampliada por el ADR 0008)  
**Responsable:** Equipo (presentado en el documento de diseño)  
**Fecha:** 04/10/2026  
**Requerimientos:** letra 6.1, 6.15, 8.1

> Registro presentado en la entrega de análisis y diseño del 4/10/2026. Se transcribe aquí para que el conjunto de
> registros de decisión quede completo en el repositorio. La organización actual, con la mensajería entre procesos,
> está en el [ADR 0008](0008-estilo-arquitectonico.md).

## Contexto

La letra deja libre el estilo interno y fija tres condiciones (sección 6.1): la lógica de dominio no depende de
infraestructura, acceso a datos ni presentación; esa regla está cubierta por una prueba de arquitectura que falla si
se rompe; y el estilo, con su organización de proyectos, queda justificado en un registro de decisión.

El despliegue pedido es un monolito modular para la API y las aplicaciones web, más un worker independiente que solo
se comunica por cola. En el documento de diseño eso ya está resuelto como una imagen web y otra del worker, con
PostgreSQL, Redis y RabbitMQ afuera del dominio.

El dominio empuja en la misma dirección:

- El envío tiene una máquina de estados declarativa (requisito 11): las transiciones válidas viven en un solo lugar y
  nadie puede cambiar el estado por fuera.
- Las tarifas y las reglas operativas se configuran en caliente y quedan versionadas (sección 6.6).
- Varios frentes (backoffice, portal, seguimiento, aplicación del repartidor y worker) tienen que aplicar las mismas
  reglas.
- El 29 de octubre entra un cambio de requerimientos no anunciado, y la letra advierte que una solución con la lógica
  dispersa no lo absorbe a tiempo.

## Decisión

Se adopta **arquitectura de cebolla** como estilo interno del monolito modular. Las dependencias apuntan hacia el
centro. El dominio no conoce Entity Framework, RabbitMQ, Redis, Razor, Blazor ni MAUI.

| Anillo | Proyecto | Qué contiene | De qué depende |
|---|---|---|---|
| Centro | Plataforma.Domain | Entidades, máquina de estados, invariantes | De nadie |
| Aplicación | Plataforma.Application | Casos de uso y puertos (`IEnvioRepositorio`) | Solo de Domain |
| Infraestructura | Plataforma.Infrastructure | EF Core, repositorios, y más adelante Redis, RabbitMQ y outbox | De Application y Domain |
| Exterior | Plataforma.Backoffice, Plataforma.PortalComercio, y luego API, MAUI y worker | Pantallas y proceso de arranque | De Application; de Infrastructure solo para componer |

Eso queda en los `.csproj`: Domain no referencia ningún otro proyecto; Application solo referencia Domain;
Infrastructure referencia Application y Domain, y es el único anillo con paquetes de EF Core. Backoffice y Portal
llaman a `AddInfrastructure()` en `Program.cs`, que es la raíz de composición: el único lugar que puede conocer todas
las capas.

La prueba `Domain_NoDeberiaDependerDe_OtrasCapas` en `Plataforma.Architecture.Tests`, hecha con NetArchTest, falla
si Domain referencia Application, Infrastructure, Backoffice o Portal. Con eso queda cubierta la segunda condición de
la sección 6.1.

**Por qué esta forma le sirve a la letra:**

- **Un dominio, varias aplicaciones.** Backoffice, portal, seguimiento, app del repartidor y worker ejecutan el mismo
  ciclo del envío; cada host es un anillo exterior distinto sobre los mismos ensamblados internos.
- **La máquina de estados queda cerrada.** `TransicionesEnvio` declara las transiciones válidas y
  `Envio.CambiarEstado` es el único camino para mover un envío. Entregado, Devuelto y Extraviado no tienen salida.
- **Las reglas se prueban sin infraestructura.** `Envio` y `TransicionesEnvio` no necesitan base de datos, host web
  ni Docker (sección 6.15).
- **Lo configurable queda afuera de la presentación.** Calcular la tarifa es dominio; guardar la versión es
  infraestructura. Cambiar una tarifa o una regla (escenario 5 de la defensa) no obliga a tocar pantallas ni a
  redesplegar.
- **El aislamiento por operador tiene un lugar para cada parte.** `OperadorId` es un dato del dominio; el filtro
  global, el interceptor de `SaveChanges` y el prefijo de operador en Redis son infraestructura (ADR 0002).
- **El cambio del 29 de octubre tiene un lugar previsible:** una regla nueva entra en Domain o en un caso de uso.
- **Los opcionales no obligan a reescribir el núcleo:** el historial en MongoDB o un modelo de lectura para el
  seguimiento son adaptadores externos.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Capas clásicas con el acceso a datos abajo | La lógica termina dependiendo del repositorio concreto o de EF Core; incumple la primera condición de la sección 6.1 y obliga a levantar una base para probar una transición de estado |
| Microservicios por aplicación o por entidad | La letra pide monolito modular más un worker. Con cuatro integrantes y una máquina de estados que debe ser única, partir el dominio multiplica contratos, despliegues y el riesgo de dos transiciones distintas para el mismo envío |
| Carpetas por funcionalidad, sin frontera de proyectos | Más rápido para una pantalla, pero deja la máquina de estados, la tarifa y el filtro de inquilino escritos en la página o el endpoint, y la prueba de arquitectura no tendría una frontera que vigilar |
| Arquitectura limpia con un proyecto por anillo fino, o hexagonal con un adaptador por tecnología desde el día uno | Comparten la regla de dependencias, pero agregan ceremonia (mediador, un DTO por operación, un proyecto por puerto) antes de tener los casos de uso. La cebolla con cuatro anillos coincide con los módulos del documento de diseño |

## Consecuencias

- Un alta simple cruza entidad, caso de uso, mapeo y pantalla. El costo es deliberado: la regla queda en un solo
  lugar y las aplicaciones la reutilizan.
- `Program.cs` referencia Infrastructure para registrar EF Core y los repositorios. Las páginas tienen que depender
  de los casos de uso: si una página usa el `DbContext` directo, el estilo se vacía aunque Domain siga limpio.
  Conviene ampliar la prueba de arquitectura para que Application tampoco referencie Infrastructure ni los hosts.
- Entity Framework necesita un constructor vacío en `Envio`. Ese constructor puede existir en el dominio; el paquete
  de EF Core no.
- Si la interfaz de usuario llama a `CambiarEstado` por su cuenta, se salta el caso de uso, la auditoría y el outbox.
  El borde estable para las pantallas y el worker son los casos de uso.
- Un cambio en el ensamblado de dominio recompila todos los hosts; en un monolito es aceptable.
- Esta decisión no resuelve por sí sola la tenencia, el caché ni el outbox: siguen siendo registros aparte (ADR
  0002, 0010 y 0011).
