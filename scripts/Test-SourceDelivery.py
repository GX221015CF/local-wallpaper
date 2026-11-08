from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import datetime

root = Path(__file__).resolve().parents[1]
run = root / 'tests/isolation' / ('source-' + datetime.datetime.now().strftime('%H%M%S%f'))
app = run / 'app'
(app / 'build').mkdir(parents=True)
(app / '.localwallpaper-test-root').write_text(
    'wallpaper-small-trial-20261103|' + root.name, encoding='utf-8')
shutil.copytree(root / 'rules', app / 'rules')
compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
sources = list((root / 'src').glob('*.cs')) + [
    root / 'tests/FirstUseRegression.cs', root / 'tests/MultiWallpaperRegression.cs',
    root / 'tests/SourceDeliveryCheck.cs', root / 'shared/DailyLog.cs', root / 'shared/AppVisuals.cs']
exe = app / 'build/SourceDeliveryTest.exe'
references = ['System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll',
              'System.Core.dll', 'System.Security.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll']
command = [str(compiler), '/nologo', '/target:exe', '/platform:x86', '/define:TEST_BUILD',
           '/main:SourceDeliveryCheck', '/out:' + str(exe)]
command += ['/r:' + name for name in references] + list(map(str, sources))


def identity(path):
    return dict(path=str(path), sha256=hashlib.sha256(path.read_bytes()).hexdigest())


(run / 'inputs-before.json').write_text(json.dumps(dict(command=command,
    inputs=[identity(path) for path in sources + [compiler]]), ensure_ascii=False, indent=2), encoding='utf-8')
result = subprocess.run(command, capture_output=True, timeout=30)
(run / 'compile.log').write_bytes(result.stdout + result.stderr)
assert result.returncode == 0, 'Compile failed; see saved log'
report = run / 'result.json'
process = subprocess.Popen([str(exe), str(report)],
    env=dict(os.environ, LOCALWALLPAPER_TEST_ROOT=str(app)), creationflags=0x08000000)
code = process.wait(20)
data = json.loads(report.read_text(encoding='utf-8-sig'))
(run / 'process.json').write_text(json.dumps(dict(pid=process.pid, exitCode=code,
    normalExit=True, forcedTermination=False, executable=identity(exe)), indent=2), encoding='utf-8')
assert code == 0 and data['passed'], data
print(json.dumps(dict(result='passed', checks=len(data['checks']), report=str(report))))
