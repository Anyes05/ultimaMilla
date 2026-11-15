# ADR 0017: Observabilidad con Serilog, OpenTelemetry y Grafana en el clúster

**Estado:** aceptada  
**Responsable:** Nadia Inthamoussu (instrumentación y tablero); despliegue en EKS: Joel Leyes  
**Última revisión:** 15/11/2026  
**Requerimientos:** letra 6.12, 6.13

## Contexto

La letra exige registro estructurado con Serilog, métricas y trazas con OpenTelemetry, un identificador de
correlación que siga una operación de punta a punta (desde la app móvil hasta el worker) y al menos un tablero
técnico con tiempo medio de respuesta, latencias p95 y p99, tasa de errores, profundidad de las colas,
sincronizaciones pendientes, avisos a comercios fallidos y aciertos del caché. La herramienta de visualización
queda a elección del equipo.

Restricciones:

- **Tres procesos distintos** (Backoffice, Portal y Worker), con varias réplicas, que se comunican por RabbitMQ: una
  traza tiene que cruzar la cola.
- **Cuenta de AWS Academy:** sin permisos para crear roles de IAM nuevos y con presupuesto limitado.
- **El mismo código tiene que funcionar en local (Docker Compose) y en EKS** sin cambios.

## Decisión

1. **Un único proyecto `Plataforma.Observabilidad`** que configura lo mismo en los tres procesos:
   - **Logs con Serilog** en JSON compacto por la salida estándar, con el identificador de traza (`TraceId`) en cada
     línea. El nivel de las sondas de salud se baja para no llenar los logs.
   - **Métricas y trazas con OpenTelemetry**, exportadas por OTLP a la dirección de la variable estándar
     `OTEL_EXPORTER_OTLP_ENDPOINT`. Sin esa variable no se exporta nada y los procesos funcionan igual.
2. **Correlación de punta a punta con el estándar W3C `traceparent`:**
   - la app móvil lo envía en cada pedido a la API;
   - el outbox guarda el contexto de la traza junto con el evento;
   - el publicador lo agrega como encabezado del mensaje de RabbitMQ;
   - el Worker continúa esa misma traza al consumirlo, incluida la llamada HTTP al sistema del comercio.
3. **Métricas propias** además de las de HTTP y del runtime: aciertos y fallos del caché **por caso de uso**,
   lecturas a la base cuando el caché no está disponible, profundidad de las colas, avisos entregados y fallidos con
   su duración e intentos, y sincronizaciones pendientes por operador.
4. **Stack `grafana/otel-lgtm` dentro del clúster** (Grafana, Prometheus, Loki y Tempo en un solo pod), desplegado por
   Terraform con un Service interno, sin publicarse por Ingress. Se accede con `kubectl port-forward`. En local es el
   mismo contenedor en Docker Compose.
5. **Tablero versionado en el repositorio** (`infra/observabilidad/dashboards/tablero-tecnico.json`) y cargado por
   Terraform con un ConfigMap: "Plataforma — Tablero técnico", con tiempo medio de respuesta, latencias p95 y p99,
   tasa de errores, requests por instancia, profundidad de las colas, avisos fallidos, aciertos del caché por caso de
   uso, caché no disponible y sincronizaciones pendientes.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| CloudWatch Container Insights y X-Ray | Requieren roles de IAM y agentes que la cuenta de AWS Academy no deja crear, tienen costo por métrica y por log, y no funcionan igual en local |
| Amazon Managed Grafana o Managed Prometheus | Mismo problema de permisos y costo, y más piezas que reconstruir al cambiar de cuenta |
| Seq o Jaeger por separado | Cada uno resuelve solo una parte (logs o trazas); habría que sumar otra herramienta para las métricas |
| Panel de .NET Aspire | Pensado para desarrollo local, sin almacenamiento duradero ni tableros versionados |
| Un identificador de correlación propio en lugar de `traceparent` | Habría que propagarlo a mano por HTTP, outbox y RabbitMQ; el estándar W3C ya lo hacen las bibliotecas de OpenTelemetry |

## Consecuencias

- **Una operación se sigue de punta a punta** con un solo `TraceId`: el pedido web, el evento en el outbox, el
  mensaje en RabbitMQ, el Worker y el aviso al comercio aparecen en la misma traza de Tempo, y sus logs se filtran por
  ese identificador en Loki.
- **Sirve de evidencia en las pruebas:** durante la prueba de carga del 13/11 el tablero mostró los tiempos de
  respuesta y los pedidos repartidos entre las réplicas nuevas del Backoffice.
- **El stack no es de producción real:** es un solo pod sin réplica ni volumen persistente; si se reinicia, se pierde
  la historia de métricas, logs y trazas. Pide 1 GiB de memoria dentro de los nodos del laboratorio.
- **Acceso solo interno:** al no publicarse por Ingress, ver el tablero exige credenciales del clúster y
  `port-forward`. Es más seguro, pero menos cómodo para mostrarlo.
- **No hay alertas configuradas:** el tablero se mira, no avisa. Agregar alertas sobre errores, latencia y colas de
  fallidos queda como mejora.
- **Si falta la variable OTLP, no se pierde el servicio:** los procesos siguen funcionando y los logs siguen saliendo
  por consola; solo se deja de exportar.
