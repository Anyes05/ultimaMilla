# Decisión: banderas de funcionalidad por operador (opcional 7.4, 2 puntos)

**Estado:** aceptada e implementada  
**Responsable:** Nadia (pantalla, Portal, seguimiento, Backoffice y worker), con Ana (núcleo y API) y Álvaro (app)  
**Última revisión:** 10/11/2026  
**Requerimientos:** letra 7.4; aclaración 3.2 del cambio de requerimientos; RF 26, 28, 31 a 36 y 37 a 41

## Contexto

La letra pide activar y desactivar funcionalidades por inquilino en tiempo de ejecución, sin desplegar.
La aclaración 3.2 del cambio de requerimientos agrega cómo se verifica:

1. Al menos dos funcionalidades reales bajo bandera, activables por operador desde una interfaz.
2. La bandera se respeta en todas las capas donde interviene la funcionalidad: API, web, worker y app del repartidor.
3. Un cambio llega a todas las instancias en un tiempo acotado y conocido, sin reiniciarlas.
4. Queda registro de quién cambió cada bandera y cuándo.
5. Las cambia el administrador de la plataforma, no los usuarios del operador.

Una bandera decide si una funcionalidad está disponible para un operador. No es una regla operativa (cuántos
intentos se permiten) ni una tarifa: esas ajustan algo que ya existe y las configura el propio operador.

## Decisión

**Catálogo cerrado en código** (`CatalogoBanderas`): una clave mal escrita no compila, y cada bandera tiene
descripción y valor por defecto. Sin fila guardada para un operador, rige el valor por defecto.

| Clave | Funcionalidad | Por defecto | Dónde se hace cumplir |
|---|---|---|---|
| `entrega-parcial` | Entrega parcial de bultos (RF 31 a 36) | activa | Sincronización de la app: rechaza resultados por bulto. La ruta del día le informa a la app si está activa. |
| `retiro-domicilio` | Retiro en domicilio (RF 37 a 41) | apagada | Caso de uso `GestionRetiros` (Portal y API), Portal (no ofrece el formulario), Backoffice Retiros y Tablero (avisan que está apagada; lo ya pedido se sigue gestionando). |
| `seguimiento-reprogramacion` | El destinatario pide otra visita (RF 26) | activa | Seguimiento público (no muestra el botón) y caso de uso `SolicitarReprogramacion` (rechaza el pedido aunque llegue directo). |
| `avisos-comercio` | Avisos HTTP a los sistemas del comercio (RF 28) | activa | Worker (no genera avisos de ese operador), Portal (avisa y no permite reenviar) y Backoffice Avisos (avisa y no permite reintentar). |

**Una sola forma de leer la bandera en el servidor:** el puerto `IBanderasOperador.EstaActivaAsync(operadorId, clave)`,
implementado por `GestionBanderas`. La pantalla nunca es la única barrera: cada caso de uso vuelve a validar.

**El worker lee la tabla `BanderasOperador` directamente** en cada evento, con la misma consulta parametrizada que
ya usa para buscar el comercio del envío. Así no depende del núcleo y el cambio rige desde el evento siguiente.
Sin fila, aplica el valor por defecto del catálogo.

**Quién las cambia:** el perfil `administrador_plataforma` (`plataforma.admin` en la semilla), que no pertenece a
ningún operador. Entra al Backoffice directo a **Plataforma → Banderas**, que muestra todos los operadores y es lo
único que puede abrir. Para ese perfil no se aplica el filtro por inquilino, porque ve a todos los operadores.
La API `/api/plataforma/banderas` (consulta, cambio e historial) responde `401` sin sesión y `403` a cualquier
otro perfil, incluido el administrador de un operador.

**Auditoría:** cada cambio agrega una fila a `CambiosBandera` con operador, clave, valor, usuario y fecha. Nunca se
modifica ni se borra. La pantalla muestra los últimos 30.

## Propagación

- La lectura se cachea en Redis con la clave `operador:{id:N}:banderas`, que incluye el operador (aclaración 3.1).
- Al cambiar una bandera, esa entrada se borra en el momento, y además tiene una vigencia de **30 s** como tope.
- Redis es compartido por todas las réplicas, así que el cambio se ve en la petición siguiente en cualquier instancia.
- Si Redis no responde, cada réplica cae a su caché en memoria con la misma vigencia: en el peor caso, 30 s.
- El seguimiento público y la ruta del día de la app tienen su propia caché, de 2 minutos y 30 segundos. Por eso la
  bandera se aplica **después** de leer esas cachés: apagar una función se ve en la consulta siguiente, sin esperar
  a que venzan. En la ruta del día, junto con las banderas, viajan las reglas que la app necesita (entrega parcial,
  firma, foto y motivos de no entrega).

**Medido en Docker Compose el 10/11** (tiempo desde que el administrador confirma el cambio hasta verlo):

| Cambio | Dónde se observó | Tiempo |
|---|---|---|
| Activar retiro | Portal ofrece el formulario | 58 ms |
| Apagar retiro | Caso de uso rechaza un formulario que ya estaba abierto | 18 ms |
| Apagar reprogramación | Seguimiento deja de ofrecerla, con el detalle ya en caché | 31 ms |
| Activar reprogramación | Seguimiento vuelve a ofrecerla | 23 ms |
| Activar avisos | El worker genera el aviso del siguiente cambio de estado | 430 ms (incluye la cola) |
| Apagar entrega parcial | La sincronización siguiente se rechaza | en la petición siguiente |

## Alternativas descartadas

- **Configuración en `appsettings` o variables de entorno:** exige reiniciar o desplegar, que es justo lo que se prohíbe.
- **Librería de banderas (Microsoft.FeatureManagement u otra):** resuelve banderas globales; por inquilino y con
  auditoría había que escribir casi lo mismo, y suma una dependencia más que explicar.
- **Que el worker referencie el núcleo:** tendría que levantar el `DbContext` principal y sus servicios solo para leer
  un valor. Leer la tabla directamente sigue el patrón que el worker ya usa y no agrega dependencias.
- **Que el worker lea solo Redis:** la entrada puede no existir (nadie la pidió todavía) y igual haría falta la base como respaldo.
- **Banderas cambiadas por el administrador del operador:** la aclaración 3.2 lo descarta: quien ofrece el servicio
  decide qué funciones tiene cada operador.

## Consecuencias

- Cada funcionalidad nueva bajo bandera exige tocar todas sus capas; el catálogo cerrado hace visible qué falta.
- Apagar una función no borra lo ya hecho: los retiros pedidos y los avisos ya generados se siguen gestionando.
  Un aviso que ya estaba en reintento se completa aunque después se apague la bandera.
- El caché de banderas agrega una lectura por petición en las páginas que las consultan; con Redis es despreciable.

## App del repartidor

La app lee `funcionalidades` de la ruta del día y solo ofrece la entrega por bulto si `entrega-parcial` está activa
y la regla del operador la admite (`Ruta.EntregaParcialHabilitada`). El servidor la vuelve a validar al sincronizar.
