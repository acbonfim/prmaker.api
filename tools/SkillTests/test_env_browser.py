"""Testes do navegador da validacao em ambiente (skills/validar-ambiente/scripts/env_browser.py) — 0092.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = importlib.util.spec_from_file_location(
    "env_browser", os.path.join(HERE, "..", "..", "skills", "validar-ambiente", "scripts", "env_browser.py"))
env_browser = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(env_browser)


class FakeRequest:
    def __init__(self, method, url):
        self.method, self.url = method, url


class FakeRoute:
    def __init__(self, method, url):
        self.request = FakeRequest(method, url)
        self.outcome = None

    def abort(self, reason):
        self.outcome = f"abort:{reason}"

    def continue_(self):
        self.outcome = "continue"


def guarded(read_only=True):
    tmp = tempfile.mkdtemp()
    s = env_browser.Session({"sid": "x", "out": tmp, "readOnly": read_only})
    s.guard_on = True
    return s


class ReadOnlyGuardTests(unittest.TestCase):
    def route(self, session, method, url):
        r = FakeRoute(method, url)
        session.route(r)
        return r.outcome

    def test_blocks_writes_but_lets_reads_through(self):
        s = guarded()
        self.assertEqual("continue", self.route(s, "GET", "https://demo.solvacelabs.com/api/plans/1"))
        self.assertEqual("continue", self.route(s, "POST", "https://apigw.solvacelabs.com/authentication/Languages/2/terms/search"))
        self.assertEqual("continue", self.route(s, "POST", "https://apigw.solvacelabs.com/authentication/Environment/info"))
        self.assertEqual("continue", self.route(s, "POST", "https://o1.ingest.us.sentry.io/api/1/envelope/"))
        self.assertEqual("abort:blockedbyclient", self.route(s, "POST", "https://apigw.solvacelabs.com/fivew2h/Plan/save"))
        self.assertEqual("abort:blockedbyclient", self.route(s, "DELETE", "https://apigw.solvacelabs.com/fivew2h/Plan/12"))
        self.assertEqual("abort:blockedbyclient", self.route(s, "PUT", "https://apigw.solvacelabs.com/fivew2h/Plan/12"))
        self.assertEqual(3, len(s.blocked))

    def test_allowed_route_passes_and_is_recorded(self):
        tmp = tempfile.mkdtemp()
        s = env_browser.Session({"sid": "x", "out": tmp, "readOnly": True, "allow": ["User-Report-Preferences"]})
        s.guard_on = True
        self.assertEqual("continue", self.route(s, "POST", "https://lear.solvacelabs.com/sa3/user-report-preferences.asp"))
        self.assertEqual(["POST lear.solvacelabs.com/sa3/user-report-preferences.asp"], s.allowed)
        self.assertEqual("abort:blockedbyclient", self.route(s, "POST", "https://lear.solvacelabs.com/sa3/sa3_salvar.asp"))

    def test_writable_environment_and_login_are_not_blocked(self):
        self.assertEqual("continue", self.route(guarded(read_only=False), "POST", "https://qa.solvacelabs.com/api/Plan/save"))
        s = guarded()
        s.guard_on = False
        self.assertEqual("continue", self.route(s, "POST", "https://demo.solvacelabs.com/api/Plan/save"))


class CredentialsTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        env_browser.CREDS = Path(self.dir) / "creds.json"

    def test_password_matches_by_environment_key_or_host_ignoring_user_case(self):
        env_browser.CREDS.write_text(json.dumps({"environments": [
            {"env": "qa", "url": "https://qa.solvacelabs.com/", "user": "admin@ewcm.com.br", "password": "p1"},
            {"env": "mondelez", "url": "https://mondelez.solvacelabs.com/", "user": "admincor@solvacelabs.com", "password": "p2"}]}))
        self.assertEqual("p1", env_browser.password_for("qa", "https://qa.solvacelabs.com/", "Admin@EWCM.com.br"))
        self.assertEqual("p2", env_browser.password_for("outro-nome", "https://mondelez.solvacelabs.com", "admincor@solvacelabs.com"))
        self.assertIsNone(env_browser.password_for("qa", "https://qa.solvacelabs.com/", "outro@x"))
        self.assertIsNone(env_browser.password_for("dev", "https://devsp.solvacelabs.com/", "admin@ewcm.com.br"))

    def test_no_file_means_no_password(self):
        self.assertIsNone(env_browser.password_for("qa", "https://qa.solvacelabs.com/", "admin@ewcm.com.br"))


class SlugTests(unittest.TestCase):
    def test_session_id_is_stable(self):
        self.assertEqual("mondelez-admincor-solvacelabs-com", env_browser.slug("mondelez-AdminCOR@solvacelabs.com", 80))
        self.assertEqual("x", env_browser.slug("", 10))


if __name__ == "__main__":
    unittest.main()
