# ultimaMilla
Plataforma de gestión logística multi-tenant de extremo a extremo desarrollada sobre .NET 10. Permite a múltiples operadores logísticos administrar sus operaciones de entrega, conectando comercios, repartidores en calle y destinatarios finales. Asignatura: Taller de Sistemas de Información .NET (Edición 2026) — UTEC / Tecnólogo en Informática

## Ejecución local

Copie `.env.example` como `.env`, defina contraseñas locales y ejecute:

```powershell
docker compose up --build
```

El Backoffice queda en `http://localhost:8080` y el Portal de Comercio en `http://localhost:8081`. PostgreSQL, Redis y RabbitMQ se ejecutan en la misma red Docker. Backoffice aplica la migración inicial de Entity Framework Core sobre PostgreSQL al iniciar; Portal usa la misma cadena de conexión.

Para crear una migración posterior, defina `ConnectionStrings__Default` en la terminal y ejecute:

```powershell
dotnet ef migrations add NombreDeLaMigracion --project src/Plataforma.Infrastructure --startup-project src/Plataforma.Backoffice
```

## Infraestructura y entrega

La infraestructura AWS vive en `infra/terraform`: EKS, RDS PostgreSQL, ECR, ALB/ACM, NGINX, Redis, RabbitMQ y la base del worker. El state y las variables sensibles no se versionan.

El workflow `.github/workflows/ci-cd.yml` prueba Domain y Architecture en cada pull request. En pushes a `main`, además publica imágenes inmutables etiquetadas con el SHA del commit:

- Backoffice: `tallernet/backend`
- Portal de Comercio: `tallernet/frontend`

Para publicar desde GitHub Actions, configure los secretos de repositorio temporales de AWS Academy `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` y `AWS_SESSION_TOKEN`. Cuando los tres secretos estén cargados, cree la variable de repositorio `AWS_PUBLISH_ENABLED` con el valor `true`; hasta entonces solo se ejecuta CI. Configure también `AWS_REGION` si no usa `us-east-1`. Estas credenciales caducan y deben rotarse al renovar la sesión de AWS Academy.

> Nota: habilitar la publicación solo después de validar las credenciales de la sesión vigente de AWS Academy evita ejecuciones fallidas contra ECR.
