from pathlib import Path
import os
import sys
import json
import hashlib
import subprocess

root = Path(__file__).resolve().parents[1]
run = Path(sys.argv[1]).resolve()
run.mkdir(parents=True, exist_ok=False)
mode = sys.argv[2] if len(sys.argv) > 2 else 'functional'
compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
sources = list((root / 'host_src').glob('*.cs')) + [root / 'shared/DailyLog.cs', root / 'shared/AppVisuals.cs', root / 'tests/NativeInputRegression.cs', root / 'tests/VideoBindingRegression.cs']
if mode == 'shutdown':
    sources += [path for path in (root / 'src').glob('*.cs') if path.name != 'Entry.cs']
    sources += [root / 'tests' / name for name in ['ShutdownRegression.cs', 'FirstUseRegression.cs', 'MultiWallpaperRegression.cs']]
(run / 'build').mkdir()
exe = run / 'build/NativeInputTest.exe'
main = {'video': 'VideoBindingRegression', 'shutdown': 'ShutdownRegression'}.get(mode, 'NativeInputRegression')
command = [str(compiler), '/nologo', '/target:exe', '/platform:x86', '/define:TEST_BUILD', '/main:' + main, '/out:' + str(exe)]
command += ['/r:' + name for name in ['System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll', 'System.Core.dll', 'System.Security.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll']]
command += ['/resource:' + str(root / 'assets/LocalWallpaper.ico') + ',LocalWallpaper.ico']
command += list(map(str, sources))
(run / 'inputs.json').write_text(json.dumps(dict(command=command, files=[dict(path=str(p), sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sources + [compiler]]), indent=2), encoding='utf-8')
compiled = subprocess.run(command, capture_output=True, timeout=30, creationflags=0x08000000)
(run / 'compile.log').write_bytes(compiled.stdout + compiled.stderr)
if compiled.returncode:
    print(compiled.stdout.decode('gb18030', errors='replace'))
    raise SystemExit(compiled.returncode)
arguments = [str(exe), str(run / 'result.json')]
if mode == 'shutdown':
    arguments = [str(exe), str(run / 'owned-shutdown'), str(run / 'result.json')]
if mode not in ('video', 'shutdown'):
    arguments += [str(run / 'owned-host'), mode]
(run / '.localwallpaper-test-root').write_text('wallpaper-small-trial-20261103|' + run.parents[2].name, encoding='utf-8')
process = subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=0x08000000, env=dict(os.environ, LOCALWALLPAPER_TEST_ROOT=str(run)))
stdout, stderr = process.communicate(timeout=20)
code = process.returncode
(run / 'stdout.txt').write_bytes(stdout)
(run / 'stderr.txt').write_bytes(stderr)
(run / 'process.json').write_text(json.dumps(dict(pid=process.pid, exitCode=code, normalExit=code in (0, 1, 2), forcedTermination=False)), encoding='utf-8')
print((run / 'result.json').read_text(encoding='utf-8-sig') if (run / 'result.json').exists() else json.dumps(dict(passed=False, exitCode=code, reportMissing=True)))
raise SystemExit(code)
