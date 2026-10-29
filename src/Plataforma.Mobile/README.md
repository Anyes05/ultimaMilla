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

Los comandos de restore son instrucciones para preparar un checkout. El 29/10
se agrego ZXing.Net.Maui.Controls 0.10.4 para escaneo de camara y se restauro
al compilar Android. Build Android: 0 errores/advertencias; 44 tests aprobados.
Los tests usan xUnit/VSTest sobre `net10.0`, enlazan el codigo independiente de
MAUI y **no requieren emulador, dispositivo ni workloads MAUI**. Usan SQLite
de prueba bajo el directorio de salida del proyecto y la eliminan al terminar;
no son pruebas de interfaz. No se ha certificado su ejecucion en CI.

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
La URL base por defecto esta configurada en `MauiProgram.cs`:
`https://api.tallernet.cloud-ip.cc/` (Backoffice, no Portal).
No se deshabilita la validacion TLS.

| Operacion | Uso |
|---|---|
| POST `/api/repartidor/login` | Usuario/clave; recibe JWT |
| GET `/api/repartidor/rutas/hoy` | Bearer; lista de rutas del dia UTC del servidor |
| POST `/api/repartidor/sincronizacion` | Lote de comprobantes y resultado por envio |
| POST `/api/repartidor/dispositivos` | Alta manual de token, plataforma android |
| DELETE `/api/repartidor/dispositivos/{token}` | Baja manual de token (404 se trata como ausente) |

El JWT se guarda con MAUI SecureStorage, no en SQLite; la clave no se guarda.
401 requiere volver al acceso y autenticarse otra vez (sin borrar la cola).
No hay refresh token automatico. 403 y demas errores se muestran explicitamente;
un fallo de red nunca se convierte en una sincronizacion exitosa ni activa el
doble silenciosamente. No se usan credenciales AWS desde la app.

La app solo admite API: no hay selector ni registro DI del modo demo.
Los dobles se conservan como soporte de pruebas automatizadas y persistencia
compartida, no como una alternativa de acceso. El seed de API usa
usuario para Norte y `sur.repartidor` para Sur, con `Demo123!` en el laboratorio.
No usar estos accesos de desarrollo en produccion.

La app usa exclusivamente `repartidor-api.db`. Si existe una base demo anterior,
no se abre, elimina ni migra a la API.
Cada base API queda ligada al operador y repartidor del login; se rechaza
reemplazarla con otro usuario mientras conserve su ruta. Cerrar sesion exige
no tener pendientes ni rechazos.

Login descarga rutas cuando no hay ruta local; reautenticarse conserva los
comprobantes. **Descargar ruta** actualiza desde el servidor solo sin pendientes
ni rechazos. Si no hay rutas se muestra una lista vacia; tras despachar desde
Backoffice hay que pulsar ese boton. Varias rutas del mismo dia/operador se
presentan juntas sin descartar envios. No se mezclan dias diferentes.

En acceso, **Configurar servidor HTTPS / Compose** persiste la URL en
Preferences (`repartidor.api.url`). Requiere cerrar sesion sin pendientes para
no enviar evidencia de un servidor a otro. HTTPS mantiene validacion TLS.
Para Compose en emulador usar `http://10.0.2.2:PUERTO/` con el puerto publicado
del Backoffice; para adb reverse, `http://localhost:PUERTO/`.
Android solo permite HTTP a localhost, 127.0.0.1 y 10.0.2.2; en dispositivo fisico
por LAN usar HTTPS con certificado confiable. No incorporar secretos backend.

## Evidencia, carga y sincronizacion automatica (29/10)

- **Escanear carga** abre ZXing con permiso de camara; valida el codigo exacto
  del envio AsignadoARuta y guarda un evento durable hacia EnTransito. Incluye
  entrada manual explicita si el lector no puede leer; no simula deteccion.
- Un solo evento pendiente por envio. Hasta confirmar la carga con el servidor,
  entregar esta bloqueado. Carga aceptada pasa al historial local; la entrega
  siguiente tiene otro eventoId y no sobrescribe la evidencia anterior.
  Los confirmados se archivan tambien en el snapshot cifrado y sobreviven a
  Descargar ruta (incluso si desaparece el envio). Cerrar sesion sin pendientes
  elimina la copia local; el servidor ya debe conservar esos comprobantes.
