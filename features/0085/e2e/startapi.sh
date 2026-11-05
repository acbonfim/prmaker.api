#!/usr/bin/env bash
cd "$(dirname "$0")"; kill $(lsof -tiTCP:5083 -sTCP:LISTEN) 2>/dev/null; sleep 1
cd ../CIME/modules/Solvace.PullRequests/src/solvace.prform.api
CS="Host=localhost;Port=55485;Database=cime;Username=postgres;Password=postgres"
ConnectionStrings__PrformDatabase="$CS" ConnectionStrings__AuthDatabase="$CS" \
Auth__UrlBase=http://127.0.0.1:8099/api ASPNETCORE_ENVIRONMENT=Development RealTime__Mode=InProcess RealTime__AllowedOrigins__0=http://localhost:4200 \
nohup dotnet run --no-build --urls http://127.0.0.1:5083 > ../../../../../.t0085/api.log 2>&1 &
