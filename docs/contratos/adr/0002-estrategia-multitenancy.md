# ADR 0002: Estrategia de multitenancy

**Estado:** aceptada (implementación detallada en el ADR 0009)  
**Responsable:** Equipo (presentado en el documento de diseño)  
**Fecha:** 04/10/2026  
**Requerimientos:** letra 6.5, 8.1

> Registro presentado en la entrega de análisis y diseño del 4/10/2026 ("Estrategia de multitenancy"). Se transcribe
> aquí para que el conjunto de registros de decisión quede completo en el repositorio. Al final se indica qué cambió
> en la implementación; el detalle vigente está en el [ADR 0009](0009-multitenancy.md).

## Contexto

La plataforma atiende a múltiples operadores logísticos desde un único despliegue. Cada operador es un inquilino y
debe permanecer aislado: no puede consultar ni modificar datos de otro operador. Los comercios existen dentro del
contexto de un operador. Los usuarios de un operador acceden a todos sus comercios; los usuarios de comercio solo a
su información dentro del operador correspondiente.

La solución es un monolito modular en .NET 10, con dos réplicas web en Amazon EKS, una única instancia privada de
Amazon RDS PostgreSQL, Redis como caché distribuido y RabbitMQ para el procesamiento asíncrono con un worker
independiente.

## Decisión

Se adopta **aislamiento por discriminador de fila** en una única base PostgreSQL y un único esquema:

1. **`OperadorId` en toda entidad que pertenece a un operador.**
2. **Filtros globales de consulta de Entity Framework Core** desde el `DbContext`, con el inquilino actual. Así el
   aislamiento no depende de que cada desarrollador recuerde filtrar en cada consulta.
3. **El inquilino se resuelve en tiempo de ejecución desde los claims de autenticación**, nunca desde parámetros
   controlados por el cliente. En el seguimiento público, el contexto se deriva de un identificador opaco del envío
   validado por la aplicación.
4. **Un interceptor de `SaveChanges` protege las escrituras:** asigna `OperadorId` a las entidades nuevas y rechaza
   altas o modificaciones cuando el operador de la entidad no coincide con el contexto activo. Los filtros protegen
   las lecturas; el interceptor, las escrituras.
5. **Cada mensaje de RabbitMQ incluye `OperadorId`.** El worker no recibe pedidos HTTP, así que establece el contexto
   del inquilino a partir del mensaje antes de procesarlo.
6. **Las claves de Redis llevan prefijo de operador**, para que el caché no entregue datos de un operador a otro.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Una base de datos por operador | Mayor costo, múltiples migraciones, aprovisionamiento por operador y una complejidad innecesaria para el alcance del laboratorio |
| Un esquema de PostgreSQL por operador | Complica el modelo de Entity Framework Core, las migraciones y el despliegue |
| Filtros manuales en cada consulta | El aislamiento dependería de la disciplina individual y sería propenso a errores |

## Consecuencias

- Un único despliegue, una única instancia de RDS y una única cadena de migraciones. Dar de alta un operador es una
  operación de la aplicación, no de la infraestructura, y el costo no crece con la cantidad de operadores.
- **Riesgos:** el uso indebido de `IgnoreQueryFilters`, SQL crudo sin filtro, mensajes de RabbitMQ sin contexto de
  operador y claves de Redis sin prefijo. Se mitigan con convenciones, revisiones y pruebas automáticas.
- **Pruebas con al menos dos operadores:** un usuario del operador A no puede leer ni modificar datos del operador B,
  y un usuario de comercio solo accede a lo suyo.
- Si un operador exigiera aislamiento físico, el uso consistente de `OperadorId` permitiría extraer sus datos a una
  base dedicada y elegir la cadena de conexión por inquilino, sin rediseñar el dominio.

## Qué cambió en la implementación

Comprobado contra el código el 15/11/2026:

| Diseño del 4/10 | Implementación |
|---|---|
| `OperadorId` en las entidades del operador | Igual |
| Filtros globales de EF Core | Igual, aplicados entidad por entidad (`AplicarFiltroOperador<T>`), sin una interfaz común `IOperatorScoped` |
| Inquilino desde los claims | Igual, a través de `IOperadorActual` |
| Interceptor de `SaveChanges` | Igual: `OperadorSaveChangesInterceptor` completa `OperadorId` en las altas e impide escribir datos de otro operador |
| `OperadorId` en cada mensaje | Igual: cada mensaje del outbox lleva su `OperadorId` |
| Prefijo de operador en Redis | Igual (`operador:{id}:…`) para los datos de un operador. Las claves del seguimiento público usan el token o el número de seguimiento, que ya identifican un único envío |
| Relación `OperadorComercio` (un comercio con varios operadores) | Simplificada: **cada comercio pertenece a un solo operador** (`Comercio.OperadorId`). Un mismo comercio que trabaje con dos operadores se da de alta en cada uno |
| Pruebas de aislamiento con dos operadores | Implementadas y en el pipeline (Norte y Sur) |
