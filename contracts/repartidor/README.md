# Contratos del repartidor (aceptados v1)

Acordados entre Álvaro (móvil) y Ana (backend). Los JSON de ejemplo son el
contrato de intercambio; el doble móvil y los endpoints `/api/repartidor/*`
deben respetarlos.

## Operaciones

| Operación | Solicitud | Respuesta |
|---|---|---|
| `POST /api/repartidor/login` | [login.request.json](login.request.json) | [login.response.json](login.response.json) |
| `GET /api/repartidor/rutas/hoy` | Sin cuerpo (Bearer) | [rutas-hoy.response.json](rutas-hoy.response.json) |
| `POST /api/repartidor/sincronizacion` | [sincronizacion.request.json](sincronizacion.request.json) | [sincronizacion.response.json](sincronizacion.response.json) |

## Convenciones

- JSON **camelCase**
- UUID como string
- Fechas de negocio `yyyy-MM-dd` (`DateOnly`)
- Instantes ISO 8601 con offset
- Estados de envío como **texto** (`EnTransito`, no números)
- Campos de ejemplo no nulos
- Fecha fija del juego de prueba: **2026-10-15**

## Decisiones de contrato

### Login
- Identificador: **`usuario`** (mismo criterio que backoffice/portal: nombre de usuario;
  en la demo el valor puede verse como correo).
- Respuesta incluye **`token`** JWT (Bearer). El doble usa un token ficticio
  `demo.jwt.repartidor`; el backend emite JWT real.
- **`rol`**: claim de plataforma en minúsculas (`repartidor`), no el nombre del enum.
- Errores: `401` credenciales inválidas; `403` rol distinto de repartidor.

### Rutas del día
- Lista de `RutaDelDia` (`OperadorVisible` + `EnvioDeRuta`), igual que Application.
- Puede haber 0..N rutas; el cliente elige cómo mostrarlas.
- Requiere Bearer del login.

### Sincronización
- Cada evento lleva **`eventoId`** durable (idempotencia / reintentos offline).
- Campos de comprobante: `nombreReceptor`, `observaciones`, `fechaDispositivo`.
- Respuesta parcial: `aplicados` / `rechazados`.
- **`estadoActual`** en rechazos cuando el envío existe en servidor (conflicto o
  transición inválida). Si el envío no está en la ruta, puede omitirse (`null`).
- Si el envío ya cambió en servidor (p. ej. otro canal), el reintento con el
  mismo `eventoId` se **rechaza** con `estadoActual` (no se sobrescribe).
  La idempotencia durable por `eventoId` queda para una iteración posterior.
- Errores de lote inválido (duplicados, fecha ajena): `400`.

## Errores HTTP mínimos

| Código | Cuándo |
|---|---|
| `400` | JSON/lote inválido |
| `401` | Sin token o credenciales inválidas |
| `403` | Token válido pero rol no repartidor |

Cuerpo de error sugerido: `{ "mensaje": "texto legible" }`.

## Alcance fuera de este contrato

Seguimiento público Redis y webhooks no viven aquí; los define Ana en Application
(`SeguimientoVisible`, claves de cache).
