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
| 7 | `GET /tecnico/cache` | Técnico | Métrica de aciertos/fallos del caché |

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

Devuelve las rutas del día del repartidor autenticado.

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
      "maximoIntentos": 3
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
| `eventos[].observaciones` | string | Máx. 500 caracteres |
| `eventos[].fechaDispositivo` | datetime con offset | Hora del teléfono al registrar |

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

**Nota de idempotencia:** en v1 el `eventoId` viaja y se valida en el lote, pero la deduplicación durable servidor (mismo `eventoId` entre requests distintos) queda para la iteración de outbox/worker. El móvil debe conservar el comprobante rechazado.

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

---

### 5.2 `GET /tecnico/cache`

Métrica del caché de lectura.

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
curl -s "{{baseUrl}}/tecnico/cache"
```

Usuario demo de repartidor Norte: `alvaro@demo.test` / `Demo123!` (solo ambientes de desarrollo/demo).

---

## 9. Versionado y cambios

- **v1** es la versión vigente. Los ejemplos en `contracts/repartidor/` forman parte del contrato.
- Cambiar nombres de campos, sacar campos o cambiar tipos = cambio *breaking*: requiere acordar con móvil y web, y bumpear versión.
- Agregar campos opcionales nuevos = cambio compatible, avisar igualmente.
- Fuera de v1 (iteraciones siguientes): dispositivos FCM (`POST/DELETE /api/repartidor/dispositivos`), outbox con `EventId` e idempotencia durable, webhooks.
