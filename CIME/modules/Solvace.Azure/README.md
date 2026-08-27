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
- Work Items - Read & Write
- Code - Read (se precisar acessar código)
- Project and Team - Read
- Build - Read (se precisar acessar builds)
- Release - Read (se precisar acessar releases)
OU mais específico:
- Work Items - Read & Write
- Project and Team - Read
6. Gere e Copie:
Clique em "Create"
⚠️ IMPORTANTE: Copie o token imediatamente! Ele só aparece uma vez.
Clique em "Copy to clipboard"
7. Atualize o appsettings.Development.json:
</details>
