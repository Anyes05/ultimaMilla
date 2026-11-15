# ADR 0014: Infraestructura en AWS y autoescalado

**Estado:** aceptada  
**Responsable:** Joel Leyes  
**Última revisión:** 14/11/2026  
**Requerimientos:** letra 6.13, 6.14, opcional 7.1

## Contexto

La letra exige desplegar en una nube pública sobre servidores reales, con HTTPS y certificado válido, al menos dos
instancias de la API detrás de un balanceador, toda la infraestructura como código y un pipeline que compile, pruebe
y despliegue. El opcional 7.1 pide además escalado automático por métricas de carga, con evidencia durante una
prueba de carga.

Restricciones del equipo:

- **Cuenta de AWS Academy:** presupuesto limitado, sesiones que vencen, roles de IAM ya creados (no se pueden crear
  roles nuevos) y laboratorios que se pausan y reanudan, con posible reemplazo de nodos. Al agotarse el crédito
  asignado, es necesario aprovisionar el ambiente en otro laboratorio y otra cuenta.
- **Varios procesos que comparten estado:** Backoffice, Portal y Worker usan la misma base, Redis y RabbitMQ.
- **Reconstrucción completa:** poder destruir y volver a levantar el ambiente es parte de lo que se evalúa.

## Decisión

1. **Kubernetes administrado (Amazon EKS)** para Backoffice/API, Portal y Worker, con un grupo de nodos t3.medium
   configurado con mínimo 2, deseado 2 y máximo 4. No se instaló un autoescalador de nodos: ese rango no implica
   escalado automático ante carga. Backoffice y Portal se aplican con Kustomize; Terraform define el Worker y el
   pipeline actualiza su imagen y cantidad de réplicas.
2. **Persistencia e imágenes administradas:** PostgreSQL en RDS (sin acceso público, almacenamiento
   cifrado, SSL obligatorio) e imágenes en ECR con escaneo al subir y etiqueta inmutable por commit.
   El historial documental se integra con MongoDB Atlas, externo a la cuenta de AWS y a este Terraform.
3. **Redis y RabbitMQ dentro del clúster**, con contraseña y sin exposición externa: RabbitMQ con su chart de Helm y
   Redis como Deployment. El stack de observabilidad (Grafana, Prometheus, Loki y Tempo) también corre en el clúster.
4. **Entrada:** Application Load Balancer con HTTPS (certificado ACM comodín para `*.tallernet.cloud-ip.cc`) y
   redirección de HTTP a HTTPS, que reparte el tráfico a NGINX Ingress; NGINX enruta por host a cada aplicación.
5. **Terraform por fases** ([Deploy-Lab.ps1](../../../infra/terraform/scripts/Deploy-Lab.ps1)): certificado y validación
   DNS, grupos de seguridad, RDS y ECR, control plane de EKS, nodos y balanceador, y por último los servicios del
   clúster (NGINX, RabbitMQ, Redis, Worker y LGTM). Las fases existen porque los
   proveedores de Kubernetes y Helm necesitan que el clúster ya exista; el script termina con un `apply` completo que
   reconcilia el estado final. [Deploy-Applications.ps1](../../../infra/terraform/scripts/Deploy-Applications.ps1)
   crea el secreto de la aplicación, ejecuta la migración y despliega las aplicaciones con imágenes ya publicadas.
   La reconstrucción parte de VPC, subredes y roles IAM existentes; los registros DNS de Cloudns requieren
   intervención manual.
6. **Autoescalado horizontal (HPA) del Backoffice** por uso de CPU:
   - mínimo 2 réplicas, para seguir atendiendo si cae una (6.13);
   - máximo 4, como límite inicial para la capacidad del laboratorio, sujeto a los recursos disponibles para todos
     los pods; cada pod del Backoffice solicita 100m de CPU y 128Mi de memoria, con límites de 500m y 256Mi;
   - objetivo del 70 % de la CPU solicitada: 70m por pod, no el 70 % del nodo ni del límite de 500m;
   - 120 segundos de estabilización antes de bajar réplicas, para no oscilar ante picos cortos;
   - métricas de CPU tomadas de metrics-server, que el pipeline instala junto con las aplicaciones.
