#!/usr/bin/env python3
"""Conservative tracked-file secret guard; not a replacement for dedicated scanners."""
import argparse
import re
import subprocess
import sys
from pathlib import Path

PATTERNS = {
    'private-key': re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----'),
    'github-token': re.compile(rb'\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36,}\b'),
    'github-fine-grained-token': re.compile(rb'\bgithub_pat_[A-Za-z0-9_]{40,}\b'),
    'aws-access-key-id': re.compile(rb'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'),
    'stripe-live-secret': re.compile(rb'\bsk_live_[A-Za-z0-9]{20,}\b'),
    'slack-token': re.compile(rb'\bxox[baprs]-[A-Za-z0-9-]{20,}\b'),
}
MAX_FILE_SIZE = 2_000_000


def scan_bytes(data):
    """Return pattern names only; never return or print matched secrets."""
    return sorted(name for name, pattern in PATTERNS.items() if pattern.search(data))


def tracked_files(root, include_untracked=False):
    command = ['git', '-C', str(root), 'ls-files', '-z']
    if include_untracked:
        command.extend(['--cached', '--others', '--exclude-standard'])
    proc = subprocess.run(command, capture_output=True, check=True)
    return [root / p.decode('utf-8', 'surrogateescape') for p in proc.stdout.split(b'\0') if p]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--include-untracked', action='store_true',
                        help='also scan non-ignored untracked files without changing the Git index')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    try:
        paths = tracked_files(root, include_untracked=args.include_untracked)
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        print(f'FAIL: git tracked-file inventory unavailable ({type(exc).__name__})', file=sys.stderr)
        return 2
    if not paths:
        print('FAIL: no files selected; secret scan coverage unknown', file=sys.stderr)
        return 2
    findings = []
    for path in paths:
        if not path.is_file() or path.is_symlink():
            continue
        try:
            if path.stat().st_size > MAX_FILE_SIZE:
                print(f'FAIL: file too large to scan: {path.relative_to(root)}', file=sys.stderr)
                return 2
            data = path.read_bytes()
        except OSError:
            print(f'FAIL: cannot read file: {path.relative_to(root)}', file=sys.stderr)
            return 2
        for kind in scan_bytes(data):
            findings.append((str(path.relative_to(root)), kind))
    for path, kind in findings:
        print(f'POTENTIAL SECRET: {path} [{kind}]')
    if findings:
        print(f'FAIL: {len(findings)} potential secret finding(s)')
        return 1
    coverage = 'tracked and non-ignored untracked' if args.include_untracked else 'tracked'
    print(f'PASS: no high-confidence secret patterns found in {len(paths)} {coverage} files (limited coverage)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
