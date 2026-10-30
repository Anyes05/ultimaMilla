# Propuesta: notificación push de reasignación urgente (opcional 7.3, 3 puntos)

Estado: **propuesta para acordar** entre Ana (evento), Álvaro (FCM y app) y Nadia (worker).

## Qué pide la letra

> Notificaciones push reales recibidas con la aplicación cerrada, aplicadas a la reasignación urgente de un
> envío, con gestión del ciclo de vida de los identificadores de dispositivo.

Y la 6.1: el worker se comunica con el resto **solo por la cola**. La API no llama a FCM directamente.

## Flujo propuesto

```
Backoffice: despachador reasigna un envío a otro repartidor (caso de uso de Ana)
   │ misma transacción: envío + outbox
   ▼
plataforma.eventos ──envioreasignado.v1──▶ plataforma.push-repartidor (cola nueva)
                                               │
                                     worker: ConsumidorPushReasignacion (Nadia)
                                               │ busca tokens del repartidor nuevo
                                               │ idempotencia por (eventId, dispositivoId)
                                               ▼
                                     IEmisorPush.EnviarAsync(...)  (Álvaro: FCM HTTP v1)
                                               │
                                     reintentos con espera + cola de fallidos (igual que avisos)
```

## Contrato del evento (lo define Ana; esta es la propuesta)

`EnvioReasignado` v1, routing key `envioreasignado.v1`, mismas convenciones que `EnvioEstadoCambiado`:

```json
{
  "eventId": "…",
  "fecha": "2026-11-06T10:00:00-03:00",
  "operadorId": "…",
  "envioId": "…",
  "numeroSeguimiento": "ENV-…",
  "repartidorAnteriorId": "… o null",
  "repartidorNuevoId": "…",
  "rutaNuevaId": "…",
  "urgente": true
}
```

- `numeroSeguimiento` viaja para armar el texto del push sin consultar la base del núcleo.
- Sin datos personales del destinatario en el mensaje (la notificación se ve en la pantalla bloqueada).

## Qué hace cada uno

| Quién | Qué | Dónde |
|---|---|---|
| **Ana** | Caso de uso de reasignación (respetando "un envío no está en dos rutas") que escribe el outbox `EnvioReasignado`; contrato en `contracts/mensajes/v1/` | Domain / Application |
| **Nadia** | Cola y consumidor en el worker; búsqueda de tokens; idempotencia; reintentos, cola de fallidos y métricas; interfaz `IEmisorPush` y una implementación de prueba que solo registra en el log | `Plataforma.Worker` |
| **Álvaro** | Implementación `EmisorPushFcm : IEmisorPush` (FCM HTTP v1 con cuenta de servicio); recepción en la app con la app cerrada; al tocar la notificación, descargar la ruta actualizada | `Plataforma.Worker/Push/` y `Plataforma.Mobile` |
| **Joel** | Secret con las credenciales de FCM (`Fcm__CredencialesJson`), referenciado solo por `queue-worker` | Kubernetes |

Así Álvaro programa **solo** la parte de FCM, contra una interfaz fija, sin tocar colas ni reintentos.

## Interfaz entre worker y FCM

```csharp
public interface IEmisorPush
{
    Task<ResultadoPush> EnviarAsync(MensajePush mensaje, CancellationToken cancellationToken);
}

public sealed record MensajePush(
    string Token, string Plataforma, string Titulo, string Cuerpo,
    IReadOnlyDictionary<string, string> Datos);   // tipo=reasignacion, envioId, rutaId

public enum ResultadoPush
{
    Enviado,
    TokenInvalido,    // FCM: UNREGISTERED / INVALID_ARGUMENT → el token se da de baja
    ErrorTransitorio  // 429 / 5xx / red → se reintenta con espera
}
```

## Ciclo de vida del token (lo exige la letra)

- **Alta y baja desde la app**: ya existen `POST` y `DELETE /api/repartidor/dispositivos` (Ana y Álvaro).
- **Baja automática**: si FCM responde `TokenInvalido`, el worker borra ese token, porque el teléfono
  desinstaló la app o renovó el token.
  - **Decisión pendiente:** hoy el worker no referencia el núcleo (`Application` e `Infrastructure`).
  - **Opción A:** el worker referencia el núcleo y usa `QuitarDispositivoRepartidor`.
  - **Opción B:** el worker publica `dispositivoinvalido.v1` y lo procesa el Backoffice.
  - Recomiendo **A**, porque el documento de diseño dice que el worker "comparte los contratos y modelo
    de dominio del monolito".

## Para la demo (escenario de la letra)

1. App del repartidor B cerrada.
2. El despachador reasigna con urgencia un envío del repartidor A al B.
3. Le llega el push a B; al tocarlo, la app abre la ruta actualizada.
4. Opcional: desinstalar la app, reasignar de nuevo y ver en el log que el token se dio de baja.

## Orden para no bloquearse

1. Acordar este documento (contrato y opción A o B).
2. Nadia deja el consumidor con `IEmisorPush` de prueba (registra en el log): se puede probar con el evento
   publicado a mano en RabbitMQ local, sin esperar a Ana ni a Álvaro.
3. Ana publica el evento real; Álvaro enchufa FCM. Ninguno espera al otro.
