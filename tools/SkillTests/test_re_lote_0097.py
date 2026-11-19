"""0097/S5: passada única (funcional + arquitetura) — áreas comuns com faixas de ID separadas por documento, um pacote
por área para os dois, consolidação sem código e referências cruzadas acompanhando os IDs compactados do outro documento.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.join(HERE, "..", "..", "skills", "engenharia-reversa", "scripts")
sys.path.insert(0, SCRIPTS)
sys.path.insert(0, HERE)


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SCRIPTS, f"{name}.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


re_pacote = load("re_pacote")
from test_re_pacote import Module, write  # noqa: E402  (módulo de exemplo com controller e views)


class LoteTests(unittest.TestCase):
    def setUp(self):
        self.m = Module()
        t = self.m.tmp.name
        self.inv_path = os.path.join(t, "inventario.json")
        with open(self.inv_path, "w", encoding="utf-8") as fh:
            json.dump(self.m.inv, fh)
        self.dirs = {"funcional": os.path.join(t, "funcional"), "arquitetura": os.path.join(t, "arquitetura")}
        for d in self.dirs.values():
            write(os.path.join(d, "modelo.md"), "## Resumo do módulo\n## Perfis e permissões\n## Regras de negócio\n## Visão técnica\n")
        self.lote_dir = os.path.join(t, "lote")
        self.plans, self.packs = re_pacote.lote(self.inv_path, [], ["RN-005"], "legado-kz", 6, 10,
                                                {"funcional": ["FN", "UC", "RN", "PRF", "GLO"], "arquitetura": ["API", "INT", "DB", "TEC"]},
                                                self.dirs, self.lote_dir, None, t)

    def tearDown(self):
        self.m.close()

    def test_one_pack_per_area_for_both_documents_with_disjoint_id_ranges(self):
        pf, pa = self.plans["funcional"], self.plans["arquitetura"]
        self.assertEqual([a["name"] for a in pf["areas"]], [a["name"] for a in pa["areas"]])
        self.assertEqual(len(self.packs), len(pf["areas"]))
        top_f = max([a["idRange"][1] for a in pf["areas"]] + [x["idRange"][1] for x in pf["specials"]])
        self.assertGreater(min(a["idRange"][0] for a in pa["areas"]), top_f)
        self.assertEqual(pf["idBase"], pa["idBase"])           # a compactação parte da mesma base
        self.assertTrue(os.path.exists(os.path.join(self.dirs["funcional"], "areas.json")))
        name, path, _, _ = self.packs[0]
        text = open(path, encoding="utf-8").read()
        self.assertIn(os.path.join(self.dirs["funcional"], f"parte-{name}.md"), text)
        self.assertIn(os.path.join(self.dirs["arquitetura"], f"parte-{name}.md"), text)
        self.assertIn(f"resumo-{name}.md", text)
        self.assertIn("## Código (trechos)", text)
        self.assertNotIn("PRF", text.split("**arquitetura**")[0].split("tipos que você cria:")[1].split("·")[0])   # PRF é da consolidação

    def test_consolidation_pack_has_titles_and_area_summaries(self):
        name = self.packs[0][0]
        write(os.path.join(self.dirs["funcional"], f"parte-{name}.md"), "## Regras de negócio\n### RN-101 — Aprovador obrigatório\n- x\n")
        write(os.path.join(self.dirs["arquitetura"], f"parte-{name}.md"), "## Integrações\n### INT-901 — Kaizen → Users\n- **Módulos:** revamp-users\n")
        write(os.path.join(self.lote_dir, f"resumo-{name}.md"), "- perfil Gestor (`Controllers/IdeiaController.cs:5`)\n")
        out = re_pacote.consolidar_pack(self.dirs, self.lote_dir, "legado-kz")
        text = open(out, encoding="utf-8").read()
        self.assertIn("RN-101 Aprovador obrigatório", text)
        self.assertIn("### INT-901 — Kaizen → Users", text)
        self.assertIn("perfil Gestor", text)
        self.assertIn("parte-consolidar.md", text)

    def test_cross_references_follow_the_other_documents_ids(self):
        t = self.m.tmp.name
        f = os.path.join(t, "f.md")
        write(f, "### RN-001 — Regra\n- usa API-2705 e API-2710; RN-001 continua\n")
        mapa = os.path.join(t, "mapa.json")
        re_pacote.save_map(mapa, "dedupe", {"API-2710": "API-2705"})
        re_pacote.save_map(mapa, "compact", {"API-2705": "API-012"})
        self.assertEqual(re_pacote.remapear(f, [mapa]), 2)
        self.assertEqual(open(f, encoding="utf-8").read(), "### RN-001 — Regra\n- usa API-012 e API-012; RN-001 continua\n")


if __name__ == "__main__":
    unittest.main()
