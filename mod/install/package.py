#!/usr/bin/env python3
"""Builds the release zips in dist/. Run after `dotnet build -c Release` in mod/GloomSpeed.

  GloomSpeed-<v>-Install-Windows.zip  double-click installer; downloads BepInEx itself
  GloomSpeed-<v>-Install-macOS.zip    same for macOS. A zip because it's what keeps the scripts executable
  GloomSpeed-<v>-Install-Linux.zip    same for Proton on Linux
  GloomSpeed-<v>-Manual-Windows.zip   BepInEx + plugin, laid out to unzip into the game folder
  GloomSpeed-<v>-Manual-macOS.zip     same for macOS
  GloomSpeed-<v>-Manual-Update.zip    only BepInEx/plugins/GloomSpeed.dll, for any OS
"""
import hashlib
import pathlib
import re
import stat
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
MOD = ROOT / 'mod'
DIST = ROOT / 'dist'
PLUGIN = MOD / 'GloomSpeed/bin/Release/GloomSpeed.dll'
NOTICES = MOD / 'install/THIRD-PARTY-NOTICES.txt'
PLUGIN_PATH = 'BepInEx/plugins/GloomSpeed.dll'
NOTICES_PATH = 'BepInEx/GloomSpeed-THIRD-PARTY-NOTICES.txt'
LICENSE = ROOT / 'LICENSE'
LICENSE_PATH = 'BepInEx/GloomSpeed-LICENSE.txt'
EXECUTABLE = {'run_bepinex.sh'}
FIXED_DATE = (2026, 1, 1, 0, 0, 0)

BEPINEX = {
    'Windows': (MOD / 'downloads/bepinex/BepInEx_win_x64_5.4.23.6.zip',
                '6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755'),
    'macOS': (MOD / 'downloads/bepinex-macos/BepInEx_macos_universal_5.4.23.6.zip',
              '2e711e21f6570da964fce4654b1976500dbe043f61778bce0008500078564638'),
}


def add(out, name, data, executable=False):
    info = zipfile.ZipInfo(name, FIXED_DATE)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.create_system = 3
    info.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
    out.writestr(info, data)


def full_zip(path, source, sha):
    if hashlib.sha256(source.read_bytes()).hexdigest() != sha:
        raise SystemExit(f'checksum mismatch: {source}')
    with zipfile.ZipFile(source) as src, zipfile.ZipFile(path, 'w') as out:
        for name in src.namelist():
            if not name.endswith('/'):
                add(out, name, src.read(name), name in EXECUTABLE)
        add(out, NOTICES_PATH, NOTICES.read_bytes())
        add(out, LICENSE_PATH, LICENSE.read_bytes())
        add(out, PLUGIN_PATH, PLUGIN.read_bytes())


def main():
    source = (MOD / 'GloomSpeed/src/Plugin.cs').read_text()
    version = re.search(r'Version = "(.+?)"', source).group(1)
    DIST.mkdir(exist_ok=True)

    for platform, (bepinex, sha) in BEPINEX.items():
        full_zip(DIST / f'GloomSpeed-{version}-Manual-{platform}.zip', bepinex, sha)

    with zipfile.ZipFile(DIST / f'GloomSpeed-{version}-Manual-Update.zip', 'w') as out:
        add(out, PLUGIN_PATH, PLUGIN.read_bytes())

    for platform, executable in (('Windows', False), ('macOS', True), ('Linux', True)):
        folder = f'GloomSpeed-{version}-Install-{platform}'
        with zipfile.ZipFile(DIST / f'{folder}.zip', 'w') as out:
            add(out, f'{folder}/GloomSpeed.dll', PLUGIN.read_bytes())
            add(out, f'{folder}/INSTALL.md', (ROOT / 'INSTALL.md').read_bytes())
            add(out, f'{folder}/LICENSE.txt', LICENSE.read_bytes())
            for script in sorted((MOD / 'install' / platform.lower()).iterdir()):
                add(out, f'{folder}/{script.name}', script.read_bytes(), executable)

    for built in sorted(DIST.glob(f'GloomSpeed-{version}-*.zip')):
        print(built.name, built.stat().st_size)


main()
