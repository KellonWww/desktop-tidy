#!/usr/bin/env python3
"""Restore pinned WebView2 and offline motion assets, then stage the web workspace."""
import argparse
import hashlib
import shutil
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / 'build' / 'web-deps'
VERSION = '1.0.3650.58'
ASSETS = {
    'gsap.min.js': 'https://cdn.jsdelivr.net/npm/gsap@3.13.0/dist/gsap.min.js',
    'ScrollTrigger.min.js': 'https://cdn.jsdelivr.net/npm/gsap@3.13.0/dist/ScrollTrigger.min.js',
    'GSAP-README.md': 'https://cdn.jsdelivr.net/npm/gsap@3.13.0/README.md',
    'Geist.woff2': 'https://cdn.jsdelivr.net/npm/geist@1.4.2/dist/fonts/geist-sans/Geist-Regular.woff2',
    'Geist-LICENSE.txt': 'https://cdn.jsdelivr.net/npm/geist@1.4.2/LICENSE.TXT',
}

SHA256 = {'Geist-LICENSE.txt': '930853ee1daa68554d9e35c8a9175affb74f699fad9a5da6ee5ebe76379d9137', 'Geist.woff2': 'cd4ee971262ec3cf32ae230b670e7d320096cd92cf8c57442f7ed105db377617', 'GSAP-README.md': '7675b3310fbe9bbed4d904d9f9afc617ae923f0463b30e4a47ae37c6bf9abb93', 'gsap.min.js': '96c01b81f44a3290e2b4532f55e2c9534b2adc43273a19f3756b2cb41f0fd0b6', 'ScrollTrigger.min.js': '308219390e5e3b84cda0c481e70caa9820883ae10bda44e6e9a149a81aac4b3f', 'webview2-1.0.3650.58.nupkg': '911a472128c82ac8baa0c486c23342cc9dd6e7dc50d754e676726642ca065c60'}

def verify(target):
    expected = SHA256[target.name]
    actual = hashlib.sha256(target.read_bytes()).hexdigest()
    if actual != expected:
        raise ValueError("Dependency hash mismatch: " + str(target))

def download(url, target):
    if target.exists():
        verify(target)
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_suffix(target.suffix + '.download')
    try:
        with urllib.request.urlopen(url, timeout=60) as response:
            temporary.write_bytes(response.read())
        if hashlib.sha256(temporary.read_bytes()).hexdigest() != SHA256[target.name]:
            raise ValueError("Downloaded dependency hash mismatch: " + target.name)
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'build' / 'web-ui')
    args = parser.parse_args()
    package = CACHE / ('webview2-' + VERSION + '.nupkg')
    download('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/' + VERSION + '/microsoft.web.webview2.' + VERSION + '.nupkg', package)
    sdk = CACHE / 'sdk'
    with zipfile.ZipFile(package) as archive:
        for name in ['lib/net462/Microsoft.Web.WebView2.Core.dll', 'lib/net462/Microsoft.Web.WebView2.WinForms.dll', 'runtimes/win-x64/native/WebView2Loader.dll', 'LICENSE.txt']:
            target = sdk / Path(name).name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read(name))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    for source in sdk.iterdir():
        shutil.copy2(source, output / ('WebView2-LICENSE.txt' if source.name == 'LICENSE.txt' else source.name))
    web = output / 'Web'
    shutil.copytree(ROOT / 'src/DesktopTidy/UI/Web', web, dirs_exist_ok=True)
    for name, url in ASSETS.items():
        target = CACHE / name
        download(url, target)
        (web / 'vendor').mkdir(exist_ok=True)
        shutil.copy2(target, web / 'vendor' / name)
    print('Offline workspace staged: ' + str(output))

if __name__ == '__main__':
    main()
