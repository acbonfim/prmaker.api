locals {
  # ---------------------------------------------------------------------------
  # Definição dos dois serviços Cloud Run deste repositório.
  # - env:        variáveis de ambiente NÃO sensíveis (texto plano).
  # - secret_env: NOME_DA_VAR => secret_id (o valor vem do Secret Manager).
  #               Use "__" para representar aninhamento de config .NET
  #               (ex.: ConnectionStrings__DefaultConnection).
  # ---------------------------------------------------------------------------
  services = {
    pullrequest = {
      service_name = "cime-pullrequest"
      # Tempo real (feature 0013): o hub SignalR roda no relay do MonsterASP e a API só publica
      # por HTTP. Sem WebSocket aberto aqui, a instância volta a dormir (antes: cobrada 24 h) e
      # não precisa mais de instância única nem de timeout longo.
      # Rollback: RealTime__Mode = "InProcess", max_instances = 1, timeout 3600, affinity true.
      max_instances    = var.max_instances
      timeout_seconds  = 300
      session_affinity = false
      env = {
        ASPNETCORE_ENVIRONMENT = "Production"
        RealTime__Mode         = "Relay"
        RealTime__RelayUrl     = var.realtime_relay_url
      }
      secret_env = {
        "ConnectionStrings__DefaultConnection"        = "mysql-default-connection"
        "ConnectionStrings__AuthenticationConnection" = "sqlserver-auth-connection"
        "Auth__Secret"                                = "jwt-secret"
        "AzureDevOps__PersonalAccessToken"            = "azuredevops-pat"
        "GitHub__Token"                               = "github-token"
        # Relay (feature 0013): chave do POST /publish e chave HMAC dos tokens do navegador.
        # Os MESMOS valores vão para os secrets do GitHub usados no deploy do relay.
        # Sem uso desde a 0016, mas ficam até a B1: remover a env também destrói o grant
        # "pullrequest:<secret>" — e o binding no GCP é compartilhado (mesma conta de serviço),
        # então tiraria o acesso da auth ao postgres-auth-connection e das revisões de rollback
        # ao realtime-apikey. Na B1: grants por secret (moved/removed) antes de tirar as envs.
        "ConnectionStrings__AuthDatabase" = "postgres-auth-connection"
        "RealTime__ApiKey"                = "realtime-apikey"
        "RealTime__RelayKey"        = "realtime-relay-key"
        "RealTime__TokenSigningKey" = "realtime-token-signing-key"
        # Módulos prform/vacations/timeline no PostgreSQL (feature 0015). A DefaultConnection (MySQL)
        # fica até a limpeza (0016): é o que a revisão anterior lê num rollback.
        "ConnectionStrings__PrformDatabase" = "postgres-prform-connection"
        # Integrações pessoais (feature 0002): chave AES-256 dos tokens dos usuários.
        # NUNCA trocar depois de em uso: os tokens salvos deixam de ser legíveis.
        "UserIntegrations__EncryptionKey" = "user-integrations-encryption-key"
      }
    }
    auth = {
      service_name     = "cime-auth"
      max_instances    = var.max_instances
      timeout_seconds  = 300
      session_affinity = false
      env = {
        ASPNETCORE_ENVIRONMENT = "Production"
      }
      secret_env = {
        # PostgreSQL, schema auth (feature 0014). A DefaultConnection (SQL Server) fica até a
        # limpeza (Q3): é o que a revisão anterior lê num rollback.
        "ConnectionStrings__AuthDatabase"      = "postgres-auth-connection"
        "ConnectionStrings__DefaultConnection" = "sqlserver-auth-connection"
        "Email__Password"                      = "email-password"
        # Chaves dos tokens (0016): saíram do código. O Auth__Secret é o mesmo jwt-secret da API
        # principal (ela valida as api-keys assinadas pela auth); o refresh ganhou secret próprio.
        "Auth__Secret"        = "jwt-secret"
        "Auth__SecretRefresh" = "jwt-refresh-secret"
      }
    }
  }

  # União de todos os secret_ids referenciados por qualquer serviço.
  referenced_secret_ids = toset(flatten([
    for svc in values(local.services) : values(svc.secret_env)
  ]))

  # Pares (serviço, secret_id) para conceder o acesso do runtime SA por secret.
  # ATENÇÃO: todos os serviços usam a MESMA conta de serviço, então "auth:X" e "pullrequest:X" são o
  # mesmo binding no GCP. Destruir um deles (ex.: um serviço deixa de usar X) remove o acesso do
  # outro também. Não remova uma env que referencia um secret ainda usado por outro serviço sem
  # antes trocar esta chave por secret_id (com blocos moved/removed) — pendência da 0016/B1.
  service_secret_grants = flatten([
    for svc_key, svc in local.services : [
      for secret_id in distinct(values(svc.secret_env)) : {
        key       = "${svc_key}:${secret_id}"
        secret_id = secret_id
      }
    ]
  ])

  # ---------------------------------------------------------------------------
  # Segredos por serviço num ÚNICO secret JSON (0016), montado pelo Cloud Run como arquivo
  # /secrets/appsettings.secrets.json e lido pelo .NET como mais um appsettings (precedência sobre
  # env). 2 versões ativas no total: dentro da cota grátis do Secret Manager (6). Os valores vêm do
  # mesmo var.secret_values. Cada secret pertence a UM serviço (sem binding compartilhado).
  # Os secrets individuais (secret_env acima) saem na 0016/B1, junto com os legados.
  # ---------------------------------------------------------------------------
  service_secret_files = {
    pullrequest = {
      secret_id = "cime-pullrequest-secrets"
      content = {
        ConnectionStrings = { PrformDatabase = var.secret_values["postgres-prform-connection"] }
        Auth              = { Secret = var.secret_values["jwt-secret"] }
        AzureDevOps       = { PersonalAccessToken = var.secret_values["azuredevops-pat"] }
        GitHub            = { Token = var.secret_values["github-token"] }
        RealTime = {
          RelayKey        = var.secret_values["realtime-relay-key"]
          TokenSigningKey = var.secret_values["realtime-token-signing-key"]
        }
        UserIntegrations = { EncryptionKey = var.secret_values["user-integrations-encryption-key"] }
      }
    }
    auth = {
      secret_id = "cime-auth-secrets"
      content = {
        ConnectionStrings = { AuthDatabase = var.secret_values["postgres-auth-connection"] }
        Email             = { Password = var.secret_values["email-password"] }
        Auth = {
          Secret        = var.secret_values["jwt-secret"]
          SecretRefresh = var.secret_values["jwt-refresh-secret"]
        }
      }
    }
  }
}

