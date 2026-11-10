# Contratos del repartidor — borrador v2 (sincronización por bulto)

**Estado:** **acordado** Ana + Álvaro (03/11/2026). Listo para que Álvaro implemente offline por bulto.  
**v1 permanece inmutable.** El servidor (`SincronizarEntregas`) aplica `bultos[]` cuando viene en el evento; ausente/vacío = v1.

## Qué agrega v2

Cada evento de sincronización puede incluir `bultos[]` con el resultado **por bulto**.  
Si `bultos` está ausente o vacío, el servidor aplica el comportamiento **v1** (estado a nivel envío).

| Campo nuevo | Tipo | Notas |
|---|---|---|
| `bultos` | array | Opcional; resultados individuales |
| `bultos[].bultoId` | UUID | Identificador del bulto |
| `bultos[].estado` | texto | `Entregado`, `NoEntregado`, `PendienteNuevaVisita`, … |
| `bultos[].motivoNoEntrega` | string | Obligatorio si no se entregó |
| `bultos[].foto` / `firma` | string | PoD asociado al bulto entregado (opcional) |

El estado del **envío** en la respuesta sigue siendo el **derivado** de los bultos (`Entregado`, `EntregadoParcialmente`, etc.).

Ejemplo: [sincronizacion.request.json](sincronizacion.request.json).

## Política de conflictos por bulto (acuerdo)

1. **Mismo `eventoId` ya aplicado** → se responde en `aplicados` sin mutar (idempotencia, igual que v1).
2. **Bulto ya `Entregado` / `Devuelto` / `Extraviado` en servidor** y el dispositivo manda otro resultado → **rechazo** con `estadoActual` del envío; no se sobrescribe el bulto.
3. **Transición de bulto inválida** (p. ej. `Entregado` → `EnRuta`) → rechazo del evento completo del envío.
4. **Operador no admite parcial** y el lote deja algunos bultos entregados y otros no → rechazo con motivo de regla operativa.
5. **Conflicto entre bultos del mismo evento** (dos resultados para el mismo `bultoId`) → `400` de lote inválido.
6. Tras parcial, bultos en `PendienteNuevaVisita` quedan **disponibles para una nueva ruta** (RF 34); el envío puede quedar `EntregadoParcialmente` o `Reprogramado` según la derivación.

## Relacionado (cerrado aparte)

- **PoD por regla** (`ExigeFirma` / `ExigeFoto`): el servidor lo valida en `SincronizarEntregas`.
- **Avisos v2** (parcial, bulto, retiro): `contracts/mensajes/v2/`.
- **Retiro en domicilio** (RF 37–41): API Portal/Backoffice `/api/retiros`; no forma parte del sync del repartidor v2.
- Escaneo de carga multi-bulto: pendiente de acuerdo con Álvaro.
