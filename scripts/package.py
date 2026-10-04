#!/usr/bin/env python3
"""Package dotnet publish output, including Mac app bundles and checksums."""
import argparse, hashlib, os, pathlib, plistlib, shutil, stat, zipfile
p = argparse.ArgumentParser()
p.add_argument('--rid', required=True)
p.add_argument('--input', required=True)
p.add_argument('--output', default='artifacts')
p.add_argument('--version', default='0.1.0')
a = p.parse_args()
root = pathlib.Path(__file__).resolve().parent.parent
out = pathlib.Path(a.output).resolve(); out.mkdir(parents=True, exist_ok=True)
stage = out / ('stage-' + a.rid)
if stage.exists(): shutil.rmtree(stage)
stage.mkdir(parents=True, exist_ok=True)
if a.rid.startswith('osx'):
    app = stage / 'TSimulator.app' / 'Contents'
    destination = app / 'MacOS'; destination.mkdir(parents=True, exist_ok=True)
    (app / 'Resources').mkdir(exist_ok=True)
    with (app / 'Info.plist').open('wb') as f:
        plistlib.dump({'CFBundleName': 'TSimulator', 'CFBundleDisplayName': 'TSimulator',
            'CFBundleIdentifier': 'org.jrt-timsah.tsimulator', 'CFBundleExecutable': 'TSimulator.Desktop',
            'CFBundlePackageType': 'APPL', 'CFBundleShortVersionString': a.version,
            'CFBundleVersion': a.version, 'NSHighResolutionCapable': True,
            'LSMinimumSystemVersion': '14.0'}, f)
else:
    destination = stage / 'TSimulator'; destination.mkdir(parents=True, exist_ok=True)
shutil.copytree(a.input, destination, dirs_exist_ok=True)
for name in ['README.md','LICENSE','THIRD_PARTY_NOTICES.md','CHANGELOG.md']:
    shutil.copy2(root / name, stage / name)
shutil.copytree(root / 'docs', stage / 'docs', dirs_exist_ok=True)
shutil.copytree(root / 'assets' / 'models' / 'official', destination / 'assets' / 'models' / 'official', dirs_exist_ok=True)
shutil.copytree(root / 'assets' / 'fonts', destination / 'assets' / 'fonts', dirs_exist_ok=True)
shutil.copytree(root / 'config', destination / 'config', dirs_exist_ok=True)
if a.rid != 'win-x64':
    for name in ['TSimulator.Desktop','TSimulator.Cli']:
        path = destination / name
        if path.exists(): path.chmod(0o755)
archive = out / f'TSimulator-{a.version}-{a.rid}.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for path in sorted(stage.rglob('*')):
        if path.is_file(): z.write(path, path.relative_to(stage))
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix('.zip.sha256').write_text(f'{digest}  {archive.name}\n')
print(archive)
