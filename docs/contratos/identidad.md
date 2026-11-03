# Contrato técnico — Identity / autenticación (letra 6.3)

**Estado:** **cerrado v1** (Ana, 03/11/2026).  
**Dueña:** Ana.  
**Álvaro:** confirmó sync v2 el mismo día; este contrato Identity cierra la elección para el merge del **08/11**.  
**Aportes:** Nadia (web) completo; Álvaro/Joel con defaults operativos abajo (pueden matizar sin cambiar la decisión A).

| Cuándo | Qué |
|--------|-----|
| **08/11** | Ana merge: ASP.NET Identity + emisión JWT/cookies conservando claims actuales |
| **09/11** | Nadia (web) y Álvaro (app) integran login contra este contrato |
| **10/11** | Prueba: login OK + acceso denegado por rol (web y app) |

---

## 1. Decisión (Ana) — Opción A

**ASP.NET Identity como almacén de usuarios** + **JWT/cookies** como hoy (misma forma de sesión).

| | |
|--|--|
| **No** | IdP externo / OpenIddict / Keycloak en este laboratorio |
| **Por qué** | Menor riesgo antes del freeze; usuarios ya en Postgres; letra 6.3 admite Identity; 2 réplicas ya usan Data Protection en Redis |
| **PKCE / redirect OIDC** | **No aplican** en v1. El login web sigue en `/login` + cookie; el móvil sigue `POST /api/repartidor/login` + Bearer |

Si más adelante el equipo quiere OIDC, será un ADR aparte; **no** bloquea 6.3 ni el 10/11.

---

## 2. Issuer, audiences y vigencia

Valores de configuración (producción = Secret/Config de Joel; no pegar secretos en Git):

| Clave | Valor de contrato | Notas |
|-------|-------------------|--------|
| Issuer (`Jwt__Emisor`) | `https://auth.tallernet.cloud-ip.cc` en EKS; en Compose puede seguir `ultima-milla` | Una sola autoridad lógica |
| Audience (`Jwt__Audiencia`) | **Una sola:** `ultima-milla` | Misma audiencia Backoffice, Portal y móvil (como hoy) |
| Access token | **8 h** (`Jwt__HorasVigencia`) | Sin refresh token en v1 |
| Firma | HMAC con `Jwt__Clave` (≥ 32 caracteres) en **Secret** | Misma clave en backoffice y portal |

Emisión: Identity autentica (password hasher / `UserManager`); tras éxito se emite el JWT con **`IEmisorToken` / `EmisorTokenJwt`** (o sucesor con la misma interfaz de claims). Los hosts **no** cambian el esquema de cookie/Bearer.

---

## 3. Claims (nombres inmutables salvo lo nuevo)

| Claim | Constante | Obligatorio | Uso |
|-------|-----------|-------------|-----|
| `sub` | `ClaimsPlataforma.UsuarioId` | sí | Id de usuario |
| `operador_id` | `ClaimsPlataforma.OperadorId` | sí para roles de operador; **no** para admin plataforma | Tenant / SignalR |
| `comercio_id` | `ClaimsPlataforma.ComercioId` | sí si `role=comercio` | Portal |
| `repartidor_id` | `ClaimsPlataforma.RepartidorId` | sí si `role=repartidor` | API / app |
| `role` | `ClaimsPlataforma.Rol` | sí | Autorización |
| `unique_name` | — | recomendado | Nombre visible |

### 3.1 Administrador de plataforma (banderas)

| | |
|--|--|
| Claim `role` | `administrador_plataforma` |
| Constante sugerida | `Roles.AdministradorPlataforma` / `RolUsuario.AdministradorPlataforma` |
| `operador_id` | **ausente** |
| Dónde entra | Solo pantallas de **plataforma** (p. ej. banderas globales), **no** Backoffice de operador ni Portal comercio |
| `AccesoActual` | Debe aceptar principal **sin** `operador_id` si el rol es `administrador_plataforma`; el filtro EF de operador **no** aplica a esas rutas (autorización explícita) |

El resto de roles (`administrador`, `despachador`, `operario_deposito`, `comercio`, `repartidor`) **no cambian**.

