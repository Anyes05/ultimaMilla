locals {
  name_prefix = "${var.project_name}-${var.environment}"

  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "Terraform"
  }
}

resource "aws_security_group" "eks_cluster" {
  name        = "${local.name_prefix}-eks-cluster"
  description = "Comunicacion del control plane de EKS con los nodos"
  vpc_id      = var.vpc_id

  egress {
    protocol    = "-1"
    from_port   = 0
    to_port     = 0
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-eks-cluster" })
}

resource "aws_security_group" "eks_nodes" {
  name        = "${local.name_prefix}-eks-nodes"
  description = "Nodos del cluster EKS"
  vpc_id      = var.vpc_id

  ingress {
    description = "Comunicacion interna entre nodos"
    protocol    = "-1"
    from_port   = 0
    to_port     = 0
    self        = true
  }

  ingress {
    description     = "Kubelet desde el control plane"
    protocol        = "tcp"
    from_port       = 10250
    to_port         = 10250
    security_groups = [aws_security_group.eks_cluster.id]
  }

  ingress {
    description     = "NGINX Ingress HTTP desde el ALB"
    protocol        = "tcp"
    from_port       = var.ingress_node_port
    to_port         = var.ingress_node_port
    security_groups = [aws_security_group.alb_public.id]
  }

  egress {
    protocol    = "-1"
    from_port   = 0
    to_port     = 0
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-eks-nodes" })
}

resource "aws_security_group" "alb_public" {
  name        = "${local.name_prefix}-alb-public"
  description = "Permite acceso publico HTTP y HTTPS al ALB"
  vpc_id      = var.vpc_id

  ingress {
    description = "HTTP publico"
    protocol    = "tcp"
    from_port   = 80
    to_port     = 80
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    description = "HTTPS publico"
    protocol    = "tcp"
    from_port   = 443
    to_port     = 443
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    protocol    = "-1"
    from_port   = 0
    to_port     = 0
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-alb-public" })
}

resource "aws_security_group" "rds_postgres" {
  name        = "${local.name_prefix}-rds-postgres"
  description = "Permite acceso a PostgreSQL solo desde EKS"
  vpc_id      = var.vpc_id

  ingress {
    description     = "PostgreSQL desde los nodos EKS"
    protocol        = "tcp"
    from_port       = 5432
    to_port         = 5432
    security_groups = [aws_security_group.eks_nodes.id]
  }

  egress {
    protocol    = "-1"
    from_port   = 0
    to_port     = 0
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-rds-postgres" })
}

resource "aws_db_subnet_group" "postgres" {
  name       = "${local.name_prefix}-postgres"
  subnet_ids = var.rds_subnet_ids

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-postgres" })
}

resource "aws_db_instance" "postgres" {
  identifier             = var.database_identifier
  engine                 = "postgres"
  engine_version         = var.database_engine_version
  instance_class         = var.database_instance_class
  allocated_storage      = 20
  max_allocated_storage  = 30
  storage_type           = "gp3"
  storage_encrypted      = true
  db_name                = var.database_name
  username               = var.database_master_username
  password               = var.database_master_password
  port                   = 5432
  db_subnet_group_name   = aws_db_subnet_group.postgres.name
  vpc_security_group_ids = [aws_security_group.rds_postgres.id]

  publicly_accessible     = false
  skip_final_snapshot     = true
  deletion_protection     = false
  backup_retention_period = 1
  monitoring_interval     = 0
  apply_immediately       = true

  tags = merge(local.common_tags, { Name = var.database_identifier })
}

resource "aws_ecr_repository" "frontend" {
  name                 = "${var.project_name}/frontend"
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecr_repository" "backend" {
  name                 = "${var.project_name}/backend"
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecr_repository" "worker" {
  name                 = "${var.project_name}/worker"
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_eks_cluster" "main" {
  name     = var.eks_cluster_name
  role_arn = var.eks_cluster_role_arn
  version  = var.eks_kubernetes_version

  enabled_cluster_log_types = ["api", "audit", "authenticator"]

  vpc_config {
    subnet_ids              = var.eks_control_plane_subnet_ids
    security_group_ids      = [aws_security_group.eks_cluster.id]
    endpoint_public_access  = true
    endpoint_private_access = false
  }

  tags = merge(local.common_tags, { Name = var.eks_cluster_name })
}

resource "aws_vpc_security_group_ingress_rule" "cluster_api_from_nodes" {
  description                  = "API Kubernetes desde los nodos EKS"
  security_group_id            = aws_eks_cluster.main.vpc_config[0].cluster_security_group_id
  referenced_security_group_id = aws_security_group.eks_nodes.id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}

resource "aws_vpc_security_group_ingress_rule" "nginx_admission_from_cluster" {
  description                  = "Webhook de admision NGINX desde el control plane EKS"
  security_group_id            = aws_security_group.eks_nodes.id
  referenced_security_group_id = aws_eks_cluster.main.vpc_config[0].cluster_security_group_id
  ip_protocol                  = "tcp"
  from_port                    = 8443
  to_port                      = 8443
}

resource "aws_launch_template" "eks_nodes" {
  name_prefix            = "${local.name_prefix}-eks-nodes-"
  update_default_version = true

  block_device_mappings {
    device_name = "/dev/xvda"

    ebs {
      delete_on_termination = true
      encrypted             = true
      volume_size           = 30
      volume_type           = "gp3"
    }
  }

  network_interfaces {
    associate_public_ip_address = true
    security_groups             = [aws_security_group.eks_nodes.id]
  }

  tag_specifications {
    resource_type = "instance"
    tags          = merge(local.common_tags, { Name = var.eks_node_group_name })
  }

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-eks-nodes" })
}

resource "aws_eks_node_group" "main" {
  cluster_name    = aws_eks_cluster.main.name
  node_group_name = var.eks_node_group_name
  node_role_arn   = var.eks_node_role_arn
  subnet_ids      = var.eks_node_subnet_ids

  instance_types = ["t3.medium"]
  capacity_type  = "ON_DEMAND"

  scaling_config {
    desired_size = 2
    min_size     = 2
    max_size     = 4
  }

  update_config {
    max_unavailable = 1
  }

  launch_template {
    id      = aws_launch_template.eks_nodes.id
    version = aws_launch_template.eks_nodes.latest_version
  }

  tags = merge(local.common_tags, { Name = var.eks_node_group_name })
}

resource "aws_lb_target_group" "nginx_ingress" {
  name        = "${local.name_prefix}-nginx"
  port        = var.ingress_node_port
  protocol    = "HTTP"
  target_type = "instance"
  vpc_id      = var.vpc_id

  health_check {
    enabled             = true
    healthy_threshold   = 2
    interval            = 30
    matcher             = "200-399"
    path                = "/healthz"
    protocol            = "HTTP"
    timeout             = 5
    unhealthy_threshold = 2
  }

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-nginx" })
}

resource "aws_lb" "public" {
  name               = "${local.name_prefix}-public"
  internal           = false
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb_public.id]
  subnets            = var.alb_subnet_ids

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-public" })
}

resource "aws_autoscaling_attachment" "nginx_ingress" {
  autoscaling_group_name = aws_eks_node_group.main.resources[0].autoscaling_groups[0].name
  lb_target_group_arn    = aws_lb_target_group.nginx_ingress.arn
}

resource "aws_acm_certificate" "legacy" {
  count = var.retain_legacy_certificate ? 1 : 0

  domain_name               = "app.${var.domain_name}"
  subject_alternative_names = ["api.${var.domain_name}", "tracking.${var.domain_name}"]
  validation_method         = "DNS"

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-public-legacy" })
}

resource "aws_acm_certificate" "public" {
  domain_name       = "*.${var.domain_name}"
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }

  tags = merge(local.common_tags, { Name = "${local.name_prefix}-public" })
}

resource "aws_acm_certificate_validation" "public" {
  certificate_arn         = aws_acm_certificate.public.arn
  validation_record_fqdns = [for validation in aws_acm_certificate.public.domain_validation_options : validation.resource_record_name]
}

moved {
  from = aws_lb_listener.http_forward[0]
  to   = aws_lb_listener.http_redirect
}

resource "aws_lb_listener" "http_redirect" {
  load_balancer_arn = aws_lb.public.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "redirect"

    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.public.arn
  port              = 443
  protocol          = "HTTPS"
  certificate_arn   = aws_acm_certificate_validation.public.certificate_arn
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.nginx_ingress.arn
  }
}

