import unittest
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_nuget_audit import audit


class NugetAuditTests(unittest.TestCase):
    def report(self, projects):
        return {'version': 1, 'parameters': '--vulnerable --include-transitive',
                'sources': ['https://api.nuget.org/v3/index.json'], 'projects': projects}

    def test_clean(self):
        self.assertEqual([], audit(self.report([{'path': 'api.csproj', 'frameworks': [{'topLevelPackages': [], 'transitivePackages': []}]}])))

    def test_clean_sdk_report_omits_frameworks(self):
        self.assertEqual([], audit(self.report([{'path': 'api.csproj'}])))

    def test_detects_transitive(self):
        payload = self.report([{'path': 'api.csproj', 'frameworks': [{'transitivePackages': [{'id': 'Example', 'resolvedVersion': '1.0', 'vulnerabilities': [{'severity': 'high', 'advisoryurl': 'https://example.org/advisory'}]}]}]}])
        self.assertEqual(1, len(audit(payload)))

    def test_rejects_empty_report(self):
        with self.assertRaises(ValueError):
            audit(self.report([]))

    def test_rejects_missing_report_metadata(self):
        with self.assertRaises(ValueError):
            audit({'projects': [{'path': 'api.csproj'}]})

    def test_rejects_malformed_frameworks(self):
        with self.assertRaises(ValueError):
            audit(self.report([{'path': 'api.csproj', 'frameworks': None}]))

    def test_rejects_incomplete_audit_parameters(self):
        payload = self.report([{'path': 'api.csproj'}])
        payload['parameters'] = '--vulnerable'
        with self.assertRaises(ValueError):
            audit(payload)

    def test_rejects_missing_sources(self):
        payload = self.report([{'path': 'api.csproj'}])
        payload['sources'] = []
        with self.assertRaises(ValueError):
            audit(payload)

    def test_rejects_diagnostics_in_clean_looking_report(self):
        for diagnostic_scope in ('report', 'project'):
            with self.subTest(scope=diagnostic_scope):
                payload = self.report([{'path': 'api.csproj'}])
                target = payload if diagnostic_scope == 'report' else payload['projects'][0]
                target['logs'] = [{'level': 'Error', 'message': 'Source unavailable'}]
                with self.assertRaises(ValueError):
                    audit(payload)


if __name__ == '__main__':
    unittest.main()
