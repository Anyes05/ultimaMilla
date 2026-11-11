# Contratos de API — Última Milla

**Versión:** v1 (acordada entre Álvaro — móvil — y Ana — backend)  
**Fuente de verdad:**

- Ejemplos JSON: [`contracts/repartidor/`](../contracts/repartidor/README.md)
- DTOs de aplicación: `src/Plataforma.Application/RutaDelDia.cs`
- Endpoints: `src/Plataforma.Backoffice/RepartidorApi.cs`

> Si un ejemplo JSON y este documento difieren, manda el endpoint real y el ejemplo versionado en `contracts/`. No se deben cambiar nombres de campos ni tipos sin avisar al equipo (cambio *breaking*).

---

## 1. Convenciones generales

| Tema | Convención |
|---|---|
| Formato | JSON `camelCase` |
| IDs | UUID como string (`"11111111-1111-..."`) |
| Fechas de negocio | `yyyy-MM-dd` (`DateOnly`) |
| Instantes | ISO 8601 con offset (ej. `2026-10-15T12:00:00-03:00`) |
| Estados de envío | Texto, no números (ej. `"EnTransito"`) |
| Base URL | Por configuración (`{{baseUrl}}`). Nunca hardcodear localhost ni la URL pública en el cliente |
| Content-Type | `application/json` |

### Autenticación

- Esquema: **Bearer JWT**
- Header: `Authorization: Bearer <token>`
- El token lo emite `POST /api/repartidor/login` (repartidor) o el login web de backoffice (roles de gestión).
- **Vigencia del JWT:** 12 horas.
- Claims relevantes:

| Claim | Significado |
|---|---|
| `sub` | Id del usuario |
| `unique_name` | Nombre de usuario |
| `operador_id` | Operador (tenant) del usuario autenticado |
| `role` | Rol de plataforma en minúsculas: `administrador`, `despachador`, `operario_deposito`, `comercio`, `repartidor` |
| `repartidor_id` | Solo en sesión de repartidor: id del perfil repartidor |
| `comercio_id` | Solo en sesión de comercio |

El `OperadorId` **siempre** sale del token, nunca del formulario ni del cuerpo del request.

### Formato de error

Cuando el endpoint devuelve cuerpo de error, usa:

```json
{ "mensaje": "Texto legible para el usuario." }
```

| Código | Cuándo |
|---|---|
| `200` | OK |
| `400` | JSON o lote inválido (duplicados, datos fuera de regla) |
| `401` | Sin token, token inválido o credenciales inválidas |
| `403` | Token válido pero rol no autorizado (o sin perfil activo) |
| `404` | Recurso no encontrado (seguimiento por token) |

---

## 2. Catálogo de endpoints

