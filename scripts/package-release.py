#!/usr/bin/env python3
"""Package the portable Windows application without diagnostic tools or user data."""
import argparse
import hashlib
from pathlib import Path
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build-dir', type=Path, default=Path('build/release'))
    parser.add_argument('--version', default='v0.1.0-preview')
    parser.add_argument('--output-dir', type=Path, default=Path('dist'))
    args = parser.parse_args()
    if not args.version or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-_' for c in args.version):
        parser.error('version must contain only letters, digits, dots, hyphens or underscores')
    root = Path(__file__).resolve().parents[1]
    build = args.build_dir if args.build_dir.is_absolute() else root / args.build_dir
    output = args.output_dir if args.output_dir.is_absolute() else root / args.output_dir
    files = [build / 'DesktopTidy.exe', build / 'DesktopTidy.exe.config']
    for path in files:
        if not path.is_file():
            parser.error('missing build artifact: ' + str(path))
    name = 'DesktopTidy-' + args.version + '-win-x64'
    output.mkdir(parents=True, exist_ok=True)
    archive = output / (name + '.zip')
    if archive.exists():
        parser.error('refusing to overwrite an existing release: ' + str(archive))
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED) as package:
        for path in files:
            package.write(path, name + '/' + path.name)
        package.write(root / 'README.md', name + '/README.md')
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (output / 'SHA256SUMS.txt').write_text(digest + '  ' + archive.name + chr(10), encoding='utf-8')
    print('Portable release: ' + str(archive))
    print('SHA-256: ' + digest)


if __name__ == '__main__':
    main()
