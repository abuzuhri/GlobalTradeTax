#!/usr/bin/env python3
"""Fail CI when NuGet's machine-readable vulnerability report contains advisories.

Reads the JSON output of: dotnet list <project> package --vulnerable --include-transitive --format json
This script does not query NuGet; the CLI command must succeed before invoking it.
"""
import json
import sys
from pathlib import Path


def audit(payload):
    if not isinstance(payload, dict) or not isinstance(payload.get('projects'), list):
        raise ValueError('Missing projects array; cannot establish a valid audit')
    if payload.get('version') != 1:
        raise ValueError('Unsupported or missing NuGet report version')
    parameters = payload.get('parameters')
    if not isinstance(parameters, str) or not {'--vulnerable', '--include-transitive'} <= set(parameters.split()):
        raise ValueError('Report must include direct and transitive vulnerability checks')
    sources = payload.get('sources')
    if not isinstance(sources, list) or not sources or any(not isinstance(source, str) or not source.strip() for source in sources):
        raise ValueError('Missing vulnerability sources')
    if payload.get('logs'):
        raise ValueError('Report contains diagnostics; audit coverage requires review')
    if not payload['projects']:
        raise ValueError('Empty projects array; cannot establish dependency coverage')
    findings = []
    for project in payload['projects']:
        if not isinstance(project, dict) or not isinstance(project.get('path'), str) or not project['path'].strip():
            raise ValueError('Malformed project or missing path')
        if project.get('logs'):
            raise ValueError('Project contains diagnostics; audit coverage requires review')
        frameworks = project.get('frameworks', [])
        if not isinstance(frameworks, list):
            raise ValueError('Malformed frameworks')
        for framework in frameworks:
            if not isinstance(framework, dict):
                raise ValueError('Malformed framework')
            for category in ('topLevelPackages', 'transitivePackages'):
                packages = framework.get(category, [])
                if not isinstance(packages, list):
                    raise ValueError(f'Malformed {category}')
                for package in packages:
                    if not isinstance(package, dict) or not isinstance(package.get('id'), str):
                        raise ValueError('Malformed package entry')
                    vulnerabilities = package.get('vulnerabilities', [])
                    if not isinstance(vulnerabilities, list):
                        raise ValueError('Malformed vulnerabilities array')
                    for vulnerability in vulnerabilities:
                        if not isinstance(vulnerability, dict):
                            raise ValueError('Malformed vulnerability entry')
                        findings.append((project.get('path', '?'), package['id'],
                                         package.get('resolvedVersion', '?'),
                                         vulnerability.get('severity', '?'),
                                         vulnerability.get('advisoryurl', vulnerability.get('advisoryUrl', '?'))))
    return findings


def main():
    if len(sys.argv) != 2:
        print('Usage: check_nuget_audit.py <nuget-audit.json>', file=sys.stderr)
        return 2
    try:
        findings = audit(json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig')))
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f'FAIL: NuGet audit report invalid: {error}', file=sys.stderr)
        return 2
    for project, package, version, severity, advisory in findings:
        print(f'VULNERABLE: {project} {package} {version} severity={severity} advisory={advisory}')
    if findings:
        print(f'FAIL: {len(findings)} NuGet vulnerability finding(s)')
        return 1
    print('PASS: No vulnerabilities in supplied NuGet audit report (not a guarantee of safety)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
