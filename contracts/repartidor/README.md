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
| `POST /api/repartidor/dispositivos` | [dispositivos.request.json](dispositivos.request.json) | [dispositivos.response.json](dispositivos.response.json) |
| `DELETE /api/repartidor/dispositivos/{token}` | Token en la ruta (Bearer) | `204` sin cuerpo |

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
- Si el envío ya cambió en servidor por **otro** canal (transición inválida), el
  reintento se **rechaza** con `estadoActual` (no se sobrescribe).
- Si el mismo `eventoId` ya fue **aplicado** (OK perdido / reintento offline),
  el servidor responde de nuevo en `aplicados` sin volver a mutar (idempotencia durable).
- Errores de lote inválido (duplicados, fecha ajena): `400`.

#### Extensión móvil 29/10 (revisión Ana)

Campos opcionales añadidos a cada evento, sin invalidar los payloads v1:
`foto` (JPEG base64 puro, sin prefijo data URI, máximo 512 KiB decodificados),
`firma` (string JSON con arreglo de trazos/puntos, máximo 64 KiB),
`latitud` y `longitud` (decimales, ambas o ninguna), `codigoEscaneado` (número
de seguimiento). `fechaDispositivo` conserva el offset original.
El servidor persiste estos campos en `PruebasDeEntrega` antes de confirmar
`aplicados`; no son adjuntos ficticios ni URLs temporales. Ausencia de evidencia
continúa siendo válida para clientes anteriores. Foto/firma inválida o posición
incompleta produce rechazo del evento, no éxito parcial de su evidencia.

- Carga: `estado: "EnTransito"`, desde `AsignadoARuta`; el código opcional debe
  coincidir con `numeroSeguimiento`. No existe un estado de dominio `Cargado`.
- Intento fallido: `estado: "NoEntregado"` desde `EnTransito`; `observaciones`
  debe contener el motivo. Se incrementa el contador y persiste `IntentosEntrega`
  con fecha del dispositivo y motivo, junto con comprobante y evento idempotente.
- Reintentar el mismo evento aplicado no duplica prueba ni intento. Reutilizar
  su identificador con otro envío/estado se rechaza.
- Máximo 100 eventos por lote. No se envían automáticamente eventos rechazados.

**Dependencias para revisar por Ana:** se agregaron `IIntentoEntregaRepositorio`
y su implementación EF (registro DI), se extendieron `ComprobanteEntrega` y
`PruebaDeEntrega`, y migración aditiva `20261029210000_EvidenciaCargaMovil`
(columna nullable `CodigoEscaneado`). Foto, firma y coordenadas ya tenían columnas.
No se agregó paquete backend ni se aplicó migración a bases existentes/AWS.
Revisar retención de datos sensibles, tamaño de requests/proxy, autorización por
ruta y almacenamiento de imágenes en texto frente a object storage antes de
producción. El servidor no interpreta puntos de firma como firma legal ni verifica
la hora/GPS del dispositivo; son evidencia declarada por el cliente.

Validación ejecutada (29/10): tests backend focalizados `EvidenciaMovilTests`,
`DespachoYCacheTests`, `Migraciones*` y build Backoffice `--no-restore`.
Sin conexión ni migración contra PostgreSQL real, sin desplegar. Build mostró
advertencia preexistente NU1902 de MessagePack 2.5.302; no se modificó ese paquete.
`src/Plataforma.Backoffice/Repartidor.http` ofrece ejemplos locales; no se
ejecutaron requests contra servicios externos.

## Errores HTTP mínimos

| Código | Cuándo |
|---|---|
| `400` | JSON/lote inválido |
| `401` | Sin token o credenciales inválidas |
| `403` | Token válido pero rol no repartidor |

Cuerpo de error sugerido: `{ "mensaje": "texto legible" }`.

### Dispositivos (FCM / push)
- `POST` registra o reutiliza el token del dispositivo del repartidor autenticado.
- `plataforma`: `android` | `ios` | `web`.
- `DELETE /dispositivos/{token}` elimina el registro (`204` si existía, `404` si no).
- Requiere Bearer de repartidor.

## Alcance fuera de este contrato

Seguimiento público Redis y webhooks no viven aquí; los define Ana en Application
(`SeguimientoVisible`, claves de cache). Mensajes de dominio: `contracts/mensajes/v1/`.
