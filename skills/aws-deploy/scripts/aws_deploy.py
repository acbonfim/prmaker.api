#!/usr/bin/env python3
"""
aws_deploy.py — esteiras de deploy do card por ambiente (feature 0100), SOMENTE LEITURA.

A API do PRMake roda fora da AWS (Cloud Run) e não tem credencial: quem coleta é a máquina, com a CLI da AWS da
pessoa e o git local. O retrato vai para o PRMake (aba AWS da Bancada, MCP prmake_aws_status).

  collect <card> [--request <id>] [--dry-run] [--out <arquivo>] [--skip-index] [--no-logs]
      1. índice de pipelines (compartilhado no PRMake) em dia: list-pipelines → get-pipeline só das novas/alteradas,
         removidas saem (pipes novas são criadas o tempo todo — a lista nunca é fixa);
      2. ambientes da configuração (AwsDeployEnvironments) → tier e Elastic Beanstalk do host pelo CloudFront;
      3. pipelines dos repositórios do card que o código do card afeta (filePaths do gatilho, monorepo);
      4. "nosso código" pelo git local: commits conhecidos (entregas, PRs) + commits que citam o card;
         na branch da pipeline? no commit implantado (último Succeeded da pipeline ou da ação de deploy)?
      5. logs (grupos do módulo no ambiente + erros das últimas 24 h, fim do log do build que falhou), lambdas e
         alarmes do módulo, CloudFront do host; o que a conta nega vai em "denied".
  index [--full] [--seed-dir <pasta>]   só o índice (--seed-dir: definições já baixadas, saída do get-pipeline)
  show <card>                            imprime o retrato gravado no PRMake

Nunca chama nada que grave nem que leia segredo (lista branca de verbos em Aws.call).
"""
import argparse
import concurrent.futures as cf
import datetime as dt
import fnmatch
import glob
import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("PRMAKE_API_BASE", "https://api.softhouse.app.br/api/v1").rstrip("/")
HOME = os.path.expanduser("~")
CACHE = os.path.join(HOME, ".prmake", "aws")
REPOS_SH = os.path.join(HOME, ".claude", "skills", ".prmake", "prmake-skills.sh")
ACCOUNT = os.environ.get("PRMAKE_AWS_ACCOUNT", "367983645102")
REGION = os.environ.get("AWS_REGION") or os.environ.get("AWS_DEFAULT_REGION") or "us-east-1"
TIERS = ("dev", "qa", "edge", "rc", "rt", "prod", "rv")
STALE_DAYS = 120
TIMEOUT = 120
MAX_PIPELINE_DTO = 16 * 1024
DENIED = re.compile(r"AccessDenied|UnauthorizedOperation|not authorized|AuthorizationError|UnrecognizedClientException|"
                    r"InvalidClientTokenId|ExpiredToken", re.I)
SECRET_LINE = re.compile(r"(?i)((?:pass(?:word)?|secret|token|api[_-]?key|access[_-]?key|private[_-]?key|pwd|"
                         r"connectionstring)\s*[:=]\s*)(\S+)")
VERDICT_ORDER = ["failed", "deploying", "waiting", "absent", "deployed", "broken"]
APP_PREFIX = re.compile(r"^(edv-solvace-|revamp-api-|revamp-|api-)", re.I)


def log(msg):
    print(msg, file=sys.stderr, flush=True)


def now_utc():
    return dt.datetime.now(dt.timezone.utc)


def iso(v):
    """Data da AWS (com fuso) → ISO em UTC; vazio se não der."""
    if not v:
        return None
    try:
        d = dt.datetime.fromisoformat(str(v).replace("Z", "+00:00"))
        if d.tzinfo is None:
            d = d.replace(tzinfo=dt.timezone.utc)
        return d.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    except ValueError:
        return str(v)


def parse_iso(v):
    try:
        return dt.datetime.fromisoformat(str(v).replace("Z", "+00:00"))
    except (TypeError, ValueError):
        return None


def mask(text):
    return SECRET_LINE.sub(lambda m: m.group(1) + "***", text or "")


