#!/usr/bin/env python3
"""Confere se o root cause (RCA) segue o modelo do prompt de Bug configurado no PRMake.

Uso: rca_check.py <prompt.txt> <rca.md> [card]

O modelo nao fica fixo aqui: os titulos sao lidos do proprio prompt (bloco "Modelo do RCA", ex.:
"Main Cause: XXX  Contributing Factors: XXX ... Ticket: {cardNumber}"). Quando o prompt pede o texto em
ingles, o RCA nao pode estar em portugues. Saida 0 = ok (ou prompt sem modelo: nada a conferir);
saida 2 = fora do padrao, com o que falta no stderr. Cards 78262/78266: RCA de correcao de dados escrito
livre, em portugues, porque o fechamento sem codigo nao passava pelo prompt.
"""
import re
import sys

MODEL_START = re.compile(r"(modelo\s+(do|de)\s+rca|rca\s+(model|template))", re.I)
MODEL_END = re.compile(r"(regras\s+gerais|general\s+rules|<\s*/?\s*RCA\s*>)", re.I)
LABEL = re.compile(r"([A-Z][A-Za-z/ ()-]{1,80}?)\s*:\s*(?:X+(?:\s*,\s*X+)*|\{\w+\})")
# Palavras comuns do portugues que nao aparecem num texto em ingles.
PT_WORDS = {"não", "nao", "que", "com", "para", "foi", "uma", "pelo", "pela", "sem", "dos", "das", "está",
            "estava", "usuário", "usuario", "cadastro", "porque", "quando", "também", "tambem", "após",
            "apos", "então", "entao", "correção", "correcao", "ainda", "mas", "nos", "nas", "ser", "são"}


def model_labels(prompt: str) -> list[str]:
    start = MODEL_START.search(prompt)
    if not start:
        return []
    block = prompt[start.end():]
    end = MODEL_END.search(block)
    if end:
        block = block[:end.start()]
    labels = []
    for m in LABEL.finditer(block):
        label = " ".join(m.group(1).split())
        if label and label not in labels:
            labels.append(label)
    return labels


def has_title(rca: str, label: str) -> bool:
    # Titulo no inicio da linha: **Label:** / **Label**: / ## Label / Label:
    pat = r"(?im)^\s*(?:#+\s*)?(?:\*\*|__)?\s*" + re.escape(label) + r"\s*(?::\s*(?:\*\*|__)?|(?:\*\*|__)\s*:?)"
    return re.search(pat, rca) is not None


def portuguese_hits(rca: str) -> list[str]:
    text = re.sub(r"`[^`]*`", " ", rca).lower()
    return [w for w in re.findall(r"[a-zà-ú]+", text) if w in PT_WORDS]


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 1
    prompt = open(sys.argv[1], encoding="utf-8").read()
    rca = open(sys.argv[2], encoding="utf-8").read()
    card = sys.argv[3] if len(sys.argv) > 3 else ""

    labels = model_labels(prompt)
    if not labels:
        print("aviso: prompt de Bug sem 'Modelo do RCA' — formato do RCA nao conferido", file=sys.stderr)
        return 0

    problems = []
    missing = [l for l in labels if not has_title(rca, l)]
    if missing:
        problems.append("faltam os titulos do modelo: " + "; ".join(missing))
    if re.search(r"ingl[eê]s|english", prompt, re.I):
        hits = portuguese_hits(rca)
        if len(hits) >= 4:
            sample = ", ".join(sorted(set(hits))[:8])
            problems.append(f"o prompt pede o texto em ingles e o RCA esta em portugues ({sample})")
    if card and any("ticket" in l.lower() for l in labels) and card not in rca:
        problems.append(f"o titulo Ticket precisa trazer o numero do card ({card})")

    if problems:
        print("RCA fora do padrao do prompt de Bug configurado no PRMake:", file=sys.stderr)
        for p in problems:
            print(f"  - {p}", file=sys.stderr)
        print("Modelo (um titulo em negrito por secao, linha em branco entre elas): "
              + " | ".join(f"**{l}:**" for l in labels), file=sys.stderr)
        print("Sem codigo (correcao de dados/configuracao/orientacao) o modelo vale igual: diga na secao de arquivos "
              "que nao houve mudanca de codigo e quais dados foram alterados.", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
