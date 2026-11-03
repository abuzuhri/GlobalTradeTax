import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_ci_workflow import check, REQUIRED

class CiWorkflowTests(unittest.TestCase):
    def test_all_required_controls(self):
        self.assertEqual(check('\n'.join(REQUIRED)), [])

    def test_missing_history_checkout(self):
        self.assertIn('fetch-depth: 0', check('\n'.join(x for x in REQUIRED if x != 'fetch-depth: 0')))

    def test_missing_scanner(self):
        self.assertIn('gitleaks/gitleaks:', check('\n'.join(x for x in REQUIRED if x != 'gitleaks/gitleaks:')))

if __name__ == '__main__':
    unittest.main()
