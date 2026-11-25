output "artifact_registry_repo" {
  description = "Caminho do repositório Docker (use como prefixo das imagens)."
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.repo.repository_id}"
}

output "workload_identity_provider" {
  description = "Valor para a variável de repositório GitHub GCP_WIF_PROVIDER."
  value       = google_iam_workload_identity_pool_provider.github.name
}

output "deployer_service_account" {
  description = "Valor para a variável de repositório GitHub GCP_DEPLOY_SA."
  value       = google_service_account.deployer.email
}

output "service_urls" {
  description = "URLs públicas dos serviços Cloud Run."
  value       = { for k, s in google_cloud_run_v2_service.services : k => s.uri }
}

output "realtime_relay_pubsub_key" {
  description = "Chave (JSON em base64) da conta do relay para ler a assinatura do Pub/Sub: valor do secret REALTIME_PUBSUB_CREDENTIALS do GitHub."
  value       = google_service_account_key.realtime_relay.private_key
  sensitive   = true
}

output "realtime_pubsub_subscription" {
  description = "Assinatura que o relay lê (RealTime:PubSubSubscription)."
  value       = google_pubsub_subscription.realtime_relay.id
}
