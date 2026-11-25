#!/usr/bin/env python3
"""0076: texto de uma evidencia do card que o Read nao abre direto — Word (.docx), Excel (.xlsx), PowerPoint (.pptx)
e e-mail (.eml). So a biblioteca padrao (funciona no Windows/Git Bash). Imagem, PDF e texto: abra com o Read.

Uso: evidence-text.py <arquivo> [--max 60000]
"""
import argparse
import email
import email.policy
import re
import sys
import zipfile
import html

# Sem xml.etree: o pyexpat de alguns python (Homebrew 3.14) vem quebrado — o OOXML e lido com expressoes regulares.
def _text(xml_fragment, tag):
    return "".join(html.unescape(t) for t in re.findall(rf"<{tag}(?:\s[^>]*)?>(.*?)</{tag}>", xml_fragment, re.S))


def _attrs(tag_text):
    return dict(re.findall(r'([\w:]+)="([^"]*)"', tag_text))


def docx(path):
    with zipfile.ZipFile(path) as z:
        doc = z.read("word/document.xml").decode("utf-8", "replace")
    out = [_text(p, "w:t") for p in re.split(r"</w:p>", doc)]
    return "\n".join(t for t in out if t.strip())


def xlsx(path, max_rows=400):
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        shared = []
        if "xl/sharedStrings.xml" in names:
            sst = z.read("xl/sharedStrings.xml").decode("utf-8", "replace")
            shared = [_text(si, "t") for si in re.findall(r"<si>(.*?)</si>", sst, re.S)]
        wb = z.read("xl/workbook.xml").decode("utf-8", "replace")
        rels = z.read("xl/_rels/workbook.xml.rels").decode("utf-8", "replace")
        targets = {a.get("Id"): a.get("Target", "") for a in (_attrs(t) for t in re.findall(r"<Relationship\s[^>]*>", rels))}
        out = []
        for sheet in re.findall(r"<sheet\s[^>]*>", wb):
            a = _attrs(sheet)
            target = targets.get(a.get("r:id"), "").lstrip("/")
            target = target if target.startswith("xl/") else f"xl/{target}"
            out.append(f"=== planilha: {html.unescape(a.get('name', ''))}")
            if target not in names:
                continue
            data = z.read(target).decode("utf-8", "replace")
            for i, row in enumerate(re.findall(r"<row[^>]*>(.*?)</row>", data, re.S)):
                if i >= max_rows:
                    out.append(f"... (mais linhas — corte em {max_rows})")
                    break
                cells = []
                for attrs, body in re.findall(r"<c(\s[^>]*)?(?:/>|>(.*?)</c>)", row, re.S):
                    ca = _attrs(attrs or "")
                    v = re.search(r"<v>(.*?)</v>", body or "", re.S)
                    if ca.get("t") == "s" and v:
                        idx = int(v.group(1))
                        cells.append(shared[idx] if idx < len(shared) else "")
                    elif ca.get("t") == "inlineStr":
                        cells.append(_text(body or "", "t"))
                    else:
                        cells.append(html.unescape(v.group(1)) if v else "")
                if any(c.strip() for c in cells):
                    out.append("\t".join(cells))
        return "\n".join(out)


def pptx(path):
    with zipfile.ZipFile(path) as z:
        slides = sorted((n for n in z.namelist() if re.match(r"ppt/slides/slide\d+\.xml$", n)),
                        key=lambda n: int(re.search(r"(\d+)", n.rsplit("/", 1)[1]).group(1)))
        out = []
        for n in slides:
            xml = z.read(n).decode("utf-8", "replace")
            text = [html.unescape(t) for t in re.findall(r"<a:t>(.*?)</a:t>", xml, re.S) if t.strip()]
            out.append(f"=== {n.rsplit('/', 1)[1]}\n" + "\n".join(text))
        return "\n".join(out)


def eml(path):
    with open(path, "rb") as f:
        msg = email.message_from_binary_file(f, policy=email.policy.default)
    head = "\n".join(f"{h}: {msg[h]}" for h in ("From", "To", "Cc", "Date", "Subject") if msg[h])
    body = msg.get_body(preferencelist=("plain", "html"))
    text = body.get_content() if body is not None else ""
    if body is not None and body.get_content_type() == "text/html":
        text = re.sub(r"<[^>]+>", " ", text)
    attachments = [p.get_filename() for p in msg.iter_attachments() if p.get_filename()]
    extra = f"\n\n(anexos do e-mail: {', '.join(attachments)})" if attachments else ""
    return f"{head}\n\n{text.strip()}{extra}"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("file")
    p.add_argument("--max", type=int, default=60000)
    a = p.parse_args()
    ext = a.file.lower().rsplit(".", 1)[-1]
    handlers = {"docx": docx, "xlsx": xlsx, "pptx": pptx, "eml": eml}
    if ext not in handlers:
        print(f"ERRO: .{ext} nao e convertido aqui — imagem/PDF/texto: abra com o Read; video/compactado: abrir a mao.", file=sys.stderr)
        sys.exit(2)
    try:
        text = handlers[ext](a.file)
    except (zipfile.BadZipFile, KeyError, ValueError) as e:
        print(f"ERRO: nao consegui ler {a.file} ({e}) — o arquivo pode estar corrompido ou protegido.", file=sys.stderr)
        sys.exit(1)
    if len(text) > a.max:
        text = text[: a.max] + f"\n... (cortado em {a.max} caracteres)"
    print(text)


if __name__ == "__main__":
    main()
