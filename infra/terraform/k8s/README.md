# Manifiestos de aplicaciones

Estos manifiestos se aplican cuando existan imágenes inmutables en ECR y los proyectos .NET expongan los contratos configurados aquí. Terraform sigue administrando el cluster, NGINX, RabbitMQ, el namespace y los secretos de plataforma; no aplique recursos duplicados desde esta carpeta.

Antes de aplicar, reemplace las imágenes `example.invalid/tallernet/*:replace-with-immutable-tag`, cree el Secret `tallernet-application-secrets` y ajuste puertos, ensamblados, rutas y variables a los proyectos reales. Luego:

```powershell
kubectl apply -k .\k8s\base
```

El API debe exponer `/live` y `/ready`; los valores de `replicas: 2` son el mínimo exigido para la demostración de balanceo.