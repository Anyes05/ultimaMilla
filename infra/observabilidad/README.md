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

## Despliegue en EKS (Joel)

Terraform instala automáticamente un Deployment interno `tallernet-otel-lgtm` con la imagen
`grafana/otel-lgtm:0.11.10`, su Service ClusterIP y los ConfigMaps de provisionamiento/tablero.
Pide 1 GiB de memoria y no se publica por Ingress.

La variable se configura automáticamente en los tres procesos (`backoffice`, `portal`, `queue-worker`), apuntando al Service interno:

   ```yaml
   - name: OTEL_EXPORTER_OTLP_ENDPOINT
     value: http://<service-otel>.tallernet.svc.cluster.local:4317
   ```

   No es un secreto: va como `value`, no en el Secret de aplicación.
El tablero `dashboards/tablero-tecnico.json` y `proveedor-tableros.yaml` se montan por ConfigMap. El tablero usa
la fuente de datos Prometheus con `uid: prometheus`, incluida en `otel-lgtm`.

Acceso seguro a Grafana, sin Ingress:

```powershell
kubectl -n tallernet port-forward service/tallernet-otel-lgtm 3000:3000
```

Abrir `http://localhost:3000` mientras el comando permanece activo.

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
| `plataforma_cache_lecturas_total{caso_uso, resultado}` | Backoffice, Portal | Lecturas del caché por caso de uso (`seguimiento`, `configuracion-operador`, `tarifas`) y resultado (`acierto`, `fallo`, `error` = caché no disponible, se leyó de la base) |
| `plataforma_cola_mensajes{cola}` | Worker | Mensajes esperando en cada cola de avisos |
| `plataforma_avisos_entregas{estado}` | Worker | Entregas de avisos por estado (`Fallido` = esperan reintento manual) |
| `plataforma_avisos_intentos_total{resultado}` | Worker | Intentos: `entregado`, `reintento`, `fallido` |
| `plataforma_avisos_duracion_seconds_*` | Worker | Tiempo de la llamada HTTP al comercio |
| `plataforma_sincronizaciones_pendientes{operador}` | Backoffice | Envíos en la calle (asignados a ruta o en tránsito) cuyo resultado el repartidor todavía no sincronizó |

**Sincronizaciones pendientes:** el servidor no ve la cola local del teléfono; mide los resultados que todavía
espera de los dispositivos (envíos asignados a ruta o en tránsito). Si la app llegara a informar su cola, se
puede sumar como otra serie del mismo panel.

## Local (compose)

```powershell
docker compose -f docker-compose.yml -f docker-compose.observabilidad.yml up -d --build
```

Grafana: http://localhost:3000 → Dashboards → Plataforma → Tablero técnico.
