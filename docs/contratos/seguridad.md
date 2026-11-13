# Seguridad: desarrollo seguro (SSDLC)

**Responsable:** Nadia Inthamoussu
**Fecha:** 13/11/2026
**Ambiente evaluado:** producción en AWS (EKS `tallernet-cluster`, cuenta del laboratorio) y código de `main`.

Este documento cubre las tres etapas del modelo de desarrollo seguro que pide la letra: requisitos, amenazas y
riesgos (planificación y diseño), controles y SAST (desarrollo), y la validación con SCA, escaneo de imágenes y
DAST (testing y despliegue).

## 1. Alcance y activos

| Activo | Por qué importa |
|---|---|
| Datos de destinatarios (nombre, dirección, teléfono) | Datos personales: no pueden filtrarse ni verse entre operadores |
| Credenciales de usuarios (Backoffice, Portal, app) | Dan acceso a la operación de un operador o de un comercio |
| Comprobantes de entrega y retiro (firma, foto, GPS) | Prueba ante reclamos: no pueden alterarse ni perderse |
| Tarifas, liquidaciones y cuenta corriente | Dinero: un cambio indebido afecta el cobro al comercio |
| Avisos a los sistemas de los comercios (webhooks) | Un aviso falso podría hacer actuar al comercio sobre un envío |
| Secretos (base, JWT, HMAC, Mongo, Firebase, SMTP) | Con ellos se suplanta a la plataforma |

La plataforma es multi-operador: el riesgo más grave del dominio es que un operador vea o cambie datos de otro.

## 2. Requisitos de seguridad según el riesgo

| Id | Requisito | Riesgo que cubre | Cómo se cumple |
|---|---|---|---|
| RS-01 | Aislamiento total entre operadores | Fuga de datos entre clientes | Filtro global por operador en EF Core (`AplicarFiltroOperador`) y pruebas de aislamiento en el CI |
| RS-02 | Contraseñas nunca en claro | Robo de la base | PBKDF2-SHA256 con sal y 100.000 iteraciones (`HashClave`) |
| RS-03 | Acceso según rol | Un usuario hace algo que no le corresponde | Roles en cookie/JWT; transiciones de estado permitidas por rol (`PermisosPerfil`) |
| RS-04 | Formularios protegidos contra CSRF | Acciones en nombre del usuario desde otra página | Token anti-CSRF en todos los formularios, incluido el login |
| RS-05 | Seguimiento público sin datos personales y sin enumeración | Adivinar números y ver envíos ajenos | Enlace con token protegido; límite de 30 consultas por minuto por IP; la página no muestra dirección ni teléfono |
| RS-06 | Avisos a comercios verificables | Avisos falsificados o repetidos | Firma HMAC-SHA256 con marca de tiempo y tolerancia de 5 min (`FirmaAviso`) |
| RS-07 | Tráfico cifrado | Captura de credenciales o datos en la red | HTTPS con certificado ACM en el balanceador; HSTS; PostgreSQL con `SSL Mode=Require` |
| RS-08 | Datos en el teléfono protegidos | Robo o pérdida del teléfono del repartidor | Base local de la app cifrada con AES-GCM (`SnapshotCipher`); biometría para confirmar entregas |
| RS-09 | Secretos fuera del código | Filtración por el repositorio | Secrets de Kubernetes y de GitHub; el script de despliegue los pide sin mostrarlos |
| RS-10 | Trazabilidad de cambios | Negar haber hecho un cambio | Historial de eventos con responsable (MongoDB), auditoría de banderas, comprobantes con firma y GPS |
| RS-11 | Dependencias e imágenes sin vulnerabilidades conocidas sin tratar | Explotación de librerías o del sistema operativo | SCA y Trivy en el CI; escaneo de ECR al subir cada imagen |
| RS-12 | Disponibilidad bajo carga y ante fallas | Caída del servicio | HPA de 2 a 4 réplicas; outbox y reintentos (ver `adr/pruebas-resiliencia.md`) |

## 3. Amenazas (STRIDE)

