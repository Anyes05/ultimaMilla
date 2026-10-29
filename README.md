# ultimaMilla
Plataforma de gestión logística multi-tenant de extremo a extremo desarrollada sobre .NET 10. Permite a múltiples operadores logísticos administrar sus operaciones de entrega, conectando comercios, repartidores en calle y destinatarios finales. Asignatura: Taller de Sistemas de Información .NET (Edición 2026) — UTEC / Tecnólogo en Informática

## Ejecución local

Hace falta [Docker Desktop](https://docs.docker.com/desktop/setup/install/windows-install/) con el motor en ejecución. Copie `.env.example` como `.env` (si aún no existe) y levante todo el stack:

```powershell
copy .env.example .env
docker compose up --build
```

Defina en `.env` `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD` y `RABBITMQ_ERLANG_COOKIE` antes de iniciar. Ese archivo contiene las credenciales locales y está excluido de Git; no las copie en `docker-compose.yml` ni las suba al repositorio.

El Backoffice queda en `http://localhost:8080` y el Portal de Comercio en `http://localhost:8081`. PostgreSQL, Redis y RabbitMQ se ejecutan en la misma red Docker. Backoffice aplica la migración inicial de Entity Framework Core sobre PostgreSQL al iniciar; Portal usa la misma cadena de conexión. RabbitMQ Management queda en `http://localhost:15672` (usuario y contraseña de `.env`).

### Recorrido Portal a Backoffice

Con los servicios iniciados, abra el Portal en `http://localhost:8081` e ingrese con `norte.comercio` / `Demo123!`. Cree un envío con un destinatario y dirección cualquiera; el Portal mostrará el número de tracking al guardarlo.

Abra el Backoffice en `http://localhost:8080` e ingrese con `norte.despachador` / `Demo123!`. El envío recién creado aparece en la tabla de monitoreo y puede cambiarse de estado. Ambos usuarios pertenecen al mismo operador, por lo que comparten los envíos. Para probar el otro operador, use `sur.comercio` y `sur.despachador` con la misma contraseña; sus envíos se mantienen aislados de Norte.

Para reiniciar la demostración desde una base vacía, detenga el stack y elimine sus volúmenes antes de volver a levantarlo:

```powershell
docker compose down --volumes
docker compose up --build
```

Para crear una migración posterior, defina `ConnectionStrings__Default` en la terminal y ejecute:

```powershell
dotnet ef migrations add NombreDeLaMigracion --project src/Plataforma.Infrastructure --startup-project src/Plataforma.Backoffice
```

## Aplicacion movil: integracion API del 19/10

La app usa por defecto `https://api.tallernet.cloud-ip.cc/` para login Bearer,
descarga de rutas y sincronizacion. El selector **Usar demo (sin API)** mantiene
el doble como modo explicito, con SQLite separado. Los contratos ya son
[aceptados v1](contracts/repartidor/README.md).
Ver [prerrequisitos, comandos y recorrido de API](src/Plataforma.Mobile/README.md).

El proyecto [Plataforma.Mobile](src/Plataforma.Mobile/Plataforma.Mobile.csproj) usa
MAUI .NET 10 y CommunityToolkit.Mvvm. Sigue fuera de `Plataforma.sln` para no
exigir las herramientas MAUI al CI del servidor. Requiere la carga MAUI, Android
SDK y JDK 21. En VS Code seleccione el proyecto movil y el dispositivo Android,
configure la ruta local de JDK 21 y ejecute con F5.

- `Models`: ruta, envio y comprobante local con estado de sincronizacion.
- `Pages`: acceso y ruta con formulario de entrega.
- `ViewModels`: estado de pantalla, validacion y comandos.
- `Services`: interfaces, navegacion y doble del servidor.
- `Data`: SQLite en el almacenamiento privado de la app (`repartidor-demo.db`).
  Guarda transaccionalmente la ruta, los comprobantes y su estado en la cola,
  junto con la sesion demo y el estado del servidor falso para repetir las
  pruebas tras un reinicio. No guarda la clave.

Las vistas nativas reutilizan la identidad visual de `site.css` del Backoffice
y `app.css` del Portal: indigo `#4f46e5`, fondo `#f8fafc`, tarjetas redondeadas,
botones primarios/secundarios y los colores de estado del Backoffice. Se
mantienen los controles MAUI y una variante oscura; no se incorporo Bootstrap
ni se cambiaron los flujos por adaptar el estilo. La fuente sigue siendo la
Open Sans ya incluida en la app (Inter no esta incluida).

La app movil ahora usa exclusivamente la API; el selector demo fue retirado.
El trabajo offline y la cola API se conservan. Consultar el README movil para
configurar servidor y ejecutar el recorrido real.

Recorrido historico del doble (solo referencia de pruebas, no disponible en la app):

1. Ingresar con `alvaro@demo.test` / `Demo123!`. Es un acceso fijo de
   demostracion, no autenticacion de produccion.
2. Ver los tres envios ficticios y desactivar **Permitir conexion**.
   Tambien se puede activar el modo avion del telefono: este doble no usa la red.
3. Registrar una entrega con nombre ficticio. Queda pendiente en SQLite; se
   puede seguir operando sin conexion. Sincronizar sin conexion simulada informa el error
   y conserva los datos.
4. Cerrar completamente la app y abrirla. Seleccionar demo y pulsar
   **Continuar sesion guardada (sin red)**; recupera la misma ruta y sus entregas, sin descargar ni reemplazar
   la cola. Es una facilidad del doble, no autenticacion offline de produccion.
5. Activar **Permitir conexion** y pulsar **Sincronizar**. `DEMO-001` se acepta; `DEMO-002` y
   `DEMO-003` simulan cambios posteriores en el servidor a Entregado y
   Extraviado. Se rechazan sin sobrescribir el servidor ni borrar el comprobante
   local. Repetir la sincronizacion no duplica entregas aceptadas.
6. El cierre de sesion se bloquea si quedan pendientes o rechazos, para evitar
   descartarlos inadvertidamente. Si no quedan cambios sin resolver, cierra la
   sesion y restablece la conexion simulada para el siguiente acceso.
   No hay resolucion manual de rechazos todavia.

**Limites:** cerrar el proceso ya no pierde la ruta ni las entregas confirmadas.
Desinstalar la app o borrar sus datos si elimina la base. El interruptor solo
simula disponibilidad del servidor; no mide la conectividad real.
La base aun no esta cifrada: use exclusivamente datos ficticios. Quedan fuera de
este avance las capacidades de terreno (foto/firma/GPS).

**Contratos aceptados v1:** los [JSON del repartidor](contracts/repartidor/README.md)
se usan tanto para el intercambio HTTP como para el doble.
El login y la ruta del doble leen esos archivos; la sincronizacion intercambia
JSON con los datos de la cola SQLite. Los ejemplos estan incluidos como recursos
desde `contracts/repartidor`, sin mantener otra copia en la app.
Las pruebas verifican solicitudes y respuestas de ejemplo y el cliente HTTP.
La clave no se persiste; el token API se almacena con MAUI SecureStorage.

Las pruebas del recorrido y los ViewModels se ejecutan por separado:

```powershell
dotnet test '.\tests\Plataforma.Mobile.Tests\Plataforma.Mobile.Tests.csproj'
```

Ese proyecto enlaza el codigo independiente de plataforma de la app para
probarlo en .NET sin instalar Android; no verifica XAML ni reemplaza las pruebas
en telefono.

## Infraestructura y entrega

La infraestructura AWS vive en `infra/terraform`: EKS, RDS PostgreSQL, ECR, ALB/ACM, NGINX, Redis, RabbitMQ y la base del worker. El state y las variables sensibles no se versionan.

El workflow `.github/workflows/ci-cd.yml` prueba Domain y Architecture en cada pull request. En pushes a `main`, además publica imágenes inmutables etiquetadas con el SHA del commit:

- Backoffice: `tallernet/backend`
- Portal de Comercio: `tallernet/frontend`

Para publicar desde GitHub Actions, configure los secretos de repositorio temporales de AWS Academy `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` y `AWS_SESSION_TOKEN`. Cuando los tres secretos estén cargados, cree la variable de repositorio `AWS_PUBLISH_ENABLED` con el valor `true`; hasta entonces solo se ejecuta CI. Configure también `AWS_REGION` si no usa `us-east-1`. Estas credenciales caducan y deben rotarse al renovar la sesión de AWS Academy.

> Nota: habilitar la publicación solo después de validar las credenciales de la sesión vigente de AWS Academy evita ejecuciones fallidas contra ECR.

Si una publicación falla por credenciales, renueve la sesión de AWS Academy, actualice los secretos del repositorio y vuelva a ejecutar el workflow desde GitHub Actions.
