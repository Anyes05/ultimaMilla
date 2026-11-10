# ADR 0010: Caché de lectura (Redis)

**Estado:** aceptada  
**Responsable:** Ana  
**Última revisión:** 09/11/2026  
**Requerimientos:** letra 6.5; aclaración 3.1 (tarifas); banderas 30 s

## Contexto

Seguimiento público, rutas del día, tarifas y banderas se leen mucho más de lo que se escriben. Redis puede caer sin frenar la operación.

## Decisión

1. Caché **solo de lectura** con TTL (`ICacheLectura`).
2. Claves versionadas en `ClavesCache` (seguimiento, rutas hoy, tarifa vigente, banderas, operador).
3. **Invalidación explícita** en escrituras: despacho, sync, inicio de ruta, cambio de estado, publicar tarifa, cambiar bandera.
4. Si Redis falla: degradar a base; `/health` queda `Degraded`.
5. Banderas: cache corto (30 s) + invalidación al cambiar.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Caché write-through de dominio | Riesgo de inconsistencia entre réplicas |
| Sin invalidación, solo TTL largo | Rutas stale (problema de Álvaro) |

## Consecuencias

Al cambiar estado o iniciar ruta se invalida `rutas:hoy:{operador}:{repartidor}:{fecha}`. Al publicar tarifas se quita `tarifa:vigente`.
