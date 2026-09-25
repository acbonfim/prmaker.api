variable "project_id" {
  description = "ID do projeto GCP onde tudo será provisionado."
  type        = string
}

variable "region" {
  description = "Região do Cloud Run e do Artifact Registry."
  type        = string
  default     = "us-central1"
}

variable "ar_repo_name" {
  description = "Nome do repositório Docker no Artifact Registry."
  type        = string
  default     = "cime"
}

variable "github_repo" {
  description = "Repositório GitHub autorizado a fazer deploy, no formato 'owner/repo'."
  type        = string
}

variable "domain" {
  description = "Domínio raiz usado nos subdomínios api./auth./app."
  type        = string
  default     = "softhouse.app.br"
}

variable "enable_domain_mapping" {
  description = "Liga os domain mappings do Cloud Run (só após verificar o domínio no Search Console)."
  type        = bool
  default     = false
}

variable "realtime_relay_url" {
  description = "URL base do relay de tempo real (feature 0013), site do MonsterASP."
  type        = string
  default     = "https://prformapi.runasp.net"
}

variable "min_instances" {
  description = "Instâncias mínimas por serviço. 0 = scale-to-zero (mais barato, com cold start)."
  type        = number
  default     = 0
}

variable "max_instances" {
  description = "Instâncias máximas por serviço."
  type        = number
  default     = 2
}

# Variáveis de ambiente NÃO sensíveis por serviço (URLs, flags, etc.).
# Chave externa = "pullrequest" ou "auth"; chave interna = nome da env var (.NET usa "__").
# Ex.: { pullrequest = { "Auth__UrlBase" = "https://cime-auth-xxxx.run.app/api" } }
variable "plain_env" {
  description = "Env vars não sensíveis por serviço (mergeadas às padrões)."
  type        = map(map(string))
  default     = {}
}

# Valores sensíveis (connection strings, tokens, senhas). NÃO comite o arquivo .tfvars
# que preencher com estes valores — use deploy/terraform/secrets.auto.tfvars (gitignorado).
# A chave de cada entrada vira o ID do secret no Secret Manager.
variable "secret_values" {
  description = "Mapa de secret_id => valor. Cada entrada cria um Secret Manager secret + versão."
  type        = map(string)
  sensitive   = true
  default     = {}
}
