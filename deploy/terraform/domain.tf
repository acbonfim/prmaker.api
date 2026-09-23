# -----------------------------------------------------------------------------
# Mapeamento de domínio custom (Opção A — grátis, SSL gerenciado pelo Google)
#
#   api.softhouse.app.br  -> Cloud Run  cime-pullrequest
#   auth.softhouse.app.br -> Cloud Run  cime-auth
#   app.softhouse.app.br  -> Firebase Hosting (configurado no repo do frontend)
#
# Fica DESLIGADO por padrão (enable_domain_mapping = false) para não travar o apply
# inicial: o mapeamento exige que o domínio esteja VERIFICADO no Google Search Console.
# Fluxo do cutover:
#   1) Verifique softhouse.app.br no Search Console (adiciona 1 TXT no Registro.br).
#   2) Set enable_domain_mapping = true e rode `terraform apply`.
#   3) Rode `terraform output domain_dns_records` e cadastre os CNAMEs no Registro.br.
# -----------------------------------------------------------------------------
locals {
  domain_mappings = var.enable_domain_mapping ? {
    "api.${var.domain}"  = google_cloud_run_v2_service.services["pullrequest"].name
    "auth.${var.domain}" = google_cloud_run_v2_service.services["auth"].name
  } : {}
}

resource "google_cloud_run_domain_mapping" "map" {
  for_each = local.domain_mappings
  location = var.region
  name     = each.key

  metadata {
    namespace = var.project_id
  }

  spec {
    route_name = each.value
  }
}

output "domain_dns_records" {
  description = "Registros DNS a cadastrar no Registro.br para cada subdomínio."
  value = {
    for k, m in google_cloud_run_domain_mapping.map :
    k => m.status[0].resource_records
  }
}
