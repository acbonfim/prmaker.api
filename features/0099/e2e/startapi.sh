#!/usr/bin/env bash
cd "$(dirname "$0")"; kill $(lsof -tiTCP:5099 -sTCP:LISTEN) 2>/dev/null; sleep 1
cd ../CIME/modules/Solvace.PullRequests/src/solvace.prform.api
CS="Host=localhost;Port=55491;Database=cime;Username=postgres;Password=postgres"
ConnectionStrings__PrformDatabase="$CS" ConnectionStrings__AuthDatabase="$CS" \
Auth__UrlBase=http://127.0.0.1:8299/api ASPNETCORE_ENVIRONMENT=Development RealTime__Mode=InProcess RealTime__AllowedOrigins__0=http://localhost:4399 \
AzureDevOps__HostOverride=http://127.0.0.1:8199 \
nohup dotnet run --no-build --urls http://127.0.0.1:5099 > ../../../../../.t0099/api.log 2>&1 &