7. **Solo el Backoffice tiene HPA:** recibe las consultas de seguimiento por `api.`, el Backoffice web y la
   sincronización de la app. El Portal queda fijo en 2 réplicas y el Worker en 1, sin una política de autoescalado
   configurada para ellos. La prueba de seguimiento usa `api.` para no agotar el límite por IP de `tracking.`;
   ese límite no impide escalar el Portal. Los bloqueos de PostgreSQL del Worker coordinan sus tareas programadas
   si hubiera varias réplicas; no son el motivo técnico para mantener una sola.
8. **Separar recuperación y reconstrucción:**
   - En el mismo laboratorio, renovar las credenciales temporales de AWS (`aws configure` y token de sesión) y
     ejecutar [scripts/Resume-Lab.ps1](../../../scripts/Resume-Lab.ps1), que delega en el script de infraestructura.
     Comprueba el grupo de nodos y los targets del ALB, recupera el grupo si corresponde y espera la disponibilidad.
   - En otra cuenta, configurar sus credenciales, región, VPC, subredes y roles; usar un estado de Terraform
     correspondiente al nuevo ambiente, sin reutilizar como propios los recursos de la cuenta anterior.
     Aprovisionar con `Deploy-Lab.ps1`, validar el nuevo certificado, actualizar DNS y las credenciales del pipeline,
     publicar las imágenes en el nuevo ECR y ejecutar el despliegue de aplicaciones.
9. **Salud de los pods y continuidad ante la caída de una instancia (6.13):**
   - **Backoffice y Portal** tienen dos sondas HTTP distintas, con dos réplicas como mínimo:
     - `readinessProbe` sobre `/ready`: solo comprueba PostgreSQL, con un límite de 3 s. Si la base no responde, el
       pod sale del balanceo y deja de recibir tráfico, pero no se reinicia.
     - `livenessProbe` sobre `/live`: no consulta ninguna dependencia, solo que el proceso responde. Así, una caída
       de Redis, RabbitMQ o la base no provoca reinicios en cadena de todas las réplicas.
     - `/health` detalla base, Redis y RabbitMQ (estos dos como Degradado) para diagnóstico; no lo usan las sondas.
   - **Worker:** no expone HTTP, así que sus sondas son de tipo `exec` y leen la antigüedad de dos archivos que
     actualiza el propio proceso cada 15 s: `/tmp/worker-vivo` (liveness) y `/tmp/worker-listo` (readiness, solo
     mientras todos los consumidores escuchan su cola). Se definen en Terraform con 30 s de demora inicial, un
     período de 15 s y 3 fallos antes de actuar.
   - **Apagado controlado:** Kubernetes saca el pod del balanceo antes de detenerlo y los pedidos en curso terminan;
     el Worker tiene 20 s para cerrar sus consumidores (`HostOptions.ShutdownTimeout`).
   - **Estado compartido entre réplicas:** las claves de protección de datos y el canal de SignalR viven en Redis, de
     modo que cualquier réplica atiende a cualquier usuario sin afinidad de sesión.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Máquinas virtuales (EC2) con Docker Compose | Compose por sí solo no coordina réplicas entre máquinas; habría que integrar balanceo, reemplazo y despliegue con piezas adicionales, como ALB y Auto Scaling |