| # | Método y ruta | Rol | Descripción |
|---|---|---|---|
| 1 | `POST /api/repartidor/login` | Público (credenciales) | Login del repartidor; emite JWT |
| 2 | `GET /api/repartidor/rutas/hoy` | `repartidor` | Hoja de ruta del día |
| 3 | `POST /api/repartidor/sincronizacion` | `repartidor` | Subir comprobantes offline |
| 4 | `GET /api/repartidores` | `despachador`, `administrador`, `operario_deposito` | Lista para el selector de despacho |
| 5 | `POST /api/despacho` | `despachador`, `administrador`, `operario_deposito` | Crear ruta + paradas y asignar envíos |
| 6 | `GET /api/seguimiento/{token}` | Público (token de seguimiento) | Seguimiento visible de un envío |
| 7 | `GET /tecnico/cache` | `administrador` | Métrica de aciertos/fallos del caché |
| 8 | `POST /api/repartidor/dispositivos` | `repartidor` | Registrar token FCM del dispositivo |
| 9 | `DELETE /api/repartidor/dispositivos/{token}` | `repartidor` | Quitar token del dispositivo |
| 10 | `POST /api/envios/{envioId}/estado` | `despachador`, `administrador`, `operario_deposito` | Cambiar estado (genera outbox + `eventId`) |
| 11 | `POST /api/retiros` | `comercio` | Solicitar retiro en domicilio (RF 37–41; bandera `retiro-domicilio`) |
| 12 | `GET /api/retiros` | `comercio`, ops | Listar retiros (comercio: solo los propios) |
| 13 | `POST /api/retiros/{id}/programar` | `despachador`, `administrador` | Asignar retiro a una ruta |
| 14 | `POST /api/retiros/{id}/completar` | ops depósito/despacho | Marcar retiro completado (+ outbox `RetiroCompletado`) |
| 15 | `POST /api/retiros/{id}/cancelar` | `comercio`, ops | Cancelar retiro |
| 16 | `GET /api/plataforma/banderas` | `administrador_plataforma` | Banderas de todos los operadores con su valor actual |
| 17 | `POST /api/plataforma/banderas` | `administrador_plataforma` | Activar o apagar una bandera de un operador (`{ operadorId, clave, activa }`); responde `204` |
| 18 | `GET /api/plataforma/banderas/historial?operadorId=` | `administrador_plataforma` | Auditoría de cambios: operador, clave, valor, usuario y fecha |
| 19 | `POST /api/repartidor/posiciones` | `repartidor` | Posiciones de la jornada en lote (RF 23); ver §3.4 |

---

## 3. Endpoints del repartidor

### 3.1 `POST /api/repartidor/login`

Autentica un usuario con rol repartidor y devuelve la sesión con JWT.

**Request**

```json
{
  "usuario": "alvaro@demo.test",
  "clave": "Demo123!"
}
```

Archivo: `contracts/repartidor/login.request.json`

| Campo | Tipo | Requerido | Notas |
|---|---|---|---|
| `usuario` | string | Sí | Nombre de usuario (en la demo puede verse como correo) |
| `clave` | string | Sí | Clave en texto plano solo sobre HTTPS / red local de dev |

**Response `200`**

```json
{
  "repartidorId": "10000000-0000-0000-0000-000000000001",
  "operadorId": "11111111-1111-1111-1111-111111111111",
  "nombre": "Alvaro - demostracion",
  "rol": "repartidor",
  "token": "<jwt>"
}
```

Archivo: `contracts/repartidor/login.response.json` (el `token` de ejemplo es ficticio; el backend emite un JWT real).

| Campo | Tipo | Notas |
|---|---|---|
| `repartidorId` | UUID | Perfil repartidor activo |
| `operadorId` | UUID | Operador al que pertenece |
| `nombre` | string | Nombre visible |
| `rol` | string | Siempre `"repartidor"` en este endpoint |
| `token` | string | JWT para `Authorization: Bearer` |

**Errores**

| Código | Caso |
|---|---|
| `401` | Credenciales inválidas |
| `403` | Usuario sin rol repartidor, o sin perfil repartidor activo |

---

### 3.2 `GET /api/repartidor/rutas/hoy`

Devuelve las rutas del día del repartidor autenticado. Los `envios` de cada ruta vienen **en orden de visita** (RF 16): urgentes primero, después agrupados por zona y, dentro de cada grupo, en el orden en que el despachador los eligió. La app los recorre en ese orden.

**Headers:** `Authorization: Bearer <token repartidor>`

**Query string**

| Parámetro | Tipo | Requerido | Notas |
|---|---|---|---|
| `fecha` | `yyyy-MM-dd` | No | Por defecto, hoy (UTC) |

**Response `200`:** lista de `RutaDelDia` (puede ser vacía, `[]`).

```json
[
  {
    "id": "30000000-0000-0000-0000-000000000001",
    "fecha": "2026-10-15",
    "operador": {
      "nombre": "Operador de demostracion",
      "colorMarca": "#512BD4",
      "maximoIntentos": 3,
      "exigeFirma": false,
      "exigeFoto": false,
      "exigeDocumentoReceptor": false
    },
    "envios": [
      {
        "id": "40000000-0000-0000-0000-000000000001",
        "numeroSeguimiento": "DEMO-001",
        "destinatario": "Destinatario de prueba 1",
        "direccionEntrega": "Calle Demo 101",
        "emailDestinatario": "destinatario1@example.test",
        "estado": "EnTransito",
        "tarifaCalculada": 100.00,
        "intentosRealizados": 0
      }
    ]
  }
]
```

