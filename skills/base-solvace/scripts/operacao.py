#!/usr/bin/env python3
"""Catálogo de operação da Solvace (feature 0040) — só leitura, só biblioteca padrão.

O menu do produto é o mapa das telas de configuração de cada módulo: TB_WCM_MENU (tela, rota, grupo, só Solvace
admin), TB_WCM_MENU_ROLE (papéis que veem o item), TB_WCM_ROLE (papéis do módulo, com descrição) e
TB_WCM_SITE_PARAMETER / TB_WCM_GLOBAL_PARAMETER (parâmetros com descrição). Este script lê esse catálogo de um banco
Global de referência (sql-query.sh, somente leitura, NUNCA os valores dos parâmetros) e escreve a seção técnica
"Configuração e operação" de cada módulo da base, no formato do arch.sh publicar-pasta.

Uso:
  operacao.py catalogo --host <alias> --db <DB_..._GLOBAL> --out catalogo.json
  operacao.py secoes <catalogo.json> <pasta-saida> [--projetos projetos.json] [--fonte "sandbox EFESO (TST)"]
      projetos.json = GET Architecture/projects (para casar aplicação → projeto); sem ele, usa o mapa padrão.
      Gera <pasta-saida>/<projeto>/085-operacao.md e, no operacao-plataforma, 090-catalogo-modulos.md e
      095-parametros.md. Depois: arch.sh section ... (ou publicar-pasta com projeto.json).
"""
import json
import os
import re
import subprocess
import sys
from collections import defaultdict

SQL = os.path.expanduser("~/.claude/skills/analisar-bug/scripts/sql-query.sh")
PLATFORM = "operacao-plataforma"

# Alias da aplicação (TB_SYS_Application.ApplicationAlias) → projeto da base. Versões "- Novo" (revamp) e legadas do
# mesmo módulo caem no mesmo projeto (seção com as duas versões).
ALIAS_TO_PROJECT = {
    "ACP": "revamp-actionplan", "PLA": "revamp-actionplan", "BOS": "revamp-bos", "SOC": "revamp-bos", "LPP": "revamp-lpp",
    "UNC": "revamp-unsafecondition", "DFT": "revamp-defecttag", "CHK": "revamp-checklist", "CLN": "revamp-centerline",
    "LIL": "revamp-cil", "MLH": "revamp-kaizen", "TRN": "revamp-training", "MST": "revamp-masterdata", "GMT": "revamp-masterdata",
    "GED": "revamp-documentation", "MOC": "revamp-moc", "RCA": "revamp-rca", "RNC": "revamp-nonconformity", "INC": "revamp-incident",
    "ALR": "revamp-alert", "WRK": "revamp-workpermit", "PJT": "revamp-project", "AST": "revamp-assessment", "REC": "revamp-complaint",
    "DOB": "revamp-digitalobeya", "USR": "revamp-users", "SYS": "revamp-users", "TEA": "revamp-users", "times": "revamp-users",
    "KPI": "revamp-scorecard", "SCC": "revamp-scorecard", "SCG": "revamp-scorecard", "SRV": "revamp-survey",
    "COM": "revamp-communication", "ADM": "revamp-administration", "QUZ": "revamp-quiz", "PRS": "revamp-praise",
    "MLG": "revamp-multilingual", "GAM": "revamp-gamification", "PST": "revamp-post", "CAF": "revamp-training",
}


def query(host, db, sql, max_rows=10000):
    out = subprocess.run(["bash", SQL, "--host", host, "-d", db, "--json", "--max-rows", str(max_rows), "-q", sql],
                         capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f"ERRO na consulta ({out.returncode}): {out.stderr.strip() or out.stdout.strip()}")
    return json.loads(out.stdout)["rows"]


