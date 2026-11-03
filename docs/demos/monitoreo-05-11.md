# Monitoreo 05/11 — guion de Nadia (tablero y comportamiento con dos instancias)

Objetivo del monitoreo (letra 8.3): *"Solución desplegada en la nube mediante infraestructura como código, con al
menos dos instancias balanceadas y el tablero de observabilidad operativo"*. Joel presenta la infraestructura;
Nadia, el tablero y el comportamiento de su código con varias instancias.

## Preparación (antes del monitoreo, en la ventana de AWS)

1. Pipeline en verde y pods en el SHA esperado (Joel).
2. Grafana abierto: `kubectl -n tallernet port-forward svc/tallernet-otel-lgtm 3000:3000` → http://localhost:3000 →
   Dashboards → Plataforma → **Plataforma — Tablero técnico**.
3. Datos previos (para que los paneles tengan valores):
   - **Un aviso fallido:** como `norte.admin`, activar la caída del receptor de prueba, cambiar de estado un envío de
     Norte, esperar unos 70 s (reintentos de 5, 15 y 45 s) y volver a activar el receptor. No reintentarlo todavía.
   - **Una sincronización pendiente:** despachar un envío de Norte a un repartidor (queda "Asignado a ruta").
   - **Tráfico:** el k6 smoke (paso 2 del guion) unos minutos antes.
4. Pestañas abiertas: tablero de Grafana, Explore → Tempo, Backoffice (Monitoreo y Tablero en vivo),
   `https://app.tallernet.cloud-ip.cc/seguimiento/<token>` y una terminal para k6.

## Guion (6 a 8 minutos)

| # | Acción | Qué se muestra | Letra |
|---|---|---|---|
| 1 | Tablero técnico | Tiempo medio, **p95 y p99**, **tasa de errores**, **profundidad de colas**, **sincronizaciones pendientes por operador**, **avisos fallidos**, **aciertos del caché por caso de uso** | 6.12 |
| 2 | `k6 run -e PERFIL=humo -e BACKOFFICE=https://backoffice.tallernet.cloud-ip.cc -e API=https://api.tallernet.cloud-ip.cc tests/carga/carga.js` | Suben las requests; **Requests por instancia** muestra las dos réplicas repartiendo carga | 6.13, 7.2 |
| 3 | Mientras corre k6, Joel baja una réplica | k6 sigue sin errores; una instancia deja de recibir tráfico y la otra absorbe | 6.13, escenario 1 |
| 4 | Explore → Tempo → una traza del worker | `deliver` (RabbitMQ) → Postgres → `POST` al comercio → receptor en el Backoffice; con su **enlace** a la traza del request original (`POST /api/envios/{id}/estado` → `publish`) | 6.12 correlación |
| 5 | Desde la traza, los logs | Logs JSON con `TraceId`, servicio e instancia: qué réplica procesó qué | 6.12 |
| 6 | `https://backoffice…/health` | Estado en JSON de base, Redis y RabbitMQ; `/ready` y `/live` para los probes | 6.13 |
| 7 | Seguimiento público (Blazor) | Etapas del envío; panel de caché "seguimiento" sube | 6.2, 6.7 |
| 8 | (si hay tiempo) Avisos fallidos → Reintentar | "Avisos fallidos" vuelve a 0 | 6.8 |

## Preguntas probables

- **¿p95 y p99 con dos réplicas son confiables?** Cada réplica envía su histograma; Prometheus los suma antes de
  calcular el percentil.
- **¿Qué pasa si Redis cae?** El caché devuelve "no está" y se lee de la base; tras el primer error se pausa 15 s para
  no esperar el timeout en cada request. `/health` pasa a *Degraded* (200). Panel "Caché no disponible".
- **¿Y si cae RabbitMQ?** `/health` *Degraded*; los eventos quedan en el outbox y se reintentan; el worker deja de
  estar listo (sonda) hasta reconectar.
- **¿Cómo evitan tareas duplicadas con varias instancias?** El barrido del worker usa un bloqueo de Postgres
  (`pg_try_advisory_xact_lock`); el outbox, un bloqueo en Redis (Ana).
- **¿Y el límite de tasa con dos réplicas?** La cuenta está en Redis: 30 por minuto por IP en total, no por réplica.
- **¿Por qué la traza del worker está "enlazada" y no es la misma?** RabbitMQ.Client 7 vincula consumidor y
  productor con un *link* (convención de OpenTelemetry para mensajería); se navega de una a otra.
- **¿Qué son los mensajes en la cola `fallidos` si "Avisos fallidos" está en 0?** Son copias de auditoría de avisos
  agotados; al reintentar desde la pantalla se reintenta desde la base y la copia queda.

## Qué no hacer en vivo

- No apagar Redis, RabbitMQ ni la base en producción (regla del equipo: nada a mano en la infraestructura).
  Si preguntan, se explica y se muestra el panel y los tests que lo cubren.
