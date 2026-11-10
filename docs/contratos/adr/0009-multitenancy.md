# ADR 0009: Multitenancy por operador

**Estado:** aceptada  
**Responsable:** Ana  
**Última revisión:** 09/11/2026  
**Requerimientos:** letra 6.3; aislamiento Norte/Sur

## Contexto

Cada operador logístico (Norte, Sur, …) es un inquilino. Los datos no pueden mezclarse entre operadores.

## Decisión

1. Columna `OperadorId` en entidades de negocio.
2. **Global query filters** de EF según `IOperadorActual` (claim JWT / cookie).
3. Casos de uso reciben `operadorId` explícito y validan pertenencia.
4. Admin de plataforma opera sin filtro de operador (Identity §3.1).

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Base por operador | Costo y migraciones inviables en el lab |
| Schema por operador | Complejidad operativa sin beneficio medible |

## Consecuencias

Un bug de filtro se mitiga con tests de aislamiento (`AislamientoOperadorTests`). Las lecturas cross-tenant del worker usan SQL parametrizado acotado.
