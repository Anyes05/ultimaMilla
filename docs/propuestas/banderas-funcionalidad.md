# Propuesta: banderas de funcionalidad por operador (opcional 7.4, 2 puntos)

Estado: **propuesta para acordar** con Ana (núcleo) y Álvaro (app). Pantalla: Nadia.
Actualizada el 01/11 con la aclaración 3.2 del documento de cambio de requerimientos.

## Qué se verifica para dar los puntos (aclaración 3.2)

1. Al menos **dos funcionalidades reales** bajo bandera, activables **por operador** desde una interfaz de administración.
2. La bandera se respeta **en todas las capas** donde interviene la funcionalidad: API, aplicaciones web, worker
   y **aplicación del repartidor**.
3. Un cambio alcanza a **todas las instancias** en un **tiempo acotado y conocido**, sin reiniciarlas.
4. Queda **registro de quién cambió cada bandera y cuándo**.
5. Las activa el **administrador de la plataforma** (quien ofrece el servicio), **no** los usuarios del operador.

Una bandera decide si una funcionalidad **está disponible o no** para un operador. No es una regla operativa
(cuántos intentos se permiten) ni una tarifa: eso ajusta algo que ya existe.

## Catálogo propuesto

| Clave | Funcionalidad | Capas donde se aplica |
|---|---|---|
| `entrega-parcial` | Entrega parcial de bultos (RF 31 a 36) | App (permite marcar bultos sueltos), API de sincronización (rechaza parciales si está apagada), Backoffice, seguimiento |
| `retiro-domicilio` | Retiro en domicilio (RF 37 a 41) | Portal (botón "Solicitar retiro"), API de comercios, Backoffice y Despacho, app (paradas de retiro) |
| `seguimiento-reprogramacion` | El destinatario pide reprogramación desde el enlace (RF 26; es el ejemplo de la aclaración) | Seguimiento público y su caso de uso |
| `avisos-comercio` | Avisos HTTP a los sistemas del comercio | Portal (suscripciones) y worker (no crea entregas) |

Las dos primeras salen del propio cambio de requerimientos (la aclaración las da como ejemplo): permiten
habilitarlo operador por operador. En la demo, por ejemplo, Norte con entrega parcial y Sur sin ella.

## Diseño

**Núcleo (Ana):**

- `BanderaOperador { OperadorId, Clave, Activa }` y `CambioBandera { OperadorId, Clave, Activa, Usuario, Fecha }`.
  La segunda es el registro de auditoría: solo se agregan filas, nunca se modifican.
- Catálogo de claves en `Application`, como constantes con descripción y valor por defecto, para que una clave mal escrita no compile.
- Puerto de lectura, el mismo para todas las capas del servidor:

  ```csharp
  public interface IBanderasOperador
  {
      Task<bool> EstaActivaAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default);
  }
  ```

- Casos de uso `ListarBanderas()` (todos los operadores, solo para el administrador de plataforma),
  `CambiarBandera(operadorId, clave, activa, usuario)` y `HistorialBanderas(operadorId?)`.
- **Propagación con tiempo acotado:** la lectura se cachea en Redis con la clave `operador:{id:N}:banderas`
  (lleva el operador, como pide la aclaración 3.1). Al cambiar una bandera, la entrada **se borra en el momento**;
  además tiene una vigencia de **30 s** como tope. Redis es compartido, así que todas las réplicas ven el cambio
  enseguida, y en el peor caso (por ejemplo, Redis caído) en 30 s. Ese número va en el ADR.
- **Perfil "administrador de plataforma":** un usuario que no pertenece a ningún operador. Es la única pantalla
  que lee banderas de **todos** los operadores, así que esa consulta salta el filtro de inquilino. Entra en la
  "lista corta y auditable" que prevé la sección 5 del documento de diseño.

**App del repartidor (Álvaro):** lee las banderas de su operador en la respuesta de `rutas/hoy` (por ejemplo,
`"funcionalidades": ["entrega-parcial"]`) y oculta o muestra la opción. La API vuelve a validar al sincronizar:
la app no es la única barrera.

**Pantalla (Nadia):** sección **Plataforma → Banderas** en el Backoffice, solo para el administrador de plataforma:

- tabla con los operadores en las filas y las banderas en las columnas, con un interruptor en cada celda;
- historial con quién cambió qué y cuándo.

**Cada uno aplica las banderas en su capa:**

- Nadia: Portal, seguimiento, Backoffice y worker.
- Álvaro: la app.
- Ana: los casos de uso y la API.

## Punto a decidir

El worker no referencia `Application`. Para leer `avisos-comercio` puede:
- **A.** Referenciar el núcleo y usar `IBanderasOperador`. Es la misma decisión que la propuesta de push.
- **B.** Leer la clave de Redis directamente.

Recomiendo **A**: una sola forma de leer las banderas en todas las capas.

## Orden para no bloquearse

1. Acordar el catálogo, la interfaz y el perfil de administrador de plataforma (este documento).
2. Con la interfaz acordada, Nadia hace la pantalla, aunque Ana todavía no la haya implementado.
3. Ana implementa; la pantalla se conecta sin cambios. Cada uno aplica las banderas de su capa.
