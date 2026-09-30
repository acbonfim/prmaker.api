Tipo: Melhoria (backend + front + skills)
Prioridade: média
Origem: pedido do usuário em 2026-09-30 (redigido pelo Claude).

1. Na tela "Skills do Claude Code" do PRMake, dar ao usuário a opção de instalar no **Windows** ou no **macOS/Linux** —
   cada sistema tem os seus comandos.
2. O instalador deve cuidar das dependências (ex.: `unzip`, `jq`, `python3`): instalar o que faltar quando possível
   (Homebrew no macOS; apt/dnf/yum/pacman/zypper/apk no Linux; winget no Windows) ou dizer exatamente o comando
   para instalar.
3. As skills precisam funcionar no Claude Code nativo do Windows (Git Bash): sem `rsync`/`unzip` obrigatórios,
   `python3` e `jq` compatíveis, venv do Python no layout do Windows (`.venv/Scripts`).