| Tipo | Amenaza | Componente | Control |
|---|---|---|---|
| **S**uplantación | Adivinar o robar la contraseña | Login web y de la app | PBKDF2; la app bloquea tras varios intentos fallidos (`ProtectorLoginRepartidor`). El login web no bloquea (riesgo R-06) |
| | Login CSRF: otra página inicia sesión con una cuenta del atacante | Login web | Token anti-CSRF validado en `/session/login` (corregido, ver §7) |
| | Robo del token de la app | API del repartidor | JWT firmado HS256 con vigencia de 8 h; solo por HTTPS |
| | Aviso falso a un comercio | Webhooks | Firma HMAC con secreto por suscripción y marca de tiempo |
| **T**ampering (alteración) | Inyección SQL | Base de datos | EF Core parametriza; el SQL manual del Worker usa `SqlQuery` con parámetros. SAST sin hallazgos |
| | Comprobante alterado desde la app | Sincronización | El servidor revalida cada evento (ruta, bultos, reglas) y es la fuente de verdad (ADR 0013) |
| | Cambio de estado no permitido | Backoffice | Transiciones válidas por estado y por rol |
| **R**epudio | Negar un cambio de estado o una entrega | Historial | Historial con responsable y fecha; comprobante con firma, foto y GPS |
| **I**nformation disclosure | Un operador ve datos de otro | Toda la plataforma | Filtro global por operador + pruebas de aislamiento en el CI |
| | Enumerar seguimientos públicos | `tracking.` | Límite de tasa (responde 429) y página sin datos personales |
| | Secretos en el repositorio o en logs | Repo, CI | Secrets de Kubernetes/GitHub; sin cabecera `Server` ni detalles de error en producción |
| | Datos legibles en un teléfono perdido | App | Base local cifrada |
| **D**enegación de servicio | Saturar el seguimiento público | `tracking.` | Límite por IP; caché en Redis |
| | Pico de carga | Backoffice | HPA; prueba k6 con 30 sincronizaciones simultáneas sin errores (`tests/carga`) |
| **E**levación de privilegios | Un comercio entra al Backoffice | Backoffice | El login rechaza roles que no corresponden al sitio |
| | Un despachador usa funciones de administración | Backoffice | Menú y páginas por rol; funciones de plataforma solo para `administrador_plataforma` |

## 4. Análisis de riesgos

Probabilidad e impacto: Baja / Media / Alta. Nivel = combinación de ambos.

| Id | Riesgo | Prob. | Impacto | Nivel | Tratamiento | Estado |
|---|---|---|---|---|---|---|
| R-01 | Fuga de datos entre operadores | Baja | Alta | Medio | Filtro global + pruebas de aislamiento en cada build | Mitigado |
| R-02 | Login CSRF en Portal y Backoffice | Media | Media | Medio | Token anti-CSRF en el formulario y validación en el endpoint | **Corregido** (hallazgo de ZAP) |
| R-03 | Vulnerabilidad Alta en `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.12 (GHSA-wr8v-9x5q-ggcm) | Baja | Media | Bajo | No hay versión corregida para .NET 10 (solo 11.0.0). Redis es interno al clúster, no se expone afuera y exige contraseña. Se actualiza cuando salga el arreglo para .NET 10 | Aceptado |
| R-04 | CSP con `script-src 'unsafe-inline'` | Baja | Media | Bajo | Necesario para los scripts en línea de Blazor y de las páginas. El resto del CSP es estricto (`default-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`). Mejora futura: nonces | Aceptado |
| R-05 | Cookies de sesión sin atributo `Secure` | Baja | Media | Bajo | Tienen `HttpOnly` y `SameSite=Lax`; el sitio solo responde por HTTPS (HSTS y redirección 80 → 443 en el balanceador). Mejora: marcar `Secure` | Mitigado, mejora pendiente |
| R-06 | Fuerza bruta en el login web (sin bloqueo por intentos) | Media | Media | Medio | PBKDF2 hace lento cada intento. Mejora: límite de tasa en `/session/login` como el de la app | Pendiente |
| R-07 | Contenedores corren como root | Baja | Media | Bajo | Imágenes oficiales de .NET, sin puertos privilegiados, límites de recursos. Mejora: `USER $APP_UID` | Pendiente |
| R-08 | Falta de `Cross-Origin-Embedder-Policy` | Baja | Baja | Bajo | Activarla bloquearía el script de cdnjs del Backoffice; el sitio no usa funciones que la requieran | Aceptado |
| R-09 | Pérdida del teléfono del repartidor | Media | Media | Medio | Base local cifrada, biometría, sesión con vencimiento | Mitigado |
| R-10 | Robo de secretos | Baja | Alta | Medio | Fuera del repo; RDS sin acceso público y solo desde los nodos EKS | Mitigado |

## 5. Controles en el desarrollo

- **Validación de entradas:** reglas en el dominio (estados, bultos, capacidades, franjas), validación de formularios
  y de la API; límites de tamaño en lotes (sincronización y posiciones).
- **Autenticación y autorización:** PBKDF2-SHA256; JWT HS256 para la app; cookie `HttpOnly` y `SameSite=Lax` para
  la web; roles; filtro por operador; token anti-CSRF.
- **Cifrado:** HTTPS (ACM), HSTS, PostgreSQL con SSL y almacenamiento cifrado en RDS, HMAC en avisos, AES-GCM en
  el teléfono, claves de Data Protection compartidas en Redis.
- **Cabeceras:** CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`,
  `Permissions-Policy`, sin cabecera `Server` (`SeguridadWeb`).

