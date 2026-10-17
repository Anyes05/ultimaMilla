# TallerNet infrastructure

Infraestructura reproducible de TallerNet para una cuenta AWS Academy: EKS, node group, RDS PostgreSQL, ECR, ALB, ACM, NGINX Ingress, RabbitMQ y worker.

Terraform crea el managed node group pero no crea ni modifica roles IAM. Cada cuenta debe proporcionar los roles EKS de control plane y nodos que entrega AWS Academy.

## Prerequisitos

- AWS CLI autenticada con las credenciales temporales de AWS Academy.
- Terraform 1.5 o superior.
- La zona DNS `tallernet.cloud-ip.cc` existente, administrable manualmente.

El plan Free DNS de Cloudns no permite automatizar registros mediante HTTP API. Terraform no contiene proveedor, credenciales ni recursos Cloudns: solicita el certificado wildcard `*.tallernet.cloud-ip.cc`, muestra el CNAME de validación completo para cargarlo manualmente y espera a que ACM emita el certificado antes de crear el ALB.

## Estado local

El estado se mantiene localmente en `terraform.tfstate`, excluido de Git. Cada nueva cuenta AWS Academy debe comenzar con un estado local nuevo y no se debe reutilizar un state de otra cuenta.

## Ejecucion local

`.env` contiene la configuración local no sensible y está excluido de Git. Cárguelo antes de ejecutar Terraform:

```powershell
. .\scripts\Import-Environment.ps1
```

La contraseña de RDS se solicita de forma sensible durante el despliegue. `Import-Environment.ps1` la omite si aparece por error en `.env`. Elimine cualquier secreto preexistente de ese archivo y rótelo si fue compartido. No deben permanecer en `.env`, archivos `.tfvars`, Git ni respuestas de chat.

```powershell
terraform fmt -check -recursive
terraform validate
terraform plan
terraform apply
```

## Reanudar laboratorio

No detenga instancias EC2 individuales del managed node group. Al reanudar un laboratorio AWS Academy, el Auto Scaling Group debe recrear los nodos y el attachment del ALB registra los targets automaticamente. Si el node group queda degradado o no hay targets saludables, ejecute:

```powershell
.\scripts\Resume-Lab.ps1
```

El script solo reinicia el ciclo de escala cuando faltan nodos o targets saludables: reduce temporalmente el node group a cero, lo restaura a minimo y deseado `2`, espera nodos `Ready` y verifica targets `healthy` en el ALB. Use `-Force` solo para forzar esa reconciliacion sobre un laboratorio que ya parece sano.

## Reconstruccion automatizada

El script inicializa proveedores, formatea y valida Terraform. Por una restricción de Terraform, solicita la contraseña sensible de RDS antes de evaluar la configuración, pero no crea RDS hasta su fase específica. En Free DNS se detiene después de solicitar ACM para que cargue los CNAME de validación en Cloudns; con el certificado emitido, el resto se ejecuta en este orden:

1. Certificado ACM y validación DNS.
2. Grupos de seguridad.
3. RDS PostgreSQL y repositorios ECR.
4. Control plane EKS.
5. Node group EKS y ALB con HTTPS.
6. NGINX Ingress, RabbitMQ, Redis y el deployment del worker.

```powershell
.\scripts\Deploy-Lab.ps1 -Action Apply
```

El script muestra el CNAME de validación ACM wildcard antes de crear los grupos de seguridad, RDS, EKS o el ALB; escriba `CONTINUAR` solamente después de cargarlo y esperar su propagación. El ALB se crea directamente con HTTPS y con redirección HTTP a HTTPS. Después de crear el ALB, el script muestra los CNAME `*`, `app`, `api` y `tracking` hacia el ALB e indica los campos a completar en Cloudns. Cree todos antes de escribir `CONTINUAR`: los CNAME de validación ACM bajo esos hosts pueden impedir que el wildcard cubra los subdominios de la aplicación.

```powershell
.\scripts\Deploy-Lab.ps1 -Action Apply
```

