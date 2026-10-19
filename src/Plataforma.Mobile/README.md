# Plataforma.Mobile - desarrollo local y handoff de CI

## Separacion del servidor

Mantener `Plataforma.Mobile` y `Plataforma.Mobile.Tests` fuera de
`Plataforma.sln` y del CI actual del servidor en Ubuntu. No agregar workloads
MAUI ni builds moviles a ese pipeline. Un futuro workflow movil sera separado.

## Plataformas

- **Android:** plataforma usada y validada actualmente, con un Samsung fisico.
  Framework `net10.0-android`.
- **Windows:** declarado en el proyecto cuando el host es Windows
  (`net10.0-windows10.0.19041.0`), pero no validado.
- **iOS y Mac Catalyst:** declarados por la plantilla en hosts no Linux, pero
  no validados. iOS necesita macOS, Xcode compatible y firma para dispositivo.

Que un framework este declarado no significa que sea un objetivo confirmado
de entrega. Por ahora, el alcance comprobado es Android. No compilar todos
los frameworks de la plantilla para validar solamente Android.

## Prerrequisitos para Android en Windows

- .NET SDK 10; version local usada: `10.0.402`.
- Workload MAUI con soporte Android. Entorno local usado: MAUI `10.0.20` y
  Microsoft.Android.Sdk.Windows `36.1.118`.
- Android SDK y platform-tools; aceptar licencias e instalar los componentes
  requeridos por el workload.
- **JDK 21**. JDK 25 no es compatible con el SDK Android usado.
- Para ejecutar: dispositivo Android autorizado por ADB o emulador iniciado.
  No se necesita dispositivo para compilar.

En esta maquina el JDK esta en `C:\Users\agaru\.jdk\jdk-21.0.10`.
La configuracion de VS Code en `.vscode\settings.json` usa esa ruta local;
otros equipos deben ajustarla. No es una ruta portable para CI.

## Comandos PowerShell

Ejecutar desde la raiz del repositorio. Ajustar las rutas del SDK/JDK a cada
maquina. Con los prerrequisitos instalados, el restore explicito para Android
es:

```powershell
$jdk = 'C:\Users\agaru\.jdk\jdk-21.0.10'
dotnet restore '.\src\Plataforma.Mobile\Plataforma.Mobile.csproj' -p:TargetFrameworks=net10.0-android "-p:JavaSdkDirectory=$jdk"
dotnet build '.\src\Plataforma.Mobile\Plataforma.Mobile.csproj' -f net10.0-android "-p:JavaSdkDirectory=$jdk" --no-restore

dotnet restore '.\tests\Plataforma.Mobile.Tests\Plataforma.Mobile.Tests.csproj'
dotnet test '.\tests\Plataforma.Mobile.Tests\Plataforma.Mobile.Tests.csproj' --no-restore --verbosity minimal
```

Los comandos de restore son instrucciones para preparar un checkout; no se
repitieron para este cambio de documentacion. Build Android y tests se
verificaron durante el desarrollo: 0 errores/advertencias y 36 tests aprobados.
Los tests usan xUnit/VSTest sobre `net10.0`, enlazan el codigo independiente de
MAUI y **no requieren emulador, dispositivo ni workloads MAUI**. Usan SQLite
temporal; no son pruebas de interfaz. No se ha certificado su ejecucion en CI.

Comando de compilacion e instalacion usado el 18/10 (serial y JDK locales,
reemplazarlos en otros equipos):

```powershell
dotnet build 'E:\Taller de .NET\ultimaMilla\src\Plataforma.Mobile\Plataforma.Mobile.csproj' -t:Run -f net10.0-android '-p:AdbTarget=-s%20RF8N72W61ZW' '-p:JavaSdkDirectory=C:\Users\agaru\.jdk\jdk-21.0.10' --no-restore
```

Para ejecutar en otro dispositivo, usar el comando de build normal agregando
`-t:Run` y `'-p:AdbTarget=-s%20SERIAL'`, reemplazando SERIAL por el valor de
`adb devices`. No ejecutar builds CLI simultaneamente con una sesion de
depuracion MAUI activa.

## Contratos y configuracion por ambiente

La app inicia en **API real**, usando HTTPS y los
[contratos aceptados v1](../../contracts/repartidor/README.md).
La URL base esta configurada en `MauiProgram.cs`:
`https://api.tallernet.cloud-ip.cc/` (Backoffice, no Portal).
No se deshabilita la validacion TLS.

| Operacion | Uso |
|---|---|
| POST `/api/repartidor/login` | Usuario/clave; recibe JWT |
| GET `/api/repartidor/rutas/hoy` | Bearer; lista de rutas del dia UTC del servidor |
| POST `/api/repartidor/sincronizacion` | Lote de comprobantes y resultado por envio |

