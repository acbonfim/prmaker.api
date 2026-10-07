"""0069: a correção testa só o que mudou, uma vez e com limite (test-changed.sh); o worktree do card liga o node_modules
do clone principal (prmake-plan.sh worktree) e o "worktree remove --force" do executor apaga só o link.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import json
import os
import shutil
import subprocess
import tempfile
import time
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.join(HERE, "..", "..", "skills", "analisar-bug", "scripts")
SCRIPT = os.path.join(SCRIPTS, "test-changed.sh")
PLAN = os.path.join(SCRIPTS, "prmake-plan.sh")

# jest falso: grava os argumentos; FAKE_JEST_RC = exit; FAKE_JEST_HANG = fica parado com um processo filho (o limite
# precisa matar os dois)
FAKE_JEST = r"""
const fs = require('fs');
fs.writeFileSync(process.env.FAKE_JEST_ARGS, JSON.stringify(process.argv.slice(2)));
if (process.env.FAKE_JEST_HANG) {
  const c = require('child_process').spawn('sleep', ['1000'], { stdio: 'ignore' });
  fs.writeFileSync(process.env.FAKE_JEST_HANG, String(c.pid));
  setInterval(() => {}, 1000);
} else {
  const rc = Number(process.env.FAKE_JEST_RC || 0);
  console.log((rc ? 'FAIL' : 'PASS') + ' src/a.spec.ts');
  console.log('Tests:       1 ' + (rc ? 'failed' : 'passed') + ', 1 total');
  process.exit(rc);
}
"""

ENV = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t", "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@t"}


def git(cwd, *args):
    return subprocess.run(["git", "-C", cwd, *args], check=True, capture_output=True, text=True, env=ENV).stdout.strip()


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        f.write(text)


@unittest.skipUnless(shutil.which("node"), "node fora do PATH")
class TestChangedTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        t = self.tmp.name
        self.origin = os.path.join(t, "origin.git")
        subprocess.run(["git", "init", "-q", "--bare", "-b", "master", self.origin], check=True)
        seed = os.path.join(t, "seed")
        subprocess.run(["git", "clone", "-q", self.origin, seed], check=True, capture_output=True)
        write(os.path.join(seed, "package.json"), '{"scripts": {"test": "jest"}, "devDependencies": {"jest": "29"}}\n')
        write(os.path.join(seed, ".gitignore"), "/node_modules\n")
        for f in ("a", "b", "c"):
            write(os.path.join(seed, "src", f + ".ts"), "export const %s = 1;\n" % f)
        write(os.path.join(seed, "src", "a.spec.ts"), "test('a', () => {});\n")
        write(os.path.join(seed, "src", "c.spec.ts"), "test('c', () => {});\n")
        git(seed, "add", "-A"); git(seed, "commit", "-qm", "base"); git(seed, "push", "-q", "origin", "HEAD:master")
        self.clone = os.path.join(t, "repos", "app")
        subprocess.run(["git", "clone", "-q", self.origin, self.clone], check=True, capture_output=True)
        write(os.path.join(self.clone, "node_modules", "jest", "bin", "jest.js"), FAKE_JEST)
        self.args_file = os.path.join(t, "args.json")
        self.home = os.path.join(t, "home")
        os.makedirs(self.home)

    def tearDown(self):
        self.tmp.cleanup()

    def worktree(self, card="1"):
        r = subprocess.run(["bash", PLAN, "worktree", card, self.clone, "hotfix/" + card, "master"],
                           capture_output=True, text=True, env={**ENV, "HOME": self.home})
        self.assertEqual(0, r.returncode, r.stderr)
        return r.stdout.strip().splitlines()[-1]

    def run_script(self, wt, *extra, env=None):
        return subprocess.run(["bash", SCRIPT, wt, *extra], capture_output=True, text=True,
                              env={**ENV, "FAKE_JEST_ARGS": self.args_file, **(env or {})})

    def jest_args(self):
        with open(self.args_file) as f:
            return json.load(f)

    def test_runs_only_specs_of_changed_files_once_without_coverage(self):
        wt = self.worktree()
        self.assertTrue(os.path.islink(os.path.join(wt, "node_modules")))
        self.assertEqual("", git(wt, "status", "--porcelain"))           # o link é ignorado pelo git
        write(os.path.join(wt, "src", "a.ts"), "export const a = 2;\n")
        git(wt, "commit", "-qam", "fix a")
        write(os.path.join(wt, "src", "b.ts"), "export const b = 2;\n")   # sem spec, não commitado
        write(os.path.join(wt, "src", "d.ts"), "export const d = 1;\n")   # novos
        write(os.path.join(wt, "src", "d.spec.ts"), "test('d', () => {});\n")
        git(wt, "push", "-q", "-u", "origin", "hotfix/1")                 # upstream vira a própria branch: base continua master
        r = self.run_script(wt)
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn("base=origin/master", r.stdout)
        self.assertIn("OK: testes passaram", r.stdout)
        args = self.jest_args()
        self.assertIn("--coverage=false", args)
        specs = args[args.index("--runTestsByPath") + 1:]
        self.assertEqual(["src/a.spec.ts", "src/d.spec.ts"], sorted(specs))

    def test_no_spec_for_changed_files(self):
        wt = self.worktree()
        write(os.path.join(wt, "src", "b.ts"), "export const b = 3;\n")
        r = self.run_script(wt)
        self.assertEqual(0, r.returncode, r.stdout)
        self.assertIn("SEM-SPEC", r.stdout)
        self.assertFalse(os.path.exists(self.args_file))

    def test_failure_returns_1_with_the_end_of_the_log(self):
        wt = self.worktree()
        write(os.path.join(wt, "src", "c.ts"), "export const c = 3;\n")
        r = self.run_script(wt, env={"FAKE_JEST_RC": "1"})
        self.assertEqual(1, r.returncode, r.stdout)
        self.assertIn("FAIL src/a.spec.ts", r.stdout)
        self.assertIn("FALHOU", r.stdout)

    def test_limit_kills_the_runner_and_its_children(self):
        wt = self.worktree()
        write(os.path.join(wt, "src", "c.ts"), "export const c = 4;\n")
        pid_file = os.path.join(self.tmp.name, "child.pid")
        start = time.time()
        r = self.run_script(wt, "--max-seconds", "3", env={"FAKE_JEST_HANG": pid_file})
        self.assertEqual(124, r.returncode, r.stdout)
        self.assertIn("LENTO", r.stdout)
        self.assertLess(time.time() - start, 20)
        with open(pid_file) as f:
            child = int(f.read())
        time.sleep(0.3)
        with self.assertRaises(OSError):
            os.kill(child, 0)

    def test_without_node_modules_says_no_runner(self):
        shutil.rmtree(os.path.join(self.clone, "node_modules"))
        wt = self.worktree()
        self.assertFalse(os.path.exists(os.path.join(wt, "node_modules")))
        write(os.path.join(wt, "src", "c.ts"), "export const c = 5;\n")
        r = self.run_script(wt)
        self.assertEqual(3, r.returncode, r.stdout)
        self.assertIn("SEM-RUNNER", r.stdout)

    def plan_test(self, wt, settings, env=None):
        card_dir = os.path.join(self.home, ".prmake", "cards", "1")
        os.makedirs(card_dir, exist_ok=True)
        with open(os.path.join(card_dir, ".prmake-settings.json"), "w") as f:
            json.dump({"available": True, "settings": settings}, f)
        return subprocess.run(["bash", PLAN, "test", "1", wt], capture_output=True, text=True,
                              env={**ENV, "HOME": self.home, "PRMAKE_TOKEN": "t", "PRMAKE_API_BASE": "http://127.0.0.1:9",
                                   "FAKE_JEST_ARGS": self.args_file, **(env or {})})

    def test_plan_test_off_skips_local_tests(self):
        wt = self.worktree()
        write(os.path.join(wt, "src", "c.ts"), "export const c = 6;\n")
        r = self.plan_test(wt, {"CorrectionLocalTests": "off"})
        self.assertEqual(0, r.returncode, r.stdout + r.stderr)
        self.assertIn("DESLIGADO", r.stdout)
        self.assertFalse(os.path.exists(self.args_file))

    def test_plan_test_uses_the_configured_limit(self):
        wt = self.worktree()
        write(os.path.join(wt, "src", "c.ts"), "export const c = 7;\n")
        pid_file = os.path.join(self.tmp.name, "child2.pid")
        r = self.plan_test(wt, {"CorrectionLocalTests": "changed", "CorrectionTestMaxSeconds": "2"}, env={"FAKE_JEST_HANG": pid_file})
        self.assertEqual(124, r.returncode, r.stdout + r.stderr)
        self.assertIn("nao terminou em 2s", r.stdout)

    def test_executor_cleanup_removes_only_the_link(self):
        wt = self.worktree()
        write(os.path.join(wt, "src", "a.ts"), "export const a = 9;\n")
        git(self.clone, "worktree", "remove", "--force", wt)
        self.assertFalse(os.path.exists(wt))
        self.assertTrue(os.path.isfile(os.path.join(self.clone, "node_modules", "jest", "bin", "jest.js")))


if __name__ == "__main__":
    unittest.main()
