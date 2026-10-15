# Propuesta de contratos del repartidor

**PROPUESTA v0.1 - pendiente de revision y acuerdo con Ana.**
No son contratos publicados por el backend ni aprobados por el equipo.
Todos los datos y credenciales son ficticios.

## Archivos para revisar

| Operacion propuesta | Solicitud | Respuesta de ejemplo |
|---|---|---|
| POST `/api/repartidor/login` | [login.request.json](login.request.json) | [login.response.json](login.response.json) |
| GET `/api/repartidor/rutas/hoy` | Sin cuerpo | [rutas-hoy.response.json](rutas-hoy.response.json) |
| POST `/api/repartidor/sincronizacion` | [sincronizacion.request.json](sincronizacion.request.json) | [sincronizacion.response.json](sincronizacion.response.json) |

JSON camelCase, identificadores UUID, fechas `yyyy-MM-dd`, instantes ISO 8601 con
offset y estados como texto (no numeros). Campos no nulos en estos ejemplos.
La fecha del juego de prueba es fija: **15/10/2026**; no representa datos reales
del dia en que se ejecute la app.

## Que ya existe y que proponemos

- **Rutas:** la forma de la lista, operador y envios sigue `RutaDelDia`,
  `OperadorVisible` y `EnvioDeRuta` de Application. El caso de uso devuelve una
  lista, no un objeto unico. El ejemplo tiene una ruta; el movil actual informa
  error si el doble recibe cero o varias, en vez de descartar rutas sin avisar.
- **Login:** `email`/`clave` y toda la respuesta son propuestos. `autenticacion`
  vale `Simulada` exclusivamente para el doble. No hay JWT ni cookie real y este
  JSON NO autoriza nada en el backend. Ana debe definir mecanismo, expiracion,
  renovacion y errores antes de conectar el cliente real. El usuario y operador
  reales deben derivarse de la identidad validada por el servidor.
- **Sincronizacion:** `fecha` y `eventos[].envioId/estado` reflejan los argumentos
  del caso de uso actual. **Son agregados propuestos**, aun no consumidos por
  ese caso de uso: `eventoId`, `nombreReceptor`, `observaciones`,
  `fechaDispositivo`. `eventoId` es el identificador durable del comprobante
  SQLite; se reutiliza al reintentar. No se genera otro al sincronizar.
- **Respuesta:** `aplicados` con `envioId/estado/intentosRealizados` y
  `rechazados` con `envioId/motivo` siguen Application. **`estadoActual` en los
  rechazos es un agregado propuesto**, no existe todavia en su DTO. Sirve para
  mostrar el estado autoritativo sin borrar la entrega local.

La respuesta de ejemplo supone que despues de descargar la ruta el envio 002
paso a Entregado y el 003 a Extraviado. El 001 sigue EnTransito. Es el escenario
de conflicto que la app ya tenia; no se agregaron nuevas capacidades de terreno.

## Uso actual en la app

Los JSON se incluyen como recursos desde esta misma carpeta, sin copias manuales.
El doble lee las credenciales y respuesta de login y la lista de rutas.
La app serializa cada lote desde la cola SQLite; el doble deserializa el lote,
calcula su resultado y devuelve JSON que la app deserializa para actualizar la
cola. Los ejemplos de sincronizacion se verifican con pruebas contra ese doble:
no se devuelve una respuesta fija si el usuario registra otras entregas.

**No hay servidor HTTP ni llamadas de red:** el intercambio JSON ocurre dentro
del proceso para permitir la demostracion offline. Las rutas HTTP de esta tabla
son propuestas; no se crearon endpoints ni se cambio el backend.
Las credenciales invalidas se representan con rechazo del login en el doble.
Codigos HTTP reales (401/403/validacion), formato de errores, autenticacion,
idempotencia del servidor, fecha de negocio/zona horaria y limites de lote
quedan pendientes de acordar; no se afirman implementados.

Las bases SQLite creadas antes de estos contratos se conservan: no se borran
entregas pendientes ni se reemplaza una ruta local al volver a ingresar.
Los UUID fijos del JSON se usan al iniciar una demo sin ruta guardada.

## Mensaje para Ana

> Prepare una propuesta en contracts/repartidor y el doble de la app ya la usa.
> La ruta y parte de sincronizacion siguen los modelos de Application.
> Necesito que revisemos login, los campos del comprobante, eventoId para
> reintentos y estadoActual en los rechazos, porque son propuestas nuevas.
> No cambie tus endpoints ni conecte la API real. Si acordamos otro formato,
> adapto el cliente y los ejemplos juntos.
