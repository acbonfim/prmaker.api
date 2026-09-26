# API principal (Solvace.PullRequests).
# IMPORTANTE: o contexto de build DEVE ser a raiz do repositório, porque este projeto
# referencia vários projetos irmãos em CIME/modules/** e o Directory.Build.props da raiz.
#   docker build -f CIME/modules/Solvace.PullRequests/src/solvace.prform.api/Dockerfile -t <img> .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore "CIME/modules/Solvace.PullRequests/src/solvace.prform.api/solvace.prform.api.csproj"
RUN dotnet publish "CIME/modules/Solvace.PullRequests/src/solvace.prform.api/solvace.prform.api.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Cloud Run envia tráfego para $PORT (8080). O aspnet:10.0 já escuta em 8080 por padrão.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "solvace.prform.api.dll"]
