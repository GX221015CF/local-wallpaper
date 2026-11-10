"""Run bounded synthetic handler checks, then reopen in a genuinely new process."""
from pathlib import Path
import datetime
import hashlib
import json
import os
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
run = Path(sys.argv[1]).resolve()
run.mkdir(parents=True, exist_ok=False)
app = run / 'app'
(app / 'build').mkdir(parents=True)
(app / '.localwallpaper-test-root').write_text(
    'wallpaper-small-trial-20261103|' + run.parents[2].name, encoding='utf-8')
(app / 'rules').mkdir()
for name in ('v3-structure-families.json', 'runtime-components.json'):
    (app / 'rules' / name).write_bytes((root / 'rules' / name).read_bytes())
compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
sources = list((root / 'src').glob('*.cs')) + [root / 'shared/DailyLog.cs', root / 'shared/AppVisuals.cs']
sources += [root / 'tests' / name for name in ('FirstUseRegression.cs', 'MultiWallpaperRegression.cs', 'PersonalExperienceRegression.cs')]
references = ['System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll',
              'System.Core.dll', 'System.Security.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll']
exe = app / 'build/PersonalExperienceTest.exe'
command = [str(compiler), '/nologo', '/target:exe', '/platform:x86', '/define:TEST_BUILD',
           '/main:PersonalExperienceRegression', '/out:' + str(exe)]
command += ['/r:' + name for name in references] + list(map(str, sources))
def identity(path):
    return dict(path=str(path), bytes=path.stat().st_size, sha256=hashlib.sha256(path.read_bytes()).hexdigest())
def save(name, value):
    (run / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
save('inputs-before.json', dict(command=command, inputs=[identity(path) for path in sources + [compiler]]))
result = subprocess.run(command, capture_output=True, timeout=40, creationflags=0x08000000)
(run / 'compile.log').write_bytes(result.stdout + result.stderr)
if result.returncode:
    print(result.stdout.decode('gb18030', errors='replace'))
    raise SystemExit(result.returncode)
processes = []
for mode in ('exercise', 'reopen'):
    report = run / (mode + '.json')
    process = subprocess.Popen([str(exe), str(report), mode], env=dict(os.environ, LOCALWALLPAPER_TEST_ROOT=str(app)), creationflags=0x08000000)
    # The handlers have their own cancellation budgets. Never control another process.
    code = process.wait(35)
    data = json.loads(report.read_text(encoding='utf-8-sig'))
    processes.append(dict(mode=mode, pid=process.pid, exitCode=code, normalExit=True, report=str(report)))
    save('processes.json', processes)
    if code or not data['passed']:
        print(json.dumps(data, ensure_ascii=False))
        raise SystemExit(1)
print(json.dumps(dict(passed=True, processes=processes, checks=sum(len(json.loads((run / (mode + '.json')).read_text(encoding='utf-8-sig'))['checks']) for mode in ('exercise','reopen'))), ensure_ascii=False))
