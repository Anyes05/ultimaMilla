# ADR 0012: Cambio de requerimientos (versionado y banderas)

**Estado:** aceptada  
**Responsable:** Ana  
**Última revisión:** 09/11/2026  
**Requerimientos:** RF 36; entrega parcial; retiros; banderas

## Contexto

El cambio de requerimientos sumó parciales, bultos, retiros y avisos nuevos sin romper comercios ya integrados ni operadores que no activan la función.

## Decisión

1. **Contrato de avisos versionado**: v1 congelado; v2 agrega `EntregadoParcialmente`, `BultoEstadoCambiado`, `RetiroSolicitado`, `RetiroCompletado`.
2. Suscripción elige versión y tipos; v1 nunca recibe tipos solo-v2.
3. **Banderas por operador** (`CatalogoBanderas`) con cache 30 s: entregan parcial, retiro domicilio, avisos, etc.
4. Sync con `bultos[]` exige bandera de parcial + reglas PoD (`ExigeFirma` / `ExigeFoto`).
5. Transiciones 10/11: “Pedir otra visita” = `Reprogramado` (solo con intentos); al agotar intentos tras `NoEntregado` → paso automático a `EnDevolucion`.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Ampliar v1 con campos opcionales | Rompe “el cambio no puede afectar” |
| Feature flags globales | Necesitamos granularidad por operador |

## Consecuencias

Nadia puede armar pantallas contra APIs ya versionadas. Álvaro sigue en contratos repartidor v1/v2 sin cambios móviles forzados para Identity (Opción A).
