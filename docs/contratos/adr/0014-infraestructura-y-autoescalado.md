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

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Máquinas virtuales (EC2) con Docker Compose | Compose por sí solo no coordina réplicas entre máquinas; habría que integrar balanceo, reemplazo y despliegue con piezas adicionales, como ALB y Auto Scaling |
| Amazon ECS con Fargate | Viable, pero se eligió Kubernetes para usar HPA, NGINX Ingress y Helm. Los manifiestos de aplicaciones son reutilizables; Terraform y las integraciones AWS no son portables sin adaptación |
| Elastic Beanstalk o App Runner | Menos control sobre la red, el balanceador y la ejecución de varios procesos que comparten Redis y RabbitMQ |
| ElastiCache y Amazon MQ en lugar de Redis y RabbitMQ en el clúster | Costo por hora alto para un laboratorio y más tiempo de creación al reconstruir; queda como mejora para producción real |
| Autoescalar también los nodos (Cluster Autoscaler o Karpenter) | Agrega configuración de IAM e integración con AWS, fuera del alcance adoptado con roles preexistentes; no se instaló y el tamaño deseado queda en 2 |
| Escalar por cantidad de pedidos en lugar de CPU | Requiere exponer esa métrica al HPA mediante un adaptador; CPU permite una primera política usando metrics-server |

## Consecuencias

- **Evidencia del autoescalado:** [tests/carga](../../../tests/carga/README.md) define los perfiles y objetivos.
  Para acreditar el opcional se debe adjuntar la salida o JSON de la corrida, su fecha y perfil, y registros del
  HPA/pods antes, durante y después. Los archivos revisados no incluyen los resultados de la corrida citada del
  13/11/2026, por lo que este ADR no da por verificados sus tiempos, escalado ni cumplimiento de los diez umbrales.
  El escenario de sincronización envía comprobantes rechazados sin escribir: mide validación, no entregas aplicadas.
- **Capacidad limitada:** llegar a 4 réplicas no garantiza mantener la latencia objetivo. Si Kubernetes muestra
  `150%/70%`, el 150 % corresponde a la CPU solicitada (150m con un request de 100m), frente al objetivo del 70 %.
  Más carga puede requerir optimización, revisar recursos y ampliar pods o nodos; estos últimos no escalan solos.
- **Disponibilidad acotada:** Redis y RabbitMQ tienen una sola réplica y almacenamiento no persistente. Recrear sus
  pods puede perder datos, incluidos mensajes de la cola. Las [pruebas de resiliencia](pruebas-resiliencia.md)
  documentan interrupciones en Compose, no garantizan recuperación sin pérdida ante recreación de pods en EKS.
- **Destruir no conserva los datos de RDS:** la configuración omite el snapshot final y no habilita protección de
  borrado. Se requiere respaldo y restauración si se necesitan conservar datos. Atlas y los secretos del repositorio
  GitHub no se destruyen con este Terraform. El pipeline necesita que el secreto Kubernetes de la aplicación ya
  exista; carga MongoDB y, si están configurados los secretos de GitHub, Firebase y SMTP.
- **Dependencia del laboratorio:** renovar credenciales y ejecutar `Resume-Lab.ps1` permite comprobar y recuperar
  el ambiente existente, no crear uno nuevo. Debe esperarse la disponibilidad antes de desplegar; los tiempos de
  recuperación dependen de AWS y del estado de los servicios.
- **Costo y ciclo de vida:** la pausa/reanudación es una condición de AWS Academy, distinta de destruir recursos.
  Reducir pods o nodos no elimina por sí solo los cargos de EKS, RDS y ALB. El cambio de cuenta exige un nuevo
  aprovisionamiento y actualizar las dependencias externas; no se resuelve solo cambiando las credenciales.
