# Status — Feature 0035 (instalação das skills no Windows e no macOS/Linux)

| Fase | Descrição | Status |
|---|---|---|
| S1 | `install.sh` instala dependências (brew/apt/dnf/yum/pacman/zypper/apk; jq baixado sem sudo); `curl \| bash` protegido (main + `</dev/null`) | ✅ |
| S2 | `prmake-skills.sh` no Git Bash: sem rsync/unzip obrigatórios, atalhos python3/jq, `setup.windows`, hook PowerShell | ✅ |
| S3 | `install.ps1` (winget: Git, jq, Python) → roda o `install.sh` no Git Bash; `kb.sh`/`kc.sh`/`sql-query.sh` compatíveis | ✅ |
| B1 | `GET /api/v1/Skills/install.ps1` | ✅ |
| F1 | Tela Skills: seletor macOS/Linux × Windows (detecta o sistema), comando e dependências de cada um | ✅ |

## Testes (2026-09-30, servidor falso dos endpoints /Skills)
- macOS sem rsync/unzip no PATH: instala tudo (bsdtar), venvs, update/status, remoção de arquivos antigos pelo fallback.
- Ubuntu 24.04 limpo (root): instala jq/unzip/python3/python3-venv e as skills; sem sudo: baixa o jq para ~/.local/bin.
- Fedora 41 limpo: dnf instala jq/unzip/python3 e as skills.
- Windows simulado (uname MINGW, jq com CRLF, só `python`): atalhos, ~/.bashrc e hook `shell: powershell` com aspas escapadas.
- Não testado em Windows real (install.ps1 e winget) — validar na primeira instalação.
