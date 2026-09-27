# Auth API (Cime.Auth).
# IMPORTANTE: o contexto de build DEVE ser a raiz do repositório para incluir o
# Directory.Build.props da raiz (ele fixa a família System.IdentityModel em 8.23.0;
# sem isso o app quebra em runtime com FileNotFoundException após o deploy).
#   docker build -f CIME/modules/Cime.Auth/src/cime.auth.api/Dockerfile -t <img> .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
# Arquitetura do alvo, preenchida pelo BuildKit (amd64 no CI/Cloud Run; arm64 num Mac).
ARG TARGETARCH
WORKDIR /src
COPY . .
# ReadyToRun (0018): código pré-compilado para o RID do alvo → menos JIT e cold start menor.
# O restore também precisa do PublishReadyToRun (baixa o compilador crossgen do RID).
RUN RID=linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) && \
    dotnet restore "CIME/modules/Cime.Auth/src/cime.auth.api/cime.auth.api.csproj" -r $RID -p:PublishReadyToRun=true && \
    dotnet publish "CIME/modules/Cime.Auth/src/cime.auth.api/cime.auth.api.csproj" \
      -c $BUILD_CONFIGURATION -r $RID --self-contained false --no-restore \
      -p:PublishReadyToRun=true -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "cime.auth.api.dll"]