def norm(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


# ── PRMake ────────────────────────────────────────────────────────────────────────────────────────────────────────
def token():
    t = os.environ.get("PRMAKE_TOKEN", "").strip()
    if t:
        return t
    p = os.path.join(HOME, ".claude", "prmake-token.txt")
    if os.path.isfile(p):
        return open(p, encoding="utf-8").read().strip()
    sys.exit("ERRO: sem token do PRMake (PRMAKE_TOKEN ou ~/.claude/prmake-token.txt)")


def api(method, path, body=None, ok404=False):
    data = json.dumps(body).encode() if body is not None else None
    for attempt in range(4):
        req = urllib.request.Request(BASE + "/" + path.lstrip("/"), data=data, method=method)
        req.add_header("x-api-key", token())
        req.add_header("X-Execution-Client", "skill")
        if data is not None:
            req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                raw = r.read()
                return json.loads(raw) if raw.strip() else None
        except urllib.error.HTTPError as e:
            if e.code == 404 and ok404:
                return None
            if e.code in (409, 429, 500, 502, 503, 504) and attempt < 3:
                time.sleep(2 * (attempt + 1))
                continue
            detail = e.read().decode(errors="replace")[:400]
            raise RuntimeError(f"PRMake {method} {path}: HTTP {e.code} {detail}")
        except urllib.error.URLError as e:
            if attempt < 3:
                time.sleep(2 * (attempt + 1))
                continue
            raise RuntimeError(f"PRMake {method} {path}: {e.reason}")


# ── AWS (só leitura) ──────────────────────────────────────────────────────────────────────────────────────────────
class Aws:
    SAFE = re.compile(r"^(list|describe|get|batch-get|filter-log-events)", re.I)
    FORBIDDEN = re.compile(r"get-secret-value|get-parameters?(-by-path)?$|get-object$|get-authorization-token|decrypt|"
                           r"get-session-token|get-credentials|generate-", re.I)

    def __init__(self, profile, region):
        self.profile, self.region = profile, region
        self.denied = set()
        self.failed = []

    def call(self, service, command, *args, regional=True):
        if not self.SAFE.match(command) or self.FORBIDDEN.search(command):
            raise RuntimeError(f"comando fora da lista de leitura: {service} {command}")
        cmd = ["aws", service, command, *args, "--output", "json"]
        if self.profile:
            cmd += ["--profile", self.profile]
        if regional:
            cmd += ["--region", self.region]
        try:
            p = subprocess.run(cmd, capture_output=True, text=True, timeout=TIMEOUT)
        except subprocess.TimeoutExpired:
            self.failed.append(f"{service} {command}: tempo esgotado")
            return None
        except FileNotFoundError:
            sys.exit("ERRO: CLI da AWS (aws) não encontrada nesta máquina")
        if p.returncode != 0:
            err = (p.stderr or "").strip()
            if DENIED.search(err):
                self.denied.add(service)
            elif not re.search(r"NotFound|does not exist|ResourceNotFound", err):
                self.failed.append(f"{service} {command}: {(err.splitlines() or [''])[-1][:200]}")
            return None
        try:
            return json.loads(p.stdout) if p.stdout.strip() else {}
        except json.JSONDecodeError:
            return None


def pick_profile():
    """AWS_PROFILE > perfil cuja conta é a da Solvace > padrão."""
    if os.environ.get("AWS_PROFILE"):
        return os.environ["AWS_PROFILE"]
    try:
        out = subprocess.run(["aws", "configure", "list-profiles"], capture_output=True, text=True, timeout=30).stdout
    except (FileNotFoundError, subprocess.TimeoutExpired):
        sys.exit("ERRO: CLI da AWS (aws) não encontrada nesta máquina — instale e configure (aws configure)")
    profiles = [p.strip() for p in out.splitlines() if p.strip()]
    profiles.sort(key=lambda p: p != "default")
    for p in profiles:
        r = subprocess.run(["aws", "sts", "get-caller-identity", "--profile", p, "--output", "json"],
                           capture_output=True, text=True, timeout=30)
        if r.returncode == 0 and json.loads(r.stdout).get("Account") == ACCOUNT:
            return p
    sys.exit(f"ERRO: nenhum perfil da CLI da AWS com acesso à conta {ACCOUNT} (aws configure list-profiles)")


def pmap(fn, items, workers=12):
    with cf.ThreadPoolExecutor(max_workers=workers) as ex:
        return list(ex.map(fn, items))


# ── índice de pipelines ───────────────────────────────────────────────────────────────────────────────────────────
def pipeline_dto(doc):
    """Saída do get-pipeline → linha do índice (AwsPipelineDto)."""
    p, meta = doc.get("pipeline") or {}, doc.get("metadata") or {}
    repos, deploy, stages, builds = [], [], [], []
    for st in p.get("stages", []):
        stages.append(st.get("name"))
        for a in st.get("actions", []):
            prov = (a.get("actionTypeId") or {}).get("provider", "")
            c = a.get("configuration") or {}
            if (a.get("actionTypeId") or {}).get("category") == "Source" or prov in ("CodeStarSourceConnection", "GitHub", "CodeCommit"):
                repo = c.get("FullRepositoryId") or (f"{c.get('Owner')}/{c.get('Repo')}" if c.get("Repo") else c.get("RepositoryName"))
                if repo:
                    repos.append({"repository": repo, "branch": c.get("BranchName") or c.get("Branch"), "action": a.get("name")})
                continue
            if prov == "CodeBuild":
                builds.append(c.get("ProjectName"))
            target = {"ElasticBeanstalk": c.get("EnvironmentName"), "CodeDeploy": c.get("DeploymentGroupName"),
                      "CodeDeployToECS": c.get("ApplicationName"), "ECS": c.get("ServiceName"), "S3": c.get("BucketName"),
                      "CloudFormation": c.get("StackName"), "Lambda": c.get("FunctionName")}.get(prov)
            if target:
                deploy.append({"action": a.get("name"), "stage": st.get("name"), "provider": prov, "target": target})
    inc, exc = [], []
    main = repos[0]["action"] if repos else None
    for t in p.get("triggers") or []:
        g = t.get("gitConfiguration") or {}
        if main and g.get("sourceActionName") not in (None, main):
            continue
        for push in g.get("push") or []:
            fp = push.get("filePaths") or {}
            inc += fp.get("includes") or []
            exc += fp.get("excludes") or []
    name = p.get("name") or ""
    suffix = name.rsplit("-", 1)[-1].lower() if "-" in name else ""
    dto = {"name": name, "updated": iso(meta.get("updated")), "type": p.get("pipelineType") or "V1",
           "repositories": repos, "filePathsInclude": sorted(set(inc)), "filePathsExclude": sorted(set(exc)),
           "tier": suffix if suffix in TIERS else None, "codebuildEnv": None, "deployTargets": deploy,
           "stages": stages, "_builds": [b for b in builds if b]}
    return dto


def fill_codebuild_tiers(aws, dtos):
    """Pipeline sem tier no nome: variável ENV/STAGE do CodeBuild (batch-get-projects, 100 por chamada)."""
    want = sorted({b for d in dtos if not d["tier"] for b in d["_builds"][:2]})
    env_of = {}
    for i in range(0, len(want), 100):
        out = aws.call("codebuild", "batch-get-projects", "--names", *want[i:i + 100]) or {}
        for pr in out.get("projects", []):
            for v in (pr.get("environment") or {}).get("environmentVariables") or []:
                if v.get("name", "").upper() in ("ENV", "STAGE", "ENVIRONMENT") and v.get("type", "PLAINTEXT") == "PLAINTEXT":
                    val = (v.get("value") or "").lower()
                    if val in TIERS:
                        env_of[pr["name"]] = val
    for d in dtos:
        if not d["tier"]:
            for b in d["_builds"]:
                if b in env_of:
                    d["codebuildEnv"] = env_of[b]
                    break


def clean_dto(d):
    d = {k: v for k, v in d.items() if not k.startswith("_")}
    while len(json.dumps(d)) > MAX_PIPELINE_DTO and (d["filePathsInclude"] or d["deployTargets"]):
        if len(d["deployTargets"]) > 20:
            d["deployTargets"] = d["deployTargets"][:20]
        elif d["filePathsInclude"]:
            d["filePathsInclude"] = d["filePathsInclude"][:-1]
        else:
            d["deployTargets"] = d["deployTargets"][:-1]
    return d


def refresh_index(aws, full=False, seed_dir=None):
    """Índice do PRMake em dia com a conta. Devolve {total, upserted, removed, updatedAt}."""
    t0 = time.time()
    listed = aws.call("codepipeline", "list-pipelines")
    if listed is None:
        raise RuntimeError("não deu para listar as pipelines (codepipeline list-pipelines)")
    current = {p["name"]: iso(p.get("updated")) for p in listed.get("pipelines", [])}
    idx = api("GET", "AwsDeploy/pipelines/index") or {}
    known = {i["name"]: iso(i.get("updated")) for i in idx.get("items", [])}
    if full:
        known_upd = {}
    else:
        known_upd = known
    todo = [n for n, u in current.items() if known_upd.get(n) != u]
    removed = [n for n in known if n not in current]
    log(f"índice: {len(current)} pipelines na conta, {len(todo)} novas/alteradas, {len(removed)} removidas")
    docs = []
    seeded = 0
    if seed_dir:
        for n in list(todo):
            f = os.path.join(seed_dir, n + ".json")
            if os.path.isfile(f):
                try:
                    doc = json.load(open(f, encoding="utf-8"))
                    if iso((doc.get("metadata") or {}).get("updated")) == current[n]:
                        docs.append(doc)
                        todo.remove(n)
                        seeded += 1
                except (ValueError, OSError):
                    pass
    done = [0]

    def get(n):
        d = aws.call("codepipeline", "get-pipeline", "--name", n)
        done[0] += 1
        if done[0] % 100 == 0:
            log(f"  … {done[0]}/{len(todo)} definições")
        return d

    docs += [d for d in pmap(get, todo, workers=16) if d]
    dtos = [pipeline_dto(d) for d in docs]
    fill_codebuild_tiers(aws, dtos)
    dtos = [clean_dto(d) for d in dtos]
    res = {"total": len(current), "upserted": 0, "removed": 0, "updatedAt": iso(now_utc())}
    batches = [dtos[i:i + 300] for i in range(0, len(dtos), 300)] or [[]]
    for i, b in enumerate(batches):
        # "full" no servidor apagaria o que não veio neste lote: as removidas já vão explícitas
        body = {"account": ACCOUNT, "region": aws.region, "upserts": b, "removed": removed if i == 0 else [], "full": False}
        if not b and not removed:
            break
        out = api("POST", "AwsDeploy/pipelines/index", body) or {}
        res["upserted"] += out.get("upserted", len(b))
        res["removed"] += out.get("removed", len(removed) if i == 0 else 0)
    log(f"índice em dia em {time.time() - t0:.0f} s (seed local: {seeded})")
    return res


# ── ambientes: CloudFront → tier e Elastic Beanstalk ──────────────────────────────────────────────────────────────
def resolve_hosts(aws, envs):
    out = aws.call("cloudfront", "list-distributions", regional=False)
    dists = ((out or {}).get("DistributionList") or {}).get("Items") or []
    by_alias = {}
    for d in dists:
        for a in (d.get("Aliases") or {}).get("Items") or []:
            by_alias[a.lower()] = d
    for e in envs:
        d = by_alias.get((e.get("host") or "").lower())
        e["_eb"], e["_cf"] = set(), None
        if not d:
            continue
        counts = {}
        for o in (d.get("Origins") or {}).get("Items") or []:
            dom = (o.get("DomainName") or "").lower()
            m = re.match(r"^[a-z0-9-]+-(" + "|".join(TIERS) + r")\.solvacelabs\.s3", dom)
            if m:
                counts[m.group(1)] = counts.get(m.group(1), 0) + 1
            if "elasticbeanstalk.com" in dom:
                e["_eb"].add(dom.split(".")[0])
        if not e.get("tier") and counts:
            e["tier"] = max(counts, key=counts.get)
        e["_cf"] = {"id": d.get("Id"), "aliases": (d.get("Aliases") or {}).get("Items") or [],
                    "origins": len((d.get("Origins") or {}).get("Items") or [])}
    return envs


def env_targets(env, p):
    """Ações de deploy da pipeline que levam a este ambiente: [(ação|None)] ou [] se a pipeline não serve o ambiente.
    Pipeline de branch de trabalho (sre/62325, perf/…, feature/…) não é esteira de ambiente."""
    if p.get("repositories") and "/" in (p["repositories"][0].get("branch") or ""):
        return []
    eb = [t for t in p.get("deployTargets") or [] if t.get("provider") == "ElasticBeanstalk"]
    if eb:
        return [t for t in eb if (t.get("target") or "").lower() in env["_eb"]]
    tier = p.get("tier") or p.get("codebuildEnv")
    return [None] if tier and tier == env.get("tier") else []


# ── git: o nosso código ───────────────────────────────────────────────────────────────────────────────────────────
class Repo:
    def __init__(self, name, path, shas, markers, card):
        self.name, self.path, self.card = name, path, card
        self.markers = markers
        self.shas = [s for s in shas if s and self.has(s)]
        self.fetched = set()
        self._contains = {}
        self.regex = r"(^|[^0-9])" + str(card) + r"([^0-9]|$)"
        self.since = None
        self.commits = []

    def git(self, *args, timeout=90):
        try:
            p = subprocess.run(["git", "-C", self.path, *args], capture_output=True, text=True, timeout=timeout,
                               env={**os.environ, "GIT_TERMINAL_PROMPT": "0"})
            return p.returncode, p.stdout
        except subprocess.TimeoutExpired:
            return 124, ""

    def has(self, sha):
        return self.git("cat-file", "-e", sha + "^{commit}")[0] == 0

    def fetch(self, branch):
        if branch in self.fetched:
            return
        self.fetched.add(branch)
        self.git("fetch", "--quiet", "--no-tags", "origin", f"+refs/heads/{branch}:refs/remotes/origin/{branch}", timeout=180)

    def ensure(self, sha):
        if sha and not self.has(sha):
            self.git("fetch", "--quiet", "--no-tags", "origin", sha, timeout=180)
        return bool(sha) and self.has(sha)

    def discover(self, branches):
        """Commits do card: SHAs conhecidos + os que citam o card nas branches do card e dos ambientes."""
        for b in branches:
            self.fetch(b)
        refs = [f"origin/{b}" for b in branches if self.git("rev-parse", "--verify", "-q", f"origin/{b}")[0] == 0]
        found = {}
        for s in self.shas:
            rc, out = self.git("log", "-1", "--format=%H%x09%cI%x09%s", s)
            if rc == 0 and out.strip():
                h, d, t = out.strip().split("\t", 2)
                found[h] = {"sha": h, "date": d, "title": t[:200]}
        if refs:
            rc, out = self.git("log", "--since=400 days ago", "-E", "-i", f"--grep={self.regex}", "--format=%H%x09%cI%x09%s",
                               "-n", "300", *refs, timeout=120)
            for line in out.splitlines():
                parts = line.split("\t", 2)
                if len(parts) == 3:
                    found.setdefault(parts[0], {"sha": parts[0], "date": parts[1], "title": parts[2][:200]})
        self.commits = sorted(found.values(), key=lambda c: c["date"])
        if self.commits:
            first = parse_iso(self.commits[0]["date"])
            if first:
                self.since = (first - dt.timedelta(days=2)).strftime("%Y-%m-%d")
        return self.commits

    def files(self):
        names = set()
        for c in self.commits[:80]:
            rc, out = self.git("show", "--no-renames", "--name-only", "--format=", "-m", "--first-parent", c["sha"])
            if rc == 0:
                names.update(l.strip() for l in out.splitlines() if l.strip())
        return sorted(names)

    def contains(self, rev):
        """O commit `rev` contém o nosso código? (algum commit do card é ancestral, ou cita o card desde o 1º commit)."""
        if not rev:
            return None
        if rev in self._contains:
            return self._contains[rev]
        if not rev.startswith("origin/") and not self.ensure(rev):
            self._contains[rev] = None
            return None
        # Um rev-list por commit (desde o 1º commit do card) e a interseção com os nossos — os merges e cherry-picks
        # ganham SHA novo, então também vale commit desse intervalo que cite o card.
        since = [f"--since={self.since}"] if self.since else ["--max-count=5000"]
        rc, out = self.git("rev-list", *since, rev, timeout=120)
        ours = {c["sha"] for c in self.commits}
        res = rc == 0 and bool(ours & set(out.split()))
        if not res and self.commits:
            rc, out = self.git("log", *since, "-E", "-i", f"--grep={self.regex}", "--format=%H", "-n", "1", rev, timeout=120)
            res = rc == 0 and bool(out.strip())
        self._contains[rev] = res
        return res


def repo_path(name):
    short = name.split("/")[-1]
    if os.path.isfile(REPOS_SH):
        try:
            p = subprocess.run(["bash", REPOS_SH, "repos", "path", short], capture_output=True, text=True, timeout=60)
            path = p.stdout.strip().splitlines()[0] if p.returncode == 0 and p.stdout.strip() else ""
            if path and os.path.isdir(path):
                return path
        except (subprocess.TimeoutExpired, IndexError):
            pass
    for guess in (os.path.join(HOME, "repos", "solvace", short), os.path.join(HOME, "repos", short)):
        if os.path.isdir(os.path.join(guess, ".git")):
            return guess
    return None


def guess_repositories(card):
    """Card sem PR/entrega no PRMake (tratado fora dele, itens antigos): clones da máquina (mapa de repositórios) com
    commit citando o card nas refs locais — sem fetch, rápido; a coleta busca as branches depois."""
    try:
        repos = json.load(open(os.path.join(HOME, ".prmake", "repos.json"), encoding="utf-8")).get("repos") or {}
    except (OSError, ValueError):
        return []
    rx = r"(^|[^0-9])" + str(card) + r"([^0-9]|$)"

    def probe(item):
        name, info = item
        path = (info or {}).get("path")
        if not path or not os.path.isdir(os.path.join(path, ".git")):
            return None
        try:
            p = subprocess.run(["git", "-C", path, "log", "--remotes=origin", "--since=400 days ago", "-E", "-i", f"--grep={rx}",
                                "-n", "1", "--format=%H"], capture_output=True, text=True, timeout=60)
        except subprocess.TimeoutExpired:
            return None
        if not p.stdout.strip():
            return None
        r = subprocess.run(["git", "-C", path, "remote", "get-url", "origin"], capture_output=True, text=True, timeout=10)
        m = re.search(r"[:/]([^/:]+/[^/]+?)(\.git)?$", r.stdout.strip())
        return {"repository": m.group(1) if m else name, "branches": [], "shas": [], "prs": []}

    return [r for r in pmap(probe, list(repos.items()), workers=8) if r]


def app_of(p):
    """Nome curto do app da esteira (edv-solvace-centerline-dev → centerline)."""
    n = APP_PREFIX.sub("", p["name"])
    return re.sub(r"-(" + "|".join(TIERS) + r")$", "", n, flags=re.I) or p["name"]


def impact_of(p, files):
    """direct = o card mexeu no projeto do próprio app; shared = só no código compartilhado (libs) que o gatilho inclui."""
    own = [i for i in p.get("filePathsInclude") or [] if not re.match(r"projects/(lib-|environments)", i)]
    if not own:
        return None  # gatilho sem filtro de pasta: não dá para dizer
    return "direct" if matches_paths(dict(p, filePathsInclude=own), files) else "shared"


def matches_paths(p, files):
    inc, exc = p.get("filePathsInclude") or [], p.get("filePathsExclude") or []
    if not inc and not exc:
        return True
    if not files:
        return True  # sem arquivos conhecidos: não dá para filtrar — melhor mostrar
    def hit(f, pats):
        return any(fnmatch.fnmatchcase(f, pat) or fnmatch.fnmatchcase(f, pat.replace("/**", "/*").replace("**/", "")) or
                   (pat.endswith("/**") and f.startswith(pat[:-2])) for pat in pats)
    return any((not inc or hit(f, inc)) and not hit(f, exc) for f in files)


# ── execuções ─────────────────────────────────────────────────────────────────────────────────────────────────────
def console_url(name, region):
    return f"https://{region}.console.aws.amazon.com/codesuite/codepipeline/pipelines/{urllib.parse.quote(name)}/view?region={region}"


def summary_msg(rev):
    try:
        return (json.loads(rev.get("revisionSummary") or "{}").get("CommitMessage") or "").strip()
    except ValueError:
        return (rev.get("revisionSummary") or "").strip()


def pipeline_runs(aws, name, repo, limit=12):
    out = aws.call("codepipeline", "list-pipeline-executions", "--pipeline-name", name, "--max-items", str(limit)) or {}
    runs = []
    for e in out.get("pipelineExecutionSummaries", []):
        src = next((r for r in e.get("sourceRevisions", []) if r.get("actionName") == repo["action"]), None) \
            or next(iter(e.get("sourceRevisions", [])), None) or {}
        runs.append({"id": e.get("pipelineExecutionId"), "status": e.get("status"), "startedAt": iso(e.get("startTime")),
                     "updatedAt": iso(e.get("lastUpdateTime")), "sha": src.get("revisionId"), "message": summary_msg(src),
                     "trigger": (e.get("trigger") or {}).get("triggerType")})
    return runs


def action_runs(aws, name, action):
    out = aws.call("codepipeline", "list-action-executions", "--pipeline-name", name, "--max-items", "60") or {}
    res = []
    for a in out.get("actionExecutionDetails", []):
        if a.get("actionName") != action:
            continue
        res.append({"pipelineExecutionId": a.get("pipelineExecutionId"), "status": a.get("status"),
                    "startedAt": iso(a.get("startTime")), "updatedAt": iso(a.get("lastUpdateTime"))})
    return res


def failed_tail(aws, name):
    st = aws.call("codepipeline", "get-pipeline-state", "--name", name) or {}
    for s in st.get("stageStates", []):
        for a in s.get("actionStates", []):
            le = a.get("latestExecution") or {}
            if le.get("status") == "Failed":
                ext = le.get("externalExecutionId")
                url = le.get("externalExecutionUrl")
                lines = []
                if ext and ":" in ext:
                    b = (aws.call("codebuild", "batch-get-builds", "--ids", ext) or {}).get("builds", [])
                    lg = (b[0].get("logs") or {}) if b else {}
                    if lg.get("groupName") and lg.get("streamName"):
                        ev = aws.call("logs", "get-log-events", "--log-group-name", lg["groupName"], "--log-stream-name",
                                      lg["streamName"], "--limit", "40", "--no-start-from-head") or {}
                        lines = [mask(e.get("message", "").rstrip())[:400] for e in ev.get("events", [])]
                summary = ((le.get("errorDetails") or {}).get("message") or le.get("summary") or "")[:400]
                return {"action": a.get("actionName"), "buildUrl": url, "lines": ([summary] if summary else []) + lines}
    return None


def evaluate_pipeline(aws, env, p, target, repo, region):
    src = next((r for r in p["repositories"] if r["repository"].lower() == repo.name.lower()), p["repositories"][0])
    branch = src.get("branch")
    runs = pipeline_runs(aws, p["name"], src)
    latest = runs[0] if runs else None
    if target:
        acts = action_runs(aws, p["name"], target["action"])
        by_id = {r["id"]: r for r in runs}
        ok = next((a for a in acts if a["status"] == "Succeeded"), None)
        last_act = acts[0] if acts else None
        dep_run = by_id.get(ok["pipelineExecutionId"]) if ok else None
        if ok and not dep_run:
            e = aws.call("codepipeline", "get-pipeline-execution", "--pipeline-name", p["name"],
                         "--pipeline-execution-id", ok["pipelineExecutionId"]) or {}
            arts = (e.get("pipelineExecution") or {}).get("artifactRevisions") or []
            if arts:
                dep_run = {"sha": arts[0].get("revisionId"), "message": (arts[0].get("revisionSummary") or "")[:300],
                           "startedAt": ok["startedAt"], "updatedAt": ok["updatedAt"], "status": "Succeeded"}
        if last_act:
            lrun = by_id.get(last_act["pipelineExecutionId"]) or {}
            latest = {"status": last_act["status"], "sha": lrun.get("sha"), "startedAt": last_act["startedAt"],
                      "updatedAt": last_act["updatedAt"], "message": lrun.get("message")}
        elif latest:
            latest = dict(latest, status="NotStarted" if latest["status"] == "InProgress" else latest["status"])
        deployed_at = ok["updatedAt"] if ok else None
    else:
        dep_run = next((r for r in runs if r["status"] == "Succeeded"), None)
        deployed_at = dep_run["updatedAt"] if dep_run else None
    last_any = latest["startedAt"] if latest else None
    stale = bool(last_any and parse_iso(last_any) and (now_utc() - parse_iso(last_any)).days > STALE_DAYS) or not runs
    repo.fetch(branch) if branch else None
    in_branch = repo.contains(f"origin/{branch}") if branch else None
    deployed = repo.contains(dep_run["sha"]) if dep_run and dep_run.get("sha") else False
    latest_has = repo.contains(latest["sha"]) if latest and latest.get("sha") else False
    st = (latest or {}).get("status")
    broken_since, broken_before_us = None, False
    if st == "Failed" and not target:
        # Esteira quebrada: desde quando falha (última vez que passou) e se já falhava antes do nosso código chegar
        hist = pipeline_runs(aws, p["name"], src, limit=50) if len(runs) >= 12 else runs
        streak = []
        for r in hist:
            if r["status"] == "Succeeded":
                break
            if r["status"] == "Failed":
                streak.append(r)
        if streak:
            broken_since = streak[-1]["startedAt"]
            ours = [r for r in streak if r.get("sha") and repo.contains(r["sha"])]
            first_ours = ours[-1]["startedAt"] if ours else None
            # falhou antes da 1ª execução com o nosso código (ou nem passou nas 50 últimas) → não é nosso
            broken_before_us = bool(first_ours and broken_since < first_ours) or \
                (len(streak) == len(hist) and any(not (r.get("sha") and repo.contains(r["sha"])) for r in streak))
    if deployed:
        verdict = "deployed"
    elif latest_has and st == "Failed" and broken_before_us:
        verdict = "broken"
    elif not in_branch and not latest_has:
        verdict = "absent"
    elif latest_has and st == "InProgress":
        verdict = "deploying"
    elif latest_has and st in ("Failed", "Stopped", "Stopping", "Cancelled"):
        verdict = "failed"
    else:
        verdict = "waiting"
    started, ended = (latest or {}).get("startedAt"), (latest or {}).get("updatedAt")
    dur = None
    if started and ended and st not in ("InProgress",):
        a, b = parse_iso(started), parse_iso(ended)
        dur = int((b - a).total_seconds()) if a and b else None
    item = {"name": p["name"], "repository": repo.name, "branch": branch, "status": st, "stage": None,
            "lastRunAt": started, "lastRunEndedAt": ended, "durationSec": dur, "trigger": (runs[0] if runs else {}).get("trigger"),
            "deployedSha": (dep_run or {}).get("sha"), "deployedMessage": ((dep_run or {}).get("message") or "")[:300],
            "deployedAt": deployed_at, "inBranch": bool(in_branch), "deployed": bool(deployed), "verdict": verdict,
            "consoleUrl": console_url(p["name"], region), "buildUrl": None,
            "action": f"{target['action']} → {target['target']}" if target else None,
            "latest": {"status": st, "sha": (latest or {}).get("sha"), "startedAt": started} if latest else None,
            "failedLogTail": [], "stale": stale, "app": app_of(p), "brokenSince": broken_since,
            "brokenBeforeUs": broken_before_us}
    if st == "Failed":
        tail = failed_tail(aws, p["name"])
        if tail:
            item["failedLogTail"], item["buildUrl"], item["stage"] = tail["lines"], tail["buildUrl"], tail["action"]
    return item


def ddmm(v):
    d = parse_iso(v)
    return d.astimezone(dt.timezone(dt.timedelta(hours=-3))).strftime("%d/%m") if d else "?"


def env_summary(pipes):
    """Contagem por app do que decide o ambiente (esteiras quebradas antes do nosso código ficam à parte)."""
    out = {"apps": len(pipes), "withCode": 0, "broken": [], "failed": [], "deploying": [], "waiting": [], "absent": []}
    for p in pipes:
        if p["deployed"]:
            out["withCode"] += 1
        elif p["verdict"] in out:
            out[p["verdict"]].append(p.get("app") or p["name"])
    return out


def env_message(env, verdict, pipes, pr=None):
    """(título curto, detalhe): o título responde "o nosso código está lá?"; o detalhe diz onde falta e por quê."""
    label = env["label"]
    br = ", ".join(sorted({p["branch"] for p in pipes if p.get("branch")}))
    sm = env_summary(pipes)
    names = lambda ns: ", ".join(ns[:4]) + (f" e mais {len(ns) - 4}" if len(ns) > 4 else "")
    many = sm["apps"] > 1
    parts = []
    if many and sm["withCode"]:
        parts.append(f"{sm['withCode']} de {sm['apps']} apps com o nosso código.")
    if sm["deploying"]:
        parts.append(f"Deploy em andamento: {names(sm['deploying'])}.")
    if sm["failed"]:
        parts.append(f"O deploy com o nosso código falhou: {names(sm['failed'])} — veja o log do build.")
    if sm["waiting"]:
        parts.append(f"Falta o deploy: {names(sm['waiting'])} (o código já está na branch {br}; a esteira ainda não rodou).")
    if sm["absent"] and verdict != "absent":
        parts.append(f"Ainda não chegou: {names(sm['absent'])}.")
    if sm["broken"]:
        since = min((p["brokenSince"] for p in pipes if p["verdict"] == "broken" and p.get("brokenSince")), default=None)
        parts.append(f"{names(sm['broken'])}: esteira quebrada desde {ddmm(since)}, antes do nosso código — não é do card.")
    if verdict == "absent":
        if pr:
            state = {"merged": "mesclado", "open": "ainda aberto"}.get(pr.get("status"), pr.get("status") or "")
            parts.insert(0, f"PR #{pr['number']} para {pr['base']} {state}.")
        elif br:
            parts.insert(0, f"Não chegou na branch {br}.")
    partial = sm["withCode"] > 0 and verdict != "deployed"
    title = {
        "deployed": f"Nosso código está em {label}",
        "deploying": f"Deploy em andamento em {label}",
        "waiting": f"Na branch de {label}, falta o deploy",
        "failed": f"O deploy com o nosso código falhou em {label}",
        "absent": f"Ainda não está em {label}",
        "broken": f"Ainda não está em {label} (esteira quebrada)",
    }[verdict]
    if partial:
        title = f"Parcial em {label}: {sm['withCode']} de {sm['apps']} apps"
    return title, " ".join(parts)


# ── logs, lambdas, alarmes ────────────────────────────────────────────────────────────────────────────────────────
def cached(name, max_age_h, fn):
    os.makedirs(CACHE, exist_ok=True)
    f = os.path.join(CACHE, name)
    if os.path.isfile(f) and time.time() - os.path.getmtime(f) < max_age_h * 3600:
        try:
            return json.load(open(f, encoding="utf-8"))
        except ValueError:
            pass
    data = fn()
    if data is not None:
        json.dump(data, open(f, "w", encoding="utf-8"))
    return data


SKIP_DIRS = re.compile(r"^(lib|docs?|tests?|spec|src|assets?|environments?|scripts?|tools?|build|dist|config|\.)", re.I)


def module_tokens(pipes, files, envs_count):
    """Módulos do card: pasta do projeto alterado (projects/<x>, ou a de cima) e, se forem poucas, o nome das esteiras."""
    toks = set()
    for f in files:
        seg = f.split("/")
        name = seg[1] if len(seg) > 2 and seg[0].lower() in ("projects", "apps", "modules", "src") else seg[0] if len(seg) > 1 else ""
        if name and not SKIP_DIRS.match(name) and len(norm(name)) >= 3:
            toks.add(norm(name))
    if len(pipes) > 3 * max(envs_count, 1):
        return toks
    for p in pipes:
        n = re.sub(r"^(edv-solvace-|revamp-api-|revamp-|api-|lambda-)", "", p["name"].lower())
        n = re.sub(r"-(" + "|".join(TIERS) + r")$", "", n)
        if len(norm(n)) >= 3:
            toks.add(norm(n))
    return toks


def collect_logs(aws, envs, tokens, segments=(), no_errors=False):
    groups = cached("log-groups.json", 24, lambda: [
        {"name": g["logGroupName"], "storedBytes": g.get("storedBytes", 0)}
        for g in (aws.call("logs", "describe-log-groups") or {}).get("logGroups", [])] or None) or []
    start = int((now_utc() - dt.timedelta(hours=24)).timestamp() * 1000)
    jobs = []
    for e in envs:
        short = (e.get("host") or "").split(".")[0].lower()
        e["_logs"] = []
        for g in groups:
            parts = g["name"].split("/")
            if len(parts) >= 4 and parts[1] == "aws" and parts[2].lower() == short and (norm(parts[3]) in tokens or norm(parts[3]) in segments):
                e["_logs"].append({"group": g["name"], "storedBytes": g["storedBytes"], "lastEventAt": None, "errors": [],
                                   "url": f"https://{aws.region}.console.aws.amazon.com/cloudwatch/home?region={aws.region}"
                                          f"#logsV2:log-groups/log-group/{urllib.parse.quote(g['name'], safe='')}"})
        e["_logs"] = e["_logs"][:4]
        jobs += [(e, lg) for lg in e["_logs"]]

    def errors(job):
        _, lg = job
        if no_errors:
            return
        out = aws.call("logs", "filter-log-events", "--log-group-name", lg["group"], "--start-time", str(start),
                       "--filter-pattern", "?ERROR ?Exception ?Error", "--max-items", "20") or {}
        ev = out.get("events", [])
        lg["errors"] = [{"at": iso(dt.datetime.fromtimestamp(x["timestamp"] / 1000, dt.timezone.utc).isoformat()),
                         "message": mask(x.get("message", "").strip())[:600]} for x in ev[-20:]]
        if ev:
            lg["lastEventAt"] = lg["errors"][-1]["at"]

    pmap(errors, jobs, workers=8)


def collect_resources(aws, tokens, tiers):
    res = {"lambdas": [], "alarms": []}
    if not tokens:
        return res
    fns = cached("lambdas.json", 24, lambda: [
        {"name": f["FunctionName"], "runtime": f.get("Runtime") or f.get("PackageType"), "lastModified": iso(f.get("LastModified"))}
        for f in (aws.call("lambda", "list-functions") or {}).get("Functions", [])] or None) or []
    res["lambdas"] = sorted([f for f in fns if any(t in norm(f["name"]) for t in tokens)],
                            key=lambda f: f.get("lastModified") or "", reverse=True)[:15]
    al = aws.call("cloudwatch", "describe-alarms", "--state-value", "ALARM") or {}
    res["alarms"] = [{"name": a["AlarmName"], "state": a.get("StateValue"), "since": iso(a.get("StateUpdatedTimestamp")),
                      "reason": (a.get("StateReason") or "")[:300]}
                     for a in al.get("MetricAlarms", [])
                     # autoscaling (TargetTracking AlarmLow/High) vive em ALARM: não é problema do módulo
                     if not a["AlarmName"].startswith("TargetTracking-") and any(t in norm(a["AlarmName"]) for t in tokens)][:15]
    return res


# ── coleta do card ────────────────────────────────────────────────────────────────────────────────────────────────
def collect(args):
    t0 = time.time()
    profile = pick_profile()
    aws = Aws(profile, REGION)
    ctx = json.load(open(args.context_file, encoding="utf-8")) if args.context_file else \
        api("GET", f"AwsDeploy/card/{args.card}/context") or {}
    envs = sorted([dict(e) for e in ctx.get("environments") or []], key=lambda e: e.get("order") or 0)
    if args.envs:
        keep = set(args.envs.split(","))
        envs = [e for e in envs if e["key"] in keep]
    snap = {"version": 2, "card": args.card, "requestId": args.request, "collectedAt": None, "collectedBy": None,
            "machine": socket.gethostname(), "durationMs": 0, "account": ACCOUNT, "region": REGION, "index": None,
            "repositories": [], "environments": [], "resources": {"lambdas": [], "alarms": []}, "denied": [], "warnings": [],
            "notes": []}
    if not args.skip_index and not args.index_dir:
        try:
            snap["index"] = refresh_index(aws)
        except RuntimeError as e:
            snap["warnings"].append(f"Índice de pipelines não atualizado: {e}")
    resolve_hosts(aws, envs)
    for e in envs:
        if not e.get("tier") and not e["_eb"]:
            snap["warnings"].append(f"{e['label']}: host {e.get('host')} não achado no CloudFront — informe o tier na configuração")

    repos, all_pipes, all_files = [], [], []
    ctx_repos = ctx.get("repositories") or []
    if args.repo:
        ctx_repos += [{"repository": r if "/" in r else f"electradv/{r}", "branches": [], "shas": [], "prs": []}
                      for r in args.repo if not any(c["repository"].split("/")[-1] == r.split("/")[-1] for c in ctx_repos)]
    if not ctx_repos:
        ctx_repos = guess_repositories(args.card)
        if ctx_repos:
            snap["warnings"].append("Sem PR/entrega do card no PRMake: repositórios achados pelos commits que citam o card nesta "
                                    "máquina (" + ", ".join(r["repository"] for r in ctx_repos) + ")")
    for r in ctx_repos:
        name = r["repository"]
        full = name if "/" in name else f"electradv/{name}"
        pipes = local_pipelines(args.index_dir, full) if args.index_dir else \
            api("GET", "AwsDeploy/pipelines?repository=" + urllib.parse.quote(full)) or []
        path = repo_path(full)
        info = {"repository": full, "localPath": bool(path), "fetchedAt": None, "commits": [], "files": 0,
                "branches": r.get("branches") or []}
        snap["repositories"].append(info)
        if not path:
            snap["warnings"].append(f"{full}: repositório não está nesta máquina (prmake-skills.sh repos scan) — sem como "
                                    "conferir o nosso código")
            continue
        repo = Repo(full, path, r.get("shas") or [], ctx.get("markers") or [], args.card)
        env_branches = sorted({rr["branch"] for p in pipes for rr in p.get("repositories", [])
                               if rr["repository"].lower() == full.lower() and rr.get("branch")})
        card_branches = sorted(set(info["branches"]) | {pr.get("head") for pr in r.get("prs") or [] if pr.get("head")})
        relevant_branches = [b for b in env_branches if any(env_targets(e, p) for e in envs for p in pipes
                                                            if any(rr.get("branch") == b for rr in p["repositories"]))]
        repo.discover(card_branches + relevant_branches)
        info["fetchedAt"] = iso(now_utc())
        info["commits"] = repo.commits[-30:]
        files = repo.files()
        info["files"] = len(files)
        all_files += files
        mine = [p for p in pipes if matches_paths(p, files)]
        repo.changed = files
        libs = sorted({f.split("/")[1] for f in files if re.match(r"projects/lib-[^/]+/", f)})
        if libs and len({app_of(p) for p in mine}) > 3:
            snap["notes"].append(
                f"{full.split('/')[-1]} é um monorepo: o card mudou código compartilhado ({', '.join(libs)}), que entra no build "
                f"de cada app — por isso cada app tem a sua esteira por ambiente e todas precisam levar o nosso código.")
        if not repo.commits:
            snap["warnings"].append(f"{full}: nenhum commit do card achado (nem SHA registrado nem commit citando {args.card})")
        repos.append((repo, mine))
        all_pipes += mine

    jobs = [(e, p, target, repo) for e in envs for repo, mine in repos for p in mine for target in env_targets(e, p)]
    log(f"{len(jobs)} esteiras do card nos {len(envs)} ambientes")
    evaluated = pmap(lambda j: evaluate_pipeline(aws, j[0], j[1], j[2], j[3], REGION), jobs, workers=16)
    for j, r in zip(jobs, evaluated):
        r["impact"] = impact_of(j[1], getattr(j[3], "changed", []))
    for e in envs:
        results = [r for j, r in zip(jobs, evaluated) if j[0] is e]
        stale = [r for r in results if r["stale"]]
        live = [r for r in results if not r["stale"]]
        if stale:
            snap["warnings"].append(f"{e['label']}: ignoradas por estarem paradas há mais de {STALE_DAYS} dias: "
                                    + ", ".join(sorted({r['name'] for r in stale})[:10]))
        for r in live:
            r.pop("stale", None)
        # Esteira principal: a que publica no Elastic Beanstalk do host (legado). Havendo uma do repositório, as outras
        # do mesmo repositório (rdc, lambdas…) aparecem, mas não decidem o veredito do ambiente.
        eb_repos = {r["repository"] for r in live if r.get("action")}
        for r in live:
            r["primary"] = bool(r.get("action")) or r["repository"] not in eb_repos
        pipes_out = sorted(live, key=lambda r: (not r["primary"], VERDICT_ORDER.index(r["verdict"]), r["name"]))
        pr, detail = None, ""
        if not repos:
            verdict, msg = "unknown", f"Sem código do card registrado para conferir {e['label']}"
        elif not pipes_out:
            verdict = "not-applicable"
            msg = f"Não se aplica — o código do card não vai para {e['label']} por estas esteiras"
        else:
            main = [p for p in pipes_out if p["primary"]] or pipes_out
            counted = [p for p in main if p["verdict"] != "broken"] or main
            verdict = next(v for v in VERDICT_ORDER if any(p["verdict"] == v for p in counted))
            branches = {p.get("branch") for p in main}
            pr = next((x for r in ctx_repos for x in r.get("prs") or [] if x.get("base") in branches), None)
            msg, detail = env_message(e, verdict, main, pr)
        snap["environments"].append({
            "key": e["key"], "label": e["label"], "host": e.get("host"), "tier": e.get("tier"), "verdict": verdict,
            "message": msg, "detail": detail, "pipelines": pipes_out, "logs": [],
            "summary": env_summary([p for p in pipes_out if p["primary"]] or pipes_out) if pipes_out else None,
            "pr": pr if pipes_out else None,
            "resources": {"cloudfront": e.get("_cf"), "elasticBeanstalk": sorted(e["_eb"])}, "_env": e})

    tokens = module_tokens(all_pipes, all_files, len(envs))
    log(f"módulos do card (logs/lambdas/alarmes): {sorted(tokens) or 'nenhum'}")
    if not args.no_logs and (tokens or all_files):
        segments = {norm(seg) for f in all_files for seg in f.split("/")[:-1] if len(norm(seg)) >= 3}
        collect_logs(aws, [x["_env"] for x in snap["environments"]], tokens, segments)
        for x in snap["environments"]:
            x["logs"] = x["_env"].get("_logs", [])
    snap["resources"] = collect_resources(aws, tokens, {e.get("tier") for e in envs})
    for x in snap["environments"]:
        x.pop("_env", None)
    for svc in ("ecs", "eks", "ecr"):
        probe = {"ecs": ("ecs", "list-clusters", "--max-items", "1"), "eks": ("eks", "list-clusters", "--max-items", "1"),
                 "ecr": ("ecr", "describe-repositories", "--max-items", "1")}[svc]
        aws.call(*probe)
    snap["denied"] = sorted(aws.denied)
    if aws.failed:
        snap["warnings"] += aws.failed[:10]
    snap["durationMs"] = int((time.time() - t0) * 1000)
    snap["collectedAt"] = iso(now_utc())
    if args.out:
        json.dump(snap, open(args.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    if args.dry_run:
        if not args.out:
            print(json.dumps(snap, ensure_ascii=False, indent=1))
    else:
        api("PUT", f"AwsDeploy/card/{args.card}", snap)
    print_summary(snap)
    return snap


def local_pipelines(folder, repository):
    """Desenvolvimento/teste sem a API: índice a partir de saídas do get-pipeline numa pasta."""
    out = []
    for f in glob.glob(os.path.join(folder, "*.json")):
        try:
            d = pipeline_dto(json.load(open(f, encoding="utf-8")))
        except ValueError:
            continue
        if any(r["repository"].lower() == repository.lower() for r in d["repositories"]):
            out.append(clean_dto(d))
    return out


def print_summary(snap):
    print(f"Card {snap['card']} — AWS ({snap.get('collectedAt')}, {snap['durationMs'] / 1000:.0f} s)")
    for e in snap["environments"]:
        print(f"- {e['label']}: {e['message']}" + (f" — {e['detail']}" if e.get("detail") else ""))
        for p in e["pipelines"]:
            sha = (p.get("deployedSha") or "")[:8]
            print(f"    {p['name']} [{p.get('status') or '—'}] último: {p.get('lastRunAt') or '—'} implantado: {sha or '—'}"
                  f" → {p['verdict']}")
    for w in snap.get("warnings", [])[:8]:
        print(f"  aviso: {w}")
    if snap.get("denied"):
        print(f"  sem permissão nesta máquina: {', '.join(snap['denied'])}")


def show(args):
    out = api("GET", f"AwsDeploy/card/{args.card}") or {}
    if not out.get("snapshot"):
        print("Sem retrato da AWS para o card.")
        return
    print_summary(out["snapshot"])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("collect")
    c.add_argument("card", type=int)
    c.add_argument("--request")
    c.add_argument("--dry-run", action="store_true")
    c.add_argument("--out")
    c.add_argument("--skip-index", action="store_true")
    c.add_argument("--no-logs", action="store_true")
    c.add_argument("--envs", help="só estes ambientes (chaves separadas por vírgula)")
    c.add_argument("--repo", action="append", help="repositório a mais (ex.: edv-solvace-apps), além dos do PRMake")
    c.add_argument("--context-file", help="teste: contexto do card em JSON (em vez de GET AwsDeploy/card/{card}/context)")
    c.add_argument("--index-dir", help="teste: pasta com saídas do get-pipeline (em vez do índice do PRMake)")
    i = sub.add_parser("index")
    i.add_argument("--full", action="store_true")
    i.add_argument("--seed-dir")
    s = sub.add_parser("show")
    s.add_argument("card", type=int)
    args = ap.parse_args()
    try:
        if args.cmd == "collect":
            collect(args)
        elif args.cmd == "index":
            print(json.dumps(refresh_index(Aws(pick_profile(), REGION), full=args.full, seed_dir=args.seed_dir)))
        else:
            show(args)
    except RuntimeError as e:
        sys.exit(f"ERRO: {e}")


if __name__ == "__main__":
    main()
