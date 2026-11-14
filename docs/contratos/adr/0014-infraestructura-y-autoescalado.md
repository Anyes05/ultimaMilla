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
  roles nuevos) y laboratorios que se detienen y reanudan, con lo que los nodos cambian.
- **Varios procesos que comparten estado:** Backoffice, Portal y Worker usan la misma base, Redis y RabbitMQ.
- **Reconstrucción completa:** poder destruir y volver a levantar el ambiente es parte de lo que se evalúa.

## Decisión

1. **Kubernetes administrado (Amazon EKS)** para las tres aplicaciones, con un grupo de nodos t3.medium de 2 a 4
   nodos. Las aplicaciones se describen con manifiestos de Kubernetes aplicados con Kustomize.
2. **Servicios administrados para lo que guarda datos:** PostgreSQL en RDS (sin acceso público, almacenamiento
   cifrado, SSL obligatorio) e imágenes en ECR con escaneo al subir y etiqueta inmutable por commit.
3. **Redis y RabbitMQ dentro del clúster**, con contraseña y sin exposición externa: RabbitMQ con su chart de Helm y
   Redis como Deployment. El stack de observabilidad (Grafana, Prometheus, Loki y Tempo) también corre en el clúster.
4. **Entrada:** Application Load Balancer con HTTPS (certificado ACM comodín para `*.tallernet.cloud-ip.cc`) y
   redirección de HTTP a HTTPS, que reparte el tráfico a NGINX Ingress; NGINX enruta por host a cada aplicación.
5. **Terraform por fases** (`scripts/Deploy-Lab.ps1`): certificado y validación DNS, grupos de seguridad, RDS y ECR,
   control plane de EKS, nodos y balanceador, y por último NGINX, RabbitMQ y Redis. Las fases existen porque los
   proveedores de Kubernetes y Helm necesitan que el clúster ya exista; el script termina con un `apply` completo que
   reconcilia el estado final. `Deploy-Applications.ps1` crea el secreto de la aplicación y despliega las
   aplicaciones, y `Resume-Lab.ps1` recupera el clúster cuando el laboratorio se reanuda.
6. **Autoescalado horizontal (HPA) del Backoffice** por uso de CPU:
   - mínimo 2 réplicas, para seguir atendiendo si cae una (6.13);
   - máximo 4, lo que entra en los 2 nodos con los recursos pedidos por cada pod (100m de CPU, límite 500m);
   - objetivo del 70 % de la CPU pedida;
   - 120 segundos de estabilización antes de bajar réplicas, para no oscilar ante picos cortos;
   - métricas de CPU tomadas de metrics-server, que el pipeline instala junto con las aplicaciones.
7. **Solo el Backoffice escala:** recibe el seguimiento público por `api.`, el Backoffice web y la sincronización de
   la app. El Portal queda fijo en 2 réplicas porque su seguimiento tiene límite de consultas por IP, y el Worker en
   1, porque sus tareas programadas usan bloqueos para no ejecutarse dos veces.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Máquinas virtuales (EC2) con Docker Compose | Sin balanceo ni reemplazo automático de instancias; el escalado habría que construirlo a mano |
| Amazon ECS con Fargate | Viable y más simple de operar, pero Kubernetes ofrece el HPA, NGINX Ingress y el chart de Helm de RabbitMQ sin piezas propias, y los mismos manifiestos sirven en cualquier nube |
| Elastic Beanstalk o App Runner | Menos control sobre la red, el balanceador y la ejecución de varios procesos que comparten Redis y RabbitMQ |
| ElastiCache y Amazon MQ en lugar de Redis y RabbitMQ en el clúster | Costo por hora alto para un laboratorio y más tiempo de creación al reconstruir; queda como mejora para producción real |
| Autoescalar también los nodos (Cluster Autoscaler o Karpenter) | Requiere permisos de IAM que la cuenta de AWS Academy no permite crear; el grupo de nodos queda en 2 |
| Escalar por cantidad de pedidos en lugar de CPU | Necesita un adaptador de métricas personalizadas; la CPU alcanza para mostrar el comportamiento y viene incluida |

## Consecuencias

- **Evidencia del autoescalado:** en la prueba de carga del 13/11/2026 (`tests/carga`) el Backoffice pasó de 2 a 4
  réplicas alrededor de un minuto después de empezar la carga y volvió solo a 2 al terminar, cumpliendo los 10
  objetivos de servicio.
- **El techo es bajo:** con 4 réplicas el CPU siguió entre 150 % y 250 % del objetivo. Más carga exige subir el máximo
  del HPA y agregar nodos, y hoy los nodos no escalan solos.
- **Redis y RabbitMQ son puntos únicos de falla dentro del clúster.** Si caen, el sistema sigue funcionando degradado
  (ver `pruebas-resiliencia.md`), pero no hay réplica.
- **Destruir el ambiente borra los datos de PostgreSQL.** MongoDB Atlas y los secretos guardados en GitHub sobreviven,
  y el pipeline vuelve a cargar Firebase, SMTP y MongoDB en el primer despliegue.
- **Dependencia del laboratorio:** al reanudarse, los nodos cambian y las aplicaciones tardan unos minutos en estar
  listas; un despliegue lanzado en ese momento puede fallar por tiempo. `Resume-Lab.ps1` resuelve los casos en que el
  grupo de nodos queda degradado.
- **Costo:** el ambiente cuesta mientras está encendido (EKS, nodos, RDS y balanceador), por eso se destruye o se
  detiene cuando no se usa y se reconstruye con los scripts.
