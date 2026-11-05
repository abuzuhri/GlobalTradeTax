#!/usr/bin/env python3
"""Offline guard for required CI security controls; not a YAML parser."""
from pathlib import Path
import sys

REQUIRED = (
    'permissions:', 'contents: read', 'persist-credentials: false',
    'fetch-depth: 0', 'gitleaks/gitleaks:', 'git log',
    'dotnet test', 'scripts/smoke_api.py', 'dependency-audit:', 'repository-checks:',
    'uk-live-contract:', 'TRADE_RUN_UK_LIVE_CONTRACT:', "--filter 'Category=UkLiveContract'",
    'uk-managed-live-contract:', 'TRADE_RUN_UK_MANAGED_LIVE_CONTRACT:', "--filter 'Category=UkManagedLiveContract'",
    'container-postgres:', 'scripts/check_postgres_integration.py', 'postgres-integration-results',
)

def check(contents: str) -> list[str]:
    return [token for token in REQUIRED if token not in contents]

if __name__ == '__main__':
    path = Path(__file__).resolve().parents[1] / '.github/workflows/dotnet-ci.yml'
    missing = check(path.read_text(encoding='utf-8'))
    if missing:
        print('CI security checks missing: ' + ', '.join(missing), file=sys.stderr)
        sys.exit(1)
    print('CI security controls present (static check only)')
