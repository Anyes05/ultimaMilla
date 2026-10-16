locals {
  rabbitmq_release_name = "${var.project_name}-rabbitmq"
  redis_service_name    = "${var.project_name}-redis"
  worker_image          = "${aws_ecr_repository.worker.repository_url}:${var.worker_image_tag}"
}

resource "kubernetes_namespace_v1" "application" {
  count = var.install_platform ? 1 : 0

  metadata {
    name = var.application_namespace

    labels = {
      "app.kubernetes.io/part-of"    = var.project_name
      "app.kubernetes.io/managed-by" = "terraform"
    }
  }
}

resource "helm_release" "nginx_ingress" {
  count = var.install_platform ? 1 : 0

  name             = "ingress-nginx"
  repository       = "https://kubernetes.github.io/ingress-nginx"
  chart            = "ingress-nginx"
  version          = var.ingress_nginx_chart_version
  namespace        = "ingress-nginx"
  create_namespace = true
  atomic           = true
  wait             = true
  timeout          = 600

  values = [yamlencode({
    controller = {
      replicaCount = 2
      service = {
        type = "NodePort"
        nodePorts = {
          http = var.ingress_node_port
        }
      }
      ingressClassResource = {
        name    = "nginx"
        enabled = true
        default = true
      }
      resources = {
        requests = {
          cpu    = "100m"
          memory = "128Mi"
        }
        limits = {
          cpu    = "500m"
          memory = "256Mi"
        }
      }
    }
  })]

  depends_on = [aws_eks_node_group.main]
}

resource "random_password" "rabbitmq" {
  count = var.install_platform ? 1 : 0

  length           = 32
  special          = true
  override_special = "_-%@"
}

resource "kubernetes_secret_v1" "rabbitmq_credentials" {
  count = var.install_platform ? 1 : 0

  metadata {
    name      = "${local.rabbitmq_release_name}-credentials"
    namespace = kubernetes_namespace_v1.application[0].metadata[0].name
  }

  data = {
    "rabbitmq-password" = random_password.rabbitmq[0].result
  }

  type = "Opaque"
}

resource "helm_release" "rabbitmq" {
  count = var.install_platform ? 1 : 0

  name       = local.rabbitmq_release_name
  repository = "oci://registry-1.docker.io/bitnamicharts"
  chart      = "rabbitmq"
  version    = var.rabbitmq_chart_version
  namespace  = kubernetes_namespace_v1.application[0].metadata[0].name
  atomic     = true
  wait       = true
  timeout    = 600

  values = [yamlencode({
    fullnameOverride = local.rabbitmq_release_name
    auth = {
      username               = "tallernet_worker"
      existingPasswordSecret = kubernetes_secret_v1.rabbitmq_credentials[0].metadata[0].name
    }
    global = {
      security = {
        allowInsecureImages = true
      }
    }
    image = {
      registry   = "docker.io"
      repository = "bitnamilegacy/rabbitmq"
      tag        = "4.1.3-debian-12-r1"
    }
    persistence = {
      enabled = false
    }
    service = {
      type = "ClusterIP"
    }
    metrics = {
      enabled = true
    }
    resources = {
      requests = {
        cpu    = "100m"
        memory = "256Mi"
      }
      limits = {
        cpu    = "500m"
        memory = "512Mi"
      }
    }
  })]

  depends_on = [kubernetes_secret_v1.rabbitmq_credentials]
}

resource "random_password" "redis" {
  count = var.install_platform ? 1 : 0

  length  = 48
  special = false
}

resource "kubernetes_secret_v1" "redis_credentials" {
  count = var.install_platform ? 1 : 0

  metadata {
    name      = "${local.redis_service_name}-credentials"
    namespace = kubernetes_namespace_v1.application[0].metadata[0].name
  }

  data = {
    "redis-password" = random_password.redis[0].result
  }

  type = "Opaque"
}

