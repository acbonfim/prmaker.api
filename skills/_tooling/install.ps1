# Instalador das skills do PRMake para o Claude Code no Windows (feature 0035). Uso (copiado da tela "Skills" do
# PRMake), no PowerShell:
#   $env:PRMAKE_TOKEN='<sua api-key>'; irm -Headers @{'x-api-key'=$env:PRMAKE_TOKEN} __PRMAKE_API_BASE__/Skills/install.ps1 | iex
# Instala o que faltar pelo winget (Git for Windows, jq, Python 3) e roda o install.sh no Git Bash - o mesmo shell que
# o Claude Code usa no Windows. $env:PRMAKE_SKILLS='analisar-bug gerar-prmake' instala so essas skills;
# $env:PRMAKE_NO_DEPS='1' nao instala dependencias (so avisa).
# Mantido em ASCII: o Windows PowerShell 5.1 le o script como texto e pode trocar os acentos.
& {
  $ErrorActionPreference = 'Stop'
  $ProgressPreference = 'SilentlyContinue'
  [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

  $Base = if ($env:PRMAKE_API_BASE) { $env:PRMAKE_API_BASE } else { '__PRMAKE_API_BASE__' }
  $Token = $env:PRMAKE_TOKEN
  $TokenFile = Join-Path $env:USERPROFILE '.claude\prmake-token.txt'
  if (-not $Token -and (Test-Path $TokenFile)) { $Token = (Get-Content $TokenFile -Raw).Trim() }
  if (-not $Token) {
    Write-Host "ERRO: informe a api-key: `$env:PRMAKE_TOKEN='<sua api-key do PRMake>'; irm ... | iex" -ForegroundColor Red
    return
  }
  $NoDeps = $env:PRMAKE_NO_DEPS -eq '1'

  function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user) | Where-Object { $_ }) -join ';'
  }

  function Test-Command([string]$Exe, [string[]]$Arguments) {
    $ErrorActionPreference = 'Continue'  # no PowerShell 5.1 o stderr redirecionado de um .exe vira erro
    try {
      & $Exe @Arguments *> $null
      return $LASTEXITCODE -eq 0
    } catch { return $false }
  }

  function Install-WithWinget([string]$Id, [string]$Label) {
    if ($NoDeps) { Write-Host "AVISO: falta $Label (PRMAKE_NO_DEPS=1 - nao instalei): winget install -e --id $Id" -ForegroundColor Yellow; return }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
      Write-Host "AVISO: falta $Label e o winget nao esta disponivel - instale manualmente e rode de novo." -ForegroundColor Yellow
      return
    }
    Write-Host "Instalando $Label (winget install -e --id $Id)..."
    & winget install -e --id $Id --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { Write-Host "AVISO: winget install $Id terminou com codigo $LASTEXITCODE" -ForegroundColor Yellow }
    Update-SessionPath
  }

  # Git Bash (nunca o bash.exe do System32, que e o WSL).
  function Find-GitBash {
    $candidates = @()
    if ($env:CLAUDE_CODE_GIT_BASH_PATH) { $candidates += $env:CLAUDE_CODE_GIT_BASH_PATH }
    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($git) { $candidates += (Join-Path (Split-Path (Split-Path $git.Source)) 'bin\bash.exe') }
    $candidates += (Join-Path $env:ProgramFiles 'Git\bin\bash.exe')
    if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Git\bin\bash.exe') }
    $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Git\bin\bash.exe')
    foreach ($c in $candidates) { if ($c -and (Test-Path $c) -and ($c -notmatch '\\System32\\')) { return $c } }
    return $null
  }

  Write-Host 'Skills do PRMake - instalacao no Windows'

  $bash = Find-GitBash
  if (-not $bash) {
    Install-WithWinget 'Git.Git' 'Git for Windows (Git Bash)'
    $bash = Find-GitBash
  }
  if (-not $bash) {
    Write-Host 'ERRO: Git Bash nao encontrado. Instale o Git for Windows (https://git-scm.com/download/win) e rode de novo.' -ForegroundColor Red
    return
  }
  Write-Host "   Git Bash: $bash"

  if (-not (Get-Command jq -ErrorAction SilentlyContinue)) { Install-WithWinget 'jqlang.jq' 'jq' }

  # O "python3"/"python" da Microsoft Store e so um atalho que abre a loja: testa se o Python roda de verdade.
  $pythonOk = (Test-Command 'py' @('-3', '-c', 'import sys')) -or
              (Test-Command 'python' @('-c', 'import sys; sys.exit(0 if sys.version_info[0] == 3 else 1)'))
  if (-not $pythonOk) { Install-WithWinget 'Python.Python.3.12' 'Python 3' }

  $tmp = Join-Path ([IO.Path]::GetTempPath()) ("prmake-install-" + [guid]::NewGuid().ToString('N') + '.sh')
  try {
    Invoke-WebRequest -UseBasicParsing -Headers @{ 'x-api-key' = $Token } -Uri "$Base/Skills/install.sh" -OutFile $tmp
  } catch {
    Write-Host "ERRO: nao consegui baixar o instalador ($($_.Exception.Message)) - confira a api-key." -ForegroundColor Red
    return
  }

  $skills = @()
  if ($env:PRMAKE_SKILLS) { $skills = @($env:PRMAKE_SKILLS -split '[\s,;]+' | Where-Object { $_ }) }

  $env:PRMAKE_TOKEN = $Token
  $env:PRMAKE_API_BASE = $Base
  & $bash ($tmp -replace '\\', '/') @skills
  $code = $LASTEXITCODE
  Remove-Item $tmp -ErrorAction SilentlyContinue

  if ($code -eq 0) {
    Write-Host ''
    Write-Host 'Pronto. Feche e abra o Claude Code (e o terminal) para ele enxergar as skills e as dependencias novas.' -ForegroundColor Green
  } else {
    Write-Host "ERRO: a instalacao terminou com codigo $code (veja as mensagens acima)." -ForegroundColor Red
  }
}
