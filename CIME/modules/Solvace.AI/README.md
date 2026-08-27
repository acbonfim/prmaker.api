# Guia: Como Gerar API Keys para Provedores de IA

Este guia explica passo a passo como obter API keys para os provedores de IA suportados: **Gemini**, **OpenAI** e **Claude**.

---

## 1. Google Gemini (Gemini API)

### Passo a passo:

1. **Acesse o Google AI Studio**
   - URL: https://aistudio.google.com/
   - Faça login com sua conta Google

2. **Navegue até a seção de API Key**
   - No menu lateral, clique em **"Get API Key"** (Obter chave de API)
   - Ou acesse diretamente: https://aistudio.google.com/app/apikey

3. **Criar novo projeto (se necessário)**
   - Se você não tiver um projeto Google Cloud, será solicitado a criar um
   - Clique em **"Create API Key in new project"** (Criar chave de API em novo projeto)
   - Ou selecione um projeto existente

4. **Copiar a API Key**
   - Uma vez gerada, a chave será exibida
   - **IMPORTANTE**: Copie a chave imediatamente, pois ela não será exibida novamente
   - Formato da chave: `AIzaSyXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX`

5. **Configurar restrições (Recomendado)**
   - Clique em **"Restrict API Key"** para adicionar restrições de segurança
   - Limite o uso por aplicativo, IP ou domínio conforme necessário

6. **Configurar no appsettings.json**
   ```json
   "Gemini": {
     "ApiKey": "SUA_API_KEY_AQUI",
     "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent",
     "Model": "gemini-2.5-flash"
   }
   ```

### Informações adicionais:
- **Limites**: O Gemini tem um tier gratuito generoso com limites de uso
- **Modelos disponíveis**: `gemini-2.5-flash`, `gemini-pro`, `gemini-pro-vision`
- **Documentação**: https://ai.google.dev/docs

---

## 2. OpenAI (ChatGPT API)

### Passo a passo:

1. **Acesse o site da OpenAI**
   - URL: https://platform.openai.com/
   - Faça login ou crie uma conta

2. **Navegue até API Keys**
   - Clique no ícone do seu perfil (canto superior direito)
   - Selecione **"API Keys"** ou acesse: https://platform.openai.com/api-keys

3. **Criar nova API Key**
   - Clique em **"Create new secret key"** (Criar nova chave secreta)
   - Dê um nome descritivo para a chave (ex: "PRMaker API")

4. **Copiar a API Key**
   - Uma janela será exibida com sua chave
   - **IMPORTANTE**: Copie a chave imediatamente, pois ela não será exibida novamente
   - Formato da chave: `sk-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX`

5. **Adicionar créditos (Necessário)**
   - A OpenAI requer créditos/pagamento para usar a API
   - Vá em **"Billing"** → **"Add payment method"**
   - Adicione um método de pagamento (cartão de crédito)

6. **Configurar no appsettings.json**
   ```json
   "OpenAI": {
     "ApiKey": "sk-SUA_API_KEY_AQUI",
     "BaseUrl": "https://api.openai.com/v1/chat/completions",
     "Model": "gpt-4"
   }
   ```