# -----------------------------------------------------------------------------
# APIs necessárias
# -----------------------------------------------------------------------------
resource "google_project_service" "apis" {
  for_each = toset([
    "run.googleapis.com",
    "artifactregistry.googleapis.com",
    "secretmanager.googleapis.com",
    "iamcredentials.googleapis.com",
    "sts.googleapis.com",
    "cloudresourcemanager.googleapis.com",
  ])
  service            = each.value
  disable_on_destroy = false
}

# -----------------------------------------------------------------------------
# Artifact Registry (Docker)
# -----------------------------------------------------------------------------
resource "google_artifact_registry_repository" "repo" {
  location      = var.region
  repository_id = var.ar_repo_name
  description   = "Imagens Docker das APIs CIME"
  format        = "DOCKER"

  depends_on = [google_project_service.apis]
}

# -----------------------------------------------------------------------------
# Secret Manager: um secret + versão por entrada de var.secret_values
# -----------------------------------------------------------------------------
resource "google_secret_manager_secret" "secrets" {
  # As chaves (IDs dos secrets) não são sensíveis; só os valores são. nonsensitive() nas
  # chaves permite o for_each sem expor os segredos.
  for_each  = nonsensitive(toset(keys(var.secret_values)))
  secret_id = each.value

  replication {
    auto {}
  }

  depends_on = [google_project_service.apis]
}

resource "google_secret_manager_secret_version" "versions" {
  for_each    = nonsensitive(toset(keys(var.secret_values)))
  secret      = google_secret_manager_secret.secrets[each.value].id
  secret_data = var.secret_values[each.value]
}

# -----------------------------------------------------------------------------
# Secret JSON por serviço (0016): um secret + versão + acesso só do runtime SA
# -----------------------------------------------------------------------------
resource "google_secret_manager_secret" "service_files" {
  for_each  = toset(keys(local.service_secret_files))
  secret_id = local.service_secret_files[each.value].secret_id

  replication {
    auto {}
  }

  depends_on = [google_project_service.apis]
}

resource "google_secret_manager_secret_version" "service_files" {
  for_each    = toset(keys(local.service_secret_files))
  secret      = google_secret_manager_secret.service_files[each.value].id
  secret_data = jsonencode(local.service_secret_files[each.value].content)
}

resource "google_secret_manager_secret_iam_member" "service_files" {
  for_each  = toset(keys(local.service_secret_files))
  secret_id = google_secret_manager_secret.service_files[each.value].secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.runtime.email}"
}

# -----------------------------------------------------------------------------
# Service Account de runtime dos serviços Cloud Run (menor privilégio)
# -----------------------------------------------------------------------------
resource "google_service_account" "runtime" {
  account_id   = "cime-run-runtime"
  display_name = "Runtime SA dos serviços Cloud Run CIME"
}

