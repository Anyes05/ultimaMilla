# Contrato técnico — Identity / autenticación (letra 6.3)

**Estado:** borrador (aportes de Nadia rellenados desde el código actual).  
**Dueña del cierre:** Ana (lo escribe, publica y cierra).  
**Plazo de cierre:** antes del **06/11/2026**.  
**Reunión sugerida:** **04 o 05/11** (15–30 min) para que los cuatro aporten y Ana cierre.

| Cuándo | Qué |
|--------|-----|
| **Ahora → 05/11** | Cada uno completa su sección abajo. Ana elige Identity vs OIDC. |
| **Antes del 06/11** | Ana publica v1 del contrato en este archivo (merge a `main`). |
| **08/11** | Ana merge de Identity/API. |
| **09/11** | Nadia (web) y Álvaro (app) integran login. |
| **10/11** | Prueba de aceptación: login OK + acceso denegado por rol (web y app). |

---

## 1. Responsables

| Quién | Rol | Qué aporta |
|-------|-----|------------|
| **Ana** | Escribe y cierra | ASP.NET Identity **o** OIDC; issuer; audiences; emisión de tokens; claims; roles; autorización API |
| **Nadia** | Web | Redirect URIs Backoffice/Portal; claims que usan las pantallas; sesión multi-réplica; SignalR |
| **Álvaro** | Móvil | Redirect URI de la app; PKCE como cliente público; renovación offline |
| **Joel** | EKS | Dominio/TLS del emisor; secretos vs variables; nombres en Kubernetes |

---

## 2. Estado actual del sistema (baseline — no inventar)

Hoy **no** hay OIDC ni ASP.NET Identity UI. Hay JWT propio:

- Emisión: `EmisorTokenJwt` (`Jwt:Clave`, `Jwt:Emisor`, `Jwt:Audiencia`, `Jwt:HorasVigencia`).
- Validación: `AddAutenticacionJwt` (cookie HttpOnly **o** `Authorization: Bearer`).
- Cookies: `acceso_backoffice`, `acceso_portal` (`CookieAcceso`).
- Claims: `ClaimsPlataforma` + `AccesoActual`.
- Data Protection (cookies/antiforgery) en **Redis**, por app.
- SignalR hub `/hubs/avisos`: autentica con la **misma cookie** JWT; grupo = `operador_id`.

Cualquier diseño nuevo debe **conservar o mapear** lo de las secciones 3–5, o documentar el reemplazo explícito.

---

## 3. Aporte Nadia (web) — COMPLETO

### 3.1 Redirect URIs (solo si eligen OIDC)

Producción (dominios actuales del lab):

| App | Sign-in | Sign-out (propuesto) |
|-----|---------|----------------------|
| Backoffice | `https://backoffice.tallernet.cloud-ip.cc/signin-oidc` | `https://backoffice.tallernet.cloud-ip.cc/signout-callback-oidc` |
| Portal | `https://app.tallernet.cloud-ip.cc/signin-oidc` | `https://app.tallernet.cloud-ip.cc/signout-callback-oidc` |

Compose / local (respaldo letra 6.1, verificación 11/11):

| App | Sign-in |
|-----|---------|
| Backoffice | `http://localhost:8080/signin-oidc` |
| Portal | `http://localhost:8081/signin-oidc` |

Si Ana elige **solo ASP.NET Identity** (cookie/JWT sin protocolo OIDC), **estas URIs no aplican**; el login sigue siendo formulario en `/login` de cada host.

### 3.2 Claims que el web y la API ya consumen

Nombres **exactos** (no renombrar sin migrar `AccesoActual` / `ClaimsPlataforma`):

| Claim | Constante | Uso |
|-------|-----------|-----|
| `sub` | `ClaimsPlataforma.UsuarioId` | Id de usuario |
| `operador_id` | `ClaimsPlataforma.OperadorId` | Filtro multi-tenant; grupo SignalR |
| `comercio_id` | `ClaimsPlataforma.ComercioId` | Obligatoria en Portal (rol comercio) |
| `repartidor_id` | `ClaimsPlataforma.RepartidorId` | App / API repartidor |
| `role` | `ClaimsPlataforma.Rol` | Autorización (`[Authorize(Roles=…)]`, `PermisosPerfil`) |
| `unique_name` | — | Nombre visible |

**Hoy `AccesoActual.Intentar` exige `operador_id`.** Un administrador de plataforma **sin** `operador_id` **rompe** el código actual: Ana debe definir el mapeo (claim opcional + rol plataforma) y el cambio en `AccesoActual` / filtros.

### 3.3 Roles (`PermisosPerfil` / `Roles`)

Valores de claim `role` (minúsculas, como en `Plataforma.Domain.Roles`):

| Rol | Valor claim | Entra a |
|-----|-------------|---------|
| Administrador | `administrador` | Backoffice |
| Despachador | `despachador` | Backoffice |
| Operario de depósito | `operario_deposito` | Backoffice |
| Comercio | `comercio` | Portal |
| Repartidor | `repartidor` | App + API Bearer |

Grupos Backoffice (`PermisosPerfil`): `Operacion`, `Planificacion`, `Deposito`, `Administracion`.

