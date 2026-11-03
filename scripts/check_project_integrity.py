#!/usr/bin/env python3
"""Offline repository structural checks; NOT a legal or financial correctness test."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
errors = []
register = (ROOT / 'TASKS.md').read_text(encoding='utf-8')
for target in re.findall(r'\]\((tasks/[^)]+\.md)\)', register):
    if not (ROOT / target).is_file():
        errors.append(f'Missing task detail file: {target}')

ids = re.findall(r'^\|\s*(\d{3})\s*\|', register, flags=re.MULTILINE)
if len(ids) != len(set(ids)):
    errors.append('Duplicate task identifiers in TASKS.md')
if len(ids) < 14:
    errors.append('Expected at least 14 tracked tasks')

for filename in ['src/GlobalTradeTax.Api/GlobalTradeTax.Api.csproj',
                 'tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj',
                 '.github/workflows/dotnet-ci.yml']:
    if not (ROOT / filename).is_file():
        errors.append(f'Missing required project file: {filename}')

# Check that task 005-008 remain explicitly tracked. This is structural only.
for task_id in ('005', '006', '007', '008'):
    if task_id not in ids:
        errors.append(f'Missing UK tariff task: {task_id}')

if errors:
    for error in errors:
        print(f'ERROR: {error}', file=sys.stderr)
    sys.exit(1)
print(f'PASS: {len(ids)} task IDs, task links, project files and UK task coverage')