Archivo completo: `contracts/repartidor/rutas-hoy.response.json`

**Reglas**

- El cliente puede recibir **0..N** rutas; decide cómo mostrarlas.
- Solo rutas del `operador_id` + `repartidor_id` del token.
- La primera lectura puede venir de base; las siguientes, del caché de rutas del día.

**Errores:** `401` sin token válido de repartidor.

---

### 3.3 `POST /api/repartidor/sincronizacion`

Sube la cola offline de comprobantes. La respuesta es **parcial**: cada evento va a `aplicados` o `rechazados`.

**Headers:** `Authorization: Bearer <token repartidor>`

**Request**

```json
{
  "fecha": "2026-10-15",
  "eventos": [
    {
      "eventoId": "50000000-0000-0000-0000-000000000001",
      "envioId": "40000000-0000-0000-0000-000000000001",
      "estado": "Entregado",
      "nombreReceptor": "Receptor de prueba 1",
      "observaciones": "",
      "fechaDispositivo": "2026-10-15T12:00:00-03:00"
    }
  ]
}
```

Archivo completo: `contracts/repartidor/sincronizacion.request.json`

| Campo | Tipo | Notas |
|---|---|---|
| `fecha` | `yyyy-MM-dd` | Fecha de la ruta a la que pertenecen los eventos |
| `eventos[].eventoId` | UUID | Id durable del comprobante en el dispositivo; se reutiliza en reintentos |
| `eventos[].envioId` | UUID | Envío de la ruta |
| `eventos[].estado` | enum string | Estado que el móvil quiere aplicar |
| `eventos[].nombreReceptor` | string | Requerido si `estado` es `Entregado`; máx. 100 caracteres |
| `eventos[].documentoReceptor` | string | Opcional. Documento de quien recibe; **requerido en una entrega si la regla del operador lo exige** (si falta, el evento vuelve en `rechazados` con el motivo). La ruta del día lo informa en `operador.exigeDocumentoReceptor` (§3.2) |
| `eventos[].observaciones` | string | Máx. 500 caracteres |
| `eventos[].fechaDispositivo` | datetime con offset | Hora del teléfono al registrar |

Reglas operativas que afectan la sincronización (RF 4): si la política de devolución es "Esperar decisión del operador",
al agotar los intentos el envío queda `NoEntregado` en lugar de pasar solo a `EnDevolucion`; el operador decide desde Monitoreo.

**Response `200`**

```json
{
  "aplicados": [
    { "envioId": "40000000-0000-0000-0000-000000000001", "estado": "Entregado", "intentosRealizados": 0 }
  ],
  "rechazados": [
    {
      "envioId": "40000000-0000-0000-0000-000000000002",
      "motivo": "El servidor informa Entregado; no se sobrescribe.",
      "estadoActual": "Entregado"
    }
  ]
}
```

Archivo completo: `contracts/repartidor/sincronizacion.response.json`

| Campo | Tipo | Notas |
|---|---|---|
| `aplicados[].envioId` | UUID | |
| `aplicados[].estado` | enum string | Estado final en servidor |
| `aplicados[].intentosRealizados` | int | Intentos acumulados |
| `rechazados[].envioId` | UUID | |
| `rechazados[].motivo` | string | Motivo legible |
| `rechazados[].estadoActual` | enum string \| `null` | Estado autoritativo si el envío existe; puede ser `null` si no está en la ruta |

**Política de conflicto (acordada)**

