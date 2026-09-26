# Auth API (Cime.Auth).
# IMPORTANTE: o contexto de build DEVE ser a raiz do repositório para incluir o
# Directory.Build.props da raiz (ele fixa a família System.IdentityModel em 7.1.2;
# sem isso o app quebra em runtime com FileNotFoundException após o deploy).
#   docker build -f CIME/modules/Cime.Auth/src/cime.auth.api/Dockerfile -t <img> .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore "CIME/modules/Cime.Auth/src/cime.auth.api/cime.auth.api.csproj"
RUN dotnet publish "CIME/modules/Cime.Auth/src/cime.auth.api/cime.auth.api.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "cime.auth.api.dll"]
