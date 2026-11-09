"""Wrap a verified release with an offline user guide. Never reads an observation store."""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import json
import re
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.1.30-import-video-cover-internal-r30'
CORE = [
    'build/LocalWallpaper.exe', 'build/LocalWallpaperLauncher.exe', 'build/release.json',
    'build/runtime-manifest.json', 'build/runtime/Desktop317495Host-refined.exe',
    'build/WallpaperLocalTool.exe', 'build/WallpaperVersionTool.exe',
    'rules/runtime-components.json', 'rules/v3-structure-families.json',
]
FIGURES = ['first-use', 'locations', 'import', 'preview', 'tray']
GUIDE_FILES = ['开始使用.html', '教程资源/guide.css', '教程资源/app-icon.png']
GUIDE_FILES += ['教程资源/' + name + extension for name in FIGURES for extension in ['.png', '.svg']]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def identity(path):
    return dict(path=str(path), bytes=path.stat().st_size, sha256=digest(path.read_bytes()))


def save(path, value):
    with path.open('x', encoding='utf-8') as output:
        json.dump(value, output, ensure_ascii=False, indent=2)


def safe_name(name):
    path = PurePosixPath(name)
    return not path.is_absolute() and '\\' not in name and ':' not in name and '..' not in path.parts


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source-package', type=Path, required=True)
    parser.add_argument('--source-manifest', type=Path, required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    args = parser.parse_args()
    out = args.output_root.resolve()
    evidence = out / 'evidence'
    if not evidence.is_dir():
        raise ValueError('Use an explicitly prepared output/evidence root')
    final = out / '成品/本地壁纸_R30'
    package = out / '本地壁纸_R30_含离线教程_20261109.zip'
    if final.exists() or package.exists():
        raise FileExistsError('Existing delivery preserved; use a new output root')
    outer = json.loads(args.source_manifest.read_text(encoding='utf-8-sig'))
    source = args.source_package.read_bytes()
    assert len(source) == outer['bytes'] and digest(source) == outer['sha256']
    script_inputs = [Path(__file__).resolve(), ROOT / 'scripts/Generate-GuideFigures.py']
    script_inputs += [ROOT / 'docs/user-guide' / name for name in GUIDE_FILES]
    save(evidence / 'packaging-inputs-before.json', dict(
        binaryPolicy='Reuse verified original R30 bytes. Documentation/packaging revision1, no compilation.',
        version=VERSION, sourcePackage=identity(args.source_package),
        sourceManifest=identity(args.source_manifest), files=[identity(path) for path in script_inputs],
        tools=[identity(Path(sys.executable))]))
    application = {}
    with zipfile.ZipFile(args.source_package) as archive:
        names = archive.namelist()
        assert len(names) == len(set(name.casefold() for name in names))
        assert all(safe_name(name) for name in names)
        original = json.loads(archive.read('package-manifest.json').decode('utf-8-sig'))
        assert original['version'] == VERSION
        listed = {row['path']: row for row in original['files']}
        assert set(names) == set(listed) | {'package-manifest.json'}
        for name, row in listed.items():
            data = archive.read(name)
            assert len(data) == row['bytes'] and digest(data) == row['sha256']
        for name in CORE:
            application['app/' + name] = archive.read(name)
    own_host = 'Desktop317495Host-refined.exe'
    contract = json.loads(application['app/rules/runtime-components.json'])
    external_names = {row['name'].casefold() for row in contract['files'] if row['name'] != own_host}
    assert len(external_names) == 23
    payload = dict(application)
    for name in GUIDE_FILES:
        payload[name] = (ROOT / 'docs/user-guide' / name).read_bytes()
    payload['打开本地壁纸.cmd'] = (
        '@echo off\r\nsetlocal\r\nstart "" "%~dp0app\\build\\LocalWallpaper.exe"\r\n').encode('ascii')
    payload['版本与支持说明.txt'] = (
        '本地壁纸 R30 · 图文教程交付修订1\n'
        '程序版本 0.1.30-import-video-cover-internal-r30\n'
        '完整解压后先读 开始使用.html，再双击 打开本地壁纸.cmd。\n'
        '程序与正式规则沿用原R30发布身份，本次整理入口与教程，无新功能编译。\n'
        '本机Windows11有分项真实结果，其他Windows未实测。自有程序为x86，需要.NET Framework4.x。\n'
        '已知CWP样本401999/317495/395741的相应使用步骤有历史结果。\n'
        '402242视频的播放/托盘为R28结果，手动封面为R29，导入前图片为R30，各项分别记录。\n'
        '398417/403178具有结构和取帧证据，尚无对应引擎播放通过结论。\n'
        '完整条件、组件准备及故障步骤见离线教程的支持范围。\n'
        '当前为内部阶段成果，第三方条件需接收者确认。\n').encode('utf-8-sig')
    payload['第三方条件说明.txt'] = (
        '接收者需自行合法准备壁纸资源和与固定23项身份契约一致的本地播放组件。\n'
        '本包只提供自有程序、宿主和规则。组件身份核验通过后，实际显示仍需手动预览确认。\n'
        'DLL从经核验的既有安装根加载，自有宿主/规则及两份必要resc资源归档会暂存于本次运行范围，正常停止后清理。\n'
        '第三方组件获取、配合加载、必要材料暂存、长期使用与分发条件尚无完整可靠结论。\n'
        '接收者须确认自己的使用权与适用条件，暂缺条件时可先完成首次配置和导入管理。\n'
        '历史官方未登录395741仅覆盖本机该资源的实际步骤，机器权益历史未排除，免费标签未知。\n'
        '本包未对未经确认的第三方内容指定开源许可。\n').encode('utf-8-sig')
    expected = set(application) | set(GUIDE_FILES) | {'打开本地壁纸.cmd', '版本与支持说明.txt', '第三方条件说明.txt'}
    assert set(payload) == expected
    for name, data in payload.items():
        assert safe_name(name)
        assert PurePosixPath(name).name.casefold() not in external_names
        if name not in application and not name.endswith('.png'):
            text = data.decode('utf-8-sig')
            assert not re.search(r'(?i)[a-z]:[\\/]+Users[\\/]+', text)
    manifest = dict(schemaVersion=1, product='本地壁纸', functionalVersion=VERSION,
        packagingRevision='offline-guide.1-20261109', entry='打开本地壁纸.cmd',
        directEntry='app/build/LocalWallpaper.exe', guide='开始使用.html',
        containsUserData=False, containsExternalRuntimeBinaries=False, containsWallpapers=False,
        files=[dict(path=name, bytes=len(data), sha256=digest(data)) for name, data in sorted(payload.items())])
    payload['交付文件清单.json'] = json.dumps(manifest, ensure_ascii=False, indent=2).encode('utf-8')
    final.mkdir(parents=True)
    for name, data in payload.items():
        target = final / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as output:
            output.write(data)
    with zipfile.ZipFile(package, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted(payload):
            archive.writestr('本地壁纸_R30/' + name, payload[name])
    with zipfile.ZipFile(package) as archive:
        assert archive.testzip() is None
        assert set(archive.namelist()) == {'本地壁纸_R30/' + name for name in payload}
        members = [dict(path=name, bytes=len(archive.read(name)), sha256=digest(archive.read(name)))
                   for name in archive.namelist()]
    save(evidence / 'zip-members.json', members)
    save(evidence / 'packaging-receipt.json', dict(result='passed', version=VERSION,
        packagingRevision=manifest['packagingRevision'], sourcePackage=identity(args.source_package),
        package=identity(package), contentsRoot=str(final), guide=str(final / '开始使用.html'),
        memberCount=len(members), appFileCount=len(CORE), binariesReusedByteExact=True,
        originalPackageFullyVerified=True, thirdPartyBinariesIncluded=False, guiStarted=False,
        compiled=False, engineLoaded=False))
    print(json.dumps(dict(result='passed', zip=str(package), bytes=package.stat().st_size,
        sha256=digest(package.read_bytes()), members=len(members)), ensure_ascii=True))


if __name__ == '__main__':
    main()
