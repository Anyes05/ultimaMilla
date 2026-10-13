variable "aws_region" {
  description = "Region AWS del laboratorio"
  type        = string
  default     = "us-east-1"
}

variable "vpc_id" {
  description = "VPC existente del laboratorio"
  type        = string
}

variable "project_name" {
  description = "Nombre corto del proyecto, usado como prefijo de recursos"
  type        = string
  default     = "tallernet"
}

variable "environment" {
  description = "Ambiente de despliegue"
  type        = string
  default     = "lab"
}

variable "eks_control_plane_subnet_ids" {
  description = "Subredes compatibles para el control plane de EKS"
  type        = list(string)
}

variable "eks_node_subnet_ids" {
  description = "Subredes donde se ejecutaran los nodos EKS"
  type        = list(string)
}

variable "alb_subnet_ids" {
  description = "Subredes publicas para el Application Load Balancer"
  type        = list(string)
}

variable "rds_subnet_ids" {
  description = "Subredes para el DB subnet group de RDS"
  type        = list(string)
}

variable "eks_cluster_name" {
  description = "Nombre del cluster EKS"
  type        = string
  default     = "tallernet-cluster"
}

variable "eks_node_group_name" {
  description = "Nombre del managed node group"
  type        = string
  default     = "tallernet-nodes"
}

variable "eks_kubernetes_version" {
  description = "Version de Kubernetes habilitada por EKS"
  type        = string
  default     = "1.32"
}

variable "eks_node_role_arn" {
  description = "ARN preexistente del rol IAM de nodos EKS. Terraform no crea ni modifica roles."
  type        = string
}

variable "ingress_node_port" {
  description = "NodePort HTTP reservado para ingress-nginx"
  type        = number
  default     = 32080
}

variable "database_name" {
  description = "Nombre de la base PostgreSQL dentro de RDS"
  type        = string
  default     = "tallernet"
}

variable "database_identifier" {
  description = "Identificador de la instancia RDS"
  type        = string
  default     = "tallernet-postgres"
}

variable "database_master_username" {
  description = "Usuario administrador de PostgreSQL"
  type        = string
  default     = "tallernet_admin"
}

variable "database_master_password" {
  description = "Contrasena del usuario administrador de PostgreSQL"
  type        = string
  sensitive   = true
}

variable "database_instance_class" {
  description = "Clase de instancia RDS"
  type        = string
  default     = "db.t3.micro"
}

variable "database_engine_version" {
  description = "Version de PostgreSQL disponible en la region"
  type        = string
  default     = "16.9"
}

variable "eks_cluster_role_arn" {
  description = "ARN del rol IAM del plano de control EKS"
  type        = string
}

variable "domain_name" {
  description = "Zona DNS existente en Cloudns, sin protocolo ni subdominio"
  type        = string
  default     = "tallernet.cloud-ip.cc"
}

variable "dns_record_ttl" {
  description = "TTL en segundos de los registros DNS administrados o mostrados por Terraform"
  type        = number
  default     = 3600

  validation {
    condition     = var.dns_record_ttl >= 60
    error_message = "dns_record_ttl debe ser de al menos 60 segundos."
  }
}

variable "retain_legacy_certificate" {
  description = "Retiene temporalmente el certificado anterior durante la migracion al wildcard"
  type        = bool
  default     = false
}

variable "install_platform" {
  description = "Instala los componentes Kubernetes tras tener EKS y los nodos activos"
  type        = bool
  default     = false
}

variable "application_namespace" {
  description = "Namespace Kubernetes de las aplicaciones TallerNet"
  type        = string
  default     = "tallernet"
}

variable "ingress_nginx_chart_version" {
  description = "Version fijada del chart ingress-nginx"
  type        = string
  default     = "4.15.1"
}

variable "rabbitmq_chart_version" {
  description = "Version fijada del chart Bitnami RabbitMQ"
  type        = string
  default     = "16.0.14"
}

variable "redis_image" {
  description = "Imagen fija de Redis para el cache distribuido interno"
  type        = string
  default     = "redis:7.4.2-alpine"
}

variable "worker_image_tag" {
  description = "Tag inmutable de la imagen worker publicada en ECR"
  type        = string
  default     = "bootstrap"
}

variable "worker_replicas" {
  description = "Cantidad de workers. Mantener en cero hasta publicar la primera imagen valida."
  type        = number
  default     = 0

  validation {
    condition     = var.worker_replicas >= 0 && var.worker_replicas <= 4
    error_message = "worker_replicas debe estar entre 0 y 4."
  }
}