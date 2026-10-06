"""0067: a análise e a engenharia reversa leem a origin/master atualizada numa cópia só de leitura — o clone de trabalho
(outra branch, arquivos alterados) não é tocado.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import os
import subprocess
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "..", "skills", "analisar-bug", "scripts", "master-wt.sh")


def git(cwd, *args):
    return subprocess.run(["git", "-C", cwd, *args], check=True, capture_output=True, text=True,
                          env={**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t", "GIT_COMMITTER_NAME": "t",
                               "GIT_COMMITTER_EMAIL": "t@t"}).stdout.strip()


class MasterCopyTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        t = self.tmp.name
        self.origin = os.path.join(t, "origin.git")
        subprocess.run(["git", "init", "-q", "--bare", "-b", "master", self.origin], check=True)
        seed = os.path.join(t, "seed")
        subprocess.run(["git", "clone", "-q", self.origin, seed], check=True, capture_output=True)
        os.makedirs(os.path.join(seed, "mod"))
        open(os.path.join(seed, "mod", "regra.cs"), "w").write("// regra v1\n")
        git(seed, "add", "-A"); git(seed, "commit", "-qm", "v1"); git(seed, "push", "-q", "origin", "HEAD:master")
        # o clone de trabalho fica numa branch de hotfix, com arquivo alterado e sem commit
        self.clone = os.path.join(t, "repos", "app")
        subprocess.run(["git", "clone", "-q", self.origin, self.clone], check=True, capture_output=True)
        git(self.clone, "checkout", "-qb", "hotfix/1")
        open(os.path.join(self.clone, "mod", "regra.cs"), "w").write("// regra do hotfix, nao commitada\n")
        # a master anda depois do clone (o clone está desatualizado)
        open(os.path.join(seed, "mod", "regra.cs"), "w").write("// regra v2 (master atual)\n")
        git(seed, "commit", "-qam", "v2"); git(seed, "push", "-q", "origin", "HEAD:master")

    def tearDown(self):
        self.tmp.cleanup()

    def run_script(self, path):
        r = subprocess.run(["bash", SCRIPT, path], capture_output=True, text=True)
        self.assertEqual(0, r.returncode, r.stderr)
        return r.stdout.strip()

    def test_reads_updated_master_without_touching_the_working_clone(self):
        out = self.run_script(os.path.join(self.clone, "mod"))
        self.assertTrue(out.endswith(os.path.join(".prmake-wt", "master", "app", "mod")), out)
        self.assertEqual("// regra v2 (master atual)\n", open(os.path.join(out, "regra.cs")).read())
        self.assertEqual("hotfix/1", git(self.clone, "rev-parse", "--abbrev-ref", "HEAD"))
        self.assertIn("regra do hotfix", open(os.path.join(self.clone, "mod", "regra.cs")).read())
        # roda de novo (master andou de novo): a cópia acompanha
        seed = os.path.join(self.tmp.name, "seed")
        open(os.path.join(seed, "mod", "regra.cs"), "w").write("// regra v3\n")
        git(seed, "commit", "-qam", "v3"); git(seed, "push", "-q", "origin", "HEAD:master")
        out2 = self.run_script(self.clone)
        self.assertEqual("// regra v3\n", open(os.path.join(out2, "mod", "regra.cs")).read())

    def test_outside_git_returns_the_same_folder(self):
        d = tempfile.mkdtemp()
        self.assertEqual(d, self.run_script(d))


if __name__ == "__main__":
    unittest.main()
