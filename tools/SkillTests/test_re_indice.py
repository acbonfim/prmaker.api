"""0097/S3: índice técnico (sem IA) — inventário da origin/master → documento `tecnico` com END/ROT/TAB/DBO/CHV/MSG,
`Onde:` com repo e caminho a partir da raiz, commit e IDs estáveis (a mesma chave mantém o ID; novos pegam o próximo).

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.join(HERE, "..", "..", "skills", "engenharia-reversa", "scripts")
sys.path.insert(0, SCRIPTS)


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SCRIPTS, f"{name}.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


re_tool = load("re_tool")
re_indice = load("re_indice")
ENV = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t", "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@t"}


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(textwrap.dedent(text))


class IndiceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        back = self.back = os.path.join(self.tmp.name, "revamp-Kaizen")
        write(os.path.join(back, "src/Api/Controllers/KaizenController.cs"), '''\
            [Route("api/v1/kaizen")]
            public class KaizenController : ControllerBase
            {
                [HttpGet("{id}")]
                public async Task<IActionResult> Get(int id) { var s = "SELECT * FROM TB_KZN_IDEIA"; return Ok(); }
                [HttpPost]
                public IActionResult Save(Ideia i) { if (i == null) throw new DomainException("Informe a ideia antes de salvar."); return Ok(); }
            }
            ''')
        write(os.path.join(back, "src/Api/appsettings.json"), '{"Kaizen": {"MaxAttachments": 5}, "ConnectionStrings": {"Db": "segredo"}}\n')
        write(os.path.join(back, "src/Data/Repo.cs"), 'var x = "EXEC STP_KZN_FECHA"; // TB_KZN_IDEIA\n')
        front = os.path.join(self.tmp.name, "apps")
        write(os.path.join(front, "projects/kaizen/src/app/app-routing.module.ts"), '''\
            const routes: Routes = [
              { path: 'ideia/:id', component: IdeiaDetailComponent },
              {
                path: 'admin',
                loadChildren: () =>
                  import('./admin/admin.module').then(
                    (m) => m.AdminModule
                  ),
              },
            ];
            ''')
        for root, name in ((back, "revamp-Kaizen"), (front, "edv-solvace-apps")):
            subprocess.run(["git", "init", "-q", "-b", "master", root], check=True)
            subprocess.run(["git", "-C", root, "remote", "add", "origin", f"https://x/{name}.git"], check=True)
            subprocess.run(["git", "-C", root, "add", "-A"], check=True)
            subprocess.run(["git", "-C", root, "commit", "-qm", "v1"], check=True, env=ENV)
        self.inv = re_tool.inventory([("backend", back), ("frontend", os.path.join(front, "projects/kaizen"))])

    def tearDown(self):
        self.tmp.cleanup()

    def gen(self, published=""):
        rows = re_indice.Index(self.inv, "revamp-kaizen", "2026-11-18").build()
        return re_indice.render(rows, "revamp-kaizen", published, ["revamp-Kaizen@abc1234"], "2026-11-18")

    def test_kinds_where_and_commit(self):
        text, counts, _ = self.gen()
        self.assertEqual(counts["END"], 2)
        self.assertEqual(counts["ROT"], 2)
        self.assertEqual(counts["TAB"], 1)
        self.assertEqual(counts["DBO"], 1)
        self.assertEqual(counts["MSG"], 1)
        self.assertIn("### END-001 — GET api/v1/kaizen/{id}", text)
        self.assertIn("- **Onde:** `revamp-Kaizen/src/Api/Controllers/KaizenController.cs:4`", text)
        self.assertIn("- **Tabelas:** TB_KZN_IDEIA", text)
        self.assertRegex(text, r"- \*\*Commit:\*\* revamp-Kaizen@[0-9a-f]{7}")
        self.assertIn("/ideia/:id → IdeiaDetailComponent", text)
        self.assertIn("/admin → AdminModule", text)
        self.assertIn("`edv-solvace-apps/projects/kaizen/src/app/app-routing.module.ts:2`", text)
        self.assertIn("### DBO-001 — procedure dbo.STP_KZN_FECHA", text)
        self.assertIn('### MSG-001 — "Informe a ideia antes de salvar."', text)
        self.assertIn("Kaizen:MaxAttachments", text)
        self.assertNotIn("segredo", text)   # valor de configuração nunca entra
        for heading in re_indice.SECTIONS.values():
            self.assertIn(f"## {heading}", text)

    def test_ids_are_stable_and_new_ones_take_the_next_number(self):
        first, _, _ = self.gen()
        published = first.replace("### END-001 — GET api/v1/kaizen/{id}", "### END-007 — GET api/v1/kaizen/{id}")
        again, _, reused = self.gen(published)
        self.assertIn("### END-007 — GET api/v1/kaizen/{id}", again)
        self.assertIn("### END-002 — POST api/v1/kaizen", again)
        self.assertEqual(reused, sum(1 for line in first.splitlines() if line.startswith("### ")))
        # um endpoint que não existia no publicado pega o próximo depois do maior (8)
        only_get = "\n".join(l for l in published.splitlines() if "POST api/v1/kaizen" not in l)
        third, _, _ = self.gen(only_get)
        self.assertIn("### END-008 — POST api/v1/kaizen", third)

    def test_cli_writes_markdown_and_summary_with_commits(self):
        inv_path = os.path.join(self.tmp.name, "inv.json")
        json.dump(self.inv, open(inv_path, "w", encoding="utf-8"))
        out, summ = os.path.join(self.tmp.name, "t.md"), os.path.join(self.tmp.name, "r.json")
        self.assertEqual(re_indice.main(["re_indice.py", "gerar", inv_path, "--modulo", "revamp-kaizen", "--out", out, "--json", summ]), 0)
        s = json.load(open(summ, encoding="utf-8"))
        self.assertEqual([c["repository"] for c in s["commits"]], ["revamp-Kaizen", "edv-solvace-apps"])
        self.assertEqual(s["items"], sum(s["counts"].values()))
        self.assertTrue(open(out, encoding="utf-8").read().startswith("# Índice técnico — revamp-kaizen"))


if __name__ == "__main__":
    unittest.main()
