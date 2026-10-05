"""Testes da cobertura de termos do glossario (skills/engenharia-reversa/scripts/re_tool.py) — 0056.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = importlib.util.spec_from_file_location(
    "re_tool", os.path.join(HERE, "..", "..", "skills", "engenharia-reversa", "scripts", "re_tool.py"))
re_tool = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(re_tool)

DOC = """# Levantamento funcional — legado-rca

## Glossário
### GLO-001 — A3
- **Sinônimos:** SA3, RCA, 5S
Relatório de análise de causa raiz.

### GLO-002 — Elaborador
- **Sinônimos:** criador do A3
Quem cria.

## Lacunas e pontos a confirmar
### GAP-020 — Termos fora do glossário
Salvar rascunho e Kaizen são de interface/outro módulo.

### GAP-021 — Exportação em PDF
- **Termos:** Filtro avançado
Não encontrada no código.

### GAP-022 — Outra lacuna
Fala de Botão azul mas não lista termos.
"""


def termo(name):
    return {"cat": "termo", "name": name, "file": "x.asp", "line": 1, "detail": "tela"}


class TermCoverageTests(unittest.TestCase):
    def cover(self, *names):
        inv = {"items": [termo(n) for n in names]}
        return re_tool.coverage(inv, DOC, "funcional", 50)

    def test_two_character_sigla_counts_when_it_is_in_the_glossary(self):
        r = self.cover("A3", "5S", "SA3")
        self.assertEqual(3, r["covered"])
        self.assertEqual([], [m["name"] for m in r["missing"]])

    def test_short_lowercase_word_is_still_ignored(self):
        self.assertEqual([], re_tool.needles(termo("de")))
        self.assertEqual(["a3"], re_tool.needles(termo("A3")))

    def test_short_sigla_matches_only_as_a_whole_word(self):
        inv = {"items": [termo("A3")]}
        doc = "## Glossário\n### GLO-001 — SA3\n- **Sinônimos:** RCA, ba3x\n"
        self.assertEqual(0, re_tool.coverage(inv, doc, "funcional", 50)["covered"])

    def test_terms_listed_in_a_glossary_gap_count_and_are_reported(self):
        r = self.cover("Salvar rascunho", "Kaizen", "Filtro avançado", "Botão azul")
        self.assertEqual(3, r["covered"])
        self.assertEqual(["Botão azul"], [m["name"] for m in r["missing"]])  # GAP sem "termos" no título nem **Termos:**
        self.assertEqual({("Salvar rascunho", "GAP-020"), ("Kaizen", "GAP-020"), ("Filtro avançado", "GAP-021")},
                         {(o["name"], o["gap"]) for o in r["outsideGlossary"]})

    def test_glossary_terms_are_not_reported_as_outside(self):
        r = self.cover("Elaborador")
        self.assertEqual(1, r["covered"])
        self.assertEqual([], r["outsideGlossary"])

    def test_question_words_keep_short_siglas_with_digit(self):
        w = re_tool.words("Como criar um A3 no 5S?")
        self.assertIn("a3", w)
        self.assertIn("5s", w)
        self.assertNotIn("um", w)


if __name__ == "__main__":
    unittest.main()
