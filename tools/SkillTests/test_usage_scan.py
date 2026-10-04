"""Testes do leitor de consumo da sessao (skills/analisar-bug/scripts/usage_scan.py) — 0055.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import json
import os
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = importlib.util.spec_from_file_location(
    "usage_scan", os.path.join(HERE, "..", "..", "skills", "analisar-bug", "scripts", "usage_scan.py"))
usage_scan = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(usage_scan)

RE_ITEM = """--- legado-rca#RN-021 (funcional, v1)
### RN-021 — Finalizar exige causa raiz (só no front)
- **Onde:** `sa3_registro.asp:3184-3199` (`Validation("finishreview")`)
"""
CONTEXTO = """=== CARD 75091
titulo: nao finaliza
=== ENGENHARIA REVERSA (Base Solvace) — consulte ANTES do código
- legado-rca#UC-003 [Caso de uso] Elaborador finaliza o A3
- **Onde:** `helpers/Controllers/FishBoneController.cs:52`
=== PLANO
etapas...
"""


class Transcript:
    def __init__(self):
        self.lines, self.n = [], 0

    def call(self, name, inp, result, ts="2026-10-04T10:00:00Z", model="claude-opus-5-5"):
        self.n += 1
        tid = f"t{self.n}"
        self.lines.append({"type": "assistant", "timestamp": ts, "message": {
            "id": f"m{self.n}", "model": model, "usage": {"input_tokens": 10, "output_tokens": 5},
            "content": [{"type": "tool_use", "id": tid, "name": name, "input": inp}]}})
        self.lines.append({"type": "user", "timestamp": ts, "message": {
            "content": [{"type": "tool_result", "tool_use_id": tid, "content": [{"type": "text", "text": result}]}]}})
        return self

    def scan(self, since=""):
        with tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False, encoding="utf-8") as f:
            for line in self.lines:
                f.write(json.dumps(line) + "\n")
        try:
            return usage_scan.scan(f.name, since)
        finally:
            os.unlink(f.name)


def by_key(out):
    return {s["key"]: s for s in out["sources"]}


class UsageScanTests(unittest.TestCase):
    def test_reverse_engineering_first_then_confirming_the_cited_file_is_not_exploration(self):
        t = (Transcript()
             .call("Bash", {"command": "bash ~/.claude/skills/analisar-bug/scripts/prmake-plan.sh contexto 75091"}, CONTEXTO)
             .call("mcp__prmake__prmake_base_get", {"refs": "legado-rca#RN-021", "card": "75091"}, RE_ITEM)
             .call("Read", {"file_path": "/Users/x/repos/edv-solvace/solvace-asp/systems/sa3/sa3_registro.asp", "offset": 3180, "limit": 30}, "x" * 400)
             .call("Read", {"file_path": "/Users/x/repos/edv-solvace/helpers/Controllers/FishBoneController.cs"}, "y" * 200)
             .call("Read", {"file_path": "/Users/x/repos/edv-solvace/solvace-asp/systems/sa3/sa3_ajax.asp"}, "z" * 4000)
             .call("Grep", {"pattern": "finishreview", "path": "/Users/x/repos/edv-solvace"}, "a" * 80))
        out = t.scan()
        s = by_key(out)
        self.assertEqual(2, s["re"]["calls"])                 # bloco do contexto + item pelo MCP
        self.assertGreater(s["re"]["tokens"], 0)
        self.assertLess(s["re"]["tokens"], usage_scan.tokens(CONTEXTO) + usage_scan.tokens(RE_ITEM))  # so o bloco da ER
        self.assertEqual((2, 150), (s["code-confirm"]["calls"], s["code-confirm"]["tokens"]))
        self.assertEqual((1, 1000), (s["code-explore"]["calls"], s["code-explore"]["tokens"]))
        self.assertEqual(1, s["code-search"]["calls"])
        self.assertEqual(["systems/sa3/sa3_ajax.asp"], [e["path"] for e in out["exploredFiles"]])
        self.assertEqual((1, 1), (out["kbCalls"], out["searchCalls"]))   # comparativos antigos continuam

    def test_reading_code_before_the_reverse_engineering_item_counts_as_exploration(self):
        t = (Transcript()
             .call("Read", {"file_path": "/r/edv-solvace/solvace-asp/systems/sa3/sa3_registro.asp"}, "x" * 40)
             .call("mcp__prmake__prmake_base_get", {"refs": "legado-rca#RN-021"}, RE_ITEM))
        s = by_key(t.scan())
        self.assertEqual(1, s["code-explore"]["calls"])
        self.assertEqual(0, s["code-confirm"]["calls"])

    def test_old_base_sections_and_kc_are_separated_from_reverse_engineering(self):
        t = (Transcript()
             .call("mcp__prmake__prmake_base_get", {"refs": "legado-rca/modulos § Telas"}, "secao antiga")
             .call("mcp__prmake__prmake_base_get", {"refs": "ART-21"}, "artigo")
             .call("Bash", {"command": "bash ~/.claude/skills/base-solvace/scripts/kb.sh show legado-moc modulos"}, "base antiga")
             .call("Bash", {"command": "bash $KB re get legado-rca#RN-021 --card 1"}, RE_ITEM)
             .call("mcp__prmake__prmake_base_search", {"query": "finalizar"}, "legado-rca#RN-021 · Finalizar"))
        s = by_key(t.scan())
        self.assertEqual(3, s["base"]["calls"])
        self.assertEqual(2, s["re"]["calls"])

    def test_card_files_skills_attachments_and_variables_are_not_product_code(self):
        t = (Transcript()
             .call("Read", {"file_path": "/Users/x/.prmake/cards/75091/dados/card-resumo.txt"}, "c")
             .call("Read", {"file_path": "/Users/x/.prmake/cards/75091/anexos-prmake/print.png"}, "img")
             .call("Bash", {"command": "cat dados/consultas-mlh-4315.sql"}, "select")
             .call("Bash", {"command": "sed -n 1,40p $D/prmake-publish.sh"}, "script")
             .call("Read", {"file_path": "/Users/x/repos/solvace/.prmake-wt/75067/edv-solvace/solvace-asp/systems/moc/moc_registro.asp"}, "w" * 8))
        out = t.scan()
        s = by_key(out)
        self.assertEqual(1, s["code-explore"]["calls"])       # so o worktree da correcao (codigo do produto)
        self.assertEqual("systems/moc/moc_registro.asp", out["exploredFiles"][0]["path"])

    def test_since_ignores_what_the_session_did_for_another_plan(self):
        t = (Transcript()
             .call("Read", {"file_path": "/r/app/src/a.ts"}, "x" * 40, ts="2026-10-04T09:00:00Z")
             .call("Read", {"file_path": "/r/app/src/b.ts"}, "x" * 40, ts="2026-10-04T11:00:00Z"))
        out = t.scan(since="2026-10-04T10:00:00Z")
        self.assertEqual(1, by_key(out)["code-explore"]["calls"])
        self.assertEqual(1, out["turns"])

    def test_explore_subagent_counts_as_code_exploration(self):
        t = Transcript().call("Agent", {"subagent_type": "Explore", "prompt": "ache onde finaliza"}, "resumo " * 50)
        out = t.scan()
        self.assertEqual(1, by_key(out)["code-explore"]["calls"])
        self.assertEqual("(subagente Explore)", out["exploredFiles"][0]["path"])


if __name__ == "__main__":
    unittest.main()