### SAST

Analizadores de seguridad de .NET con todas las reglas de la categoría Security activas en toda la solución
(`Directory.Build.props`: `AnalysisModeSecurity=All`, reglas CA2xxx, CA3xxx y CA5xxx). Corren en cada build, local
y del CI, y sus hallazgos salen como advertencias.

**Resultado (13/11):** compilación de la solución completa en Release, **0 advertencias de seguridad**. Para
comprobar que las reglas están activas se compiló aparte un proyecto de prueba con el mismo archivo y código
inseguro a propósito: marcó **CA5351** (uso de MD5).

## 6. Testing y despliegue

### SCA (dependencias)

Paso `Dependency vulnerability scan (SCA)` del CI: `dotnet list package --vulnerable --include-transitive` en cada
build, con el resultado en el resumen del run. **Resultado (13/11):** 1 paquete vulnerable, el de R-03; ningún otro
proyecto tiene paquetes vulnerables.

### Escaneo de imágenes

- **ECR:** `scan_on_push = true` en los tres repositorios (Terraform): cada imagen se escanea al subirla.
- **Trivy** (versión fija 0.58.1) en el CI, sobre las tres imágenes publicadas, vulnerabilidades HIGH y CRITICAL,
  con el resultado en el resumen del run. Prueba local de la imagen base `mcr.microsoft.com/dotnet/aspnet:10.0`
  (Ubuntu 24.04): **0 HIGH y 0 CRITICAL**.

### DAST (OWASP ZAP)

Escaneo *baseline* (pasivo, sin enviar formularios) contra producción, 13/11:

| Sitio | Fallas | Advertencias | Aprobados | Hallazgo real |
|---|---|---|---|---|
| Portal (`app.`) | 0 | 7 | 61 | Login sin token anti-CSRF (R-02) |
| Backoffice (`backoffice.`) | 0 | 5 | 63 | Login sin token anti-CSRF (R-02) |
| Seguimiento (`tracking.`) | 0 | 6 | 62 | Ninguno |

El resto de las advertencias son R-04 (CSP), R-08 (COEP), revisar `Cache-Control` en páginas de login (riesgo bajo)
y tres informativas o falsos positivos: contenido no cacheable (esperado en páginas dinámicas), detección de la
cookie anti-CSRF y un comentario dentro de un archivo JavaScript que trae Blazor.

### Hardening de producción

- RDS: sin acceso público, solo desde el grupo de seguridad de los nodos EKS, almacenamiento cifrado, SSL obligatorio.
- Balanceador: solo 80 y 443; 80 redirige a 443; certificado ACM.
- EKS: logs de auditoría (`api`, `audit`, `authenticator`); límites de CPU y memoria en los pods; probes de salud.
- Redis y RabbitMQ con contraseña y solo dentro del clúster.
- Secretos en Kubernetes y en GitHub, nunca en el repositorio.

## 7. Corrección y validación de vulnerabilidades

| Hallazgo | Herramienta | Corrección | Validación |
|---|---|---|---|
| Login sin token anti-CSRF (Portal y Backoffice) | ZAP, regla 10202 | El formulario incluye el token y `/session/login` lo valida; sin token válido vuelve al login. El script de k6 pide el token antes de iniciar sesión | Repetir ZAP tras el despliegue: la regla 10202 debe pasar en los dos sitios |
| SignalR Redis vulnerable | SCA | Sin arreglo disponible en .NET 10 | Aceptado con mitigación (R-03); el CI lo sigue reportando hasta que se actualice |

## 8. Pendientes

- Repetir ZAP en Portal y Backoffice después del despliegue y registrar el resultado en §7.
- R-05, R-06 y R-07: mejoras de bajo riesgo para después de la entrega.
