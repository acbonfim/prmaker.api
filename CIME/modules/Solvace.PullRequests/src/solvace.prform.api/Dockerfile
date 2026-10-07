# API principal (Solvace.PullRequests).
# IMPORTANTE: o contexto de build DEVE ser a raiz do repositório, porque este projeto
# referencia vários projetos irmãos em CIME/modules/** e o Directory.Build.props da raiz.
#   docker build -f CIME/modules/Solvace.PullRequests/src/solvace.prform.api/Dockerfile -t <img> .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
# Arquitetura do alvo, preenchida pelo BuildKit (amd64 no CI/Cloud Run; arm64 num Mac).
ARG TARGETARCH
WORKDIR /src
COPY . .
# ReadyToRun (0018): código pré-compilado para o RID do alvo → menos JIT e cold start menor.
# O restore também precisa do PublishReadyToRun (baixa o compilador crossgen do RID).
RUN RID=linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) && \
    dotnet restore "CIME/modules/Solvace.PullRequests/src/solvace.prform.api/solvace.prform.api.csproj" -r $RID -p:PublishReadyToRun=true && \
    dotnet publish "CIME/modules/Solvace.PullRequests/src/solvace.prform.api/solvace.prform.api.csproj" \
      -c $BUILD_CONFIGURATION -r $RID --self-contained false --no-restore \
      -p:PublishReadyToRun=true -o /app/publish /p:UseAppHost=false

# Executor do PRMake (0039): binário único por sistema (self-contained, trimmed), servido em
# GET /api/v1/ExecutionWorker/agent/{rid}; version.txt = <Version> do csproj (o executor se atualiza por ele).
# Stage próprio: não depende do resto do repositório, então o BuildKit o roda EM PARALELO com o build
# da API acima, e os 5 RIDs publicam em paralelo (cada um numa cópia do projeto: obj/ separado).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS agent
WORKDIR /src
COPY tools/Cime.ExecutionAgent/ tools/Cime.ExecutionAgent/
RUN set -e; PIDS=""; \
    PROJ=tools/Cime.ExecutionAgent/Cime.ExecutionAgent.csproj; \
    for RID in osx-arm64 osx-x64 win-x64 linux-x64 linux-arm64; do \
      ( cp -r tools/Cime.ExecutionAgent "/tmp/agent-$RID" && \
        dotnet publish "/tmp/agent-$RID/Cime.ExecutionAgent.csproj" -c Release -r $RID -o "/app/agent/$RID" ) & \
      PIDS="$PIDS $!"; \
    done; \
    FAIL=0; for P in $PIDS; do wait $P || FAIL=1; done; \
    [ $FAIL -eq 0 ] && \
    for RID in osx-arm64 osx-x64 win-x64 linux-x64 linux-arm64; do test -d "/app/agent/$RID"; done && \
    sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' $PROJ > /app/agent/version.txt && \
    test -s /app/agent/version.txt

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Skills do Claude Code servidas pela API (0024): GET /api/v1/Skills.
COPY --from=build /src/skills ./skills
COPY --from=agent /app/agent ./agent
# Cloud Run envia tráfego para $PORT (8080). O aspnet:10.0 já escuta em 8080 por padrão.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
# 0070: o Cloud Run mata o container em 512 MiB (heap + memória nativa); com o heap no padrão (75%) a soma passava do
# limite em rajadas. 65% (hex 0x41) + GCConserveMemory: no teste local com a Base real e o estresse da sequência do OOM,
# 0 OutOfMemory e pico de RSS ~480 MB.
ENV DOTNET_GCHeapHardLimitPercent=0x41 \
    DOTNET_GCConserveMemory=5
ENTRYPOINT ["dotnet", "solvace.prform.api.dll"]
