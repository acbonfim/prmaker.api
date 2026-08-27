## Create database

CREATE USER 'cliqxauth'@'localhost' IDENTIFIED BY 'cliqxauth#2026';
GRANT ALL PRIVILEGES ON * . * TO 'cliqxauth'@'localhost';

->ef core migration dentro de cliqx.auth.api

dotnet ef migrations add Init (Caso não exista)
dotnet ef database update

## Manual do publish NET CORE

dotnet publish cliqx.auth.api/cliqx.auth.api.csproj --configuration "Release" --framework net7.0 /p:
TargetLatestRuntimePatch=false --self-contained true -r linux-x64 --output "cliqx.auth.api/bin/Release/net7.0/publish"

## Ambiente

- QA -> dotnet run --launch-profile QA
- PROD -> dotnet run