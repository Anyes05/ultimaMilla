# Plataforma.Mobile - desarrollo local y handoff de CI

## Preparacion de defensa: pendientes del 11/11

La app Android incorpora documento del receptor, biometria nativa, control de
carga por bulto, captura offline de retiros y SDK FCM. La compilacion no acredita
por si sola GPS continuo, recepcion push ni un recorrido de entrega/retiro real.
No se ejecutaron tests en esta implementacion; se agregaron regresiones al
archivo existente RepartidorHttpTests.

### Biometria y documento (6.11, RF 20)

Configurar huella o reconocimiento facial en Android 9 o posterior. Guardar una
entrega, intento o retiro pide identidad biometrica. Cancelar o fallar no guarda
el comprobante. Salir con pendientes, rechazos o escaneos tambien pide biometria;
si se confirma, elimina la sesion y conserva los datos de la cuenta original.
La app no guarda huellas ni imagenes del rostro ni sustituye biometria por una
confirmacion simple. Un dispositivo sin biometria configurada no puede completar
esas operaciones.

La regla ExigeDocumentoReceptor llega en la ruta y el documento se conserva en
el comprobante cifrado y se envia como documentoReceptor. Comprobar una regla
que lo exige: sin documento la app debe impedir guardar una entrega; con el
documento, verificar su comprobante en el servidor. Un intento sin entrega no
exige documento.

### Carga por bulto (RF 19)

Escanear carga solicita los codigos de todos los bultos esperados del envio.
Un codigo ajeno avisa sobrante, uno ya contado avisa repetido y cancelar muestra
los faltantes. No se emite la carga hasta completar el conjunto. Si el contrato
no incluye bultos, mantiene el escaneo por numero de seguimiento.

### Retiros en jornada mixta (RF 40)

Requiere desplegar el servidor actualizado y aplicar la migracion
20261111235817_NadiaRetirosEnLaApp y su predecesora NadiaRetiroFranjaYMotivo. Esta sesion no despliega ni migra el
ambiente publicado. Habilitar retiro en domicilio y programar un retiro en una
ruta del repartidor. La ruta devuelve retiros con direccion, contacto, cantidad,
fecha y posicion; la app los muestra junto a las entregas, indicando su parada.

Registrar retiro utiliza el mismo formulario de evidencia: codigos unicos de
todos los bultos esperados, nombre del responsable, foto JPEG hasta 512 KiB,
firma, GPS y hora. Con red desactivada debe guardarse y sobrevivir al reinicio o
al cierre de sesion. Solo la cuenta original puede retomarlo. Al volver la red,
se envia a POST /api/repartidor/retiros/sincronizacion con fecha y eventos.
La respuesta contiene aplicados y rechazados identificados por retiroId. La misma solicitud se puede reenviar tras una
confirmacion perdida; no se vuelve a completar ni a publicar el retiro.

Los rechazos conservan evidencia y motivo; Reintentar rechazados es manual y
reutiliza el mismo evento. Los comprobantes de dias anteriores permanecen en la
cola aunque cambie la ruta. El servidor valida operador, ruta y repartidor antes
de registrar, guarda evidencia y outbox en la misma transaccion y no publica
fotos o firmas en RabbitMQ ni en el listado general de retiros.

### FCM real (6.11 y 7.3)

1. Registrar en el proyecto Firebase una app Android con package
  com.companyname.plataforma.mobile y obtener su google-services.json. Ubicarlo
  en Platforms/Android antes de compilar; ese archivo esta ignorado por Git.
2. Activar Firebase Cloud Messaging HTTP v1. En el worker configurar
  Push__Fcm__ProjectId con el mismo proyecto (opcional si figura en la cuenta).
  Credenciales solo desde un Secret: Push__Fcm__CuentaServicioJson con la cuenta
  de servicio autorizada. Nunca incluir la cuenta de servicio en la APK ni en Git.
3. Sin Push__Fcm__CuentaServicioJson, el worker usa el emisor de laboratorio:
  escribe en logs y no entrega notificaciones al telefono. En contenedores hay
  que inyectar esa variable explicitamente mediante la configuracion de despliegue.
4. Conceder permiso de notificaciones. Abrir la ruta registra automaticamente
  el token real; su renovacion registra el nuevo y elimina el anterior de la
  misma cuenta. La pantalla distingue registro confirmado, falta de permiso y
  Firebase sin configurar. No imprime el token completo.
5. Cerrar la app sin forzar su detencion desde Ajustes. Reasignar con urgencia
  un envio al repartidor desde Backoffice. Debe recibirse el push con la app
  cerrada y, al tocarlo, abrir y actualizar la ruta. El texto no expone datos
  personales en la pantalla bloqueada. Android no garantiza recibir push tras
  una detencion forzada.

Salir intenta dar de baja el token en el servidor; sin red esa baja no puede
confirmarse. El worker elimina tokens que FCM declare UNREGISTERED usando
operador, repartidor y token como condicion. Permisos insuficientes del proyecto
Firebase o un ProjectId incorrecto no se consideran entrega exitosa.

### GPS y ADR 0013