**Nuevo (banderas / org):** administrador de **plataforma** (sin `operador_id`). Nombre de claim **pendiente Ana** (ej. `administrador_plataforma`). No entra al Backoffice de operador ni al Portal de comercio salvo que el contrato lo diga.

### 3.4 Quién puede entrar a cada host

- **Backoffice:** solo `PuedeEntrarAlBackoffice` → Administrador, Despachador, OperarioDeposito.
- **Portal:** solo rol `comercio` + `comercio_id` del operador correspondiente.
- **API repartidor / móvil:** rol `repartidor` + Bearer (hoy).

### 3.5 Sesión con 2 réplicas (obligatorio conservar)

- Cookies de acceso HttpOnly; cifrado/antiforgery vía **Data Protection** con claves en **Redis** (`plataforma:backoffice:data-protection` / equivalente Portal).
- SignalR: misma cookie; backplane Redis. El contrato **no** puede exigir sticky sessions para el hub.

### 3.6 Hub SignalR

- Ruta: `/hubs/avisos`.
- Auth: misma sesión web que el Backoffice.
- Aislamiento: grupo `operador:{operadorId:N}` desde claim `operador_id` (el cliente no elige operador).

---

## 4. Aporte Álvaro (móvil) — PENDIENTE

Completar antes del 06/11:

- [ ] Redirect URI de la app (ej. `tallernet://callback`) — solo si OIDC.
- [ ] Confirmación: cliente **público** → PKCE obligatorio; **sin** client secret en el binario.
- [ ] Cómo renueva token sin red (política offline) y qué pasa al volver online.
- [ ] Si Identity puro (sin OIDC): ¿sigue `POST /api/repartidor/login` + Bearer como hoy?

Baseline actual: login API repartidor + JWT Bearer; token en SecureStorage (ver contratos en `contracts/repartidor/`).

---

## 5. Aporte Joel (EKS) — PENDIENTE

Completar antes del 06/11 (nombres, no valores secretos en Git):

| Variable / secreto | ¿Secret o ConfigMap? | Notas |
|--------------------|----------------------|--------|
| `Jwt__Clave` (hoy) o claves Identity/OIDC | **Secret** | Ya documentado en `infra/terraform/k8s/README.md` |
| `Jwt__Emisor` / issuer URL | Config o Secret | Ana define valor final |
| `Jwt__Audiencia` / audiences | Config | Ana define |
| Connection string IdP (si OIDC externo) | **Secret** | Solo si aplica |
| Client secrets web (si OIDC confidential) | **Secret** | Preferible Identity sin secretos de cliente en hosts |

- [ ] Dominio y certificado TLS del emisor (mismo ALB/ACM o host dedicado).
- [ ] Lista final de env vars en Deployments `backoffice`, `portal`, `queue-worker` (si el worker valida JWT).

**No** pegar claves reales en este archivo ni en el chat.

---

## 6. Decisiones de Ana — PENDIENTE (cierra el contrato)

### 6.1 Elección (sugerencia del equipo)

La letra acepta **ASP.NET Identity como almacén de usuarios**. Es la opción de **menor riesgo** antes del freeze (usuarios en el mismo Postgres, sin IdP extra).

- [ ] **Opción A — ASP.NET Identity + JWT/cookies** (recomendada por tiempo)
- [ ] **Opción B — OIDC** (OpenIddict / Keycloak / …) + PKCE

### 6.2 Completar

- [ ] Issuer definitivo.
- [ ] Audience(s): ¿una (`ultima-milla`) o separadas (`backoffice` / `portal` / `mobile`)?
- [ ] Vigencia access token / refresh (si hay).
- [ ] Emisión: ¿sigue `IEmisorToken` o lo reemplaza Identity?
- [ ] Claim del administrador de plataforma y regla: **sin** `operador_id`.
- [ ] Cambio en `AccesoActual` / filtro EF cuando no hay operador.
- [ ] Plan de migración de usuarios semilla Norte/Sur.
- [ ] Criterio de aceptación 10/11: login OK + 403/denegado por rol en web y app.

---

## 7. Criterio de aceptación (organización definitiva)

Identity **no se declara** cumplido sin:

1. Login exitoso en Backoffice, Portal y app.
2. Acceso **denegado** por rol (ej. comercio no entra al Backoffice; despachador no administra banderas/tarifas según `PermisosPerfil`).
3. Sesión válida en **2 réplicas** (cookie / Data Protection Redis).
4. Hub SignalR autenticado con la misma sesión (si el tablero sigue en Backoffice).

---

## 8. Mensaje listo para el grupo

```
El contrato de Identity lo escribe Ana, con aportes de cada uno (docs/contratos/identidad.md).
Nadia ya cargó lo del web: redirect URIs, claims (operador_id, comercio_id, role, …),
roles de PermisosPerfil + admin plataforma sin operador, sesión 2 réplicas + SignalR.
Álvaro: redirect de la app y PKCE / login móvil.
Joel: dominio, TLS y nombres de secretos/vars en EKS (sin pegar secretos).
Ana: cierra Identity vs OIDC + issuer/audiences antes del 06/11.
¿Lo vemos el 04 o el 05/11?
```
