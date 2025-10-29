
<details>
<summary>Como Gerar Token do GitHub</summary>

1. Acesse o GitHub:
   1. Vá para: https://github.com
   2.  Faça login na sua conta
2. Acesse as Configurações:
   1. Clique na sua foto de perfil (canto superior direito)
   2. Selecione "Settings"
3. Navegue para Developer Settings:
   1. No menu lateral esquerdo, role para baixo
   2. Clique em "Developer settings" (no final da lista)
4. Acesse Personal Access Tokens:
   1. Clique em "Personal access tokens"
   2. Selecione "Tokens (classic)" ou "Fine-grained tokens" (recomendo classic)
5. Gere um Novo Token:
   1. Clique em "Generate new token"
   2. Selecione "Generate new token (classic)"
6. Configure o Token:
- Note: Dê um nome descritivo, ex: Solvace PR Maker API
- Expiration: Escolha um prazo (recomendo 90 dias ou "No expiration")
- Scopes (permissões): Marque as seguintes:
  - repo (acesso completo aos repositórios)
  - repo:status (acesso ao status dos commits)
  - repo_deployment (acesso aos deployments)
  - public_repo (acesso aos repositórios públicos)
  - repo:invite (acesso aos convites)
  - security_events (acesso aos eventos de segurança)
  - OU se for Fine-grained token:
Selecione o repositório específico (electradv/edv-solvace)
Marque "Contents" como "Read"

7. Gere e Copie:
   - Clique em "Generate token"
   - IMPORTANTE: Copie o token imediatamente! Ele só aparece uma vez.
  
8. Atualize o appsettings.Development.json:
 
`{
  "GITHUB_TOKEN": "SEU_TOKEN_AQUI",
  "GITHUB_OWNER": "electradv",
  "GITHUB_REPO": "edv-solvace",
  "GITHUB_BRANCH": "master"
}`


### Dicas de Segurança:
1. Nunca commite o token no Git
2. Use apenas em appsettings.Development.json (já está no .gitignore)
3. Renove periodicamente o token
4. Use escopo mínimo necessário (apenas repo para leitura)
</details>

<details>
<summary>
Como Gerar Token do Azure DevOps (PAT)
</summary>

1. Acesse o Azure DevOps:
Vá para: https://dev.azure.com
Faça login com sua conta Microsoft/Organização
2. Selecione sua Organização:
Se você tem acesso a múltiplas organizações, selecione a "solvacelabs"
Se não aparecer, clique em "Browse all organizations"
3. Acesse as Configurações de Usuário:
Clique na sua foto de perfil (canto superior direito)
Selecione "Personal access tokens"
4. Gere um Novo Token:
Clique em "+ New Token"
5. Configure o Token:
Name: Dê um nome descritivo, ex: Solvace PR Maker API
Organization: Selecione "solvacelabs" (ou a organização correta)
Expiration: Escolha um prazo (recomendo 90 dias ou "Custom defined")
Scopes (permissões): Marque as seguintes:
✅ Work Items - Read & Write
✅ Code - Read (se precisar acessar código)
✅ Project and Team - Read
✅ Build - Read (se precisar acessar builds)
✅ Release - Read (se precisar acessar releases)
OU mais específico:
✅ Work Items - Read & Write
✅ Project and Team - Read
6. Gere e Copie:
Clique em "Create"
⚠️ IMPORTANTE: Copie o token imediatamente! Ele só aparece uma vez.
Clique em "Copy to clipboard"
7. Atualize o appsettings.Development.json:
</details>