Con jornada activa y ubicacion habilitada: pantalla apagada tres minutos,
modo avion manteniendo GPS, recuperar red y comprobar hora/posiciones en Flota
en la calle. Verificar tambien que el servicio se detenga al completar la
jornada o salir. No confundir proceso activo con lote aceptado por el servidor.

Leer [ADR 0013](../../docs/contratos/adr/0013-conflictos-sincronizacion.md).
Para defenderlo: el telefono propone eventos inmutables con identificador; el
servidor decide contra asignacion, estado y reglas vigentes; un rechazo no borra
evidencia; reenviar un evento confirmado no duplica efectos. Caso clave: entregar
offline un envio reasignado debe conservar el comprobante y mostrar el rechazo
al recuperar conexion. Cerrar sesion no equivale a borrar pendientes.

Se elimino CierreRutasSinIntento y su excepcion de catalogo porque no estaban
registrados y podian competir con evidencia offline. El cierre diario de
indicadores del worker sigue igual; no se agrego una entrega fallida automatica.

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

## Rastreo de ubicacion de la jornada (Android)

Con sesion valida y envios en transito de hoy, abrir o reanudar la ruta inicia
un servicio Android de primer plano con notificacion persistente. Solicita
ubicacion mientras se usa la app y, desde Android 13, permiso de notificaciones.
No solicita ACCESS_BACKGROUND_LOCATION. Una vez iniciado, el servicio puede
continuar con la pantalla bloqueada; no garantiza sobrevivir a un cierre forzado
ni a restricciones de bateria del fabricante. Tras ese cierre, abrir la ruta
permite iniciarlo de nuevo. Sin envios en transito o al cerrar sesion se detiene.

El proveedor GPS recibe un intervalo minimo solicitado de 30 segundos, sujeto
a disponibilidad y permisos. Cada muestra guarda coordenadas, hora y precision
cifradas en SQLite; la cola se separa por operador y repartidor. Envia lotes de
hasta 500 a POST /api/repartidor/posiciones cada 60 segundos mientras el servicio
esta activo, al reconectar y al finalizar operaciones de la ruta. El HTTP GPS
no mantiene el bloqueo de las entregas mientras espera la respuesta.

Solo una confirmacion valida elimina las muestras enviadas. Se purgan las de
mas de 24 horas al procesar su cuenta. Ante errores de red o HTTP 400 se conserva
el lote y se registra el fallo para diagnostico; un lote rechazado requiere
revision y puede impedir avanzar esa cola. Un HTTP 401 de la sesion vigente
exige volver a autenticarse sin borrar posiciones ni comprobantes. Si se cierra
sesion offline, las posiciones no confirmadas quedan para la misma cuenta;
nunca se envian con la cuenta del siguiente usuario.

Verificacion manual pendiente: ruta en transito, permisos concedidos, GPS activo,
pantalla bloqueada, recuperacion de red y aparicion de posiciones en el Backoffice.
La compilacion e instalacion Android no certifican ese recorrido ni el de FCM.

## Contratos y configuracion por ambiente

Actualizar ruta muestra el progreso y avisa si otra operacion impide la consulta.
La sincronizacion automatica no se ejecuta con la cola vacia ni borra el aviso
anterior al completarse. La lectura del token seguro tiene un limite de 10 segundos:
si el almacenamiento no responde, se libera la operacion y se muestra el error
sin borrar la sesion ni los comprobantes. La solicitud HTTP mantiene su limite de
30 segundos. No se fuerza el desbloqueo mientras una escritura sigue en curso.

Ante HTTP 401 en una solicitud autenticada se conserva la ruta, evidencia e
identificadores y se marca que hace falta autenticar de nuevo. La app muestra
acceso sin ejecutar Cerrar sesion ni borrar la cola. Tampoco restaura esa sesion
como valida tras reiniciar ni reintenta automaticamente hasta ingresar de nuevo.
El login de la misma cuenta conserva pendientes y desbloquea la sincronizacion;
otra cuenta/operador no puede reemplazar esa ruta. HTTP 403 no invalida la sesion.

Cada solicitud HTTP de la app crea una Activity W3C y envia traceparent.
Si hay una operacion padre, conserva su TraceId con un SpanId nuevo y propaga
tracestate. No agrega credenciales, coordenadas ni evidencia a las etiquetas.
Esto permite continuar la correlacion en ASP.NET, outbox y RabbitMQ; no instala
un exportador OTLP en el telefono ni certifica una traza completa en produccion.

El contrato vigente de Identity es [identidad v1](../../docs/contratos/identidad.md):
ASP.NET Identity como almacen de usuarios, login HTTP y JWT Bearer existentes.
El acuerdo cerrado indica que OIDC, redirect y PKCE NO aplican en v1.
No se inventan endpoints de autorizacion ni clientId, y no se cambia el login.
Una futura migracion OIDC requiere un acuerdo separado con el equipo.

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
reemplazarla con otro usuario mientras conserve su ruta. Cerrar sesion elimina
el token y permite volver al acceso aunque haya pendientes o rechazos. En ese
caso, o si hay escaneos guardados, conserva los datos locales para la cuenta
original. Hay que ingresar con esa cuenta y resolverlos antes de cambiar de
usuario. Sin pendientes ni escaneos se libera la ruta al cerrar sesion.

