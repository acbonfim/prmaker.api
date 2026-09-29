#!/usr/bin/env python3
"""Converte Markdown -> HTML5 (subconjunto usado nos textos de RCA e comentarios).

Suporta: paragrafos separados por linha em branco, titulos (## / ###),
negrito **texto**, italico *texto*, codigo inline `code` (renderizado com
fundo cinza), listas nao ordenadas (linhas iniciando com "* ", "- " ou "+ ")
e regua horizontal (linha com 3+ "-", "*" ou "_"). Emite estilos inline para
que a discussion/RCA do Azure DevOps fique com bom espacamento e enfase.
Le de stdin (ou de um arquivo passado como 1o argumento) e escreve em stdout.
"""
import sys
import re
import html

# estilos inline (o sanitizador do DevOps preserva style em comentarios)
CODE_STYLE = "background:#eceef1;padding:1px 6px;border-radius:4px;font-family:Consolas,Menlo,monospace;font-size:0.95em"
P_STYLE = "margin:0 0 12px;line-height:1.5"
HR_STYLE = "border:none;border-top:1px solid #d0d7de;margin:18px 0"
UL_STYLE = "margin:0 0 12px 0;padding-left:22px;line-height:1.5"
H_STYLE = "margin:18px 0 8px;font-weight:600"


def inline(text: str) -> str:
    # escapa primeiro; as tags que geramos depois nao sao reescapadas
    text = html.escape(text, quote=False)
    # codigo inline `x` (fundo cinza)
    text = re.sub(r"`([^`]+)`", lambda m: f'<code style="{CODE_STYLE}">{m.group(1)}</code>', text)
    # negrito **x**
    text = re.sub(r"\*\*(.+?)\*\*", lambda m: f"<strong>{m.group(1)}</strong>", text)
    # italico *x* (evita casar com ** ja consumido)
    text = re.sub(r"(?<!\*)\*(?!\s)([^*]+?)\*(?!\*)", lambda m: f"<em>{m.group(1)}</em>", text)
    return text.strip()


def convert(md: str) -> str:
    md = md.replace("\r\n", "\n").replace("\r", "\n")
    lines = md.split("\n")
    out = []
    i = 0
    n = len(lines)
    bullet_re = re.compile(r"^\s*[-*+]\s+(.*)$")
    hr_re = re.compile(r"^\s*([-*_])\1{2,}\s*$")
    head_re = re.compile(r"^\s*(#{1,4})\s+(.*)$")
    while i < n:
        line = lines[i]
        if not line.strip():
            i += 1
            continue
        if hr_re.match(line):  # regua horizontal (divisor)
            out.append(f'<hr style="{HR_STYLE}">')
            i += 1
            continue
        h = head_re.match(line)
        if h:  # titulo
            level = min(len(h.group(1)), 4)
            out.append(f'<h{level} style="{H_STYLE}">{inline(h.group(2))}</h{level}>')
            i += 1
            continue
        m = bullet_re.match(line)
        if m:  # bloco de lista
            items = []
            while i < n and bullet_re.match(lines[i]) and not hr_re.match(lines[i]):
                items.append(bullet_re.match(lines[i]).group(1))
                i += 1
            out.append(f'<ul style="{UL_STYLE}">')
            out.extend(f"  <li>{inline(it)}</li>" for it in items)
            out.append("</ul>")
        else:  # paragrafo (junta linhas ate proxima linha em branco/lista/hr/titulo)
            para = []
            while (i < n and lines[i].strip() and not bullet_re.match(lines[i])
                   and not hr_re.match(lines[i]) and not head_re.match(lines[i])):
                para.append(lines[i].strip())
                i += 1
            out.append(f'<p style="{P_STYLE}">{inline(" ".join(para))}</p>')
    return "\n".join(out) + "\n"


def main() -> None:
    src = open(sys.argv[1], encoding="utf-8").read() if len(sys.argv) > 1 else sys.stdin.read()
    sys.stdout.write(convert(src))


if __name__ == "__main__":
    main()