| Amazon ECS con Fargate | Viable, pero se eligió Kubernetes para usar HPA, NGINX Ingress y Helm. Los manifiestos de aplicaciones son reutilizables; Terraform y las integraciones AWS no son portables sin adaptación |
| Elastic Beanstalk o App Runner | Menos control sobre la red, el balanceador y la ejecución de varios procesos que comparten Redis y RabbitMQ |
| ElastiCache y Amazon MQ en lugar de Redis y RabbitMQ en el clúster | Costo por hora alto para un laboratorio y más tiempo de creación al reconstruir; queda como mejora para producción real |
| Autoescalar también los nodos (Cluster Autoscaler o Karpenter) | Agrega configuración de IAM e integración con AWS, fuera del alcance adoptado con roles preexistentes; no se instaló y el tamaño deseado queda en 2 |
| Escalar por cantidad de pedidos en lugar de CPU | Requiere exponer esa métrica al HPA mediante un adaptador; CPU permite una primera política usando metrics-server |
| Usar `/health` (con Redis y RabbitMQ) como sonda de vida | Una caída de Redis o RabbitMQ reiniciaría todas las réplicas a la vez, aunque el sistema puede seguir funcionando degradado |
| Sonda HTTP para el Worker | Obligaría a abrir un puerto HTTP solo para la salud; los archivos de latido muestran además si los consumidores siguen escuchando sus colas |

## Consecuencias

- **Evidencia del autoescalado:** [tests/carga](../../../tests/carga/README.md) define los perfiles y objetivos, y
  [tests/carga/resultados/2026-11-13](../../../tests/carga/resultados/2026-11-13) guarda el JSON de k6 y el registro
  del HPA y los pods antes, durante y después. En la corrida completa del perfil `carga` del 13/11/2026 (11:58 a
  12:06, 23.705 requests) se cumplieron los 10 umbrales, y el HPA llevó el Backoffice de 2 a 4 réplicas a las 11:59,
  alrededor de un minuto después de empezar, con la CPU al 234 % de lo solicitado; al terminar volvió solo a 2
  (12:09). Una corrida anterior, interrumpida a mano a los 4 minutos, ya había escalado de 2 a 4 a las 11:50.
  El escenario de sincronización envía comprobantes rechazados sin escribir: mide validación, no entregas aplicadas.
- **Capacidad limitada:** en la corrida del 13/11, con 4 réplicas (el máximo) la CPU siguió entre 150 % y 250 % de
  lo solicitado (150m a 250m con un request de 100m, frente al objetivo de 70m) y aun así se cumplieron los tiempos
  objetivo. El ambiente quedó cerca de su límite: llegar a 4 réplicas no garantiza mantener la latencia con más carga.
  Más carga puede requerir optimización, revisar recursos y ampliar pods o nodos; estos últimos no escalan solos.
- **Disponibilidad acotada:** Redis y RabbitMQ tienen una sola réplica y almacenamiento no persistente. Recrear sus
  pods puede perder datos, incluidos mensajes de la cola. Las [pruebas de resiliencia](pruebas-resiliencia.md)
  documentan interrupciones en Compose, no garantizan recuperación sin pérdida ante recreación de pods en EKS.
- **Destruir no conserva los datos de RDS:** la configuración omite el snapshot final y no habilita protección de
  borrado. Se requiere respaldo y restauración si se necesitan conservar datos. Atlas y los secretos del repositorio
  GitHub no se destruyen con este Terraform. El pipeline necesita que el secreto Kubernetes de la aplicación ya
  exista; carga MongoDB y, si están configurados los secretos de GitHub, Firebase y SMTP.
- **Caída de una instancia (escenario 1 de la defensa):** con dos réplicas y la sonda de disponibilidad, al borrar un
  pod del Backoffice el balanceador sigue enviando tráfico a la otra mientras Kubernetes crea el reemplazo. Los
  pedidos que estaban en curso en el pod borrado pueden fallar; los siguientes no.
- **Dependencia del laboratorio:** renovar credenciales y ejecutar `Resume-Lab.ps1` permite comprobar y recuperar
  el ambiente existente, no crear uno nuevo. Debe esperarse la disponibilidad antes de desplegar; los tiempos de
  recuperación dependen de AWS y del estado de los servicios.
- **Costo y ciclo de vida:** la pausa/reanudación es una condición de AWS Academy, distinta de destruir recursos.
  Reducir pods o nodos no elimina por sí solo los cargos de EKS, RDS y ALB. El cambio de cuenta exige un nuevo
  aprovisionamiento y actualizar las dependencias externas; no se resuelve solo cambiando las credenciales.