- Si el envío sigue `EnTransito` y la transición es válida → **se acepta** la entrega del dispositivo.
- Si el servidor ya lo marcó `Entregado` o `Extraviado` (u otro estado que no admite la transición) → **se rechaza**, se devuelve `estadoActual` y el comprobante **queda guardado en el dispositivo**.
- Nunca se sobrescribe el estado del servidor con el del teléfono en un conflicto.

**Errores**

| Código | Caso |
|---|---|
| `400` | Lote inválido (p. ej. `eventoId` o `envioId` duplicados) |
| `401` | Sin token válido de repartidor |
| `200` con rechazos | Conflictos individuales (no es error del lote) |

**Idempotencia durable:** el mismo `eventoId` entre requests distintos, si ya fue aplicado, vuelve en `aplicados` con el estado guardado (sin reaplicar). Los conflictos de transición siguen yendo a `rechazados` con `estadoActual`.

### 3.4 `POST /api/repartidor/posiciones`

Posición del repartidor durante la jornada (RF 23). La app las junta mientras está sin señal y las manda en lote;
el Tablero muestra la última de cada repartidor con ruta hoy (RF 29) y se actualiza en vivo.

**Headers:** `Authorization: Bearer <token repartidor>`

```json
{
  "posiciones": [
    { "latitud": -34.9011, "longitud": -56.1645, "fechaDispositivo": "2026-11-11T10:15:00-03:00", "precisionMetros": 8 }
  ]
}
```

| Campo | Tipo | Notas |
|---|---|---|
| `posiciones[].latitud` | decimal | Entre -90 y 90 |
| `posiciones[].longitud` | decimal | Entre -180 y 180 |
| `posiciones[].fechaDispositivo` | datetime con offset | Hora del teléfono al tomar la posición |
| `posiciones[].precisionMetros` | decimal | Opcional; precisión que informa el GPS |

**Response `200`:** `{ "aceptadas": 1, "descartadas": 0 }`. Se descartan, sin rechazar el lote, las posiciones con más
de 24 h o con hora más de 5 min en el futuro. Máximo 500 posiciones por lote.

| Código | Caso |
|---|---|
| `400` | Lote vacío, de más de 500 posiciones, o con coordenadas o precisión fuera de rango |
| `401` | Sin token válido de repartidor |

Sugerencia para la app: una posición cada 30 a 60 s mientras la ruta está en curso, y el lote pendiente al recuperar señal.

---

## 4. Endpoints de gestión (web)

### 4.1 `GET /api/repartidores`

Lista los repartidores **activos del operador autenticado**. Lo consume el selector de despacho del backoffice.

**Headers:** `Authorization: Bearer <token despachador|administrador|operario_deposito>`

**Response `200`**

```json
[
  { "id": "10000000-0000-0000-0000-000000000001", "nombre": "Alvaro - demostracion", "activo": true }
]
```

| Campo | Tipo | Notas |
|---|---|---|
| `id` | UUID | |
| `nombre` | string | Ordenado alfabéticamente |
| `activo` | boolean | En este endpoint siempre `true` (solo activos) |

**Errores:** `401` sin sesión válida de gestión.

---

### 4.2 `POST /api/despacho`

Crea una ruta con paradas y asigna los envíos. Lo consume la pantalla de despacho.

**Headers:** `Authorization: Bearer <token despachador|administrador|operario_deposito>`

**Request**

```json
{
  "repartidorId": "10000000-0000-0000-0000-000000000001",
  "fecha": "2026-10-16",
  "envioIds": ["8b75a582-659b-4d53-a010-be43bbb6d68a"]
}
```

| Campo | Tipo | Notas |
|---|---|---|
| `repartidorId` | UUID | Debe existir y estar activo en el operador del token |
| `fecha` | `yyyy-MM-dd` | Fecha de la ruta |
| `envioIds` | UUID[] | Al menos 1, sin duplicados; cada envío debe estar en `EnDeposito` y sin ruta previa |

**Response `200`**

```json
{ "id": "<rutaId>", "fecha": "2026-10-16", "repartidorId": "10000000-0000-0000-0000-000000000001" }
```

**Efectos**

