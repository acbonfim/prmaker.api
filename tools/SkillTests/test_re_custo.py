"""0097/S5: custo e tempo da geração por documento a partir dos transcripts (sessão principal + subagentes).

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import json
import os
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.join(HERE, "..", "..", "skills", "engenharia-reversa", "scripts")


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SCRIPTS, f"{name}.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


re_custo = load("re_custo")


def assistant(ts, mid, cmd=None, read=1000, write=100, out=50, model="claude-sonnet-x"):
    content = [{"type": "tool_use", "name": "Bash", "input": {"command": cmd}}] if cmd else [{"type": "text", "text": "ok"}]
    return {"type": "assistant", "timestamp": ts, "message": {"id": mid, "model": model, "content": content, "usage": {
        "input_tokens": 3, "cache_read_input_tokens": read, "cache_creation_input_tokens": write, "output_tokens": out}}}


def user(ts, text):
    return {"type": "user", "timestamp": ts, "message": {"role": "user", "content": text}}


def dump(path, rows):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        for r in rows:
            fh.write(json.dumps(r) + "\n")


class CustoTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        proj = os.path.join(self.tmp.name, "-Users-x-repos")
        # sessão principal: start funcional, depois start arquitetura; a mesma mensagem (id) repetida por bloco conta uma vez
        main = [user("2026-10-20T10:00:00Z", "/engenharia-reversa legado-x funcional"),
                assistant("2026-10-20T10:00:10Z", "m1", 'bash "$RE" start legado-x funcional', read=5000),
                assistant("2026-10-20T10:00:10Z", "m1", None, read=5000),
                assistant("2026-10-20T10:02:00Z", "m2", "bash $RE areas legado-x funcional", read=6000),
                assistant("2026-10-20T11:00:00Z", "m3", "bash $RE start legado-x arquitetura", read=7000),   # pausa > 5 min: não conta
                assistant("2026-10-20T11:03:00Z", "m4", "bash $RE start legado-y funcional", read=9999),     # outro módulo
                assistant("2026-10-20T11:04:00Z", "m5", None, read=9999)]
        dump(os.path.join(proj, "s1.jsonl"), main)
        dump(os.path.join(proj, "s1", "subagents", "agent-a.jsonl"), [
            user("2026-10-20T10:01:00Z", "Subagente da engenharia reversa — modulo legado-x, documento funcional, area cadastro. Leia..."),
            assistant("2026-10-20T10:01:30Z", "a1", None, read=20000, out=3000),
            assistant("2026-10-20T10:03:00Z", "a2", None, read=30000, out=4000)])
        dump(os.path.join(proj, "s1", "subagents", "agent-b.jsonl"), [
            user("2026-10-20T10:01:00Z", "Subagente da engenharia reversa — modulo legado-y, documento funcional, area z."),
            assistant("2026-10-20T10:01:30Z", "b1", None, read=777777)])
        # sessão do lote (0097): principal + subagente com "documentos funcional+arquitetura"
        dump(os.path.join(proj, "s2.jsonl"), [assistant("2026-11-20T09:00:00Z", "l1", "bash $RE lote legado-x", read=1000),
                                             assistant("2026-11-20T09:01:00Z", "l2", None, read=1000)])
        dump(os.path.join(proj, "s2", "subagents", "agent-c.jsonl"), [
            user("2026-11-20T09:00:30Z", "Subagente da engenharia reversa — modulo legado-x, documentos funcional+arquitetura, area a. Leia"),
            assistant("2026-11-20T09:02:00Z", "c1", None, read=40000)])
        self.projects = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def test_tokens_and_active_time_by_document(self):
        data = re_custo.collect("legado-x", self.projects)
        f = data["funcional"]
        self.assertEqual(f["cache_read"], 5000 + 6000 + 20000 + 30000)
        self.assertEqual(f["mainCacheRead"], 11000)
        self.assertEqual(f["subagents"], 1)
        self.assertEqual(f["calls"], 4)
        self.assertEqual(f["output"], 50 + 50 + 3000 + 4000)
        self.assertAlmostEqual(f["minutes"], 3.0, places=1)   # 10:00:00 → 10:03:00, sem pausas longas
        self.assertEqual(data["arquitetura"]["cache_read"], 7000)
        self.assertNotIn(9999, [d["cache_read"] for d in data.values()])
        self.assertEqual(data["lote"]["cache_read"], 42000)
        self.assertEqual(data["lote"]["subagents"], 1)

    def test_since_filters_sessions_and_report_has_total(self):
        data = re_custo.collect("legado-x", self.projects, since=re_custo.ts("2026-11-01T00:00:00Z"))
        self.assertEqual(list(data), ["lote"])
        text = re_custo.report("legado-x", re_custo.collect("legado-x", self.projects))
        self.assertIn("TOTAL", text)
        self.assertIn("funcional", text)


if __name__ == "__main__":
    unittest.main()
