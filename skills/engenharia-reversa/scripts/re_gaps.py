#!/usr/bin/env python3
"""GAP do gerador (feature 0097) — lacuna que fala do processo de geração e não do sistema — sem LLM, sem rede.

"Endpoint X não está no pacote", "fora desta área", "sem Figma", "o subagente banco cuida"… não dizem nada do sistema e
poluíam o índice (e as análises). LISTA ÚNICA: o PRMake tem a mesma em `ReverseGeneratorGaps.cs` (domínio Reverse) — o
teste de paridade em tools/SkillTests compara as duas; mude as duas juntas.

  re_gaps.py remover <documento.md> [--publicado publicado.md]
      tira os GAPs do gerador do documento (o `juntar` roda sozinho). GAP que já estava publicado vira
      `### GAP-012 — (removido) lacuna do processo de geração` (análises antigas citam o ID); o resto sai.
  re_gaps.py avisar <documento.md>
      só lista (o `check` mostra como aviso).
"""
import os
import re
import sys
import unicodedata

# Trechos que, em qualquer GAP, dizem que a lacuna é do gerador (já normalizados: sem acento, minúsculas, um espaço).
PHRASES = [
    "fora do pacote",
    "nao esta no pacote",
    "pacote nao traz",
    "pacote nao inclui",
    "sem figma",
    "figma nao",
    "nao lido nesta area",
    "fora desta area",
    "orcamento da area",
    "subagente",
    "inventario nao",
    "retrato nao",
]
# Só contam quando o GAP não cita código (nenhuma evidência arquivo:linha, banco ou AWS).
PHRASES_WITHOUT_EVIDENCE = [
    "nao foi possivel ler",
]

R_GAP = re.compile(r"^(#{2,4})\s+\**`?GAP-(\d{1,4})\b\s*(?:[—–:\-]+\s*)?(.*)$")
R_HEADING = re.compile(r"^(#{1,6})\s")
R_EVIDENCE = re.compile(r"[\w./\\-]+\.(?:cs|cshtml|razor|asp|aspx|ascx|inc|js|ts|html|sql|json|ya?ml|xml|config|vb|py):\d+")
R_DB_AWS = re.compile(r"\*\*Onde:?\*\*:?[^\n]*?\b(?:banco|aws)\b|\*\*Banco:?\*\*", re.I)


def normalize(value):
    """Igual ao ReverseGeneratorGaps.Normalize: sem acento, minúsculas, o que não é [a-z0-9] vira UM espaço, sem pontas."""
    out, space = [], False
    for ch in unicodedata.normalize("NFD", value or ""):
        if unicodedata.category(ch) == "Mn":
            continue
        c = ch.lower()
        if len(c) == 1 and ("a" <= c <= "z" or "0" <= c <= "9"):
            out.append(c)
            space = False
        elif not space and out:
            out.append(" ")
            space = True
    return "".join(out).rstrip()


def cites_code(text):
    return bool(R_EVIDENCE.search(text or "") or R_DB_AWS.search(text or ""))


def matches(text, code=None):
    """O texto (título + corpo do GAP) fala do processo de geração?"""
    n = " " + normalize(text) + " "
    if any(p in n for p in PHRASES):
        return True
    if code is None:
        code = cites_code(text)
    return not code and any(p in n for p in PHRASES_WITHOUT_EVIDENCE)


def blocks(text):
    """[(início, fim, nível, número, título, texto)] dos GAPs (cabeçalho até o próximo de nível igual ou maior), fora de cercas."""
    lines = text.split("\n")
    out, cur, fence = [], None, False
    for i, line in enumerate(lines):
        if line.lstrip().startswith("```"):
            fence = not fence
        if fence:
            continue
        h = R_HEADING.match(line)
        if h and cur and len(h.group(1)) <= cur[2]:
            out.append((cur[0], i, cur[2], cur[3], cur[4], "\n".join(lines[cur[0]:i])))
            cur = None
        m = R_GAP.match(line)
        if m:
            if cur:
                out.append((cur[0], i, cur[2], cur[3], cur[4], "\n".join(lines[cur[0]:i])))
            cur = (i, None, len(m.group(1)), int(m.group(2)), m.group(3).strip())
    if cur:
        out.append((cur[0], len(lines), cur[2], cur[3], cur[4], "\n".join(lines[cur[0]:])))
    return out


def generator_gaps(text):
    """GAPs do gerador: [(ID, título, início, fim, nível)] (os já marcados "(removido)" ficam de fora)."""
    found = []
    for start, end, level, num, title, body in blocks(text):
        if title.lower().startswith("(removido)"):
            continue
        if matches(body):
            found.append((f"GAP-{num:03d}", title, start, end, level))
    return found


def remove(path, published=None):
    text = open(path, encoding="utf-8").read()
    pub_ids = set()
    if published and os.path.exists(published):
        pub_ids = {f"GAP-{int(m.group(2)):03d}" for m in map(R_GAP.match, open(published, encoding="utf-8").read().split("\n")) if m}
    found = generator_gaps(text)
    if not found:
        return []
    lines = text.split("\n")
    for gid, title, start, end, level in sorted(found, key=lambda x: -x[2]):
        if gid in pub_ids:
            lines[start:end] = [f"{'#' * level} {gid} — (removido) lacuna do processo de geração, não do sistema", ""]
        else:
            lines[start:end] = []
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines))
    return found


def main(argv):
    if len(argv) < 3 or argv[1] not in ("remover", "avisar"):
        print(__doc__)
        return 0 if len(argv) < 2 or argv[1] in ("-h", "--help") else 2
    if argv[1] == "avisar":
        found = generator_gaps(open(argv[2], encoding="utf-8").read())
        if found:
            print(f"AVISO: {len(found)} GAP(s) do gerador (falam do pacote/área/subagente/Figma, não do sistema) — saem no juntar:")
            for gid, title, *_ in found[:20]:
                print(f"  - {gid} {title[:110]}")
        return 0
    published = argv[argv.index("--publicado") + 1] if "--publicado" in argv else None
    found = remove(argv[2], published)
    print(f"GAPs do gerador removidos: {len(found)}" + (" (" + ", ".join(g for g, *_ in found[:12]) + (" …" if len(found) > 12 else "") + ")" if found else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
