#!/usr/bin/env python3
"""0075: monta a fotografia de um trecho de codigo para o PRMake (PUT ExecutionPlan/{plano}/code-refs/{ref}).

Le o arquivo na pasta do repositorio (a copia da master na analise; o worktree do card na correcao), recorta o foco
+ contexto, calcula a mudanca proposta (diff unificado) a partir do trecho novo e imprime o JSON do corpo.
O Claude nao cola codigo na conversa: so indica arquivo, linhas e (se houver) o trecho novo num arquivo.

Uso:
  coderef.py snapshot --dir <repo> --file <caminho> --lines A[-B] [--repo <nome>] [--change ...] [--title ...]
                      [--why ...] [--after <arquivo|->] [--notes <json>] [--context N] [--branch <nome>]
  coderef.py implemented --dir <repo> --file <caminho> --sha <commit>
"""
import argparse
import difflib
import json
import os
import subprocess
import sys

MAX_LINES = 600
MAX_CHARS = 64 * 1024

LANGUAGES = {
    ".cs": "csharp", ".cshtml": "razor", ".ts": "typescript", ".tsx": "typescript", ".js": "javascript",
    ".jsx": "javascript", ".mjs": "javascript", ".html": "html", ".htm": "html", ".asp": "asp", ".aspx": "asp",
    ".inc": "asp", ".css": "css", ".scss": "scss", ".sql": "sql", ".json": "json", ".java": "java", ".py": "python",
    ".sh": "bash", ".xml": "xml", ".config": "xml", ".vb": "vbnet", ".yml": "yaml", ".yaml": "yaml", ".md": "markdown",
}


def die(msg):
    print(f"ERRO: {msg}", file=sys.stderr)
    sys.exit(1)


def git(repo_dir, *args):
    try:
        return subprocess.run(["git", "-C", repo_dir, *args], capture_output=True, text=True, check=True).stdout.strip()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return ""


def read_lines(path):
    # ASP/legado costuma estar em latin-1/cp1252: le sem quebrar e devolve texto.
    with open(path, "rb") as f:
        data = f.read()
    for enc in ("utf-8-sig", "cp1252", "latin-1"):
        try:
            text = data.decode(enc)
            break
        except UnicodeDecodeError:
            continue
    return text.replace("\r\n", "\n").replace("\r", "\n").split("\n")


def read_after(arg):
    if arg is None:
        return None
    text = sys.stdin.read() if arg == "-" else open(arg, encoding="utf-8").read()
    text = text.replace("\r\n", "\n")
    if text.endswith("\n"):
        text = text[:-1]
    return [] if text == "" else text.split("\n")


def parse_lines(spec):
    try:
        a, _, b = spec.partition("-")
        start, end = int(a), int(b or a)
    except ValueError:
        die(f"--lines invalido: '{spec}' (use 239-289 ou 325)")
    if start < 1 or end < start:
        die(f"--lines invalido: '{spec}'")
    return start, end


def unified(old, new, rel):
    diff = difflib.unified_diff([l + "\n" for l in old], [l + "\n" for l in new], f"a/{rel}", f"b/{rel}", n=3)
    return "".join(diff)


def snapshot(args):
    rel = args.file.replace("\\", "/").lstrip("/")
    path = os.path.join(args.dir, rel)
    change = args.change or ("change" if args.after is not None else "read")
    after = read_after(args.after)
    if change == "remove" and after is None:
        after = []
    notes = json.load(open(args.notes, encoding="utf-8")) if args.notes else None

    body = {
        "repository": args.repo, "path": rel, "change": change, "title": args.title, "why": args.why,
        "language": LANGUAGES.get(os.path.splitext(rel)[1].lower()), "annotations": notes,
    }

    if change == "create":
        if after is None:
            die("--change create precisa do conteudo do arquivo novo em --after")
        body.update(startLine=1, endLine=max(1, len(after)), proposal=unified([], after, rel))
        return body

    if not os.path.isfile(path):
        die(f"arquivo nao encontrado: {path}")
    lines = read_lines(path)
    if lines and lines[-1] == "":
        lines = lines[:-1]
    start, end = parse_lines(args.lines)
    if end > len(lines):
        die(f"--lines {start}-{end} passa do fim do arquivo ({len(lines)} linhas)")

    ctx = max(0, args.context)
    focus = end - start + 1
    if focus > MAX_LINES:
        die(f"o foco tem {focus} linhas — cite um trecho menor (maximo {MAX_LINES} com o contexto)")
    ctx = min(ctx, (MAX_LINES - focus) // 2)
    first = max(1, start - ctx)
    last = min(len(lines), end + ctx)
    window = lines[first - 1:last]
    text = "\n".join(window)
    while len(text) > MAX_CHARS and (first < start or last > end):
        first, last = min(start, first + 10), max(end, last - 10)
        window = lines[first - 1:last]
        text = "\n".join(window)
    if len(text) > MAX_CHARS:
        die("o trecho citado passa de 64 KB — cite um trecho menor")

    sha = git(args.dir, "rev-parse", "HEAD")
    branch = args.branch or git(args.dir, "rev-parse", "--abbrev-ref", "HEAD")
    body.update(commitSha=sha or None, branch=None if branch in ("", "HEAD") else branch,
                startLine=start, endLine=end, firstLine=first, snapshot=text)
    if after is not None:
        new = lines[:start - 1] + after + lines[end:]
        body["proposal"] = unified(lines, new, rel) or None
        if body["proposal"] is None:
            die("o trecho novo (--after) e igual ao atual — nada a propor")
    return body


def implemented(args):
    rel = args.file.replace("\\", "/").lstrip("/")
    diff = git(args.dir, "show", "--format=", "--no-color", args.sha, "--", rel)
    if not diff:
        die(f"o commit {args.sha} nao muda {rel} (ou o commit nao existe em {args.dir})")
    full = git(args.dir, "rev-parse", args.sha) or args.sha
    return {"implemented": diff + "\n", "implementedSha": full}


def main():
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("snapshot")
    s.add_argument("--dir", required=True)
    s.add_argument("--file", required=True)
    s.add_argument("--lines", default="1")
    s.add_argument("--repo")
    s.add_argument("--change", choices=["change", "create", "remove", "suspect", "read"])
    s.add_argument("--title")
    s.add_argument("--why")
    s.add_argument("--after")
    s.add_argument("--notes")
    s.add_argument("--context", type=int, default=30)
    s.add_argument("--branch")
    i = sub.add_parser("implemented")
    i.add_argument("--dir", required=True)
    i.add_argument("--file", required=True)
    i.add_argument("--sha", required=True)
    args = p.parse_args()
    body = snapshot(args) if args.cmd == "snapshot" else implemented(args)
    json.dump({k: v for k, v in body.items() if v is not None}, sys.stdout, ensure_ascii=False)


if __name__ == "__main__":
    main()
