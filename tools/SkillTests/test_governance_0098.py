"""0098: hook de governança do Claude Code (skills/_tooling/prmake-governance.py) — casamento das ferramentas, card da
branch, decisão block/warn/allow contra um PRMake falso local, fail-open, fila em lote e caminhos do Windows.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import json
import os
import socket
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "..", "skills", "_tooling", "prmake-governance.py")
TOOL = os.path.join(HERE, "..", "..", "skills", "_tooling", "prmake-skills.sh")

spec = importlib.util.spec_from_file_location("prmake_governance", SCRIPT)
gov = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gov)


def hooks_config(**over):
    cfg = {
        "mode": "enforce", "failOpen": True, "timeoutMs": 1500, "cacheSeconds": 60, "version": "v1",
        "toolMatchers": ["Bash", "Read|Edit|Write|MultiEdit|NotebookEdit"],
        "hooks": [
            {"key": "claude-session", "event": "claude:SessionStart", "matcher": "", "action": "record", "config": {}},
            {"key": "claude-prompt", "event": "claude:UserPromptSubmit", "matcher": "", "action": "record", "config": {}},
            {"key": "claude-pretool", "event": "claude:PreToolUse", "matcher": "Bash|Read|Edit|Write|MultiEdit|NotebookEdit",
             "action": "evaluate", "config": {}},
            {"key": "claude-posttool", "event": "claude:PostToolUse", "matcher": "Bash|Write|Edit|MultiEdit|mcp__prmake__*",
             "action": "record", "config": {}},
            {"key": "claude-stop", "event": "claude:Stop", "matcher": "", "action": "record", "config": {}},
        ],
    }
    cfg.update(over)
    return cfg


class FakePrmake:
    """PRMake falso: GET Governance/hooks/config, POST Governance/evaluate e POST Governance/hooks/events."""

    def __init__(self):
        self.config = hooks_config()
        self.decision = {"decision": "allow", "mode": "enforce", "reason": None, "rules": [], "wouldDecide": "allow"}
        self.events_status = 200
        self.calls = []        # (método, caminho, corpo, x-api-key)
        self.event_batches = []
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def _send(self, status, body=None):
                raw = json.dumps(body).encode() if body is not None else b""
                self.send_response(status)
                self.send_header("content-type", "application/json")
                self.send_header("content-length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

            def do_GET(self):
                fake.calls.append(("GET", self.path, None, self.headers.get("x-api-key")))
                if self.path.endswith("/Governance/hooks/config"):
                    return self._send(200, fake.config)
                self._send(404, {})

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers.get("content-length") or 0)) or b"null")
                fake.calls.append(("POST", self.path, body, self.headers.get("x-api-key")))
                if self.path.endswith("/Governance/evaluate"):
                    return self._send(200, fake.decision)
                if self.path.endswith("/Governance/hooks/events"):
                    if fake.events_status == 200:
                        fake.event_batches.append(body)
                    return self._send(fake.events_status, {"accepted": len(body)} if fake.events_status == 200 else {})
                self._send(404, {})

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = "http://127.0.0.1:%d/api/v1" % self.server.server_address[1]

    def count(self, suffix):
        return sum(1 for c in self.calls if c[1].endswith(suffix))

    def evaluated(self):
        return [c[2] for c in self.calls if c[1].endswith("/Governance/evaluate")]

    def stop(self):
        self.server.shutdown()
        self.server.server_close()


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def save(path, data):
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(data if isinstance(data, str) else json.dumps(data))


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.home = self.tmp.name
        self.prmake = os.path.join(self.home, ".prmake")
        self.fake = FakePrmake()
        # pasta de trabalho numa branch de bug (o card sai dela)
        self.repo = os.path.join(self.home, "repos", "app")
        os.makedirs(os.path.join(self.repo, ".git"))
        with open(os.path.join(self.repo, ".git", "HEAD"), "w") as fh:
            fh.write("ref: refs/heads/bugfix/78720-qa\n")

    def tearDown(self):
        self.fake.stop()
        self.tmp.cleanup()

    def env(self, **extra):
        env = {k: v for k, v in os.environ.items() if not k.startswith(("PRMAKE_", "CLAUDE_"))}
        env.update({"HOME": self.home, "USERPROFILE": self.home, "PRMAKE_HOME": self.prmake, "PRMAKE_TOKEN": "tk-123",
                    "PRMAKE_API_BASE": self.fake.base, "TMPDIR": self.home})
        env.update({k: v for k, v in extra.items() if v is not None})
        return env

    def hook(self, data, *args, **env):
        data = dict({"session_id": "s-1", "cwd": self.repo}, **data)
        p = subprocess.run([sys.executable, SCRIPT, *args], input=json.dumps(data).encode(), capture_output=True,
                           env=self.env(**env), timeout=20)
        self.assertEqual(p.returncode, 0, p.stderr.decode())
        out = p.stdout.decode().strip()
        return json.loads(out) if out else None

    def pre(self, command, tool="Bash", **env):
        return self.hook({"hook_event_name": "PreToolUse", "tool_name": tool, "tool_input": {"command": command},
                          "tool_use_id": "tu-%s" % abs(hash(command))}, **env)


class MatchingTests(unittest.TestCase):
    def test_glob_like_the_server(self):
        self.assertTrue(gov.glob_match("Bash", "bash"))
        self.assertTrue(gov.glob_match("Read|Edit|Write", "Edit"))
        self.assertTrue(gov.glob_match("Read | Edit", "Edit"))
        self.assertTrue(gov.glob_match("mcp__prmake__*", "mcp__prmake__prmake_advance"))
        self.assertTrue(gov.glob_match("*", "Glob"))
        self.assertTrue(gov.glob_match("", "qualquer"))
        self.assertTrue(gov.glob_match("Note?ookEdit", "NotebookEdit"))
        self.assertFalse(gov.glob_match("Write", "WriteX"))
        self.assertFalse(gov.glob_match("mcp__prmake__*", "mcp__outro__x"))
        self.assertFalse(gov.glob_match("Bash.*", "Bash"))  # ponto é literal, não regex
        self.assertTrue(gov.matches_any(["Bash", "Read|Edit"], "Read"))
        self.assertFalse(gov.matches_any([], "Bash"))

    def test_card_from_branch(self):
        self.assertEqual(gov.card_from_branch("hotfix/78720"), 78720)
        self.assertEqual(gov.card_from_branch("bugfix/77881-qa"), 77881)
        self.assertEqual(gov.card_from_branch("feature/77988-rv"), 77988)
        self.assertEqual(gov.card_from_branch("Feature/0098"), 98)
        self.assertIsNone(gov.card_from_branch("feature/governanca"))
        self.assertIsNone(gov.card_from_branch("master"))
        self.assertIsNone(gov.card_from_branch(None))
        self.assertEqual(gov.branch_from_head("ref: refs/heads/hotfix/1-rv\n"), "hotfix/1-rv")
        self.assertIsNone(gov.branch_from_head("3f2a9c0d\n"))  # HEAD destacado

    def test_branch_from_repo_and_worktree(self):
        with tempfile.TemporaryDirectory() as t:
            repo = os.path.join(t, "repo")
            os.makedirs(os.path.join(repo, ".git", "worktrees", "wt"))
            with open(os.path.join(repo, ".git", "HEAD"), "w") as fh:
                fh.write("ref: refs/heads/hotfix/111\n")
            sub = os.path.join(repo, "src", "mod")
            os.makedirs(sub)
            self.assertEqual(gov.branch_of(sub), "hotfix/111")
            # worktree: .git é arquivo com gitdir relativo
            with open(os.path.join(repo, ".git", "worktrees", "wt", "HEAD"), "w") as fh:
                fh.write("ref: refs/heads/bugfix/222-qa\n")
            wt = os.path.join(t, "wt")
            os.makedirs(wt)
            with open(os.path.join(wt, ".git"), "w") as fh:
                fh.write("gitdir: ../repo/.git/worktrees/wt\n")
            self.assertEqual(gov.branch_of(wt), "bugfix/222-qa")
            old = os.environ.pop("PRMAKE_CARD", None)
            try:
                self.assertEqual(gov.card_number(wt), 222)
                os.environ["PRMAKE_CARD"] = "#333"
                self.assertEqual(gov.card_number(wt), 333)  # o executor manda o card por variável
            finally:
                os.environ.pop("PRMAKE_CARD", None)
                if old is not None:
                    os.environ["PRMAKE_CARD"] = old
        self.assertIsNone(gov.branch_of(None))

    def test_windows_paths(self):
        self.assertTrue(gov.is_abs("C:/Users/ana/.claude"))
        self.assertTrue(gov.is_abs("D:\\repos\\app"))
        self.assertFalse(gov.is_abs("repos/app"))
        self.assertEqual(gov.norm_path("C:\\Users\\ana\\.aws\\credentials"), "C:/Users/ana/.aws/credentials")
        self.assertEqual(gov.tool_path({"notebook_path": "C:\\n\\a.ipynb"}), "C:/n/a.ipynb")
        self.assertEqual(gov.tool_path({"file_path": "/home/ana/x.cs"}), "/home/ana/x.cs")
        self.assertIsNone(gov.tool_path({"command": "ls"}))
        self.assertEqual(gov.branch_from_head("ref: refs/heads/hotfix/9\r\n"), "hotfix/9")  # HEAD com CRLF

    def test_input_summary(self):
        self.assertEqual(gov.tool_input_text("Bash", {"command": "git status"}, 4000), "git status")
        self.assertEqual(len(gov.tool_input_text("Bash", {"command": "x" * 900}, 500)), 500)
        big = gov.tool_input_text("Write", {"file_path": "/a.cs", "content": "y" * 10_000}, 4000)
        self.assertLessEqual(len(big), 4000)
        self.assertIn('"file_path": "/a.cs"', big)
        self.assertIn("(+9700)", big)


class DecisionTests(Base):
    def test_block_denies_with_rule_and_reason(self):
        self.fake.decision = {"decision": "block", "mode": "enforce", "reason": "Push forçado não é permitido.",
                              "rules": [{"key": "no-force-push", "name": "Sem push forçado", "effect": "block",
                                         "message": "Push forçado não é permitido."}], "wouldDecide": "block"}
        out = self.pre("git push --force origin bugfix/78720-qa")
        hso = out["hookSpecificOutput"]
        self.assertEqual(hso["hookEventName"], "PreToolUse")
        self.assertEqual(hso["permissionDecision"], "deny")
        self.assertIn("Barrado pela governança do PRMake: Push forçado não é permitido. (regra no-force-push)",
                      hso["permissionDecisionReason"])
        body = self.fake.evaluated()[0]
        self.assertEqual(body["source"], "hook")
        self.assertEqual(body["event"], "PreToolUse")
        self.assertEqual(body["tool"], "Bash")
        self.assertEqual(body["input"], "git push --force origin bugfix/78720-qa")
        self.assertEqual(body["cardNumber"], 78720)
        self.assertEqual(body["sessionId"], "s-1")
        self.assertTrue(body["machine"])
        self.assertEqual(self.fake.calls[0][3], "tk-123")

    def test_warn_passes_with_context(self):
        self.fake.decision = {"decision": "warn", "mode": "enforce", "reason": None,
                              "rules": [{"key": "card-reset-audited", "name": "Reset", "effect": "warn",
                                         "message": "Confira se é isso mesmo."}], "wouldDecide": "warn"}
        out = self.pre("curl -X POST https://exemplo")
        self.assertNotIn("permissionDecision", out["hookSpecificOutput"])
        self.assertIn("Aviso da governança do PRMake: Confira se é isso mesmo. (regra card-reset-audited)",
                      out["hookSpecificOutput"]["additionalContext"])
        self.assertIn("card-reset-audited", out["systemMessage"])

    def test_allow_says_nothing(self):
        self.assertIsNone(self.pre("git status"))
        self.assertEqual(self.fake.count("/Governance/evaluate"), 1)

    def test_only_tools_the_rules_match(self):
        self.assertIsNone(self.hook({"hook_event_name": "PreToolUse", "tool_name": "Glob", "tool_input": {"pattern": "*.cs"}}))
        self.assertIsNone(self.hook({"hook_event_name": "PreToolUse", "tool_name": "mcp__prmake__prmake_advance",
                                     "tool_input": {"card": "1"}}))
        self.assertEqual(self.fake.count("/Governance/evaluate"), 0)
        # Read casa com o claude-pretool e com as regras: vai com o caminho do arquivo
        self.hook({"hook_event_name": "PreToolUse", "tool_name": "Read",
                   "tool_input": {"file_path": "C:\\Users\\ana\\.aws\\credentials"}})
        body = self.fake.evaluated()[0]
        self.assertEqual(body["path"], "C:/Users/ana/.aws/credentials")
        self.assertIn("credentials", body["input"])

    def test_source_executor_and_specialist(self):
        self.pre("ls", PRMAKE_EXECUTOR="1", PRMAKE_CARD="555")
        self.pre("ls -la", PRMAKE_EXECUTOR="1", PRMAKE_SPECIALIST="1")
        bodies = self.fake.evaluated()
        self.assertEqual(bodies[0]["source"], "executor")
        self.assertEqual(bodies[0]["cardNumber"], 555)
        self.assertEqual(bodies[1]["source"], "specialist")

    def test_mode_off_does_nothing(self):
        self.fake.config = hooks_config(mode="off")
        self.assertIsNone(self.pre("git push --force"))
        self.assertEqual(self.fake.count("/Governance/evaluate"), 0)

    def test_config_is_cached(self):
        self.pre("ls")
        self.pre("pwd")
        self.assertEqual(self.fake.count("/Governance/hooks/config"), 1)
        cache = load(os.path.join(self.prmake, "governance.json"))
        self.assertEqual(cache["config"]["version"], "v1")
        cache["fetchedAt"] = time.time() - 120  # passou do cacheSeconds
        save(os.path.join(self.prmake, "governance.json"), cache)
        self.pre("whoami")
        self.assertEqual(self.fake.count("/Governance/hooks/config"), 2)

    def test_executor_hook_is_not_repeated(self):
        # dentro do executor 1.5.0 só o hook dele (--from-executor) trabalha
        self.assertIsNone(self.pre("git status", PRMAKE_GOVERNANCE_HOOK="executor"))
        self.assertEqual(len(self.fake.calls), 0)
        self.fake.decision = {"decision": "block", "mode": "enforce", "reason": "não", "rules": [], "wouldDecide": "block"}
        out = self.hook({"hook_event_name": "PreToolUse", "tool_name": "Bash", "tool_input": {"command": "x"}},
                        "--from-executor", PRMAKE_GOVERNANCE_HOOK="executor")
        self.assertEqual(out["hookSpecificOutput"]["permissionDecision"], "deny")


class FailOpenTests(Base):
    def test_server_down_lets_it_pass_quickly(self):
        start = time.time()
        out = self.pre("git push --force", PRMAKE_API_BASE="http://127.0.0.1:%d/api/v1" % free_port())
        self.assertIsNone(out)
        self.assertLess(time.time() - start, 8)
        self.assertTrue(os.path.exists(os.path.join(self.prmake, "governance.log")))

    def test_cached_config_and_evaluate_down(self):
        self.pre("ls")  # lê e guarda a config
        down = "http://127.0.0.1:%d/api/v1" % free_port()
        self.assertIsNone(self.pre("git push --force", PRMAKE_API_BASE=down))

    def test_fail_closed_when_admin_wants(self):
        self.fake.config = hooks_config(failOpen=False)
        self.pre("ls")
        down = "http://127.0.0.1:%d/api/v1" % free_port()
        out = self.pre("git push --force", PRMAKE_API_BASE=down)
        self.assertEqual(out["hookSpecificOutput"]["permissionDecision"], "deny")
        self.assertIn("failOpen", out["hookSpecificOutput"]["permissionDecisionReason"])

    def test_no_token_never_blocks(self):
        data = {"hook_event_name": "PreToolUse", "tool_name": "Bash", "tool_input": {"command": "git push -f"}}
        e = self.env()
        e.pop("PRMAKE_TOKEN")
        p = subprocess.run([sys.executable, SCRIPT], input=json.dumps(data).encode(), capture_output=True, env=e, timeout=20)
        self.assertEqual(p.returncode, 0)
        self.assertEqual(p.stdout.strip(), b"")
        self.assertEqual(len(self.fake.calls), 0)

    def test_garbage_stdin(self):
        p = subprocess.run([sys.executable, SCRIPT], input=b"nao e json", capture_output=True, env=self.env(), timeout=20)
        self.assertEqual(p.returncode, 0)
        self.assertEqual(p.stdout.strip(), b"")


class QueueTests(Base):
    def post(self, i, command="dotnet build"):
        tid = "tu-%d" % i
        self.hook({"hook_event_name": "PreToolUse", "tool_name": "Bash", "tool_input": {"command": command}, "tool_use_id": tid})
        self.hook({"hook_event_name": "PostToolUse", "tool_name": "Bash", "tool_input": {"command": command},
                   "tool_use_id": tid, "tool_response": {"stdout": "ok", "stderr": "", "interrupted": False}})

    def queue_lines(self):
        try:
            return read(os.path.join(self.prmake, "governance-queue.jsonl")).splitlines()
        except OSError:
            return []

    def test_batches_on_stop_without_prompt_text(self):
        self.hook({"hook_event_name": "UserPromptSubmit", "prompt": "texto secreto do pedido"})
        self.post(1, "git commit -m '" + "z" * 900 + "'")
        self.assertEqual(self.fake.event_batches, [])  # ainda na fila
        self.assertEqual(len(self.queue_lines()), 2)
        self.hook({"hook_event_name": "Stop", "stop_hook_active": False})
        self.assertEqual(len(self.fake.event_batches), 1)
        batch = self.fake.event_batches[0]
        self.assertEqual([e["event"] for e in batch], ["UserPromptSubmit", "PostToolUse", "Stop"])
        self.assertNotIn("texto secreto", json.dumps(batch))
        self.assertEqual(batch[0]["detail"], "pedido com 23 caracteres")
        post = batch[1]
        self.assertEqual(post["tool"], "Bash")
        self.assertLessEqual(len(post["input"]), 500)
        self.assertEqual(post["outcome"], "ok")
        self.assertIsInstance(post["durationMs"], int)
        self.assertEqual(post["cardNumber"], 78720)
        self.assertIn("startedAt", post)
        self.assertIn("finishedAt", post)
        self.assertNotIn("_t", post)
        self.assertIsInstance(batch[2]["durationMs"], int)  # duração do turno (do pedido ao fim)
        self.assertEqual(self.queue_lines(), [])

    def test_sends_after_30_events(self):
        for i in range(30):
            self.post(i)
        self.assertEqual(sum(len(b) for b in self.fake.event_batches), 30)

    def test_sends_after_60_seconds(self):
        self.post(1)
        q = os.path.join(self.prmake, "governance-queue.jsonl")
        e = json.loads(read(q).splitlines()[0])
        e["_t"] = time.time() - 61
        save(q, json.dumps(e) + "\n")
        self.post(2)
        # o próximo hook vê o evento velho e manda a fila (o PostToolUse do 2 fica para o próximo lote)
        self.assertEqual(len(self.fake.event_batches), 1)
        self.assertEqual(self.fake.event_batches[0][0]["input"], "dotnet build")
        self.assertEqual(len(self.queue_lines()), 1)

    def test_failed_send_keeps_events(self):
        self.fake.events_status = 500
        self.post(1)
        self.hook({"hook_event_name": "Stop"})
        self.assertEqual(self.fake.event_batches, [])
        sending = os.path.join(self.prmake, "governance-queue.jsonl.sending")
        self.assertEqual(len(read(sending).splitlines()), 2)
        self.fake.events_status = 200
        p = subprocess.run([sys.executable, SCRIPT, "flush"], capture_output=True, env=self.env(), timeout=20)
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertEqual(sum(len(b) for b in self.fake.event_batches), 2)
        self.assertFalse(os.path.exists(sending))

    def test_batch_limit_200(self):
        os.makedirs(self.prmake, exist_ok=True)
        with open(os.path.join(self.prmake, "governance-queue.jsonl"), "w") as fh:
            for i in range(450):
                fh.write(json.dumps({"event": "PostToolUse", "tool": "Bash", "_t": time.time()}) + "\n")
        p = subprocess.run([sys.executable, SCRIPT, "flush"], capture_output=True, env=self.env(), timeout=20)
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertEqual([len(b) for b in self.fake.event_batches], [200, 200, 50])

    def test_session_start_records_and_flushes(self):
        self.hook({"hook_event_name": "SessionStart", "source": "startup"})
        self.assertEqual(len(self.fake.event_batches), 1)
        evt = self.fake.event_batches[0][0]
        self.assertEqual(evt["event"], "SessionStart")
        self.assertIn("startup", evt["detail"])

    def test_disabled_hook_records_nothing(self):
        cfg = hooks_config()
        cfg["hooks"] = [h for h in cfg["hooks"] if h["key"] != "claude-posttool"]
        self.fake.config = cfg
        self.post(1)
        self.assertEqual(self.queue_lines(), [])


@unittest.skipIf(os.name == "nt", "o instalador é testado no bash")
class InstallerTests(unittest.TestCase):
    """prmake-skills.sh governance on/off: só os nossos hooks, idempotente, sem tocar nos de terceiros."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.home = self.tmp.name
        tool_dir = os.path.join(self.home, ".claude", "skills", ".prmake")
        os.makedirs(tool_dir)
        save(os.path.join(tool_dir, "prmake-governance.py"), read(SCRIPT))
        self.settings = os.path.join(self.home, ".claude", "settings.json")
        save(self.settings, {"model": "opus", "hooks": {
            "SessionStart": [{"hooks": [{"type": "command", "command": 'bash "$HOME/.claude/skills/.prmake/prmake-skills.sh" update --quiet', "timeout": 60}]}],
            "PreToolUse": [{"matcher": "Bash", "hooks": [{"type": "command", "command": "echo terceiro"}]}]}})

    def tearDown(self):
        self.tmp.cleanup()

    def tool(self, *args):
        env = {k: v for k, v in os.environ.items() if not k.startswith(("PRMAKE_", "CLAUDE_"))}
        env.update({"HOME": self.home, "PRMAKE_SKIP_MCP": "1", "PRMAKE_API_BASE": "http://127.0.0.1:%d/api/v1" % free_port()})
        return subprocess.run(["bash", TOOL, *args], capture_output=True, text=True, env=env, timeout=60)

    def ours(self):
        s = load(self.settings)
        return {ev: [h["command"] for g in groups for h in g.get("hooks", []) if "prmake-governance.py" in h["command"]]
                for ev, groups in s.get("hooks", {}).items()}

    def test_on_is_idempotent_and_off_removes_only_ours(self):
        self.assertEqual(self.tool("governance", "on").returncode, 0)
        self.tool("governance", "on")
        ours = self.ours()
        for ev in ("SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop"):
            self.assertEqual(len(ours[ev]), 1, ev)
            self.assertTrue(ours[ev][0].endswith("|| true"))  # script sumido nunca barra a ferramenta (exit 2)
        s = load(self.settings)
        self.assertEqual(len(s["hooks"]["SessionStart"]), 2)  # o update continua lá
        self.assertIn("echo terceiro", json.dumps(s["hooks"]["PreToolUse"]))
        status = self.tool("governance", "status").stdout
        self.assertIn("SessionStart", status)

        self.assertEqual(self.tool("governance", "off").returncode, 0)
        s = load(self.settings)
        self.assertNotIn("prmake-governance.py", json.dumps(s))
        self.assertIn("prmake-skills.sh", json.dumps(s["hooks"]["SessionStart"]))
        self.assertEqual(s["hooks"]["PreToolUse"], [{"matcher": "Bash", "hooks": [{"type": "command", "command": "echo terceiro"}]}])
        self.assertEqual(s["model"], "opus")
        self.assertIn("desligados", self.tool("governance", "status").stdout)


if __name__ == "__main__":
    unittest.main()