El JWT se guarda con MAUI SecureStorage, no en SQLite; la clave no se guarda.
401 requiere volver al acceso y autenticarse otra vez (sin borrar la cola).
No hay refresh token automatico. 403 y demas errores se muestran explicitamente;
un fallo de red nunca se convierte en una sincronizacion exitosa ni activa el
doble silenciosamente. No se usan credenciales AWS desde la app.

El selector **Usar demo (sin API)** permite volver al doble de forma explicita.
Las credenciales ficticias del doble son `alvaro@demo.test` / `Demo123!`.
Su ruta de ejemplo tiene fecha fija 15/10/2026. El seed de API usa ese mismo
usuario para Norte y `sur.repartidor` para Sur, con `Demo123!` en el laboratorio.
No usar estos accesos de desarrollo en produccion.

Se mantienen bases independientes: `repartidor-demo.db` (se conserva la demo
anterior) y `repartidor-api.db`. No se migra la cola ficticia a la API.
Cada base API queda ligada al operador y repartidor del login; se rechaza
reemplazarla con otro usuario mientras conserve su ruta. Cerrar sesion exige
no tener pendientes ni rechazos.

Login descarga rutas cuando no hay ruta local; reautenticarse conserva los
comprobantes. **Descargar ruta** actualiza desde el servidor solo sin pendientes
ni rechazos. Si no hay rutas se muestra una lista vacia; tras despachar desde
Backoffice hay que pulsar ese boton. Varias rutas del mismo dia/operador se
presentan juntas sin descartar envios. No se mezclan dias diferentes.

Para otro ambiente cambiar la URL HTTPS en `MauiProgram.cs` y recompilar.
No incorporar secretos de backend a la app ni al repositorio.

## Casos offline y sincronizacion para validar

- Login HTTP/Bearer y login simulado, descarga JSON y conservacion de ruta SQLite.
- Registrar entrega sin conexion simulada y conservarla tras cerrar el proceso.
- Continuar la sesion guardada sin red; permite trabajar sobre datos previamente
  descargados, no autentica ante el servidor ni renueva el token.
- Sincronizar un lote construido desde la cola SQLite, incluido un lote vacio.
- DEMO-001: aceptar entrega si el servidor doble sigue EnTransito.
- DEMO-002 y DEMO-003: rechazar por Entregado/Extraviado y conservar el
  comprobante local y el motivo, sin sobrescribir el estado del servidor.
- Impedir cierre de sesion con pendientes o rechazos para no perder comprobantes.
- Serializacion/deserializacion de contratos y errores de entrada.

Los 36 tests cubren logica, persistencia, contratos y HTTP con un handler de prueba
(401/403/500, red/timeout, lotes inconsistentes, conflictos, reintento con el mismo
eventoId, multiples rutas y aislamiento entre modos), no el renderizado XAML.
En Samsung se verificaron arranque, recuperacion tras cierre del proceso,
sincronizacion y navegacion/formulario. Tambien login HTTPS real y descarga
de cero rutas para el 19/10 UTC. Para cerrar el recorrido real falta despachar
una ruta de Norte para ese dia y `alvaro@demo.test`, descargarla, entregar
offline, sincronizar y comprobar el estado en Backoffice. No se crearon envios
ni rutas en el backend durante esta validacion.

Se creo el AVD `UltimaMilla_API36` con la imagen Android 36 ya instalada
(`google_apis;x86_64`). Se verifico modo avion real en ese emulador (valor Android
1): entrega ficticia en el doble, cierre forzado del proceso, recuperacion sin
red y sincronizacion aceptada al volver. No se corto la conexion del telefono.
Esto no certifica la persistencia del backend.

Para ejecutar en el emulador desde la raiz del repositorio:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\emulator\emulator.exe" -avd UltimaMilla_API36 -memory 2048
# En otra terminal, con el emulador iniciado:
dotnet build '.\src\Plataforma.Mobile\Plataforma.Mobile.csproj' -t:Run -f net10.0-android '-p:AdbTarget=-s%20emulator-5554' '-p:JavaSdkDirectory=C:\Users\agaru\.jdk\jdk-21.0.10' -p:EmbedAssembliesIntoApk=true --no-restore
```

`EmbedAssembliesIntoApk=true` evita depender de archivos de Fast Deployment.
Verificar el serial con `adb devices`.

SQLite esta en el almacenamiento privado, **sin cifrado**: usar solo datos
ficticios. Idempotencia durable del servidor, cifrado, foto/firma/GPS y push
no quedan certificados por estas pruebas. El contrato v1 permite rechazar un
reintento ya aplicado; se conserva el comprobante para no perder evidencia.

## Futuro workflow separado

Coordinar con Joel un runner Windows para Android y, si se confirma ese objetivo,
Windows. iOS requiere un job macOS separado. Los unit tests pueden ejecutarse sin
dispositivo; las pruebas de UI y de modo avion requieren otra estrategia.
No se creo ni modifico ningun workflow como parte de este handoff.
