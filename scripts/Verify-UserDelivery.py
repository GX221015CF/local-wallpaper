"""Verify and independently unpack the final recipient ZIP without starting a GUI."""
from pathlib import Path, PurePosixPath
from urllib.parse import unquote, urlsplit
from html.parser import HTMLParser
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def identity(path):
    return dict(path=str(path), bytes=path.stat().st_size,
                sha256=hashlib.sha256(path.read_bytes()).hexdigest())


def save(path, value):
    with path.open('x', encoding='utf-8') as output:
        json.dump(value, output, ensure_ascii=False, indent=2)


class GuideParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.ids = []
        self.references = []
        self.images = []
        self.scripts = 0

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if 'id' in attrs:
            self.ids.append(attrs['id'])
        for attribute in ['href', 'src']:
            if attribute in attrs:
                self.references.append((tag, attrs[attribute]))
        if tag == 'img':
            assert attrs.get('alt'), 'Image needs explanatory alternative text'
            self.images.append(attrs)
        if tag == 'script':
            self.scripts += 1


def run_process(command, cwd, output_path, timeout):
    process = subprocess.Popen(list(map(str, command)), cwd=cwd, stdout=subprocess.PIPE,
        stderr=subprocess.PIPE, creationflags=0x08000000)
    stdout, stderr = process.communicate(timeout=timeout)
    output_path.write_bytes(stdout + stderr)
    return dict(command=list(map(str, command)), pid=process.pid, exitCode=process.returncode,
                normalExit=True, forcedTermination=False, log=str(output_path))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--attempt', default='02')
    args = parser.parse_args()
    out = args.output_root.resolve()
    evidence = out / 'evidence'
    verify = out / 'verify' / ('attempt-' + args.attempt)
    verify.mkdir()
    receipt = json.loads((evidence / 'packaging-receipt.json').read_text(encoding='utf-8'))
    package = Path(receipt['package']['path'])
    assert identity(package) == receipt['package']
    extraction = verify / 'final-extracted'
    assert not extraction.exists()
    extraction.mkdir()
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        assert len(names) == len({name.casefold() for name in names})
        assert {PurePosixPath(name).parts[0] for name in names} == {'本地壁纸_R30'}
        assert all(not PurePosixPath(name).is_absolute() and '..' not in PurePosixPath(name).parts
                   and ':' not in name and '\\' not in name for name in names)
        archive.extractall(extraction)
    contents = extraction / '本地壁纸_R30'
    manifest = json.loads((contents / '交付文件清单.json').read_text(encoding='utf-8'))
    expected = {row['path'] for row in manifest['files']} | {'交付文件清单.json'}
    assert {path.relative_to(contents).as_posix() for path in contents.rglob('*') if path.is_file()} == expected
    for row in manifest['files']:
        actual = identity(contents / row['path'])
        assert actual['bytes'] == row['bytes'] and actual['sha256'] == row['sha256']
    application = contents / 'app'
    assert not (application / 'build/data-location.json').exists()
    assert not any(path.name in ['catalog.json', 'settings.json', 'runtime-location.json', 'source-location.json']
                   or path.suffix.lower() in ['.cwp', '.mp4', '.resc', '.dll', '.log', '.jsonl']
                   for path in contents.rglob('*') if path.is_file())
    guide = contents / '开始使用.html'
    text = guide.read_text(encoding='utf-8')
    assert '\ufffd' not in text and '<html lang="zh-CN">' in text and '操作示意' in text
    page = GuideParser()
    page.feed(text)
    assert page.scripts == 0 and len(page.ids) == len(set(page.ids))
    for tag, reference in page.references:
        url = urlsplit(reference)
        assert not url.scheme and not url.netloc, 'Guide must stay offline'
        if url.path:
            target = (contents / unquote(url.path)).resolve()
            assert target.is_relative_to(contents) and target.is_file()
        elif url.fragment:
            assert unquote(url.fragment) in page.ids
    css = (contents / '教程资源/guide.css').read_text(encoding='utf-8')
    assert '@media(max-width:600px)' in css and '@media(max-width:960px)' in css
    assert '@import' not in css and 'url(' not in css.lower()
    for svg in (contents / '教程资源').glob('*.svg'):
        document = ET.fromstring(svg.read_text(encoding='utf-8'))
        assert not any(node.tag.endswith('script') or node.tag.endswith('image') for node in document.iter())
    launch = (contents / '打开本地壁纸.cmd').read_text(encoding='ascii')
    assert 'start "" "%~dp0app\\build\\LocalWallpaper.exe"' in launch
    assert (contents / 'app/build/LocalWallpaper.exe').is_file()
    steps = []
    official = run_process([application / 'build/WallpaperVersionTool.exe', 'verify', application],
        contents, verify / 'official-release-verify.log', 20)
    assert official['exitCode'] == 0
    steps.append(official)
    handler = verify / 'handler-check'
    handler.mkdir()
    shutil.copytree(application, handler / 'app')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
    source = ROOT / 'tests/PublishedRecipientCheck.cs'
    exe = handler / 'PublishedRecipientCheck.exe'
    command = [compiler, '/nologo', '/target:exe', '/platform:x86', '/r:System.Web.Extensions.dll',
               '/r:System.Core.dll', '/out:' + str(exe), source]
    save(verify / 'published-handler-inputs-before.json', dict(command=list(map(str, command)),
        inputs=[identity(path) for path in [compiler, source, compiler.parent / 'System.Web.Extensions.dll',
                                          compiler.parent / 'System.Core.dll']]))
    compiled = run_process(command, handler, verify / 'published-handler-compile.log', 30)
    assert compiled['exitCode'] == 0
    steps.append(compiled)
    handler_results = []
    for mode in ['prepare', 'reopen']:
        result_path = handler / (mode + '.json')
        executed = run_process([exe, mode, handler / 'app', handler / 'data-choice', result_path],
            handler, verify / ('published-' + mode + '.log'), 20)
        result = json.loads(result_path.read_text(encoding='utf-8-sig'))
        save(verify / ('published-' + mode + '-process.json'), executed)
        assert executed['exitCode'] == 0 and result['passed'], result
        steps.append(executed)
        handler_results.append(result)
    assert handler_results[0]['storeId'] == handler_results[1]['storeId']
    # The pristine final extraction remains in new-user state after verification.
    assert not (application / 'build/data-location.json').exists()
    assert all(identity(contents / row['path'])['sha256'] == row['sha256'] for row in manifest['files'])
    report = dict(result='passed', package=identity(package), extractionRoot=str(contents),
        memberCount=len(expected), allowlistMatched=True, appIdentityMatched=True,
        pristineExtractedAppHasNoUserBinding=True, tutorialImageCount=len(page.images),
        tutorialAnchors=page.ids, tutorialRelativeLinksComplete=True, offlineResourcesOnly=True,
        staticResponsiveRulesChecked=True, realBrowserRendered=False,
        illustrations='Five original operation diagrams reviewed as local PNGs; no app screenshots or wallpaper media included',
        officialReleaseToolExecuted=True, publishedHandlerResults=handler_results, steps=steps,
        guiEntryStarted=False, engineLoaded=False, playbackStarted=False,
        scope='Final ZIP content and static tutorial checks, actual release verifier, isolated setup handlers and a fresh reopen process. Not real GUI startup or browser appearance evidence.')
    save(evidence / 'delivery-verification.json', report)
    print(json.dumps(dict(result='passed', members=len(expected),
        handlerChecks=sum(len(result['checks']) for result in handler_results),
        extractionRoot=str(contents), realBrowserRendered=False), ensure_ascii=True))


if __name__ == '__main__':
    main()