### Informações adicionais:
- **Preços**: Baseados em tokens usados (veja: https://openai.com/pricing)
- **Modelos disponíveis**: `gpt-4`, `gpt-4-turbo`, `gpt-3.5-turbo`
- **Trial**: Novas contas recebem créditos gratuitos para teste
- **Documentação**: https://platform.openai.com/docs

---

## 3. Anthropic Claude

### Passo a passo:

1. **Acesse o Console da Anthropic**
   - URL: https://console.anthropic.com/
   - Faça login ou crie uma conta

2. **Navegue até API Keys**
   - No menu lateral, clique em **"API Keys"**
   - Ou acesse diretamente: https://console.anthropic.com/settings/keys

3. **Criar nova API Key**
   - Clique em **"Create Key"** (Criar chave)
   - Dê um nome descritivo para a chave (ex: "PRMaker API")

4. **Copiar a API Key**
   - Uma janela será exibida com sua chave
   - **IMPORTANTE**: Copie a chave imediatamente, pois ela não será exibida novamente
   - Formato da chave: `sk-ant-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX`

5. **Adicionar créditos (Necessário)**
   - O Claude requer créditos/pagamento para usar a API
   - Vá em **"Billing"** → **"Add payment method"**
   - Adicione um método de pagamento (cartão de crédito)

6. **Configurar no appsettings.json**
   ```json
   "Claude": {
     "ApiKey": "sk-ant-SUA_API_KEY_AQUI",
     "BaseUrl": "https://api.anthropic.com/v1/messages",
     "Model": "claude-3-5-sonnet-20241022"
   }
   ```

### Informações adicionais:
- **Preços**: Baseados em tokens usados (veja: https://www.anthropic.com/pricing)
- **Modelos disponíveis**: 
  - `claude-3-5-sonnet-20241022` (recomendado)
  - `claude-3-opus-20240229`
  - `claude-3-sonnet-20240229`
  - `claude-3-haiku-20240307`
- **Trial**: Contas novas recebem créditos gratuitos para teste
- **Documentação**: https://docs.anthropic.com/

---

## 4. Configuração no appsettings.json

Depois de obter todas as API keys, configure no arquivo `appsettings.json` ou `appsettings.Development.json`:

```json
{
  "AI": {
    "Provider": "Gemini",  // Altere para: "Gemini", "OpenAI" ou "Claude"
    "Gemini": {
      "ApiKey": "AIzaSyXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
      "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent",
      "Model": "gemini-2.5-flash"
    },
    "OpenAI": {
      "ApiKey": "sk-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
      "BaseUrl": "https://api.openai.com/v1/chat/completions",
      "Model": "gpt-4"
    },
    "Claude": {
      "ApiKey": "sk-ant-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
      "BaseUrl": "https://api.anthropic.com/v1/messages",
      "Model": "claude-3-5-sonnet-20241022"
    }
  }
}
```

### Alterar o provedor

Para usar um provedor diferente, altere apenas a propriedade `"Provider"`:

```json
"Provider": "OpenAI"  // ou "Claude" ou "Gemini"
```

---

## 5. Segurança e Boas Práticas

### ⚠️ IMPORTANTE - Segurança

1. **Nunca commite API keys no Git**
   - Use `appsettings.Development.json` (que deve estar no `.gitignore`)
   - Ou use variáveis de ambiente

2. **Restringir uso das API keys**
   - Configure restrições por IP, domínio ou aplicativo quando possível
   - Revise regularmente o uso das chaves nos painéis de cada provedor

3. **Rotação de chaves**
   - Gere novas chaves periodicamente
   - Revogue chaves antigas que não estão mais em uso

4. **Monitorar uso e custos**
   - Acompanhe o consumo de tokens/créditos em cada provedor
   - Configure alertas de limite de gastos quando disponível

### Variáveis de Ambiente (Alternativa Segura)

Se preferir não usar `appsettings.json`, configure via variáveis de ambiente:

```bash
# Windows PowerShell
$env:AI__Provider = "OpenAI"
$env:AI__OpenAI__ApiKey = "sk-..."
$env:AI__OpenAI__BaseUrl = "https://api.openai.com/v1/chat/completions"
$env:AI__OpenAI__Model = "gpt-4"
```

```bash
# Linux/Mac
export AI__Provider=OpenAI
export AI__OpenAI__ApiKey=sk-...
export AI__OpenAI__BaseUrl=https://api.openai.com/v1/chat/completions
export AI__OpenAI__Model=gpt-4
```

---

## 6. Testando as API Keys

Após configurar, teste usando o endpoint da API:

```bash
POST /api/v1/AI/generate
Headers:
  x-api-key: SEU_X_API_KEY
Content-Type: text/plain

Body: "Explique o que é inteligência artificial"
```

Ou use o Postman (veja a coleção em `Postman_Collection.json`).

---

## 7. Troubleshooting

### Erro: "ApiKey não configurado"
- Verifique se a chave está correta no `appsettings.json`
- Confirme que não há espaços extras antes/depois da chave
- Reinicie a aplicação após alterar a configuração

### Erro: "401 Unauthorized"
- Verifique se a API key está correta e válida
- Confirme que a chave não expirou ou foi revogada
- Verifique se há créditos suficientes na conta (OpenAI e Claude)

### Erro: "403 Forbidden"
- Verifique se a API key tem permissões adequadas
- Confirme que não há restrições de IP/domínio bloqueando o acesso
- Verifique os limites de uso da sua conta

### Erro: "429 Too Many Requests"
- Você atingiu o limite de rate limit da API
- Aguarde alguns minutos ou verifique seu plano de uso

---

## 8. Links Úteis

- **Gemini**: https://aistudio.google.com/
- **OpenAI**: https://platform.openai.com/
- **Claude**: https://console.anthropic.com/

---

**Última atualização**: Dezembro 2024



### Conta Test
Não travou. Aqui estão informações sobre tokens de teste:

## Tokens de teste disponíveis

### 1. Google Gemini — gratuito (melhor opção para testes)

Disponibilidade:
- Gratuito com limites generosos
- Sem necessidade de cartão de crédito

Como obter:
1. Acesse: https://aistudio.google.com/
2. Faça login com conta Google
3. Vá em "Get API Key": https://aistudio.google.com/app/apikey
4. Crie uma API key — é gratuita

Limites gratuitos:
- 60 requisições por minuto (RPM)
- 1.500 requisições por dia (RPD)
- 32.000 tokens por minuto (TPM)

Recomendação: use o Gemini para testes — é gratuito e não requer pagamento.

---

### 2. OpenAI (ChatGPT) — créditos gratuitos

Disponibilidade:
- Novas contas recebem $5 em créditos gratuitos
- Requer cadastro de cartão (mas não cobra automaticamente)

Como obter:
1. Acesse: https://platform.openai.com/
2. Crie uma conta nova
3. Vá em "API Keys": https://platform.openai.com/api-keys
4. Crie uma API key
5. Adicione um método de pagamento (cartão) — necessário mesmo para usar os créditos gratuitos

Créditos gratuitos:
- $5 para novas contas (suficiente para muitos testes)
- Exemplo: ~500.000 tokens com GPT-3.5-turbo

Observação: mesmo com créditos gratuitos, é necessário cadastrar um cartão.

---

### 3. Anthropic Claude — créditos gratuitos

Disponibilidade:
- Novas contas recebem $5 em créditos gratuitos
- Requer cadastro de cartão

Como obter:
1. Acesse: https://console.anthropic.com/
2. Crie uma conta
3. Vá em "API Keys": https://console.anthropic.com/settings/keys
4. Crie uma API key
5. Adicione um método de pagamento

Créditos gratuitos:
- $5 para novas contas
- Exemplo: ~250.000 tokens com Claude 3 Sonnet

Observação: requer cadastro de cartão.

---

## Resumo e recomendação

Para testes sem cadastro de cartão:
- Use Gemini — é totalmente gratuito e suficiente para desenvolvimento

Para testes com créditos gratuitos:
- OpenAI: $5 em créditos gratuitos + cartão necessário
- Claude: $5 em créditos gratuitos + cartão necessário

---

## Sugestão prática

1. Teste inicial: use Gemini (gratuito, sem cartão)
2. Comparação: se precisar testar outros provedores, use os créditos gratuitos do OpenAI ou Claude

Quer que eu atualize o README do módulo AI com essas informações de tokens de teste?