- Crea `Ruta` + una `Parada` por envío (orden = orden del array).
- Cada envío pasa a `AsignadoARuta`.
- Publica el seguimiento en caché e invalida el caché de rutas del día.

**Errores:** `400` datos inválidos, repartidor inexistente/inactivo, envío inexistente, ya en ruta o no despachable. `401` sin sesión de gestión.

---

## 5. Seguimiento y caché

### 5.1 `GET /api/seguimiento/{token}`

Seguimiento visible de un envío por su **token público** (no por número de seguimiento ni por id).

**Response `200`**

```json
{
  "numeroSeguimiento": "ENV-392721",
  "tokenSeguimientoPublico": "<token>",
  "destinatario": "Destinatario",
  "direccionEntrega": "Calle 1",
  "estado": "EnDeposito",
  "intentosRealizados": 0
}
```

**Errores:** `404` con `{ "mensaje": "Seguimiento no encontrado." }`.

**Caché:** la lectura se sirve desde Redis/memoria si está; al cambiar el estado (backoffice, despacho o sync) el backend republica el seguimiento al instante.

El **detalle** del Portal (`ObtenerDetalleSeguimiento`) además expone `bultosEntregados` / `bultosTotal` (N de M), marca del operador y el historial con `motivo` y `responsable` por evento.

---

### 5.2 Retiros en domicilio (`/api/retiros`)

Requiere bandera `retiro-domicilio` activa en el operador. El comercio solicita y cancela en el **Portal**; despacho programa y completa en el **Backoffice**.

| Método | Ruta | Body | Notas |
|---|---|---|---|
| `POST` | `/api/retiros` | `{ direccion, fecha, contacto?, observaciones? }` | Emite outbox `RetiroSolicitado`. Responde `{ id, estado, fechaSolicitada, tarifaCalculada }` |
| `GET` | `/api/retiros` | — | Lista del operador (filtrada por comercio si aplica); cada retiro incluye `tarifaCalculada` y `reglaTarifariaId` |
| `POST` | `/api/retiros/{id}/programar` | `{ rutaId }` | Estado → `Programado` |
| `POST` | `/api/retiros/{id}/completar` | `{ envioAsociadoId? }` | Estado → `Completado` + `RetiroCompletado` |
| `POST` | `/api/retiros/{id}/cancelar` | `{ motivo? }` | Estado → `Cancelado` |

**Tarifa del retiro (RF 41):** se fija al pedirlo con el "precio por retiro" de la versión tarifaria vigente, menos la mayor bonificación que corresponda al comercio (las de volumen cuentan los envíos que el comercio ya hizo en el mes). Los retiros completados se suman a la liquidación del comercio. Las versiones publicadas antes de este cambio tienen precio por retiro 0.

Contrato de avisos HTTP: `contracts/mensajes/v2/aviso-retiro-*.json`.

---

### 5.3 `GET /tecnico/cache`

Métrica del caché de lectura. Requiere sesión de `administrador` (sin sesión responde `401`; con otro perfil, `403`).

**Response `200`**

```json
{ "hits": 4, "misses": 2, "ratio": 0.6667 }
```

| Campo | Tipo | Notas |
|---|---|---|
| `hits` | long | Lecturas servidas desde caché |
| `misses` | long | Lecturas que fueron a base/fuente |
| `ratio` | decimal | `hits / (hits + misses)`, redondeado a 4 decimales; `0` si no hubo lecturas |

**Nota:** los contadores viven en el proceso que responde (se reinician al redeploy/reinicio). Lo consume la pantalla de caché del backoffice.

---

## 6. Modelos compartidos

### `SesionRepartidor`
`repartidorId`, `operadorId`, `nombre`, `rol`, `token` — ver §3.1.

### `RutaDelDia`
| Campo | Tipo |
|---|---|
| `id` | UUID |
| `fecha` | `yyyy-MM-dd` |
| `operador` | `OperadorVisible` |
| `envios` | `EnvioDeRuta[]` |

