"""0097/S4: GAP do gerador (fala do pacote/área/subagente/Figma, não do sistema) — removido no `juntar`, avisado no
`check`; lista idêntica à do PRMake (`ReverseGeneratorGaps.cs`, teste de paridade).

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import os
import re
import tempfile
import textwrap
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.join(HERE, "..", "..", "skills", "engenharia-reversa", "scripts")
CSHARP = os.path.join(HERE, "..", "..", "CIME", "modules", "Solvace.Knowledge", "src", "solvace.knowledge.domain", "Reverse",
                      "ReverseGeneratorGaps.cs")


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SCRIPTS, f"{name}.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


re_gaps = load("re_gaps")


def csharp_list(text, name):
    m = re.search(name + r"\s*=\s*\[(.*?)\];", text, re.S)
    return re.findall(r'"([^"]*)"', m.group(1)) if m else None


class ParityTests(unittest.TestCase):
    @unittest.skipUnless(os.path.exists(CSHARP), "ReverseGeneratorGaps.cs ausente")
    def test_same_phrases_as_the_prmake(self):
        text = open(CSHARP, encoding="utf-8").read()
        self.assertEqual(csharp_list(text, "Phrases"), re_gaps.PHRASES)
        self.assertEqual(csharp_list(text, "PhrasesWithoutEvidence"), re_gaps.PHRASES_WITHOUT_EVIDENCE)

    def test_normalize_like_the_csharp(self):
        self.assertEqual(re_gaps.normalize("  Não está no PACOTE!! (área)  "), "nao esta no pacote area")
        self.assertEqual(re_gaps.normalize("Orçamento-da_área"), "orcamento da area")


class GapTests(unittest.TestCase):
    DOC = textwrap.dedent('''\
        # Funcional
        ## Lacunas e pontos a confirmar
        ### GAP-001 — Endpoint de gravação não está no pacote
        - O controller fica fora desta área.
        ### GAP-002 — Cache não é invalidado ao excluir
        - **Onde:** `solvace-core/x/RoomService.cs:40`
        ### GAP-003 — Não foi possível ler o método
        - sem detalhe
        ### GAP-004 — Não foi possível ler o arquivo de configuração no runtime
        - **Onde:** `solvace-core/x/appsettings.json:3`
        ### GAP-005 — Itens do subagente banco
        texto
        ### GAP-006 — (removido) já tirado antes
        ## Outra seção
        texto final
        ''')

    def test_detects_only_process_gaps(self):
        found = [g for g, *_ in re_gaps.generator_gaps(self.DOC)]
        self.assertEqual(found, ["GAP-001", "GAP-003", "GAP-005"])

    def test_remove_keeps_published_ids_as_removed_stubs(self):
        with tempfile.TemporaryDirectory() as d:
            doc, pub = os.path.join(d, "doc.md"), os.path.join(d, "pub.md")
            open(doc, "w", encoding="utf-8").write(self.DOC)
            open(pub, "w", encoding="utf-8").write("### GAP-005 — Itens do subagente banco\n")
            re_gaps.remove(doc, pub)
            out = open(doc, encoding="utf-8").read()
        self.assertNotIn("GAP-001", out)
        self.assertNotIn("GAP-003", out)
        self.assertIn("### GAP-005 — (removido) lacuna do processo de geração", out)
        self.assertIn("### GAP-002 — Cache não é invalidado", out)
        self.assertIn("### GAP-004", out)
        self.assertIn("## Outra seção\ntexto final", out)


if __name__ == "__main__":
    unittest.main()
