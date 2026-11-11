from pathlib import Path
import hashlib
import json
import os
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
run = Path(sys.argv[1]).resolve()
run.mkdir(parents=True, exist_ok=False)
app = Path(sys.argv[2]).resolve() if len(sys.argv)>2 else run / 'app'
(app / 'build').mkdir(parents=True,exist_ok=True)
(app / '.localwallpaper-test-root').write_text('wallpaper-small-trial-20261103|' + run.parents[2].name, encoding='utf-8')
(app / 'rules').mkdir(exist_ok=True)
for name in ('runtime-components.json', 'v3-structure-families.json'):
    (app / 'rules' / name).write_bytes((root / 'rules' / name).read_bytes())
compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
sources = list((root / 'src').glob('*.cs')) + [root / 'shared/DailyLog.cs', root / 'shared/AppVisuals.cs']
sources += [root / 'tests' / name for name in ('FirstUseRegression.cs','MultiWallpaperRegression.cs','ImportExperienceRegression.cs','ImportResponseRegression.cs','ImportListRegression.cs')]
if '--baseline' in sys.argv[4:]:
    original = subprocess.check_output(['git', '-C', str(root), 'show', '5eb391e:src/ImportDialog.cs'])
    baseline = run / 'BaselineImportDialog.cs'
    baseline.write_bytes(original)
    sources = [baseline if path == root / 'src/ImportDialog.cs' else path for path in sources]
exe = app / 'build' / (run.name.replace('-','_') + 'ResponseTest.exe')
command = [str(compiler),'/nologo','/target:exe','/platform:x86','/define:TEST_BUILD','/main:ImportListRegression','/out:'+str(exe)]
command += ['/r:'+name for name in ('System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Core.dll','System.Security.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')]
command += ['/resource:'+str(root/'assets/LocalWallpaper.ico')+',LocalWallpaper.ico']
command += list(map(str,sources))
if '--response' in sys.argv[4:]:
    command = [value.replace('/main:ImportListRegression', '/main:ImportResponseRegression') for value in command]
(run/'inputs-before.json').write_text(json.dumps(dict(command=command,inputs=[dict(path=str(p),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sources+[compiler]]),ensure_ascii=False,indent=2),encoding='utf-8')
result = subprocess.run(command,capture_output=True,timeout=40,creationflags=0x08000000)
(run/'compile.log').write_bytes(result.stdout+result.stderr)
if result.returncode:
    print(result.stdout.decode('gb18030',errors='replace'));raise SystemExit(result.returncode)
processes=[]
for mode in (sys.argv[3] if len(sys.argv)>3 else 'native',):
    report=run/(mode+'.json')
    process=subprocess.Popen([str(exe),str(report),mode],env=dict(os.environ,LOCALWALLPAPER_TEST_ROOT=str(app)),creationflags=0x08000000)
    code=process.wait(55)
    data=json.loads(report.read_text(encoding='utf-8-sig')) if report.exists() else dict(passed=False,exitCode=code,stage=(report.with_suffix(report.suffix+'.timeout').read_text(encoding='utf-8') if report.with_suffix(report.suffix+'.timeout').exists() else 'unknown'))
    processes.append(dict(mode=mode,pid=process.pid,exitCode=code,normalExit=True,forcedTermination=False,report=str(report)))
    (run/'processes.json').write_text(json.dumps(processes),encoding='utf-8')
    print(json.dumps(dict(passed=data.get('passed'),checks=data.get('checks'),error=data.get('error'),report=str(report)),ensure_ascii=False))
    if code:raise SystemExit(code)
