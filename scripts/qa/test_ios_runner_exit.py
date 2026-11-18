"""Regression for the actual macOS runner EXIT cleanup, without starting services."""
from pathlib import Path
import subprocess
import tempfile

script = Path(__file__).with_name('ios-simulator-ci.sh').read_text()
cleanup = script[script.index('cleanup() {'):script.index('trap cleanup EXIT')]
# Execute the real definition. Commands used only for resource disposal are
# stubbed; a successful disposal must not overwrite an earlier suite failure.
with tempfile.TemporaryDirectory(prefix='academy-ios-exit-') as folder:
    root = Path(folder)
    (root / 'evidence').mkdir()
    (root / 'postgres').mkdir()
    (root / 'postgres/postmaster.pid').touch()
    prefix = ('set -euo pipefail\nqa_dir=' + folder + '\n'
              'simulator=synthetic\napi_pid=\n'
              'gtimeout() { return 0; }\npg_ctl() { return 0; }\n'
              + cleanup + 'trap cleanup EXIT\n')
    cases = [('explicit failure', 'exit 7', 7),
             ('command failure', 'false', 1),
             ('unset variable on macOS Bash', 'echo "$missing_qa_variable"', 1),
             ('early zero without completion', 'exit 0', 1),
             ('completed success', 'printf success > "$qa_dir/evidence/suite-completed.txt"\nexit 0', 0),
             ('completed marker with later failure', 'exit 9', 9)]
    for name, command, expected in cases:
        result = subprocess.run(['/bin/bash', '-c', prefix + command],
                                capture_output=True, text=True)
        assert result.returncode == expected, (name, result.returncode, result.stderr)
        print(f'PASS: {name} -> {expected}')