### `OperadorVisible` (configuración del operador)
| Campo | Tipo | Ejemplo Norte / Sur |
|---|---|---|
| `nombre` | string | Operador Norte / Operador Sur |
| `colorMarca` | string | `azul` / `verde` |
| `maximoIntentos` | int | `3` / `1` |

### `EnvioDeRuta`
`id`, `numeroSeguimiento`, `destinatario`, `direccionEntrega`, `emailDestinatario`, `estado`, `tarifaCalculada`, `intentosRealizados`.

### `ComprobanteEntrega`
Ver tabla en §3.3.

### `EventoAplicado` / `EventoRechazado`
Ver tabla en §3.3.

### `RepartidorVisible`
`id`, `nombre`, `activo`.

### `SeguimientoVisible`
`numeroSeguimiento`, `tokenSeguimientoPublico`, `destinatario`, `direccionEntrega`, `estado`, `intentosRealizados`.

---

## 7. Estados de envío

Valores posibles (`EstadoEnvio`):

`Admitido`, `EnDeposito`, `AsignadoARuta`, `EnTransito`, `Entregado`, `NoEntregado`, `Reprogramado`, `EnDevolucion`, `Devuelto`, `Extraviado`.

Transiciones relevantes para la sync del repartidor (desde `EnTransito`): `Entregado`, `NoEntregado`, `Extraviado`. El resto lo valida el dominio (`TransicionesEnvio`); una transición inválida va a `rechazados` con `estadoActual`.

---

## 8. Ejemplos con `curl`

```bash
# Login repartidor
curl -s -X POST "{{baseUrl}}/api/repartidor/login" \
  -H "Content-Type: application/json" \
  -d '{"usuario":"alvaro@demo.test","clave":"Demo123!"}'

# Hoja de ruta (reemplazar <token>)
curl -s "{{baseUrl}}/api/repartidor/rutas/hoy" \
  -H "Authorization: Bearer <token>"

# Sincronización
curl -s -X POST "{{baseUrl}}/api/repartidor/sincronizacion" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d @contracts/repartidor/sincronizacion.request.json

# Métrica de caché
curl -s -b cookies.txt "{{baseUrl}}/tecnico/cache"   # cookie de sesión de un administrador
```

Usuario demo de repartidor Norte: `alvaro@demo.test` / `Demo123!` (solo ambientes de desarrollo/demo).

---

## 9. Versionado y cambios

