"""0069: cognito-query.sh guarda a lista de pools por 24 h (a AWS já levou 25-30 s por chamada) e põe timeout em toda
chamada; ambiente fora do cache relista uma vez; --refresh força.

Rodar: python3 -m unittest discover -s tools/SkillTests
"""
import os
import subprocess
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "..", "skills", "analisar-bug", "scripts", "cognito-query.sh")

# aws falso: registra cada chamada; list-user-pools lê os pools de $FAKE_POOLS (id<TAB>nome por linha)
FAKE_AWS = r"""#!/usr/bin/env bash
echo "$*" >> "$FAKE_AWS_LOG"
case "$*" in
  *list-user-pools*)
    jq -Rn '{UserPools: [inputs | split("\t") | {Id: .[0], Name: .[1]}]}' < "$FAKE_POOLS" ;;
  *) echo '{}' ;;
esac
"""


class CognitoCacheTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        t = self.tmp.name
        bin_dir = os.path.join(t, "bin")
        os.makedirs(bin_dir)
        aws = os.path.join(bin_dir, "aws")
        with open(aws, "w") as f:
            f.write(FAKE_AWS)
        os.chmod(aws, 0o755)
        self.pools = os.path.join(t, "pools.tsv")
        self.log = os.path.join(t, "aws.log")
        self.set_pools("p1\tefeso\np2\tsandboxefeso\n")
        self.env = {**os.environ, "PATH": bin_dir + os.pathsep + os.environ["PATH"], "PRMAKE_HOME": os.path.join(t, "prmake"),
                    "FAKE_POOLS": self.pools, "FAKE_AWS_LOG": self.log}
        self.env.pop("AWS_PROFILE", None)

    def tearDown(self):
        self.tmp.cleanup()

    def set_pools(self, text):
        with open(self.pools, "w") as f:
            f.write(text)

    def run_script(self, *args):
        r = subprocess.run(["bash", SCRIPT, *args], capture_output=True, text=True, env=self.env)
        self.assertEqual(0, r.returncode, r.stderr)
        return r.stdout.strip()

    def calls(self):
        if not os.path.exists(self.log):
            return []
        with open(self.log) as f:
            return f.read().splitlines()

    def test_second_lookup_uses_the_cache_and_calls_have_timeouts(self):
        self.assertEqual("p2", self.run_script("pool-id", "sandboxefeso"))
        self.assertEqual(1, len(self.calls()))
        self.assertIn("--cli-read-timeout", self.calls()[0])
        self.assertEqual("p2", self.run_script("pool-id", "sandboxefeso"))
        self.assertEqual("p1\tefeso", self.run_script("pools", "efeso").splitlines()[0])
        self.assertEqual(1, len(self.calls()))

    def test_environment_missing_from_cache_lists_again(self):
        self.run_script("pool-id", "efeso")
        self.set_pools("p1\tefeso\np2\tsandboxefeso\np3\ttakeda\n")
        self.assertEqual("p3", self.run_script("pool-id", "takeda"))
        self.assertEqual(2, len(self.calls()))

    def test_refresh_forces_listing(self):
        self.run_script("pools")
        self.set_pools("p9\tnovo\n")
        self.assertEqual("p9\tnovo", self.run_script("pools", "--refresh"))
        self.assertEqual(2, len(self.calls()))


if __name__ == "__main__":
    unittest.main()