Login descarga rutas cuando no hay ruta local; reautenticarse conserva los
comprobantes. **Descargar ruta** actualiza desde el servidor solo sin pendientes
ni rechazos. Si no hay rutas se muestra una lista vacia; tras despachar desde
Backoffice hay que pulsar ese boton. Varias rutas del mismo dia/operador se
presentan juntas sin descartar envios. No se mezclan dias diferentes.

El acceso ya no muestra configuracion del servidor ni el boton de continuar
sesion. Al iniciar, una sesion local guardada abre directamente la ruta,
incluso sin red; cerrar sesion impide esa restauracion en el siguiente inicio.
Si falla la lectura local se muestra el error en acceso sin borrar datos.
Esto no renueva el JWT: una sesion vencida requiere autenticarse de nuevo.
Se conserva la URL previamente configurada en Preferences (`repartidor.api.url`)
para no cambiar de servidor los comprobantes existentes; en instalaciones nuevas
se utiliza la URL HTTPS por defecto. HTTPS mantiene validacion TLS.
Para Compose en emulador usar `http://10.0.2.2:PUERTO/` con el puerto publicado
del Backoffice; para adb reverse, `http://localhost:PUERTO/`.
Android solo permite HTTP a localhost, 127.0.0.1 y 10.0.2.2; en dispositivo fisico
por LAN usar HTTPS con certificado confiable. No incorporar secretos backend.

## Evidencia, carga y sincronizacion automatica (29/10)

La ruta no muestra el aviso fijo de guardado local ni el resumen numerico tras
sincronizar. Se mantienen los errores y avisos de rechazo que requieren atencion.

El boton se encuentra al pie de la pantalla junto a Actualizar ruta y Sincronizar.
Empezar recorrido muestra una parada a la vez en el orden de la lista actual,
sin optimizacion geografica. Solo incluye envios que permiten carga o entrega.
Guardar correctamente una entrega o intento avanza a la siguiente; un error o
cancelar no avanza. Funciona con guardado offline sin esperar sincronizacion.
Anterior permite consultar la parada previa sin duplicar registros. Pausar
recorrido devuelve la lista diaria; Empezar reconstruye las paradas disponibles.
Al terminar vuelve a la lista, conservando pendientes y rechazos. Cargar un
envio no avanza: debe confirmarse la carga antes de registrar su entrega.

Los errores visibles de conexion, acceso, sincronizacion, almacenamiento,
camara y GPS usan mensajes orientados al repartidor con el siguiente paso.
Los codigos HTTP y excepciones se mantienen en el diagnostico, no en pantalla.

Al guardar una entrega o intento fallido desde el formulario se obtiene una
posicion GPS nueva automaticamente (precision alta, timeout de 20 segundos).
No reutiliza una captura manual anterior. Si falla o no hay permisos, no guarda
el comprobante y conserva el formulario para reintentar. No requiere Internet
para solicitar GPS. No hay boton de captura manual de GPS.

Los estados usan textos simples: Entrega registrada, En camino y Pendiente de
carga. La sincronizacion se informa por separado para distinguir comprobantes
pendientes o rechazados de los ya confirmados, sin etiquetas tecnicas.
La pantalla de ruta prioriza resumen y tarjetas de envios, con Actualizar ruta
y Sincronizar siempre accesibles al pie, y tarjetas compactas sin renglones
vacios. Los botones del pie se apilan en ventanas
menores de 380 unidades; el contenido limita su ancho a 760 en pantallas grandes.
Los reintentos y tokens se agrupan en
Opciones de sincronizacion y dispositivo dentro de la lista desplazable.
Solo se muestra la accion de carga o entrega que permite el estado del envio.
No hay interruptor de conexion en la interfaz: para probar offline se usa el
modo avion del dispositivo. Cerrar sesion reemplaza Volver al acceso y conserva
el bloqueo si existen pendientes o rechazos; no es una salida que omita ese control.

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
  certificado. La foto no normaliza orientacion EXIF. La imagen JPEG adjunta se
  previsualiza debajo de Tomar foto; se limpia al cambiar de envio, cancelar el
  formulario o iniciar una nueva captura.
- **Intento fallido** guarda NoEntregado y exige motivo en Observaciones.
  El backend debe aceptar el evento antes de marcarlo como sincronizado.
- Con la pantalla de ruta activa, auto-sync al volver Internet, activar/reanudar
  ventana y cada 30 segundos con Internet. También revalida la ruta y sus banderas
  mientras la app está activa. No corre con app cerrada ni garantiza
  background Android; no usa WorkManager. Respeta la disponibilidad de Internet,
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

Al sincronizar por HTTP, FechaDispositivo se normaliza a UTC para compatibilidad
con PostgreSQL. Se conserva el mismo instante, el eventoId y la hora con offset
del comprobante local. Tambien se aplica a pendientes guardados antes de esta
correccion: no se necesita borrar la cola ni volver a registrar las entregas.

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
- Restaurar automaticamente la sesion guardada sin red; permite trabajar sobre datos previamente
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