resource "kubernetes_deployment_v1" "redis" {
  count = var.install_platform ? 1 : 0

  metadata {
    name      = local.redis_service_name
    namespace = kubernetes_namespace_v1.application[0].metadata[0].name
    labels = {
      "app.kubernetes.io/name"       = local.redis_service_name
      "app.kubernetes.io/component"  = "cache"
      "app.kubernetes.io/part-of"    = var.project_name
      "app.kubernetes.io/managed-by" = "terraform"
    }
  }

  spec {
    replicas = 1

    selector {
      match_labels = {
        "app.kubernetes.io/name" = local.redis_service_name
      }
    }

    template {
      metadata {
        labels = {
          "app.kubernetes.io/name" = local.redis_service_name
        }
      }

      spec {
        automount_service_account_token = false

        container {
          name    = "redis"
          image   = var.redis_image
          command = ["sh", "-c"]
          args    = ["exec redis-server --appendonly no --save '' --maxmemory 192mb --maxmemory-policy allkeys-lru --requirepass \"$REDIS_PASSWORD\""]

          port {
            name           = "redis"
            container_port = 6379
          }

          env {
            name = "REDIS_PASSWORD"

            value_from {
              secret_key_ref {
                name = kubernetes_secret_v1.redis_credentials[0].metadata[0].name
                key  = "redis-password"
              }
            }
          }

          readiness_probe {
            exec {
              command = ["sh", "-c", "redis-cli --no-auth-warning -a \"$REDIS_PASSWORD\" ping"]
            }

            initial_delay_seconds = 5
            period_seconds        = 10
            timeout_seconds       = 3
            failure_threshold     = 3
          }

          liveness_probe {
            exec {
              command = ["sh", "-c", "redis-cli --no-auth-warning -a \"$REDIS_PASSWORD\" ping"]
            }

            initial_delay_seconds = 15
            period_seconds        = 20
            timeout_seconds       = 3
            failure_threshold     = 3
          }

          resources {
            requests = {
              cpu                 = "50m"
              memory              = "128Mi"
              "ephemeral-storage" = "128Mi"
            }

            limits = {
              cpu                 = "250m"
              memory              = "256Mi"
              "ephemeral-storage" = "512Mi"
            }
          }
        }
      }
    }
  }

  depends_on = [kubernetes_secret_v1.redis_credentials]
}

resource "kubernetes_service_v1" "redis" {
  count = var.install_platform ? 1 : 0

  metadata {
    name      = local.redis_service_name
    namespace = kubernetes_namespace_v1.application[0].metadata[0].name
    labels = {
      "app.kubernetes.io/name"       = local.redis_service_name
      "app.kubernetes.io/component"  = "cache"
      "app.kubernetes.io/part-of"    = var.project_name
      "app.kubernetes.io/managed-by" = "terraform"
    }
  }

  spec {
    selector = {
      "app.kubernetes.io/name" = local.redis_service_name
    }

    port {
      name        = "redis"
      port        = 6379
      target_port = "redis"
      protocol    = "TCP"
    }

    type = "ClusterIP"
  }

  depends_on = [kubernetes_deployment_v1.redis]
}

resource "kubernetes_deployment_v1" "queue_worker" {
  count = var.install_platform ? 1 : 0

  metadata {
    name      = "queue-worker"
    namespace = kubernetes_namespace_v1.application[0].metadata[0].name
    labels = {
      "app.kubernetes.io/name"       = "queue-worker"
      "app.kubernetes.io/component"  = "worker"
      "app.kubernetes.io/part-of"    = var.project_name
      "app.kubernetes.io/managed-by" = "terraform"
    }
  }

  spec {
    replicas = var.worker_replicas

    selector {
      match_labels = {
        "app.kubernetes.io/name" = "queue-worker"
      }
    }

    template {
      metadata {
        labels = {
          "app.kubernetes.io/name" = "queue-worker"
        }
      }

      spec {
        container {
          name              = "queue-worker"
          image             = local.worker_image
          image_pull_policy = "Always"

          env {
            name  = "RabbitMq__Host"
            value = "${local.rabbitmq_release_name}.${var.application_namespace}.svc.cluster.local"
          }

          env {
            name  = "RabbitMq__Port"
            value = "5672"
          }

          env {
            name  = "RabbitMq__Username"
            value = "tallernet_worker"
          }

          env {
            name = "RabbitMq__Password"

            value_from {
              secret_key_ref {
                name = kubernetes_secret_v1.rabbitmq_credentials[0].metadata[0].name
                key  = "rabbitmq-password"
              }
            }
          }

          resources {
            requests = {
              cpu    = "100m"
              memory = "128Mi"
            }

            limits = {
              cpu    = "500m"
              memory = "256Mi"
            }
          }
        }
      }
    }
  }

  depends_on = [helm_release.rabbitmq]
}