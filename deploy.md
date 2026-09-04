# Deploy

Guia de build para gerar os artefatos de deploy da aplicação (Auth API, PullRequest API e Frontend).

Todos os artefatos são gerados na pasta `/publish` na raiz deste projeto, com a seguinte estrutura:

```
publish/
├── web/            # Frontend Angular (arquivos estáticos)
├── pullrequest/    # API principal (Solvace.PullRequests)
└── authapi/        # API de autenticação (Cime.Auth)
```

## Pré-requisitos

- .NET 8.0 SDK
- Node.js + Angular CLI (`ng`) v20
- Executar os comandos a partir da **raiz** deste repositório (`prform.api`)

> Login no AWS CodeArtifact (pacotes NuGet privados), se necessário:
> ```bash
> aws codeartifact login --tool dotnet --repository revamp --domain solvace --domain-owner 367983645102 --region us-east-1
> ```

---

## 1. PullRequest API (`publish/pullrequest`)

```bash
dotnet publish CIME/modules/Solvace.PullRequests/src/solvace.prform.api/solvace.prform.api.csproj \
  -c Release \
  -o ./publish/pullrequest
```

## 2. Auth API (`publish/authapi`)

```bash
dotnet publish CIME/modules/Cime.Auth/src/cime.auth.api/cime.auth.api.csproj \
  -c Release \
  -o ./publish/authapi
```

## 3. Frontend / Web (`publish/web`)

O projeto do frontend fica em `../solvace.prform.web/prform-app` (referenciado em `.claude/`).
É um Angular 20 (builder `@angular/build:application`), cuja build de produção sai em `dist/prform-app/browser`.

```bash
# Build de produção
cd ../solvace.prform.web/prform-app
ng build --configuration production --base-href "/" --source-map

# Copiar os arquivos estáticos para a pasta de publish deste projeto
rm -rf ../../solvace/prform.api/publish/web
mkdir -p ../../solvace/prform.api/publish/web
cp -R dist/prform-app/browser/. ../../solvace/prform.api/publish/web/

# Voltar para a raiz da API
cd ../../solvace/prform.api
```

---

## Deploy completo (tudo de uma vez)

Execute a partir da raiz do projeto (`prform.api`):

```bash
# Limpa a pasta de publish
rm -rf ./publish

# Backend - PullRequest API
dotnet publish CIME/modules/Solvace.PullRequests/src/solvace.prform.api/solvace.prform.api.csproj \
  -c Release -o ./publish/pullrequest

# Backend - Auth API
dotnet publish CIME/modules/Cime.Auth/src/cime.auth.api/cime.auth.api.csproj \
  -c Release -o ./publish/authapi

# Frontend - Web
( cd ../solvace.prform.web/prform-app \
  && ng build --configuration production --base-href "/" --source-map )
mkdir -p ./publish/web
cp -R ../solvace.prform.web/prform-app/dist/prform-app/browser/. ./publish/web/
```
