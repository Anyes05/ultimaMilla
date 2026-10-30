# Propuesta: banderas de funcionalidad por operador (opcional 7.4, 2 puntos)

Estado: **propuesta para acordar** con Ana (lectura en la aplicación). Pantalla: Nadia.

## Qué pide la letra

> Activación y desactivación de funcionalidades por inquilino en tiempo de ejecución, sin nuevo despliegue.

Para que cuente, cada bandera tiene que **cambiar el comportamiento real** y aplicarse en el servidor;
no alcanza con esconder un botón.

## Catálogo propuesto (pocas y visibles en la demo)

| Clave | Por defecto | Qué cambia | Dónde se aplica |
|---|---|---|---|
| `avisos-comercio` | activa | Si está apagada, el operador no envía avisos HTTP a sus comercios | Worker (al crear entregas) |
| `seguimiento-historial` | activa | Si está apagada, el seguimiento público muestra solo el estado actual | Seguimiento público |
| `seguimiento-reprogramacion` | apagada | El destinatario puede pedir reprogramación desde el enlace | Seguimiento público y su caso de uso |
| `tablero-tiempo-real` | activa | Si está apagada, el tablero no recibe actualizaciones en vivo | Backoffice y hub |

Con dos operadores configurados distinto (por ejemplo, Norte con reprogramación y Sur sin ella) se muestra
el aislamiento y la configuración por inquilino en la misma demo (escenario 4 de la defensa).

## Lo que haría Ana (núcleo)

- Entidad `BanderaOperador { OperadorId, Clave, Activa, ActualizadaEn, ActualizadaPor }`, con filtro de
  operador como el resto.
- Catálogo de claves y valores por defecto en `Application` (constantes con descripción), para que una clave
  mal escrita no compile.
- Puerto de lectura:

  ```csharp
  public interface IBanderasOperador
  {
      Task<bool> EstaActivaAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default);
  }
  ```

- Caso de uso `CambiarBandera(operadorId, clave, activa, usuario)`, con auditoría, y `ListarBanderas(operadorId)`.
- Caché en Redis con clave `operador:{operadorId:N}:banderas` (prefijo de operador, sección 5 del diseño),
  **invalidada al cambiar**. Como Redis es compartido, el cambio se ve enseguida en las dos réplicas.

## Lo que haría Nadia

- Pantalla **Administración → Banderas** (solo administrador): lista con interruptor, descripción, quién y
  cuándo la cambió.
- Aplicar las banderas que caen en mis componentes: `avisos-comercio` (worker), `seguimiento-*` (seguimiento
  público) y `tablero-tiempo-real`.

## Punto a decidir

El worker no referencia `Application`. Para `avisos-comercio` puede:
- **A.** Referenciar el núcleo y usar `IBanderasOperador`. Es la misma decisión que en la propuesta de push.
- **B.** Leer la clave de Redis directamente.

Recomiendo **A**, para tener una sola forma de leer las banderas.

## Orden para no bloquearse

1. Acordar el catálogo y la interfaz (este documento).
2. Ana publica la interfaz y el caso de uso; con eso yo ya puedo hacer la pantalla contra la interfaz.
3. Cada uno aplica las banderas de su área.
