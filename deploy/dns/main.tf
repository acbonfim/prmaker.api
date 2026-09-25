terraform {
  required_version = ">= 1.5.0"
  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }
}

provider "google" {
  project = var.project_id
}

variable "project_id" {
  type    = string
  default = "cime-prod"
}

# Zona pública do domínio no Cloud DNS.
resource "google_dns_managed_zone" "softhouse" {
  name        = "softhouse-app-br"
  dns_name    = "softhouse.app.br."
  description = "Zona DNS do softhouse.app.br (migrada do MonsterASP)"
}

locals {
  zone = google_dns_managed_zone.softhouse.name
  dn   = google_dns_managed_zone.softhouse.dns_name # "softhouse.app.br."
}

# --- Raiz (mantém apontando pro MonsterApp por enquanto) ---
resource "google_dns_record_set" "root_a" {
  managed_zone = local.zone
  name         = local.dn
  type         = "A"
  ttl          = 300
  rrdatas      = ["148.251.141.67"]
}

# --- CNAMEs que vão pro Cloud Run (o cutover de verdade) ---
resource "google_dns_record_set" "api" {
  managed_zone = local.zone
  name         = "api.${local.dn}"
  type         = "CNAME"
  ttl          = 300
  rrdatas      = ["ghs.googlehosted.com."]
}

resource "google_dns_record_set" "auth" {
  managed_zone = local.zone
  name         = "auth.${local.dn}"
  type         = "CNAME"
  ttl          = 300
  rrdatas      = ["ghs.googlehosted.com."]
}

resource "google_dns_record_set" "app" {
  managed_zone = local.zone
  name         = "app.${local.dn}"
  type         = "CNAME"
  ttl          = 300
  rrdatas      = ["ghs.googlehosted.com."]
}

# --- Relay de tempo real (feature 0013) — site no MonsterASP ---
# Preencha com o host do site (ex.: "site12345.siteasp.net.") depois de criá-lo no painel;
# vazio => o registro não é criado. Depois de propagar, ative o Let's Encrypt no painel.
variable "realtime_cname_target" {
  type    = string
  default = ""
}

resource "google_dns_record_set" "realtime" {
  count        = var.realtime_cname_target == "" ? 0 : 1
  managed_zone = local.zone
  name         = "realtime.${local.dn}"
  type         = "CNAME"
  ttl          = 300
  rrdatas      = [var.realtime_cname_target]
}

# --- www (mantém no MonsterApp) ---
resource "google_dns_record_set" "www" {
  managed_zone = local.zone
  name         = "www.${local.dn}"
  type         = "CNAME"
  ttl          = 300
  rrdatas      = ["site40779.siteasp.net."]
}

# --- E-mail (MonsterASP mailasp.net) — replicado EXATAMENTE ---
resource "google_dns_record_set" "mx" {
  managed_zone = local.zone
  name         = local.dn
  type         = "MX"
  ttl          = 3600
  rrdatas      = ["10 mail2025.mailasp.net."]
}

resource "google_dns_record_set" "autodiscover" {
  managed_zone = local.zone
  name         = "autodiscover.${local.dn}"
  type         = "CNAME"
  ttl          = 3600
  rrdatas      = ["mail2025.mailasp.net."]
}

resource "google_dns_record_set" "webmail" {
  managed_zone = local.zone
  name         = "webmail.${local.dn}"
  type         = "CNAME"
  ttl          = 3600
  rrdatas      = ["mail2025.mailasp.net."]
}

resource "google_dns_record_set" "srv_autodiscover" {
  managed_zone = local.zone
  name         = "_autodiscover._tcp.${local.dn}"
  type         = "SRV"
  ttl          = 3600
  rrdatas      = ["10 10 443 mail2025.mailasp.net."]
}

# --- TXT (SPF + verificação do Google, ambos no apex) ---
resource "google_dns_record_set" "txt" {
  managed_zone = local.zone
  name         = local.dn
  type         = "TXT"
  ttl          = 3600
  rrdatas = [
    "\"v=spf1 a mx include:spf.mailasp.net ~all\"",
    "\"google-site-verification=i9MhzJXvjatt1lvRgzKa_GiAE-mb69wt2Dhd53iourA\"",
  ]
}

output "cloud_dns_nameservers" {
  description = "Nameservers do Cloud DNS — configure estes no Registro.br."
  value       = google_dns_managed_zone.softhouse.name_servers
}
