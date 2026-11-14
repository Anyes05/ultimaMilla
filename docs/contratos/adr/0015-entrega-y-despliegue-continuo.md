# ADR 0015: Entrega y despliegue continuo en EKS

**Estado:** aceptada  
**Responsable:** Joel Leyes  
**Última revisión:** 14/11/2026  
**Requerimientos:** letra 6.14, 6.15

## Contexto

Backoffice/API, Portal y Worker deben poder publicarse de forma repetible, con pruebas antes del despliegue,
secretos fuera del repositorio y migraciones controladas. El laboratorio usa credenciales temporales de AWS:
un pipeline correcto también puede fallar si venció la sesión o si el clúster todavía se está recuperando.

La elección de AWS, EKS y Terraform se fundamenta en el [ADR 0014](0014-infraestructura-y-autoescalado.md).
Este registro cubre cómo se entrega el código a esa infraestructura, no su aprovisionamiento.

## Decisión

1. **GitHub Actions como coordinador**, con ejecución ante pull requests y pushes a `main`, además de ejecución
   manual. El trabajo de publicación depende de que termine correctamente el trabajo de pruebas y no se ejecuta
   para pull requests.
2. **Validación antes de publicar:** restaurar y compilar en Release con .NET 10; ejecutar pruebas de dominio,
   infraestructura/aislamiento, integración con PostgreSQL efímero, arquitectura, avisos y lógica móvil.
   Las pruebas móviles del pipeline no equivalen a probar la aplicación MAUI en un dispositivo.
3. **Tres imágenes Docker en ECR**, una por aplicación, etiquetadas con el SHA del commit. Los repositorios usan
   etiquetas inmutables: si la imagen de ese SHA ya existe, se reutiliza. No se usa `latest` para identificar
   la versión que se despliega.
4. **Publicación y despliegue condicionados:** `AWS_PUBLISH_ENABLED` habilita los pasos que necesitan AWS.
   `WORKER_DEPLOY_ENABLED` determina si el Worker queda en una réplica o en cero. Un workflow sin errores con AWS
   deshabilitado no demuestra una publicación ni un despliegue.
5. **Separar infraestructura, configuración y aplicaciones:**
   - Terraform crea los recursos AWS y los servicios base del clúster, incluido el Deployment del Worker.
   - Kustomize agrupa los manifiestos de Backoffice, Portal, Services, Ingress y HPA.
   - El pipeline sustituye las imágenes por las del commit y actualiza la imagen y las réplicas del Worker.
   - Los ConfigMaps contienen configuración no secreta, como el aprovisionamiento de los tableros LGTM.
   - Los Secrets contienen conexiones y credenciales; los manifiestos versionados referencian sus claves.
6. **Inicialización y actualización de secretos:** `Deploy-Applications.ps1` crea el Secret
   `tallernet-application-secrets`. El pipeline exige que exista, carga la conexión MongoDB desde GitHub Secrets
   y actualiza Firebase/SMTP solamente si sus valores están configurados. Las credenciales temporales de AWS
   del repositorio deben renovarse cuando corresponda; `aws configure` en una computadora no las actualiza.
7. **Migración antes del rollout:** ejecutar el Job `database-migration` con la imagen del Backoffice del commit
   y esperar su finalización antes de aplicar los Deployments. Si falla, el paso se interrumpe.
   Las aplicaciones tienen deshabilitadas las migraciones al arrancar para no competir entre réplicas.
8. **Esperar la disponibilidad:** comprobar el rollout de Backoffice, Portal y, si está habilitado, Worker.
   Ante un fallo de rollout se imprimen pods, eventos y logs para diagnóstico. El pipeline instala metrics-server
   y espera su disponibilidad; el HPA controla las réplicas del Backoffice sin que cada despliegue las sobrescriba.
9. **Controles de dependencias e imágenes:** el pipeline informa dependencias NuGet vulnerables y ejecuta Trivy.
   Estos controles son informativos en la configuración actual: no bloquean la entrega por vulnerabilidades.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Compilar y desplegar manualmente desde una computadora | Dificulta repetir la entrega y asociar las imágenes con el código probado |
| Una etiqueta mutable como `latest` | No identifica inequívocamente qué versión se ejecuta ni cuál se quiere recuperar |
| Migrar al arrancar cada réplica | Introduce competencia y acopla el inicio de todos los pods a cambios de esquema |
| Ejecutar Terraform completo en cada push de aplicación | Mezcla cambios de infraestructura con entregas de código y requiere gestionar credenciales, estado y pasos DNS |
| Guardar credenciales en YAML o Dockerfiles | Expone secretos en Git o en las capas de las imágenes |

## Consecuencias

- **Trazabilidad:** el SHA permite relacionar código, imagen y despliegue; deben conservarse los resultados del
  workflow para demostrar qué pasos se ejecutaron realmente.
- **Recuperación explícita:** no hay un rollback automático definido por el workflow. Volver a una imagen anterior
  requiere verificar su compatibilidad con el esquema; una migración aplicada no se deshace por revertir el Deployment.
- **Alcance de las comprobaciones:** un Job completo y un rollout disponible no prueban todos los flujos funcionales.
  La aceptación necesita además demostraciones sobre el ambiente remoto.
- **Secretos:** Kubernetes Secret no es por sí solo un gestor externo de secretos ni cifrado por usar base64.
  Se debe restringir el acceso; no publicar su contenido, el estado de Terraform ni archivos locales de credenciales.
- **Dos gestores:** Terraform define el Worker y el pipeline actualiza campos operativos; `platform.tf` ignora cambios
  en imagen y réplicas para evitar que un `apply` posterior revierta esos campos.
- **Cambio de cuenta:** se deben renovar credenciales del pipeline, aprovisionar el nuevo ambiente, inicializar sus
  secretos y publicar las imágenes en su ECR. Reanudar el laboratorio existente es un procedimiento diferente.

## Referencias de implementación

- [Workflow CI/CD](../../../.github/workflows/ci-cd.yml).
- [Plataforma administrada por Terraform](../../../infra/terraform/platform.tf).
- [Despliegue inicial de aplicaciones](../../../infra/terraform/scripts/Deploy-Applications.ps1).
- [Base Kustomize](../../../infra/terraform/k8s/base/kustomization.yaml).
- [Job de migración](../../../infra/terraform/k8s/base/migration.yaml).
- [Deployments web](../../../infra/terraform/k8s/base/frontend.yaml).
