# Plano — Feature 0046

| Fase | O quê |
|---|---|
| S1 | `card-init.sh`/`prmake-plan.sh`: raiz padrão `~/.prmake/cards` (CARDS_DIR continua valendo); `migrate_card_dir` move o card de `~/.claude/cards` (ou copia o que falta, sem sobrescrever) |
| S2 | `prmake-skills.sh`: `ensure_cards_access` (marcador `cards-v1`) põe `additionalDirectories` + `Edit(~/.prmake/cards/**)` no `settings.json` em install/update/permissions; `doctor` mostra |
| S3 | SKILL.md + `correcao.md`: pasta do card = a do `contexto`; script/análise/chamado sempre nos arquivos do plano (`sync` ou `prmake_file`); nunca travar a etapa por gravação local |
| B1 | MCP `prmake_file(card, name, content, kind?, key?, description?)` → `UploadArtifactAsync` (tipo pelo nome, como o upload REST) |
| E1 | Executor 1.0.5: `--add-dir ~/.prmake/cards` (`Paths.CardsRoot`, CARDS_DIR) — sobe a versão para autoatualizar |
| T1 | Testes: Claude Code `dontAsk` grava em `~/.prmake/cards` (com `--add-dir` e com o settings do instalador) e não em `~/.claude/cards`; migração; MCP `prmake_file` na API local |
