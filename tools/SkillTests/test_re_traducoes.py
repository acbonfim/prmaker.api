"""Testes do leitor de traducoes do Multilingual (skills/engenharia-reversa/scripts/re_traducoes.py) — 0056."""
import importlib.util
import json
import os
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = importlib.util.spec_from_file_location(
    "re_traducoes", os.path.join(HERE, "..", "..", "skills", "engenharia-reversa", "scripts", "re_traducoes.py"))
re_traducoes = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(re_traducoes)


def tmp_json(data):
    f = tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8")
    json.dump(data, f)
    f.close()
    return f.name


class TraducoesTests(unittest.TestCase):
    def test_credentials_accept_the_aws_secret_as_it_comes(self):
        p = tmp_json({"prod": {"Host": "h", "Port": 5432, "DbName": "mlg", "UserName": "u", "Password": "p"}})
        self.assertEqual({"host": "h", "port": 5432, "dbname": "mlg", "user": "u", "password": "p"}, re_traducoes.credentials(p, "prod"))
        self.assertIsNone(re_traducoes.credentials(p, "dev"))
        os.unlink(p)

    def test_credentials_in_lowercase_and_incomplete_block(self):
        p = tmp_json({"prod": {"host": "h", "dbname": "mlg", "username": "u", "password": "p"}, "dev": {"host": "h"}})
        self.assertEqual(5432, re_traducoes.credentials(p, "prod")["port"])
        self.assertIsNone(re_traducoes.credentials(p, "dev"))
        self.assertIsNone(re_traducoes.credentials("/nao/existe.json", "prod"))
        os.unlink(p)

    def test_module_terms_come_from_labels_menus_and_application_without_duplicates(self):
        inv = tmp_json({"items": [{"cat": "rotulo", "name": "Elaborador"}, {"cat": "rotulo", "name": "elaborador"},
                                  {"cat": "tabela", "name": "TB_SA3_A3"}, {"cat": "rotulo", "name": "Causa raiz"}]})
        cat = tmp_json({"menus": [{"item": "Busca de A3", "grp": "Melhoria"}], "application": {"name": "A3", "alias": "SA3"}})
        self.assertEqual(["Elaborador", "Causa raiz", "Busca de A3", "Melhoria", "A3", "SA3"], re_traducoes.module_terms(inv, cat))
        os.unlink(inv); os.unlink(cat)


if __name__ == "__main__":
    unittest.main()
