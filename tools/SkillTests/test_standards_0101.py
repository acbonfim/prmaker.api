"""0101: padrões de banco e de aplicação — hook de governança (skills/_tooling/prmake-governance.py) conferindo .sql e
migrações no PostToolUse/PreToolUse, e os subcomandos `standards`/`standards-check` (+ upload de .sql) do
analisar-bug/scripts/prmake-plan.sh e o upload do especialista-do-caso/scripts/prmake-case.sh, contra um PRMake falso.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
GOV = os.path.join(ROOT, "skills", "_tooling", "prmake-governance.py")
PLAN = os.path.join(ROOT, "skills", "analisar-bug", "scripts", "prmake-plan.sh")
CASE = os.path.join(ROOT, "skills", "especialista-do-caso", "scripts", "prmake-case.sh")

spec = importlib.util.spec_from_file_location("prmake_governance_0101", GOV)
gov = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gov)

FINDING = {"key": "db-fk7", "code": "FK7", "title": "Nome da FK fora do padrão FK_[schema]_[Tabela]_[Coluna]",
           "effect": "warn", "line": 3, "excerpt": "CONSTRAINT fk_x FOREIGN KEY", "hint": "Use FK_ap_Plan_SiteId"}


def hooks_config(**over):
    cfg = {
        "mode": "enforce", "failOpen": True, "timeoutMs": 1500, "cacheSeconds": 60, "version": "v1",
        "toolMatchers": ["Bash"],
        "hooks": [
            {"key": "claude-pretool", "event": "claude:PreToolUse", "matcher": "Bash|Read|Edit|Write|MultiEdit|NotebookEdit",
             "action": "evaluate", "config": {}},
            {"key": "claude-posttool", "event": "claude:PostToolUse", "matcher": "Bash|Write|Edit|MultiEdit|mcp__prmake__*",
             "action": "record", "config": {}},
        ],
    }
    cfg.update(over)
    return cfg


def check_resp(decision="warn", findings=None, mode="enforce", would=None):
    return {"decision": decision, "mode": mode, "wouldDecide": would or decision,
            "findings": [FINDING] if findings is None else findings}


class FakePrmake:
    """PRMake falso: config dos hooks, evaluate (allow), eventos, standards/check, standards/for-card, upload de arquivo
    do plano e da conversa do caso."""

    def __init__(self):
        self.config = hooks_config()
        self.check = check_resp()
        self.check_status = 200
        self.for_card = "## Padrões de banco e de aplicação\n\n- **FK7** — FK_[schema]_[Tabela]_[Coluna]\n"
        self.for_card_status = 200
        self.calls = []   # (método, caminho, corpo)
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def _send(self, status, body=None, ctype="application/json"):
                raw = body.encode() if isinstance(body, str) else (json.dumps(body).encode() if body is not None else b"")
                self.send_response(status)
                self.send_header("content-type", ctype)
                self.send_header("content-length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

            def do_GET(self):
                fake.calls.append(("GET", self.path, None))
                if self.path.endswith("/Governance/hooks/config"):
                    return self._send(200, fake.config)
                if "/Governance/standards/for-card/" in self.path:
                    if fake.for_card_status != 200:
                        return self._send(fake.for_card_status, {})
                    return self._send(200, fake.for_card, "text/markdown; charset=utf-8")
                self._send(404, {})

            def do_POST(self):
                raw = self.rfile.read(int(self.headers.get("content-length") or 0))
                ctype = self.headers.get("content-type") or ""
                body = json.loads(raw or b"null") if "json" in ctype else raw
                fake.calls.append(("POST", self.path, body))
                if self.path.endswith("/Governance/evaluate"):
                    return self._send(200, {"decision": "allow", "mode": "enforce", "rules": []})
                if self.path.endswith("/Governance/hooks/events"):
                    return self._send(200, {"accepted": len(body)})
                if self.path.endswith("/Governance/standards/check"):
                    if fake.check_status != 200:
                        return self._send(fake.check_status, {})
                    return self._send(200, fake.check)
                if self.path.endswith("/artifacts"):
                    return self._send(201, {"kind": "script", "size": len(raw)})
                if self.path.endswith("/files"):
                    return self._send(200, {"number": 7, "name": "script.sql"})
                self._send(404, {})

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = "http://127.0.0.1:%d/api/v1" % self.server.server_address[1]

    def checks(self):
        return [c[2] for c in self.calls if c[1].endswith("/Governance/standards/check")]

    def stop(self):
        self.server.shutdown()
        self.server.server_close()


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def write(path, data, mode="w"):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, mode, **({} if "b" in mode else {"encoding": "utf-8"})) as fh:
        fh.write(data)


SQL = "CREATE TABLE ap.Plan (\n  Id INT NOT NULL,\n  CONSTRAINT fk_x FOREIGN KEY (SiteId) REFERENCES ap.Site(Id)\n);\n"


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.home = self.tmp.name
        self.prmake = os.path.join(self.home, ".prmake")
        self.fake = FakePrmake()
        # repositório numa branch de bug, com o remote origin (o nome do repositório sai dele)
        self.repo = os.path.join(self.home, "repos", "actionplan")
        write(os.path.join(self.repo, ".git", "HEAD"), "ref: refs/heads/bugfix/78720-qa\n")
        write(os.path.join(self.repo, ".git", "config"),
              '[core]\n\tbare = false\n[remote "upstream"]\n\turl = https://x/outro.git\n'
              '[remote "origin"]\n\turl = https://github.com/solvace/revamp-actionplan-api.git\n'
              '\tfetch = +refs/heads/*:refs/remotes/origin/*\n')

    def tearDown(self):
        self.fake.stop()
        self.tmp.cleanup()

    def env(self, **extra):
        env = {k: v for k, v in os.environ.items() if not k.startswith(("PRMAKE_", "CLAUDE_"))}
        env.update({"HOME": self.home, "USERPROFILE": self.home, "PRMAKE_HOME": self.prmake, "PRMAKE_TOKEN": "tk-123",
                    "PRMAKE_API_BASE": self.fake.base, "TMPDIR": self.home, "CARDS_DIR": os.path.join(self.home, "cards")})
        env.update({k: v for k, v in extra.items() if v is not None})
        return env


class HookTests(Base):
    def hook(self, data, **env):
        data = dict({"session_id": "s-1", "cwd": self.repo}, **data)
        p = subprocess.run([sys.executable, GOV], input=json.dumps(data).encode(), capture_output=True,
                           env=self.env(**env), timeout=30)
        self.assertEqual(p.returncode, 0, p.stderr.decode())
        out = p.stdout.decode().strip()
        return json.loads(out) if out else None

    def post(self, path, tool="Edit", tid="tu-1", **env):
        return self.hook({"hook_event_name": "PostToolUse", "tool_name": tool, "tool_use_id": tid,
                          "tool_input": {"file_path": path, "old_string": "a", "new_string": "b"},
                          "tool_response": {"filePath": path, "success": True}}, **env)

    def pre_write(self, path, content, tid="tu-w", **env):
        return self.hook({"hook_event_name": "PreToolUse", "tool_name": "Write", "tool_use_id": tid,
                          "tool_input": {"file_path": path, "content": content}}, **env)

    def post_write(self, path, content, tid="tu-w", **env):
        return self.hook({"hook_event_name": "PostToolUse", "tool_name": "Write", "tool_use_id": tid,
                          "tool_input": {"file_path": path, "content": content},
                          "tool_response": {"filePath": path, "type": "create"}}, **env)

    def test_edit_sql_returns_findings_as_context(self):
        path = os.path.join(self.repo, "db", "scripts", "01_78720_plan.sql")
        write(path, SQL)
        out = self.post(path)
        hso = out["hookSpecificOutput"]
        self.assertEqual(hso["hookEventName"], "PostToolUse")
        ctx = hso["additionalContext"]
        self.assertIn("Padrões do PRMake (0101) em 01_78720_plan.sql: 1 achado(s)", ctx)
        self.assertIn("- FK7 [warn] linha 3:", ctx)
        self.assertIn("dica: Use FK_ap_Plan_SiteId", ctx)
        self.assertIn("FK7", out["systemMessage"])   # warn também aparece para a pessoa
        body = self.fake.checks()[0]
        self.assertEqual(body["target"], "sql")
        self.assertEqual(body["text"], SQL)
        self.assertEqual(body["path"], "db/scripts/01_78720_plan.sql")      # relativo à raiz do repositório
        self.assertEqual(body["repository"], "revamp-actionplan-api")      # remote origin, não o upstream
        self.assertEqual(body["card"], 78720)
        self.assertEqual(body["source"], "hook")

    def test_migration_cs_and_generated_files(self):
        mig = os.path.join(self.repo, "src", "infra", "Migrations", "20261124_AddPlan.cs")
        write(mig, 'migrationBuilder.CreateTable(name: "Plan", schema: "ap");\n')
        self.fake.check = check_resp("allow", mode="monitor", would="block")
        out = self.post(mig, tool="MultiEdit")
        self.assertIn("decisão allow (modo monitor, no enforce seria block)", out["hookSpecificOutput"]["additionalContext"])
        self.assertNotIn("systemMessage", out)   # monitorar: só o Claude fica sabendo
        self.assertEqual(self.fake.checks()[0]["target"], "migration")
        for gen in ("20261124_AddPlan.Designer.cs", "DefaultContextModelSnapshot.cs"):
            p = os.path.join(self.repo, "src", "infra", "Migrations", gen)
            write(p, "// gerado\n")
            self.assertIsNone(self.post(p, tid="tu-" + gen))
        node = os.path.join(self.repo, "db", "migrations", "2026", "001-plan.js")
        write(node, "exports.up = knex => knex.schema.createTable('plan')\n")
        self.post(node, tid="tu-node")
        self.assertEqual([b["target"] for b in self.fake.checks()], ["migration", "migration"])

    def test_other_files_are_not_checked(self):
        cs = os.path.join(self.repo, "src", "Services", "PlanService.cs")
        write(cs, "class PlanService {}\n")
        self.assertIsNone(self.post(cs))
        self.assertIsNone(self.hook({"hook_event_name": "PostToolUse", "tool_name": "Bash", "tool_use_id": "b1",
                                     "tool_input": {"command": "cat x.sql"}, "tool_response": {"stdout": ""}}))
        self.assertEqual(self.fake.checks(), [])

    def test_no_findings_says_nothing(self):
        path = os.path.join(self.repo, "ok.sql")
        write(path, "SELECT Id FROM ap.Plan;\n")
        self.fake.check = check_resp("allow", findings=[])
        self.assertIsNone(self.post(path))
        self.assertEqual(len(self.fake.checks()), 1)

    def test_at_most_10_findings(self):
        path = os.path.join(self.repo, "big.sql")
        write(path, SQL)
        self.fake.check = check_resp("warn", findings=[dict(FINDING, code="N%d" % i, line=i) for i in range(1, 15)])
        ctx = self.post(path)["hookSpecificOutput"]["additionalContext"]
        self.assertIn("14 achado(s)", ctx)
        self.assertIn("- N10 [warn] linha 10:", ctx)
        self.assertNotIn("- N11 ", ctx)
        self.assertIn("… e mais 4", ctx)

    def test_write_sql_block_is_denied_before_writing(self):
        path = os.path.join(self.repo, "db", "02_78720_fk.sql")
        self.fake.check = check_resp("block", findings=[dict(FINDING, effect="block")])
        out = self.pre_write(path, SQL)
        hso = out["hookSpecificOutput"]
        self.assertEqual(hso["permissionDecision"], "deny")
        self.assertIn("Barrado pelos padrões do PRMake", hso["permissionDecisionReason"])
        self.assertIn("FK7 [block] linha 3", hso["permissionDecisionReason"])
        self.assertIn("Efeito block", hso["permissionDecisionReason"])
        body = self.fake.checks()[0]
        self.assertEqual(body["text"], SQL)   # o conteúdo do Write (o arquivo ainda não existe)
        self.assertEqual(body["path"], "db/02_78720_fk.sql")
        self.assertFalse(os.path.exists(path))

    def test_write_sql_warn_passes_and_post_reuses_the_check(self):
        path = os.path.join(self.repo, "db", "03_78720_x.sql")
        self.assertIsNone(self.pre_write(path, SQL))
        write(path, SQL)
        out = self.post_write(path, SQL)
        self.assertIn("FK7 [warn]", out["hookSpecificOutput"]["additionalContext"])
        self.assertEqual(len(self.fake.checks()), 1)   # não confere (nem grava na trilha) duas vezes

    def test_write_of_migration_is_checked_after(self):
        mig = os.path.join(self.repo, "Migrations", "20261124_X.cs")
        self.assertIsNone(self.pre_write(mig, "x"))
        self.assertEqual(self.fake.checks(), [])   # no PreToolUse só .sql
        write(mig, "migrationBuilder.Sql(\"SELECT * FROM x\");\n")
        self.assertIn("FK7", self.post_write(mig, "x")["hookSpecificOutput"]["additionalContext"])

    def test_api_down_lets_it_pass(self):
        path = os.path.join(self.repo, "down.sql")
        write(path, SQL)
        self.post(path, tid="cfg")   # guarda a config
        self.fake.check_status = 500
        self.assertIsNone(self.post(path, tid="t500"))
        self.assertIsNone(self.pre_write(path, SQL, tid="t500w"))
        down = "http://127.0.0.1:%d/api/v1" % free_port()
        start = time.time()
        self.assertIsNone(self.post(path, tid="tdown", PRMAKE_API_BASE=down))
        self.assertIsNone(self.pre_write(path, SQL, tid="tdownw", PRMAKE_API_BASE=down))
        self.assertLess(time.time() - start, 15)

    def test_missing_endpoint_is_not_asked_again(self):
        path = os.path.join(self.repo, "a.sql")
        write(path, SQL)
        self.fake.check_status = 404
        self.assertIsNone(self.post(path, tid="a"))
        self.assertIsNone(self.post(path, tid="b"))
        self.assertEqual(len(self.fake.checks()), 1)

    def test_disabled_when_evaluate_off_or_mode_off(self):
        path = os.path.join(self.repo, "a.sql")
        write(path, SQL)
        cfg = hooks_config()
        cfg["hooks"] = [h for h in cfg["hooks"] if h["action"] != "evaluate"]
        self.fake.config = cfg
        self.assertIsNone(self.post(path, tid="a"))
        self.assertIsNone(self.pre_write(path, SQL, tid="b"))
        self.assertEqual(self.fake.checks(), [])
        os.remove(os.path.join(self.prmake, "governance.json"))
        self.fake.config = hooks_config(mode="off")
        self.assertIsNone(self.post(path, tid="c"))
        os.remove(os.path.join(self.prmake, "governance.json"))
        self.fake.config = hooks_config(standards=False)
        self.assertIsNone(self.post(path, tid="d"))
        self.assertEqual(self.fake.checks(), [])

    def test_failed_edit_is_not_checked(self):
        path = os.path.join(self.repo, "a.sql")
        write(path, SQL)
        out = self.hook({"hook_event_name": "PostToolUse", "tool_name": "Edit", "tool_use_id": "e",
                         "tool_input": {"file_path": path}, "tool_response": {"is_error": True, "error": "x"}})
        self.assertIsNone(out)
        self.assertEqual(self.fake.checks(), [])

    def test_utf16_and_200kb_limit(self):
        path = os.path.join(self.repo, "ssms.sql")
        write(path, b"\xff\xfe" + "SELECT Id FROM ap.Plan; -- ação\n".encode("utf-16-le"), "wb")
        self.post(path, tid="u16")
        self.assertEqual(self.fake.checks()[0]["text"], "SELECT Id FROM ap.Plan; -- ação\n")
        big = os.path.join(self.repo, "big.sql")
        write(big, "-- x\n" * 100_000)
        self.post(big, tid="big")
        self.assertEqual(len(self.fake.checks()[1]["text"]), 200 * 1024)


class HelperTests(unittest.TestCase):
    def test_targets(self):
        self.assertEqual(gov.standards_target("/r/db/x.SQL"), "sql")
        self.assertEqual(gov.standards_target("C:\\r\\src\\Migrations\\20261124_X.cs"), "migration")
        self.assertEqual(gov.standards_target("/r/db/migrations/v1/001.sql"), "sql")
        self.assertEqual(gov.standards_target("/r/db/migrations/v1/001.ts"), "migration")
        self.assertIsNone(gov.standards_target("/r/src/Migrations/20261124_X.Designer.cs"))
        self.assertIsNone(gov.standards_target("/r/src/Migrations/DefaultContextModelSnapshot.cs"))
        self.assertIsNone(gov.standards_target("/r/src/Migrations/README.md"))
        self.assertIsNone(gov.standards_target("/r/src/MigrationsHelper.cs"))
        self.assertIsNone(gov.standards_target(None))

    def test_repo_name_from_url(self):
        self.assertEqual(gov.repo_name_from_url("https://github.com/acbonfim/prform.api.git"), "prform.api")
        self.assertEqual(gov.repo_name_from_url("https://dev.azure.com/org/proj/_git/edv-solvace-api"), "edv-solvace-api")
        self.assertEqual(gov.repo_name_from_url("git@ssh.dev.azure.com:v3/org/proj/revamp-auth"), "revamp-auth")
        self.assertEqual(gov.repo_name_from_url("git@github.com:org/app.git/"), "app")
        self.assertIsNone(gov.repo_name_from_url(""))

    def test_origin_of_worktree_uses_commondir(self):
        with tempfile.TemporaryDirectory() as t:
            main = os.path.join(t, "main")
            write(os.path.join(main, ".git", "config"), '[remote "origin"]\n  url = git@github.com:org/edv-solvace.git\n')
            write(os.path.join(main, ".git", "worktrees", "wt", "HEAD"), "ref: refs/heads/hotfix/42\n")
            write(os.path.join(main, ".git", "worktrees", "wt", "commondir"), "../..\n")
            wt = os.path.join(t, "wt")
            write(os.path.join(wt, ".git"), "gitdir: ../main/.git/worktrees/wt\n")
            os.makedirs(os.path.join(wt, "sql"))
            self.assertEqual(gov.origin_repo(os.path.join(wt, "sql")), "edv-solvace")
            self.assertEqual(gov.branch_of(os.path.join(wt, "sql")), "hotfix/42")
            self.assertIsNone(gov.origin_repo(t))


@unittest.skipIf(os.name == "nt" or not shutil.which("jq"), "scripts bash com jq")
class PlanScriptTests(Base):
    def setUp(self):
        super().setUp()
        self.cards = os.path.join(self.home, "cards")
        write(os.path.join(self.cards, "78720", ".prmake-plan.json"), json.dumps({"planId": "p1", "card": "78720"}))
        # repositório git de verdade para o remote origin do arquivo
        self.git = os.path.join(self.home, "git-repo")
        os.makedirs(self.git)
        subprocess.run(["git", "init", "-q", self.git], check=True)
        subprocess.run(["git", "-C", self.git, "remote", "add", "origin", "https://dev.azure.com/o/p/_git/edv-solvace"],
                       check=True)

    def plan(self, *args, **env):
        return subprocess.run(["bash", PLAN, *args], capture_output=True, text=True, env=self.env(**env), timeout=60)

    def test_standards_prints_and_caches(self):
        p = self.plan("standards", "78720", "--repo", "edv-solvace")
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("FK7", p.stdout)
        gets = [c[1] for c in self.fake.calls if "for-card" in c[1]]
        self.assertEqual(gets, ["/api/v1/Governance/standards/for-card/78720?repository=edv-solvace"])
        self.assertTrue(os.path.exists(os.path.join(self.cards, "78720", "dados", "padroes-edv-solvace.md")))
        self.plan("standards", "78720", "--repo", "edv-solvace")
        self.assertEqual(len([c for c in self.fake.calls if "for-card" in c[1]]), 1)   # cache de 10 min
        self.plan("standards", "78720", "--repo", "edv-solvace", "--refresh")
        self.assertEqual(len([c for c in self.fake.calls if "for-card" in c[1]]), 2)

    def test_standards_empty_and_down(self):
        self.fake.for_card = ""
        p = self.plan("standards", "78720")
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("Nenhum padrao", p.stdout)
        self.fake.for_card_status = 500
        p = self.plan("standards", "78721")
        self.assertEqual(p.returncode, 0)
        self.assertIn("indisponiveis", p.stderr)

    def test_standards_check_warn_block_and_down(self):
        f = os.path.join(self.git, "scripts", "01_78720_fk.sql")
        write(f, SQL)
        p = self.plan("standards-check", "78720", f)
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("decisao warn", p.stdout)
        self.assertIn("FK7 [warn] linha 3", p.stdout)
        self.assertIn("dica: Use FK_ap_Plan_SiteId", p.stdout)
        body = self.fake.checks()[0]
        self.assertEqual(body, {"text": SQL, "target": "sql", "path": "01_78720_fk.sql", "source": "skill",
                                "repository": "edv-solvace", "card": 78720})

        self.fake.check = check_resp("block", findings=[dict(FINDING, effect="block")])
        p = self.plan("standards-check", "78720", f, "--repo", "outro", PRMAKE_EXECUTOR="1")
        self.assertEqual(p.returncode, 3)
        self.assertIn("decisao block", p.stdout)
        self.assertEqual(self.fake.checks()[1]["repository"], "outro")
        self.assertEqual(self.fake.checks()[1]["source"], "executor")

        mig = os.path.join(self.git, "src", "Migrations", "20261124_X.cs")
        write(mig, "x\n")
        self.fake.check = check_resp("allow", findings=[])
        self.assertEqual(self.plan("standards-check", "78720", mig).returncode, 0)
        self.assertEqual(self.fake.checks()[2]["target"], "migration")
        self.plan("standards-check", "78720", mig, "--target", "code")
        self.assertEqual(self.fake.checks()[3]["target"], "code")

        p = self.plan("standards-check", "78720", f, PRMAKE_API_BASE="http://127.0.0.1:%d/api/v1" % free_port())
        self.assertEqual(p.returncode, 0)
        self.assertIn("nao conferi", p.stderr)

    def test_upload_sql_checks_first(self):
        f = os.path.join(self.cards, "78720", "scripts", "01_78720_fk.sql")
        write(f, SQL)
        p = self.plan("upload", "78720", f, "script")
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("FK7 [warn]", p.stdout)
        self.assertIn("enviado: 01_78720_fk.sql", p.stdout)
        self.fake.check = check_resp("block", findings=[dict(FINDING, effect="block")])
        p = self.plan("upload", "78720", f, "script")
        self.assertNotEqual(p.returncode, 0)
        self.assertIn("efeito block", p.stderr)
        uploads = [c for c in self.fake.calls if c[1].endswith("/ExecutionPlan/p1/artifacts")]
        self.assertEqual(len(uploads), 1)   # o bloqueado não subiu
        # .md não passa pela conferência; PRMake sem o recurso não trava o .sql
        md = os.path.join(self.cards, "78720", "analises", "analise.md")
        write(md, "# x\n")
        self.assertEqual(self.plan("upload", "78720", md, "analysis").returncode, 0)
        self.fake.check_status = 404
        self.assertEqual(self.plan("upload", "78720", f, "script").returncode, 0)
        self.assertEqual(len([c for c in self.fake.calls if c[1].endswith("/artifacts")]), 3)

    def test_case_upload_sql(self):
        f = os.path.join(self.home, "proposta.sql")
        write(f, SQL)
        run = lambda: subprocess.run(["bash", CASE, "upload", "78720", f], capture_output=True, text=True,
                                     env=self.env(), timeout=60)
        p = run()
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("FK7 [warn]", p.stdout)
        self.assertIn("arquivo #7", p.stdout)
        self.assertEqual(self.fake.checks()[0]["source"], "specialist")
        self.fake.check = check_resp("block")
        p = run()
        self.assertEqual(p.returncode, 1)
        self.assertIn("efeito block", p.stderr)
        self.assertEqual(len([c for c in self.fake.calls if c[1].endswith("/CaseChat/78720/files")]), 1)


if __name__ == "__main__":
    unittest.main()