---

## 4. Quién entra a cada host (sin cambio)

- **Backoffice:** Administrador, Despachador, OperarioDeposito (`PuedeEntrarAlBackoffice`).
- **Portal:** `comercio` + `comercio_id`.
- **API / app repartidor:** `repartidor` + Bearer.
- **Admin plataforma:** host o área dedicada acordada con Nadia (no reutilizar menú de operador sin `operador_id`).

---

## 5. Sesión multi-réplica y SignalR (obligatorio conservar)

- Cookies HttpOnly `acceso_backoffice` / `acceso_portal`.
- Data Protection en **Redis** (claves por app).
- SignalR `/hubs/avisos`: misma cookie; backplane Redis; grupo desde `operador_id`.
- **Sin** sticky sessions.

---

## 6. Álvaro (móvil) — defaults de contrato v1

| Tema | Decisión |
|------|----------|
| Login | Sigue **`POST /api/repartidor/login`** + Bearer (contratos `contracts/repartidor/`) |
| OIDC / redirect `tallernet://…` | **No** en v1 |
| PKCE | No aplica (no hay cliente OIDC) |
| Token offline | Se guarda en SecureStorage; al expirar → nuevo login; sin refresh en v1 |
| Prueba 10/11 | Login OK con repartidor semilla; 401/403 si rol incorrecto o token inválido |

Álvaro puede ampliar notas en un PR; **no** cambia la Opción A.

---

## 7. Joel (EKS) — nombres (sin valores)

| Clave | Tipo | Deployments |
|-------|------|-------------|
| `Jwt__Clave` | Secret | backoffice, portal (y API repartidor = backoffice) |
| `Jwt__Emisor` | Config o Secret | idem |
| `Jwt__Audiencia` | Config | `ultima-milla` |
| `Jwt__HorasVigencia` | Config | `8` |
| Connection string Postgres | Secret (ya) | incluye tablas Identity al migrar |

- TLS: mismo ALB/ACM del lab (`*.tallernet.cloud-ip.cc`).
- El issuer URL es **lógico** (claim); no exige un Deployment “auth” aparte en v1.
- Worker: **no** valida JWT de usuario en v1 (solo colas).

---

## 8. Migración de usuarios (Ana, merge 08/11)

1. Agregar paquetes Identity + store en el mismo Postgres (`AspNetUsers` / roles o mapa a `UsuarioPlataforma` — decisión de implementación en el PR; el contrato exige **mismos logins semilla**).
2. Seed Norte/Sur: mismos `DatosSemilla` (`norte.admin`, `norte.comercio`, `alvaro@demo.test`, etc.) y clave de desarrollo **solo en Compose**; en EKS claves del Secret.
3. Login web y API deben seguir funcionando con esos usuarios tras el migrate.
4. Passwords: rehash con Identity; no dejar texto plano.

---

## 9. Criterio de aceptación (10/11) — no negociable

Identity **no se declara** cumplido sin:

1. Login OK en Backoffice, Portal y app (usuarios semilla).
2. Acceso **denegado** por rol (comercio ↛ Backoffice; despachador ↛ administración de tarifas/banderas según `PermisosPerfil`).
3. Sesión válida con **2 réplicas** (cookie + Redis DP).
4. Hub SignalR autenticado con la misma sesión Backoffice.

---

## 10. Aporte Nadia (referencia; ya completo)

Redirect OIDC del borrador **no se usan** en v1. Claims, roles de operador, sesión Redis y SignalR quedan como en su sección original (conservados arriba).

---

## 11. Mensaje para el grupo

```
Contrato Identity v1 CERRADO (docs/contratos/identidad.md):
Opción A — ASP.NET Identity + JWT/cookies (sin OIDC en este lab).
Issuer lógico auth.tallernet… / audiencia unica ultima-milla / 8h.
Claims actuales se mantienen; admin plataforma = role administrador_plataforma sin operador_id.
Móvil: sigue POST /api/repartidor/login + Bearer.
Merge Ana 08/11; integración Nadia/Álvaro 09; prueba 10/11.
```