`-ValidateManualDns` se conserva para completar un certificado cuyos CNAME ya haya confirmado en una ejecución anterior, sin volver a solicitar esa confirmación. Si se reemplaza un certificado explícito por el wildcard, el script conserva el anterior hasta que el wildcard esté emitido y el ALB use el nuevo; después lo elimina en la reconciliación final. Las ejecuciones posteriores siguen las mismas fases de forma idempotente y nunca desactivan la plataforma ni proponen destruir NGINX, RabbitMQ o Redis. La reconstrucción completa exige roles EKS preexistentes de la cuenta AWS Academy y que las imágenes de aplicación ya hayan sido publicadas en ECR; Terraform no puede construir una aplicación cuyo código aún no exista en este repositorio.

No ejecute `destroy` sin una confirmacion explicita del equipo: elimina EKS, RDS, ALB, ACM, DNS y ECR.

## Plataforma Kubernetes

La infraestructura se orquesta por fases porque los proveedores Kubernetes y Helm necesitan que EKS y sus nodos ya existan. `Deploy-Lab.ps1` realiza las fases automáticamente y acaba con un `apply` completo sin `-target`, que reconcilia el estado final. Solo use los comandos manuales si está recuperando un recurso puntual.

El Launch Template asocia solamente el security group propio de los nodos. Terraform crea explicitamente la regla `TCP 443` desde ese grupo hacia el security group administrado por EKS; no se debe adjuntar este ultimo directamente a las interfaces de los nodos.

La segunda fase instala y configura automaticamente:

- `ingress-nginx` como `NodePort` HTTP fijo `32080`, que recibe trafico unicamente desde el ALB. El chart se descarga desde su paquete oficial fijado por version.
- RabbitMQ interno mediante un servicio `ClusterIP`, con usuario y contrasena generados por Terraform dentro de un Secret de Kubernetes. El chart se obtiene desde OCI, con una imagen legacy fijada porque Bitnami retiró la imagen versionada del catálogo público principal.
- Redis autenticado mediante un servicio `ClusterIP`, con memoria limitada y política `allkeys-lru`. Se usa exclusivamente como caché distribuido y no persiste datos: el dominio transaccional continúa en RDS.
- Namespace `tallernet` y el `Deployment` independiente `queue-worker`.

RabbitMQ usa almacenamiento efimero porque el rol preexistente de nodos AWS Academy no posee permisos para el driver EBS CSI. La API y el worker deben declarar topologia durable de exchanges, colas y DLQ al iniciar; cuando se habilite almacenamiento persistente, se cambia `persistence.enabled` sin cambiar la aplicacion.

El worker comienza con cero replicas para no intentar descargar la imagen `bootstrap`. Cuando el pipeline publique la primera imagen inmutable en ECR, actualice en `.env`:

```text
TF_VAR_worker_image_tag=<tag-publicado-por-ci>
TF_VAR_worker_replicas=1
```

Luego ejecute `terraform plan` y `terraform apply`. El worker recibe `RabbitMq__Host`, `RabbitMq__Port`, `RabbitMq__Username` y `RabbitMq__Password` por variables de entorno y Secret de Kubernetes.

Las APIs deben usar el endpoint `redis_endpoint`, obtener la clave `redis-password` desde el Secret `${project_name}-redis-credentials`, y publicar métricas de aciertos, fallos e invalidaciones con OpenTelemetry. Terraform instala la dependencia, pero esas políticas de caché e instrumentos son responsabilidades de la aplicación.

## Limites actuales

Esta carpeta incluye Dockerfiles base, `docker-compose.yml`, Redis y manifiestos Kubernetes de referencia. Todavía no contiene los proyectos .NET funcionales, imágenes de aplicación publicadas, el stack de observabilidad completo ni workflows de CI/CD. No se deben declarar como cumplidos hasta agregarlos y verificarlos con sus respectivos proyectos.

## Trafico publico

ACM emite un certificado para `*.tallernet.cloud-ip.cc`. Terraform muestra los CNAME de validación y los alias `*`, `app`, `api` y `tracking` hacia el ALB para crearlos manualmente. Los aliases explícitos son necesarios cuando los CNAME de validación de ACM crean nodos bajo esos subdominios; el ALB termina HTTPS para `app`, `api`, `tracking` y futuros subdominios de la zona.

El ALB reenvia por HTTP al NodePort `32080` del controlador ingress-nginx. Los Ingress de frontend, API y seguimiento publico se agregaran cuando existan sus Services Kubernetes.