- Formulario: foto MediaPicker Android, muestreo de imagen y compresion JPEG
  hasta 512 KiB, firma dibujada GraphicsView (JSON de trazos <=64 KiB), GPS con
  permisos/timeout y hora de guardado del dispositivo con offset. Cancelar foto
  no inventa evidencia. Foto/GPS fallidos muestran mensaje y permiten reintentar.
  Evidencias son opcionales conforme al contrato; no son firma legal ni GPS
  certificado. La foto no normaliza orientacion EXIF; previsualizacion pendiente.
- **Intento fallido** guarda NoEntregado y exige motivo en Observaciones.
  El backend debe aceptar el evento antes de marcarlo como sincronizado.
- Con la pantalla de ruta activa, auto-sync al volver Internet, activar/reanudar
  ventana y cada 45 segundos con Internet. No corre con app cerrada ni garantiza
  background Android; no usa WorkManager. Respeta Permitir conexion,
  no interrumpe formulario ni captura. Manual y automatico comparten guards;
  el servicio impide registro/descarga/login/logout concurrentes durante await.
- Maximo 100 envios por lote. Timeouts/respuestas invalidas conservan los mismos
  eventoId; una confirmacion perdida se reintenta idempotentemente. Solo pendientes
  se envian automaticamente; rechazos conservan comprobante y motivo y requieren
  resolver el conflicto con el operador. **Reintentar rechazados** es una accion
  manual explicita que reenvia el mismo comprobante/eventoId sin editarlo; si
  persiste el conflicto seguira rechazado. No se reenvian en bucle ni se descartan.
- Alta/baja token es cliente HTTP manual, no integra SDK FCM, no obtiene token
  real ni recibe notificaciones. No registrar valores ficticios en produccion.

## Cifrado local

El payload JSON de EstadoDemo Id=1 esta cifrado AES-256-GCM, nonce aleatorio y
autenticacion de version/fila. Clave generada en SecureStorage, nunca hardcodeada.
**No es SQLCipher ni cifrado integral del archivo SQLite**: esquema/metadatos
siguen visibles. Demo y API usan archivos separados. Se desactiva backup Android.
Al abrir un snapshot plano se deserializa/valida, cifra, verifica roundtrip y
confirma transaccionalmente sin resetear la cola; secure_delete y VACUUM eliminan
paginas libres locales de la migracion. No elimina copias historicas externas.
Una clave perdida o ciphertext alterado produce error, no reinicializacion.
No borrar SecureStorage/reinstalar sin resolver pendientes: no hay recuperacion
remota de la clave ni exportacion de evidencia. Inyeccion ISnapshotCipher permite
probar todo en net10 sin dependencias MAUI.

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

Los 44 tests cubren logica, persistencia, contratos y HTTP con un handler de prueba
(401/403/500, red/timeout, lotes inconsistentes, conflictos, reintento con el mismo
eventoId, multiples rutas, aislamiento entre modos, carga/entrega secuenciales,
evidencias enviadas, exclusion concurrente, token alta/baja, migracion cifrada y
clave incorrecta), no el renderizado XAML ni eventos Connectivity reales.
En una version anterior en Samsung se verificaron arranque, recuperacion tras cierre del proceso,
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

Esta iteracion valida compilacion Android y tests net10, no instala ni ejecuta
camara/GPS/firma en telefono. Falta prueba fisica de permisos, escaneo, foto,
trazos y cambio real de red, y recorrido integrado contra backend local con su
migracion aplicada. Se modifico backend local como indica contracts/repartidor;
no se llamo produccion ni se modificaron recursos AWS. Compresion de
foto solo implementada en Android; iOS/Windows/Mac Catalyst no certificados.
No hay push FCM, sync app cerrada ni gestion de recuperacion de clave perdida.
El contrato actual confirma nuevamente eventoId ya aplicado sin duplicar prueba.

## Futuro workflow separado

Coordinar con Joel un runner Windows para Android y, si se confirma ese objetivo,
Windows. iOS requiere un job macOS separado. Los unit tests pueden ejecutarse sin
dispositivo; las pruebas de UI y de modo avion requieren otra estrategia.
No se creo ni modifico ningun workflow como parte de este handoff.