- **v1** es la versión vigente. Los ejemplos en `contracts/repartidor/` forman parte del contrato.
- Cambiar nombres de campos, sacar campos o cambiar tipos = cambio *breaking*: requiere acordar con móvil y web, y bumpear versión.
- Agregar campos opcionales nuevos = cambio compatible, avisar igualmente.
- Mensajes de dominio (cola / outbox): ver [`contracts/mensajes/v1/`](../contracts/mensajes/v1/README.md). El primero es `EnvioEstadoCambiado`.
- Dispositivos: [`contracts/repartidor/dispositivos.request.json`](../contracts/repartidor/dispositivos.request.json).
- **Caso real Backoffice:** en Monitoreo, al cambiar un estado se muestra el `EventId` de outbox. Equivalente API: `POST /api/envios/{id}/estado` con `{ "nuevoEstado": "EnDeposito" }` → responde `eventId`.
- Fuera del backend Ana (Nadia/Joel): worker de avisos a comercios, SignalR, DLQ avanzada, webhooks.
- Avisos a comercios (webhooks firmados): contrato versionado aparte, v1 y v2, en la [sección 10](#10-avisos-a-comercios-letra-69-rf-36).

---

## 10. Avisos a comercios (letra 6.9, RF 36)

**Dueña:** Nadia.
**Código:** `src/Plataforma.Avisos/ContratoAvisos.cs` (versiones), `FirmaAviso.cs` (firma), `Worker/ConsumidorEntregas.cs` (envío).

### 10.1 Por qué está versionado

- **Letra 6.9**: los avisos son una "suscripción configurable por comercio y por tipo de evento", firmados, con reintentos, DLQ e historial.
- **Cambio de requerimientos, RF 36**: "como los sistemas de los comercios ya reciben avisos con el formato actual, el cambio no puede afectarlos, por lo que el contrato de los avisos deberá versionarse".
- El cambio de requerimientos (sección 2.3) se tiene que incorporar sin reescribir lo existente.

Por eso **v1 queda congelado**: no se le agregan campos ni tipos de evento. Lo nuevo va en **v2**. Cada suscripción elige su versión. Las suscripciones creadas antes del cambio quedan en v1, porque la migración pone 1 por defecto.

### 10.2 Pedido HTTP (igual en todas las versiones)

`POST` a la URL de la suscripción, con `Content-Type: application/json` y estos encabezados:

| Encabezado | Valor |
|---|---|
| `X-Plataforma-Evento` | `eventId` (sirve para idempotencia en el comercio) |
| `X-Plataforma-Version` | `1` o `2`: la versión del cuerpo. Es nuevo, y un receptor v1 lo puede ignorar. |
| `X-Plataforma-Timestamp` | segundos Unix |
| `X-Plataforma-Firma` | `sha256=` + HMAC-SHA256(secreto, `"{timestamp}.{cuerpo}"`) en hexadecimal |

- **Éxito:** cualquier respuesta 2xx.
- **Fallo:** cualquier otra respuesta, o un timeout. Se reintenta a los 5, 15 y 45 s. Después la entrega queda "fallida" y se puede reintentar a mano desde el Portal.
- **Duplicados:** el mismo `eventId` puede llegar más de una vez y el comercio debe descartarlo.

### 10.3 Cuerpo v1 (formato original, sin cambios)

```json
{
  "eventId": "6f1c…",
  "tipo": "EnvioEstadoCambiado",
  "fecha": "2026-11-03T15:04:05+00:00",
  "envioId": "a2b4…",
  "estadoAnterior": "EnTransito",
  "estadoNuevo": "Entregado"
}
```

Tipos de evento (estados) que recibe: `EnDeposito`, `AsignadoARuta`, `EnTransito`, `Entregado`, `NoEntregado`, `Reprogramado`, `EnDevolucion`, `Devuelto`, `Extraviado`. Un estado que no está en esta lista **nunca** se envía a una suscripción v1.

### 10.4 Cuerpo v2

```json
{
  "version": 2,
  "eventId": "6f1c…",
  "tipo": "EnvioEstadoCambiado",
  "fecha": "2026-11-03T15:04:05+00:00",
  "envio": {
    "id": "a2b4…",
    "numeroSeguimiento": "ENV-E57E…",
    "estadoAnterior": "EnTransito",
    "estadoNuevo": "EntregadoParcialmente"
  }
}
```

Diferencias con v1:

- **`version`** viaja en el cuerpo, además de en el encabezado.
- **Datos del envío agrupados en `envio`**: así los eventos de bulto o de retiro pueden sumar su propio objeto sin romper el formato.
- **`numeroSeguimiento`**: es el número que el comercio le muestra a su cliente. Puede llegar `null` si todavía no está asignado.
- **Tipos de evento**: todos los de v1, más `EntregadoParcialmente`, `BultoEstadoCambiado`, `RetiroSolicitado` y `RetiroCompletado`.

Ejemplos cerrados: `contracts/mensajes/v2/`. Los comercios v2 deben ignorar los `tipo` que no conocen.

### 10.5 Cómo elige la versión el comercio

En el Portal, en Avisos → "Formato del aviso", se elige v1 o v2. Si se elige v1, los tipos marcados "solo v2" se descartan al guardar. El historial muestra, para cada aviso, con qué versión se armó y el cuerpo exacto que se envió y firmó ("Ver mensaje").

Cada aviso guarda su propia versión (`EntregaAviso.VersionContrato`). Si el comercio cambia de versión, los reintentos de avisos viejos se envían con el cuerpo original, que es el que se firmó.
