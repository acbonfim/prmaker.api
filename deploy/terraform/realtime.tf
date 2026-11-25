# -----------------------------------------------------------------------------
# Tempo real pelo Pub/Sub (2026-11-25)
#
# De parte dos IPs de saída do Cloud Run o relay do MonsterASP não abre conexão TCP: a instância que sai por um
# desses IPs passa a vida inteira sem tempo real (2026-11-25: 7 de 11 instâncias com 100% de falha no POST
# /publish, as outras com 0%). A API publica neste tópico (rede do Google) e o relay busca na assinatura —
# a conexão sai do MonsterASP para o Google, nunca o contrário.
#
# Ordem de implantação (ver deploy/README.md, "Tempo real pelo Pub/Sub"):
#   1. terraform apply (realtime_pubsub_publish = false): tópico, assinatura, conta do relay e a chave dela;
#   2. secret REALTIME_PUBSUB_CREDENTIALS no GitHub com a chave (output realtime_relay_pubsub_key) e deploy do relay;
#   3. GET /health do relay com pubSub.state = "listening";
#   4. terraform apply com realtime_pubsub_publish = true: a API passa a publicar aqui.
# Rollback: realtime_pubsub_publish = false (a API volta ao POST /publish, que o relay continua aceitando).
# Custo: dentro da cota grátis do Pub/Sub (10 GiB/mês; os eventos são de poucos KB).
# -----------------------------------------------------------------------------
resource "google_pubsub_topic" "realtime" {
  name = "cime-realtime"

  # Mensagens guardadas em Frankfurt, perto do relay (MonsterASP, Alemanha), que lê pelo endpoint europe-west3
  # (RealTime:PubSubEndpoint). Guardadas nos EUA, a entrega cruzava o Atlântico: ~60 ms com picos de 2–3 s em ~1 de cada
  # 5 eventos (endpoint global) ou ~850 ms em todos (endpoint us-central1). A travessia fica só na publicação da API.
  message_storage_policy {
    allowed_persistence_regions = ["europe-west3"]
  }

  depends_on = [google_project_service.apis]
}

resource "google_pubsub_subscription" "realtime_relay" {
  name  = "cime-realtime-relay"
  topic = google_pubsub_topic.realtime.id

  ack_deadline_seconds = 10
  # Evento é aviso de "mudou": o relay descarta o que chega com mais de 30 s; guardar mais que isso não serve.
  message_retention_duration = "600s"
  retain_acked_messages      = false

  # Sem expiração por inatividade (o padrão apaga a assinatura depois de 31 dias sem uso).
  expiration_policy {
    ttl = ""
  }

  retry_policy {
    minimum_backoff = "1s"
    maximum_backoff = "10s"
  }
}

# A API (conta de runtime do Cloud Run) publica no tópico.
resource "google_pubsub_topic_iam_member" "realtime_publisher" {
  topic  = google_pubsub_topic.realtime.name
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.runtime.email}"
}

# O relay (fora do Google) lê a assinatura com uma conta própria, que só pode isso.
resource "google_service_account" "realtime_relay" {
  account_id   = "cime-realtime-relay"
  display_name = "Relay de tempo real (MonsterASP): lê a assinatura cime-realtime-relay"
}

resource "google_pubsub_subscription_iam_member" "realtime_relay_subscriber" {
  subscription = google_pubsub_subscription.realtime_relay.name
  role         = "roles/pubsub.subscriber"
  member       = "serviceAccount:${google_service_account.realtime_relay.email}"
}

# Chave da conta do relay (o MonsterASP não tem credencial do Google). Fica no state local (que já guarda os
# segredos) e vai para o secret REALTIME_PUBSUB_CREDENTIALS do GitHub. Trocar: terraform apply -replace=<este recurso>
# e atualizar o secret.
resource "google_service_account_key" "realtime_relay" {
  service_account_id = google_service_account.realtime_relay.name
}
