# Manifiestos de aplicaciones

Terraform administra EKS, NGINX, Redis, RabbitMQ y el namespace `tallernet`. Estos manifiestos despliegan Portal y Backoffice con dos réplicas, usando Redis autenticado, probes `/live` y `/ready`, e imágenes inmutables de ECR.

## Despliegue automatizado

El script obtiene endpoints y repositorios desde Terraform, solicita las credenciales sensibles solamente en el terminal, crea o actualiza el Secret, ejecuta una migracion y despliega Portal, Backoffice e Ingress con el tag inmutable indicado:

```powershell
.\scripts\Deploy-Applications.ps1 -ImageTag <SHA-de-40-caracteres>
```

`Deploy-Lab.ps1 -Action Apply -ApplicationImageTag <SHA-de-40-caracteres>` ejecuta ese mismo paso al terminar la fase de plataforma. Sin ese parametro, Terraform no despliega aplicaciones ni solicita sus secretos.

## Secret de aplicación

Antes del primer despliegue, cree `tallernet-application-secrets` en el namespace `tallernet`. Debe contener estas claves, sin versionar sus valores:

- `ConnectionStrings__Default`: cadena PostgreSQL de RDS.
- `Jwt__Clave`: clave aleatoria de firma JWT de al menos 32 caracteres.
- `Jwt__Emisor`: emisor JWT de producción.
- `Jwt__Audiencia`: audiencia JWT de producción.
- `Avisos__ClaveFirma`: clave HMAC compartida entre Worker y receptor de prueba; se solicita durante `Deploy-Applications.ps1` y no se versiona.
- `Mongo__ConnectionString`: URI `mongodb+srv://` de Atlas para el historial documental; se solicita durante `Deploy-Applications.ps1` y no se versiona.

Claves opcionales. `Deploy-Applications.ps1` conserva las que ya existen al volver a aplicar el Secret y, en un ambiente nuevo, las pide (Enter para omitir: el worker sigue funcionando y deja el push y los correos en su log):

- `Push__Fcm__CuentaServicioJson`: JSON completo de la cuenta de servicio de Firebase (proyecto `tallernet-push`), para el push de reasignación urgente. El script pide la ruta del archivo; el archivo nunca va en el repositorio.
- `Notificaciones__SmtpHost`, `Notificaciones__SmtpUsuario`, `Notificaciones__SmtpClave` y `Notificaciones__Remitente`: servidor SMTP para el correo al destinatario.

Para no tener que cargarlas a mano en cada ambiente nuevo, guárdelas como secrets del repositorio en GitHub (Settings → Secrets and variables → Actions): `FIREBASE_SERVICE_ACCOUNT_JSON` (el contenido completo del JSON), `SMTP_HOST`, `SMTP_USUARIO`, `SMTP_CLAVE` y `SMTP_REMITENTE`. El pipeline las carga en este Secret en cada despliegue, igual que `MONGODB_CONNECTION_STRING`; si alguna no existe, deja la clave como esté.

Terraform crea por separado `tallernet-redis-credentials` y `tallernet-rabbitmq-credentials`; no duplique sus claves en este Secret. Las aplicaciones obtienen la contraseña de Redis desde el Secret de plataforma.

## Imágenes y despliegue

Reemplace los tags `bootstrap` por el SHA inmutable publicado en ECR antes de aplicar. El repositorio `tallernet/backend` corresponde a Backoffice y `tallernet/frontend` a Portal.

Ejecute primero la migración, espere que termine correctamente y luego despliegue las aplicaciones:

```powershell
kubectl -n tallernet apply -f .\k8s\base\migration.yaml
kubectl -n tallernet wait --for=condition=complete job/database-migration --timeout=5m
kubectl apply -k .\k8s\base
```

`app.tallernet.cloud-ip.cc` sirve Portal. `backoffice.tallernet.cloud-ip.cc` sirve Backoffice y `api.tallernet.cloud-ip.cc` dirige los endpoints del repartidor de Backoffice.