def catalogo(host, db, out):
    apps = query(host, db, "SELECT CAST(idApplication AS varchar(36)) id, ApplicationName name, ApplicationAlias alias, Description description,"
                           " SubUrlPath subUrl, IsMarketable marketable, IsCorporate corporate FROM TB_SYS_Application")
    menus = query(host, db, "SELECT CAST(m.MENU_ID AS varchar(36)) id, CAST(m.APPLICATION_ID AS varchar(36)) app, g.MENU_GROUP_NAME grp,"
                            " m.MENU_DESC item, m.MENU_ORDER ord, m.TARGET_PATH path, m.TARGET_PATH_NEW pathNew, m.ONLY_SOLVACE_ADMIN onlySolvaceAdmin"
                            " FROM TB_WCM_MENU m LEFT JOIN TB_WCM_MENU_GROUP g ON g.MENU_GROUP_ID = m.MENU_GROUP_ID WHERE m.ACTIVE = 1")
    menu_roles = query(host, db, "SELECT CAST(mr.MENU_ID AS varchar(36)) menu, r.ROLE_NAME role FROM TB_WCM_MENU_ROLE mr JOIN TB_WCM_ROLE r ON r.ROLE_ID = mr.ROLE_ID")
    menu_deny = query(host, db, "SELECT CAST(md.MENU_ID AS varchar(36)) menu, r.ROLE_NAME role FROM TB_WCM_MENU_ROLE_DEAUTHORIZE md JOIN TB_WCM_ROLE r ON r.ROLE_ID = md.ROLE_ID")
    roles = query(host, db, "SELECT ROLE_NAME name, ROLE_DESCRIPTION description, CAST(APPLICATION_ID AS varchar(36)) app, IS_INTERNAL internal FROM TB_WCM_ROLE")
    site_params = query(host, db, "SELECT PARAM_KEY [key], DESCRIPTION description, IS_CONF_ADM confAdm, CAST(APPLICATION_ID AS varchar(36)) app FROM TB_WCM_SITE_PARAMETER")
    # Só chave e descrição — nunca PARAM_VALUE (pode ter segredo).
    global_params = query(host, db, "SELECT PARAM_KEY [key], DESCRIPTION description, IS_CONF_ADM confAdm FROM TB_WCM_GLOBAL_PARAMETER")
    roles_by_menu, deny_by_menu = defaultdict(list), defaultdict(list)
    for r in menu_roles: roles_by_menu[r["menu"].lower()].append(r["role"])
    for r in menu_deny: deny_by_menu[r["menu"].lower()].append(r["role"])
    by_app = defaultdict(lambda: {"menus": [], "roles": [], "siteParams": []})
    for m in menus:
        m["roles"] = sorted(set(roles_by_menu.get(m["id"].lower(), [])))
        m["deniedRoles"] = sorted(set(deny_by_menu.get(m["id"].lower(), [])))
        by_app[(m["app"] or "").lower()]["menus"].append(m)
    for r in roles: by_app[(r["app"] or "").lower()]["roles"].append(r)
    for p in site_params: by_app[(p["app"] or "").lower()]["siteParams"].append(p)
    for a in apps:
        a.update(by_app.get(a["id"].lower(), {"menus": [], "roles": [], "siteParams": []}))
    data = {"source": {"host": host, "db": db}, "apps": apps, "globalParams": global_params,
            "siteParamsWithoutApp": by_app.get("", {}).get("siteParams", [])}
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print(f"catálogo: {len(apps)} aplicações, {len(menus)} itens de menu, {len(roles)} papéis, {len(site_params)} parâmetros de planta,"
          f" {len(global_params)} globais → {out}")


def where(m):
    """Rota legível: @angular:/x@app:y → tela Angular (app y) /x; senão o caminho legado."""
    new = (m.get("pathNew") or "").strip()
    mm = re.match(r"@angular:(?P<route>[^@]*)@app:(?P<app>[^@]*)", new)
    if mm:
        return f"Angular `{mm['app']}` `{mm['route'] or '/'}`"
    if new:
        return f"`{new[:90]}`"
    old = (m.get("path") or "").strip()
    return f"legado `{old[:90]}`" if old else "—"


def version(app):
    name = app["name"] or ""
    if "terminated" in name.lower():
        return "descontinuada"
    return "nova (revamp)" if re.search(r"-\s*novo|novo\s*$", name, re.I) else "legada"


def md_app(app):
    lines = [f"### {app['name']} — versão {version(app)} (alias `{app['alias']}`, id `{app['id']}`)"]
    flags = []
    if app.get("marketable"): flags.append("aparece em Administração → Módulos (pode ser habilitado por planta)")
    else: flags.append("não aparece em Administração → Módulos (IsMarketable = 0)")
    if app.get("corporate"): flags.append("corporativo: só Solvace admin vê")
    lines.append("- " + "; ".join(flags) + ".")
    if app["menus"]:
        lines.append("")
        lines.append("**Telas do módulo (menu lateral)** — grupo › item: onde abre · quem vê")
        by_group = defaultdict(list)
        for m in sorted(app["menus"], key=lambda x: ((x.get("grp") or "~"), x.get("ord") or 0)):
            by_group[m.get("grp") or "Sem grupo"].append(m)
        for grp, items in by_group.items():
            for m in items:
                who = []
                if m.get("onlySolvaceAdmin"): who.append("só Solvace admin")
                if m["roles"]: who.append("só com o papel " + ", ".join(f"*{r}*" for r in m["roles"]))
                if m["deniedRoles"]: who.append("escondido para " + ", ".join(f"*{r}*" for r in m["deniedRoles"]))
                lines.append(f"- {grp} › **{m.get('item') or '?'}**: {where(m)} · {'; '.join(who) if who else 'todos com acesso ao módulo'}")
    if app["roles"]:
        lines.append("")
        lines.append("**Papéis do módulo** (atribuídos por planta em `TB_WCM_USER_ROLE`):")
        for r in sorted(app["roles"], key=lambda x: x["name"] or ""):
            desc = (r.get("description") or "").strip()
            lines.append(f"- *{r['name']}*{' (interno)' if r.get('internal') else ''}{': ' + desc if desc and desc != r['name'] else ''}")
    if app["siteParams"]:
        lines.append("")
        lines.append("**Parâmetros por planta** (`TB_WCM_SITE_PARAMETER`; valor por planta em `TB_WCM_SITE_PARAMETER_VALUE`):")
        for p in sorted(app["siteParams"], key=lambda x: x["key"]):
            lines.append(f"- `{p['key']}`: {(p.get('description') or 'sem descrição').strip()}"
                         + (" — aparece na configuração do admin" if p.get("confAdm") else ""))
    return "\n".join(lines)


