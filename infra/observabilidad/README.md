# Observabilidad (letra 6.12)

Responsable del código: Nadia. Responsable del despliegue en EKS: Joel.

## Qué ya está en el código (en producción desde `8631a48`)

| Proceso | Logs (Serilog) | Métricas (OpenTelemetry) | Trazas (OpenTelemetry) |
|---|---|---|---|
| Backoffice | JSON por línea en stdout | HTTP (latencia, códigos), caché, runtime | Requests, Postgres, RabbitMQ, HTTP saliente |
| Portal | JSON por línea en stdout | HTTP, caché, runtime | Requests, Postgres, RabbitMQ |
| Worker | JSON por línea en stdout | Colas, avisos, runtime | Consumo de RabbitMQ (continúa la traza del mensaje), Postgres, HTTP al comercio |

Todo se envía por **OTLP gRPC** a la dirección de la variable estándar `OTEL_EXPORTER_OTLP_ENDPOINT`.
**Sin esa variable no se exporta nada** y las apps funcionan igual (es el estado actual de producción).

## Qué hace falta en EKS (Joel)

1. Un receptor OTLP con almacenamiento y Grafana. Dos opciones:
   - **Simple (la misma de compose):** un Deployment de `grafana/otel-lgtm:0.11.10` con un Service que exponga
     `4317/TCP` (OTLP gRPC, interno) y `3000/TCP` (Grafana). Es un solo pod; pide alrededor de 1 GB de RAM.
   - **Por partes:** OpenTelemetry Collector + Prometheus + Tempo + Loki + Grafana (más piezas, más memoria).
2. La variable en los tres Deployments (`backoffice`, `portal`, `queue-worker`), apuntando al Service interno:

   ```yaml
   - name: OTEL_EXPORTER_OTLP_ENDPOINT
     value: http://<service-otel>.tallernet.svc.cluster.local:4317
   ```

   No es un secreto: va como `value`, no en el Secret de aplicación.
3. El tablero: cargar `dashboards/tablero-tecnico.json` en Grafana. Con `otel-lgtm` alcanza con montar
   `proveedor-tableros.yaml` en `/otel-lgtm/grafana/conf/provisioning/dashboards/` y la carpeta `dashboards/`
   en `/otel-lgtm/tableros-plataforma` (por ejemplo, con un ConfigMap). El tablero usa la fuente de datos
   Prometheus con `uid: prometheus`, que es la que trae `otel-lgtm`.
4. Acceso a Grafana: por `kubectl port-forward` alcanza para la demo; si se publica por el Ingress, tiene que
   llevar autenticación (por defecto `otel-lgtm` entra como administrador anónimo).

## Cómo verificar que funciona

1. En los logs de cualquier pod no tiene que aparecer un error de exportación OTLP.
2. En Grafana → Explore → Prometheus, la consulta `target_info` devuelve `plataforma-backoffice`,
   `plataforma-portal` y `plataforma-worker`; con dos réplicas, dos `instance` por web.
3. El tablero **Plataforma → Tablero técnico** muestra datos. Para generar tráfico: navegar el Backoffice,
   consultar el seguimiento público, o correr k6 (`tests/carga`).
4. En Explore → Tempo, buscar `{resource.service.name="plataforma-worker"}`: una traza de aviso muestra
   `deliver` (RabbitMQ) → Postgres → `POST` al comercio → `POST /api/receptor-prueba/avisos` en el Backoffice.

## Métricas propias (nombres en Prometheus)

| Métrica | Origen | Significado |
|---|---|---|
| `http_server_request_duration_seconds_*` | Backoffice, Portal | Latencia y códigos HTTP (media, p95, p99, errores) |
| `plataforma_cache_aciertos_total`, `plataforma_cache_fallos_total` | Backoffice, Portal | Lecturas del caché por réplica (el tablero las suma) |
| `plataforma_cola_mensajes{cola}` | Worker | Mensajes esperando en cada cola de avisos |
| `plataforma_avisos_entregas{estado}` | Worker | Entregas de avisos por estado (`Fallido` = esperan reintento manual) |
| `plataforma_avisos_intentos_total{resultado}` | Worker | Intentos: `entregado`, `reintento`, `fallido` |
| `plataforma_avisos_duracion_seconds_*` | Worker | Tiempo de la llamada HTTP al comercio |

Pendiente de definir (Ana y Álvaro): **sincronizaciones pendientes**. El tablero tiene el panel reservado.

## Local (compose)

```powershell
docker compose -f docker-compose.yml -f docker-compose.observabilidad.yml up -d --build
```

Grafana: http://localhost:3000 → Dashboards → Plataforma → Tablero técnico.