# Runtime SA pode ler apenas os secrets efetivamente usados pelos serviços.
resource "google_secret_manager_secret_iam_member" "runtime_access" {
  for_each  = { for g in local.service_secret_grants : g.key => g }
  secret_id = google_secret_manager_secret.secrets[each.value.secret_id].secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.runtime.email}"
}

# -----------------------------------------------------------------------------
# Serviços Cloud Run
# -----------------------------------------------------------------------------
resource "google_cloud_run_v2_service" "services" {
  for_each = local.services

  name                = each.value.service_name
  location            = var.region
  deletion_protection = false
  ingress             = "INGRESS_TRAFFIC_ALL"

  template {
    service_account = google_service_account.runtime.email
    timeout         = "${each.value.timeout_seconds}s"
    session_affinity = each.value.session_affinity

    scaling {
      min_instance_count = var.min_instances
      max_instance_count = each.value.max_instances
    }

    # Secret JSON do serviço como arquivo (0016); ver local.service_secret_files.
    volumes {
      name = "app-secrets"
      secret {
        secret = google_secret_manager_secret.service_files[each.key].secret_id
        items {
          version = "latest"
          path    = "appsettings.secrets.json"
        }
      }
    }

    containers {
      # Imagem placeholder no primeiro apply; o GitHub Actions passa a atualizar a imagem
      # a cada deploy (ver lifecycle.ignore_changes abaixo para evitar drift).
      image = "us-docker.pkg.dev/cloudrun/container/hello"

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
        cpu_idle = true
      }

      volume_mounts {
        name       = "app-secrets"
        mount_path = "/secrets"
      }

      dynamic "env" {
        # env base (locals) + env extra não-secreta vinda do tfvars (var.plain_env).
        for_each = merge(each.value.env, lookup(var.plain_env, each.key, {}))
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = each.value.secret_env
        content {
          name = env.key
          value_source {
            secret_key_ref {
              secret  = google_secret_manager_secret.secrets[env.value].secret_id
              version = "latest"
            }
          }
        }
      }
    }
  }

  depends_on = [
    google_project_service.apis,
    google_secret_manager_secret_iam_member.runtime_access,
    google_secret_manager_secret_iam_member.service_files,
    google_secret_manager_secret_version.service_files,
  ]

  lifecycle {
    # A imagem é gerida pelo pipeline (GitHub Actions). Terraform não deve reverter.
    ignore_changes = [
      template[0].containers[0].image,
      client,
      client_version,
    ]
  }
}

# Acesso público (5 usuários chamam a API direto). Remova se for expor só via gateway/IAP.
resource "google_cloud_run_v2_service_iam_member" "public" {
  for_each = google_cloud_run_v2_service.services
  name     = each.value.name
  location = var.region
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# -----------------------------------------------------------------------------
# Workload Identity Federation: GitHub Actions -> GCP (sem chave estática)
# -----------------------------------------------------------------------------
resource "google_service_account" "deployer" {
  account_id   = "cime-github-deployer"
  display_name = "GitHub Actions deployer (CIME)"
}

# O deployer pode: gerir Cloud Run, escrever imagens no AR e "atuar como" o runtime SA.
resource "google_project_iam_member" "deployer_run_admin" {
  project = var.project_id
  role    = "roles/run.admin"
  member  = "serviceAccount:${google_service_account.deployer.email}"
}

resource "google_project_iam_member" "deployer_ar_writer" {
  project = var.project_id
  role    = "roles/artifactregistry.writer"
  member  = "serviceAccount:${google_service_account.deployer.email}"
}

resource "google_service_account_iam_member" "deployer_act_as_runtime" {
  service_account_id = google_service_account.runtime.name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${google_service_account.deployer.email}"
}

resource "google_iam_workload_identity_pool" "github" {
  workload_identity_pool_id = "github-pool"
  display_name              = "GitHub Actions Pool"
  depends_on                = [google_project_service.apis]
}

resource "google_iam_workload_identity_pool_provider" "github" {
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "github-provider"
  display_name                       = "GitHub OIDC"

  attribute_mapping = {
    "google.subject"       = "assertion.sub"
    "attribute.repository" = "assertion.repository"
  }

  # Só aceita tokens vindos do repositório informado.
  attribute_condition = "assertion.repository == \"${var.github_repo}\""

  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }
}

# Permite que workflows DESSE repo GitHub personifiquem o deployer SA.
resource "google_service_account_iam_member" "wif_impersonation" {
  service_account_id = google_service_account.deployer.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.github.name}/attribute.repository/${var.github_repo}"
}
