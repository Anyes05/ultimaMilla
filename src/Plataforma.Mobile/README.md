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
verificaron durante el desarrollo: 0 errores/advertencias y 24 tests aprobados.
Los tests usan xUnit/VSTest sobre `net10.0`, enlazan el codigo independiente de
MAUI y **no requieren emulador, dispositivo ni workloads MAUI**. Usan SQLite
temporal; no son pruebas de interfaz. No se ha certificado su ejecucion en CI.

En esta maquina se usaron directorios de salida aislados para evitar bloqueos
de archivos del editor. Este es el comando exacto de la ultima compilacion e
instalacion correcta (rutas de sesion locales, no copiar al workflow):

```powershell
dotnet build 'E:\Taller de .NET\ultimaMilla\src\Plataforma.Mobile\Plataforma.Mobile.csproj' -t:Run -f net10.0-android '-p:AdbTarget=-s%20RF8N72W61ZW' '-p:JavaSdkDirectory=C:\Users\agaru\.jdk\jdk-21.0.10' '-p:IntermediateOutputPath=C:\Users\agaru\.copilot\session-state\5b542746-8272-424a-9433-d388c3fa5569\files\mobile-contract-validation\obj\' '-p:OutputPath=C:\Users\agaru\.copilot\session-state\5b542746-8272-424a-9433-d388c3fa5569\files\mobile-contract-validation\bin\' --no-restore
```

Para ejecutar en otro dispositivo, usar el comando de build normal agregando
`-t:Run` y `'-p:AdbTarget=-s%20SERIAL'`, reemplazando SERIAL por el valor de
`adb devices`. No ejecutar builds CLI simultaneamente con una sesion de
depuracion MAUI activa.

## Contratos y configuracion por ambiente

La app **no consume HTTP actualmente**: usa un doble dentro del proceso con
JSON incluidos como recursos desde [contracts/repartidor](../../contracts/repartidor/README.md).
Son una **propuesta v0.1 pendiente de acuerdo con Ana**, no endpoints publicados:

| Operacion propuesta | Uso |
|---|---|
| POST `/api/repartidor/login` | Credenciales y sesion simuladas |
| GET `/api/repartidor/rutas/hoy` | Lista con una ruta de prueba |
| POST `/api/repartidor/sincronizacion` | Lote de comprobantes y resultado por envio |

La demo no necesita URL de API, secretos, JWT, cookies ni servicios externos.
Las credenciales ficticias son `alvaro@demo.test` / `Demo123!`.
La ruta de ejemplo tiene fecha fija 15/10/2026.

Para integrar la API real se deben acordar URL HTTPS por ambiente, autenticacion,
expiracion/renovacion de sesion, errores HTTP y permisos por operador/repartidor.
Eso aun no esta implementado. No incorporar secretos de backend a la app ni al
repositorio; un cliente movil no puede proteger un secreto compartido embebido.

## Casos offline y sincronizacion para validar

- Login simulado, descarga JSON y conservacion de la ruta SQLite.
- Registrar entrega sin conexion simulada y conservarla tras cerrar el proceso.
- Continuar la sesion demo guardada sin red; no es autenticacion offline real.
- Sincronizar un lote construido desde la cola SQLite, incluido un lote vacio.
- DEMO-001: aceptar entrega si el servidor doble sigue EnTransito.
- DEMO-002 y DEMO-003: rechazar por Entregado/Extraviado y conservar el
  comprobante local y el motivo, sin sobrescribir el estado del servidor.
- Impedir cierre de sesion con pendientes o rechazos para no perder comprobantes.
- Serializacion/deserializacion de contratos y errores de entrada.

Los 24 tests cubren logica, persistencia y contratos, no el renderizado XAML.
En Samsung se verificaron arranque, recuperacion tras cierre del proceso,
sincronizacion y navegacion/formulario. Sigue pendiente la prueba manual con
modo avion real; el switch de conexion simula desconexion.

SQLite esta en el almacenamiento privado, **sin cifrado**: usar solo datos
ficticios. API real, idempotencia de servidor, cifrado, foto/firma/GPS y push
no quedan certificados por estas pruebas.

## Futuro workflow separado

Coordinar con Joel un runner Windows para Android y, si se confirma ese objetivo,
Windows. iOS requiere un job macOS separado. Los unit tests pueden ejecutarse sin
dispositivo; las pruebas de UI y de modo avion requieren otra estrategia.
No se creo ni modifico ningun workflow como parte de este handoff.
