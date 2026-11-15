# ultimaMilla
Plataforma de gestión logística multi-tenant de extremo a extremo desarrollada sobre .NET 10. Permite a múltiples operadores logísticos administrar sus operaciones de entrega, conectando comercios, repartidores en calle y destinatarios finales. Asignatura: Taller de Sistemas de Información .NET (Edición 2026) — UTEC / Tecnólogo en Informática

## Ejecución local

Hace falta [Docker Desktop](https://docs.docker.com/desktop/setup/install/windows-install/) con el motor en ejecución. Copie `.env.example` como `.env` (si aún no existe) y levante todo el stack:

```powershell
copy .env.example .env
docker compose up --build
```

Defina en `.env` `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD` y `RABBITMQ_ERLANG_COOKIE` antes de iniciar. Ese archivo contiene las credenciales locales y está excluido de Git; no las copie en `docker-compose.yml` ni las suba al repositorio.

El Backoffice queda en `http://localhost:8080` y el Portal de Comercio en `http://localhost:8081`. PostgreSQL, Redis, RabbitMQ y MongoDB se ejecutan en la misma red Docker. Backoffice aplica la migración inicial de Entity Framework Core sobre PostgreSQL al iniciar; Portal usa la misma cadena de conexión. RabbitMQ Management queda en `http://localhost:15672` (usuario y contraseña de `.env`) y MongoDB queda disponible en `mongodb://localhost:27017`.

### Recorrido Portal a Backoffice

Con los servicios iniciados, abra el Portal en `http://localhost:8081` e ingrese con `norte.comercio` y la clave de desarrollo de la semilla (`ClaveDesarrollo` en [DatosSemilla.cs](src/Plataforma.Application/DatosSemilla.cs)). Cree un envío con un destinatario y dirección cualquiera; el Portal mostrará el número de tracking al guardarlo.

Abra el Backoffice en `http://localhost:8080` e ingrese con `norte.despachador` y la misma clave. El envío recién creado aparece en la tabla de monitoreo y puede cambiarse de estado. Ambos usuarios pertenecen al mismo operador, por lo que comparten los envíos. Para probar el otro operador, use `sur.comercio` y `sur.despachador` con la misma contraseña; sus envíos se mantienen aislados de Norte.

Para reiniciar la demostración desde una base vacía, detenga el stack y elimine sus volúmenes antes de volver a levantarlo:

```powershell
docker compose down --volumes
docker compose up --build
```

Para crear una migración posterior, defina `ConnectionStrings__Default` en la terminal y ejecute:

```powershell
dotnet ef migrations add NombreDeLaMigracion --project src/Plataforma.Infrastructure --startup-project src/Plataforma.Backoffice
```

## Usuarios de demostración

La semilla crea dos operadores (Norte y Sur) con estos usuarios, en local y en producción. Son datos de
demostración: no hay datos reales de personas. La clave de desarrollo está en la semilla (`ClaveDesarrollo` en
[DatosSemilla.cs](src/Plataforma.Application/DatosSemilla.cs)); el acceso a producción se entrega aparte.

| Rol | Dónde entra | Usuario (Norte) | Usuario (Sur) |
|---|---|---|---|
| Administrador de plataforma (banderas de todos los operadores) | Backoffice | `plataforma.admin` | — |
| Administrador del operador | Backoffice | `norte.admin` | `sur.admin` |
| Despachador | Backoffice | `norte.despachador` | `sur.despachador` |
| Operario de depósito | Backoffice | `norte.operario` | `sur.operario` |
| Comercio | Portal de Comercio | `norte.comercio` | `sur.comercio` |
| Repartidor | App móvil | `alvaro@demo.test` | `sur.repartidor` |

El destinatario no tiene cuenta: consulta su envío en el seguimiento público con el enlace o el número de seguimiento.

**Dónde probar en producción** (con el laboratorio de AWS encendido):

| Aplicación | Dirección |
|---|---|
| Backoffice (plataforma, administrador, despachador y operario) | https://backoffice.tallernet.cloud-ip.cc |
| Portal de Comercio | https://app.tallernet.cloud-ip.cc |
| Seguimiento público | https://tracking.tallernet.cloud-ip.cc/seguimiento |
| API del repartidor (la usa la app móvil) | https://api.tallernet.cloud-ip.cc |

Para comprobar el aislamiento entre operadores, entrar con un usuario de Norte y otro de Sur: cada uno ve solo los
envíos de su operador.

## Servicios locales

| Servicio | Dirección |
|---|---|
| Backoffice (y API del repartidor) | http://localhost:8080 |
| Portal de Comercio y seguimiento público | http://localhost:8081 |
| Correos al destinatario (Mailpit) | http://localhost:8025 |
| RabbitMQ Management | http://localhost:15672 (usuario y clave de `.env`) |
| PostgreSQL / Redis / MongoDB | `localhost:5432` / `localhost:6379` / `mongodb://localhost:27017` |

Para sumar la observabilidad (Grafana con Prometheus, Loki y Tempo, y el tablero técnico) se agrega el compose de
observabilidad; Grafana queda en http://localhost:3000 (`admin` / `admin`):

```powershell
docker compose -f docker-compose.yml -f docker-compose.observabilidad.yml up -d --build
```

## Aplicación móvil

La app del repartidor está hecha con .NET MAUI (Android) y CommunityToolkit.Mvvm, y consume la API del Backoffice
(`https://api.tallernet.cloud-ip.cc/` en producción). Funciona sin conexión con una base SQLite cifrada, registra
entregas totales o parciales por bulto con firma, foto, GPS y documento, escanea códigos QR, pide biometría para las
operaciones sensibles, envía la posición durante la jornada y recibe notificaciones push de Firebase con la app
cerrada. Queda fuera de `Plataforma.sln` para no exigir la carga de MAUI al CI del servidor.

Los prerrequisitos (carga MAUI, Android SDK, JDK 21), los comandos y la configuración del servidor están en
[src/Plataforma.Mobile/README.md](src/Plataforma.Mobile/README.md). Los contratos de la API están en
[contracts/repartidor](contracts/repartidor/README.md) y la política de conflictos de sincronización en el
[ADR 0013](docs/contratos/adr/0013-conflictos-sincronizacion.md).

## Pruebas

```powershell
dotnet test Plataforma.sln
dotnet test tests/Plataforma.Mobile.Tests/Plataforma.Mobile.Tests.csproj
```

| Suite | Qué cubre |
|---|---|
| `Plataforma.Domain.Tests` | Estados, transiciones, reglas operativas, tarifas, capacidad, recepción y retiros |
| `Plataforma.Infrastructure.Tests` | Casos de uso con EF Core, aislamiento entre operadores y, con `PRUEBAS_POSTGRES` definida, integración contra PostgreSQL real |
| `Plataforma.Architecture.Tests` | Que el Domain no dependa de ninguna otra capa |
| `Plataforma.Avisos.Tests` | Firma, reintentos, versionado e idempotencia de los avisos a comercios |
| `Plataforma.Mobile.Tests` | Lógica de la app sin Android: recorrido offline, cola de sincronización, cifrado local y concurrencia |

La prueba de carga con k6 y sus resultados están en [tests/carga](tests/carga/README.md).

## Despliegue remoto (AWS)

La infraestructura está descrita en Terraform en [infra/terraform](infra/terraform/README.md): EKS, RDS PostgreSQL,
ECR, ALB con certificado ACM, NGINX Ingress, Redis, RabbitMQ, el stack de observabilidad y el Worker. El estado y las
variables sensibles no se versionan. Hace falta AWS CLI con las credenciales temporales de AWS Academy, Terraform 1.5 o
superior, `kubectl` y `helm`.

| Script | Para qué |
|---|---|
| [infra/terraform/scripts/Import-Environment.ps1](infra/terraform/scripts/Import-Environment.ps1) | Carga la configuración local no sensible de `.env` antes de usar Terraform |
| [infra/terraform/scripts/Deploy-Lab.ps1](infra/terraform/scripts/Deploy-Lab.ps1) | Crea o reconcilia toda la infraestructura por fases (`-Action Plan` o `-Action Apply`) |
| [infra/terraform/scripts/Deploy-Applications.ps1](infra/terraform/scripts/Deploy-Applications.ps1) | Crea el secreto de la aplicación pidiendo las claves sin mostrarlas, ejecuta la migración y despliega las aplicaciones con un SHA ya publicado |
| [scripts/Resume-Lab.ps1](scripts/Resume-Lab.ps1) | Recupera el clúster cuando el laboratorio de AWS Academy se reanuda y los nodos cambian |

### Primer despliegue en una cuenta nueva

1. Desde `infra/terraform`: `. .\scripts\Import-Environment.ps1` y luego `.\scripts\Deploy-Lab.ps1 -Action Apply`.
   El script se detiene para cargar en Cloudns el CNAME de validación del certificado y, después, los CNAME de
   `app`, `api`, `backoffice` y `tracking` hacia el balanceador; se continúa escribiendo `CONTINUAR`.
2. Cargar en GitHub (Settings → Secrets and variables → Actions) los secrets del pipeline (tabla de abajo) y
   ejecutar el workflow para publicar las imágenes en ECR.
3. Crear el secreto de la aplicación y desplegar:
   `.\scripts\Deploy-Applications.ps1 -ImageTag <SHA-del-commit> -EnableWorker`.
4. Volver a ejecutar el workflow: desde ahí, cada push a `main` despliega solo.

Para reanudar un laboratorio pausado: renovar las credenciales (`aws configure` y token de sesión) y ejecutar
`.\scripts\Resume-Lab.ps1`. Para destruir el ambiente: `terraform destroy` desde `infra/terraform`, solo con acuerdo
del equipo, porque borra EKS, RDS (con sus datos), el balanceador, el certificado y ECR. El detalle de cada fase y de
la recuperación está en [infra/terraform/README.md](infra/terraform/README.md) y en el
[ADR 0014](docs/contratos/adr/0014-infraestructura-y-autoescalado.md).

### Pipeline de CI/CD

[.github/workflows/ci-cd.yml](.github/workflows/ci-cd.yml) corre en cada pull request y en cada push a `main`:

1. **Compilar y probar:** análisis de dependencias vulnerables (SCA), compilación con los analizadores de seguridad
   (SAST) y todas las suites de pruebas, incluida la integración contra un PostgreSQL efímero y la app móvil.
2. **Publicar y desplegar** (solo en `main`): construye las imágenes `tallernet/backend` (Backoffice),
   `tallernet/frontend` (Portal) y `tallernet/worker`, etiquetadas con el SHA del commit; las escanea con Trivy; carga
   las claves en el secreto de Kubernetes; ejecuta la migración y espera que cada despliegue quede sano.

| Secret de GitHub | Para qué |
|---|---|
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`, `AWS_REGION` | Credenciales temporales de AWS Academy; caducan y se renuevan con cada sesión |
| `AWS_PUBLISH_ENABLED` | `true` para publicar y desplegar; sin él solo corren las pruebas |
| `WORKER_DEPLOY_ENABLED` | `true` para desplegar el Worker |
| `MONGODB_CONNECTION_STRING` | Historial en MongoDB Atlas |
| `FIREBASE_SERVICE_ACCOUNT_JSON` | Push de reasignación urgente (opcional) |
| `SMTP_HOST`, `SMTP_USUARIO`, `SMTP_CLAVE`, `SMTP_REMITENTE` | Correos al destinatario (opcional) |

Si una publicación falla por credenciales, renovar la sesión de AWS Academy, actualizar los secrets y volver a ejecutar
el workflow.

### Historial documental MongoDB

El historial analítico usa MongoDB Atlas sin afectar PostgreSQL: cada `EnvioEstadoCambiado` llega por la cola propia `plataforma.historial-eventos` y se inserta en `tallernet.eventos_envio`. El Worker crea automáticamente la base, colección e índices al arrancar; no se crean manualmente desde Atlas.

Cada documento tiene `_id = eventId`, por lo que reintentos o mensajes duplicados no duplican el historial. Incluye `operadorId` para aislamiento multi-tenant, `envioId`, estado anterior/nuevo, fecha del evento, fecha de registro, versión y `traceId`.

| Índice automático | Uso |
| --- | --- |
| `_id_` único | Idempotencia por evento. |
| `{ operadorId: 1, envioId: 1, fecha: 1 }` | Historial ordenado de un envío. |
| `{ operadorId: 1, fecha: -1, _id: -1 }` | Listado por período y paginación por cursor. |
| `{ operadorId: 1, estadoNuevo: 1, fecha: -1 }` | Consultas y análisis por estado. |
| `{ registradoEn: 1 }` con TTL de 180 días | Retención automática de la copia analítica. |

Si Atlas no está disponible, los avisos a comercios no se detienen: el consumidor de historial reintenta con espera creciente y deriva el mensaje agotado a `plataforma.historial-eventos.fallidos`. La API autenticada `GET /api/historial/eventos` filtra siempre por el `operadorId` del JWT y pagina con cursor, sin aceptar el tenant desde el cliente.

En Atlas solo se configura el usuario técnico con `readWrite` sobre `tallernet`, la regla de red y el Secret de GitHub `MONGODB_CONNECTION_STRING`. El CD lo copia como `Mongo__ConnectionString` al Secret de EKS sin exponerlo. El detalle técnico está en [la documentación de MongoDB](docs/propuestas/mongodb-eventos.md).

En Compose se usa `mongo:8.2.2`, con la cadena interna `mongodb://mongo:27017/tallernet` para Backoffice y Worker. La base, colección `eventos_envio` e índices se crean automáticamente al arrancar el consumidor; el volumen `mongo-data` preserva los datos locales.

## Documentación

| Tema | Dónde |
|---|---|
| Registros de decisión de arquitectura (ADR 0001 a 0017) | [docs/contratos/adr](docs/contratos/adr) |
| Contratos de la API | [docs/contratos-api.md](docs/contratos-api.md) y [contracts/repartidor](contracts/repartidor/README.md) |
| Seguridad (requisitos, amenazas, riesgos, SAST, SCA, DAST) | [docs/contratos/seguridad.md](docs/contratos/seguridad.md) |
| Pruebas de resiliencia | [docs/contratos/adr/pruebas-resiliencia.md](docs/contratos/adr/pruebas-resiliencia.md) |
| Prueba de carga y autoescalado | [tests/carga](tests/carga/README.md) |
| Observabilidad | [infra/observabilidad](infra/observabilidad/README.md) |
| Diagramas de infraestructura, despliegue y arquitectura | https://joelleyes.github.io/NET/infraestructura.html |
