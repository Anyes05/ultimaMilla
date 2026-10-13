output "rds_endpoint" {
  description = "Endpoint de la instancia PostgreSQL"
  value       = aws_db_instance.postgres.address
}

output "rds_port" {
  description = "Puerto de PostgreSQL"
  value       = aws_db_instance.postgres.port
}

output "security_group_ids" {
  description = "IDs de los grupos de seguridad creados"
  value = {
    eks_cluster = aws_security_group.eks_cluster.id
    eks_nodes   = aws_security_group.eks_nodes.id
    alb_public  = aws_security_group.alb_public.id
    rds         = aws_security_group.rds_postgres.id
  }
}

output "ecr_repository_urls" {
  description = "URLs de los repositorios ECR"
  value = {
    frontend = aws_ecr_repository.frontend.repository_url
    backend  = aws_ecr_repository.backend.repository_url
    worker   = aws_ecr_repository.worker.repository_url
  }
}

output "eks_cluster_name" {
  description = "Nombre del cluster EKS"
  value       = aws_eks_cluster.main.name
}

output "eks_cluster_endpoint" {
  description = "Endpoint publico del control plane EKS"
  value       = aws_eks_cluster.main.endpoint
}

output "application_load_balancer" {
  description = "Datos del ALB publico"
  value = {
    dns_name = aws_lb.public.dns_name
    zone_id  = aws_lb.public.zone_id
  }
}

output "application_urls" {
  description = "URLs publicas cubiertas por el certificado y alias wildcard tras completar validacion ACM y DNS"
  value = {
    app      = "https://app.${var.domain_name}"
    api      = "https://api.${var.domain_name}"
    tracking = "https://tracking.${var.domain_name}"
  }
}

output "acm_certificate_arn" {
  description = "ARN del certificado publico ACM"
  value       = aws_acm_certificate.public.arn
}

output "manual_acm_validation_records" {
  description = "CNAME de validacion ACM para crear manualmente antes de crear la infraestructura"
  value = [
    for validation in aws_acm_certificate.public.domain_validation_options : {
      zone  = var.domain_name
      name  = trimsuffix(trimsuffix(validation.resource_record_name, "."), ".${var.domain_name}")
      type  = validation.resource_record_type
      value = trimsuffix(validation.resource_record_value, ".")
      ttl   = var.dns_record_ttl
    }
  ]
}

output "manual_application_dns_records" {
  description = "CNAME wildcard hacia el ALB para crear manualmente despues de crear el ALB"
  value = [
    for name in ["*"] : {
      zone  = var.domain_name
      name  = name
      type  = "CNAME"
      value = aws_lb.public.dns_name
      ttl   = var.dns_record_ttl
    }
  ]
}

output "rabbitmq_endpoint" {
  description = "Endpoint interno RabbitMQ usado por API y worker cuando la plataforma esta instalada"
  value       = "${local.rabbitmq_release_name}.${var.application_namespace}.svc.cluster.local:5672"
}

output "redis_endpoint" {
  description = "Endpoint interno Redis para cache distribuido cuando la plataforma esta instalada"
  value       = "${local.redis_service_name}.${var.application_namespace}.svc.cluster.local:6379"
}