HEADER = """Gerada do catálogo do produto ({fonte}): menus, papéis e parâmetros cadastrados no banco Global. Os nomes de
tela são os do menu desse ambiente (podem variar de idioma por cliente). Como habilitar o módulo na planta, perfis
(Solvace admin × admin do cliente × admin do módulo), parâmetros e diagnóstico "não aparece" valem para todos os
módulos: ver o projeto `operacao-plataforma`.
"""


def secoes(cat_path, out, projetos=None, fonte="banco de referência"):
    cat = json.load(open(cat_path, encoding="utf-8"))
    known = None
    if projetos:
        known = {p["key"] for p in json.load(open(projetos, encoding="utf-8"))}
    groups, unmapped = defaultdict(list), []
    for app in cat["apps"]:
        if not (app["menus"] or app["roles"] or app["siteParams"]):
            continue
        key = ALIAS_TO_PROJECT.get(app.get("alias") or "")
        if key and (known is None or key in known):
            groups[key].append(app)
        else:
            unmapped.append(app)
    order = {"nova (revamp)": 0, "legada": 1, "descontinuada": 2}
    for key, apps in groups.items():
        apps.sort(key=lambda a: (order[version(a)], a["name"]))
        body = ["## Configuração e operação", "", HEADER.format(fonte=fonte).strip(), ""]
        body += [md_app(a) + "\n" for a in apps]
        os.makedirs(os.path.join(out, key), exist_ok=True)
        with open(os.path.join(out, key, "085-operacao.md"), "w", encoding="utf-8") as f:
            f.write("\n".join(body).rstrip() + "\n")
    # Catálogo completo e parâmetros globais no projeto transversal.
    os.makedirs(os.path.join(out, PLATFORM), exist_ok=True)
    cat_lines = ["## Catálogo de módulos (aplicações) e onde configurar", "", HEADER.format(fonte=fonte).strip(), "",
                 "| Módulo | Alias | Versão | Habilitável por planta | Projeto na base |", "|---|---|---|---|---|"]
    for app in sorted(cat["apps"], key=lambda a: (a["name"] or "")):
        key = ALIAS_TO_PROJECT.get(app.get("alias") or "")
        mapped = key if key and (known is None or key in known) else "—"
        cat_lines.append(f"| {app['name']} | `{app.get('alias') or '—'}` | {version(app)} | {'sim' if app.get('marketable') else 'não'} | {mapped} |")
    if unmapped:
        cat_lines += ["", "## Módulos sem projeto na base — telas, papéis e parâmetros", ""]
        cat_lines += [md_app(a) + "\n" for a in sorted(unmapped, key=lambda a: a["name"] or "")]
    with open(os.path.join(out, PLATFORM, "090-catalogo-modulos.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(cat_lines).rstrip() + "\n")
    par = ["## Parâmetros globais (do cliente) e de planta", "",
           "Globais (`TB_WCM_GLOBAL_PARAMETER`, valem para o cliente inteiro); os marcados *configuração do admin* "
           "(`IS_CONF_ADM = 1`) aparecem em Administração → Funcionalidades do cliente. Os de planta "
           "(`TB_WCM_SITE_PARAMETER`) estão na seção de operação de cada módulo; abaixo, os de planta sem módulo.", "",
           "### Globais"]
    for p in sorted(cat["globalParams"], key=lambda x: x["key"]):
        par.append(f"- `{p['key']}`: {(p.get('description') or 'sem descrição').strip()}{' — *configuração do admin*' if p.get('confAdm') else ''}")
    if cat.get("siteParamsWithoutApp"):
        par += ["", "### De planta sem módulo"]
        for p in sorted(cat["siteParamsWithoutApp"], key=lambda x: x["key"]):
            par.append(f"- `{p['key']}`: {(p.get('description') or 'sem descrição').strip()}")
    with open(os.path.join(out, PLATFORM, "095-parametros.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(par).rstrip() + "\n")
    print(f"seções: {len(groups)} módulos com 085-operacao.md, {len(unmapped)} aplicações sem projeto no catálogo → {out}")


def opt(args, name, default=None):
    return args[args.index(name) + 1] if name in args and args.index(name) + 1 < len(args) else default


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in ("catalogo", "secoes"):
        print(__doc__); sys.exit(1)
    args = sys.argv[2:]
    if sys.argv[1] == "catalogo":
        catalogo(opt(args, "--host") or sys.exit("--host"), opt(args, "--db") or sys.exit("--db"), opt(args, "--out", "catalogo.json"))
    else:
        if len(args) < 2: print(__doc__); sys.exit(1)
        secoes(args[0], args[1], opt(args, "--projetos"), opt(args, "--fonte", "banco de referência"))


if __name__ == "__main__":
    main()
