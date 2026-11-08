#!/usr/bin/env python3
"""Emit a checked migration transaction for psql; connection settings stay with psql."""
import argparse
import hashlib
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = (
    ('001_init.sql', 'cd8eca852d3538209e954dd8a89b9426fa361f9bbdd72bba753998d10615a9e2'),
    ('002_tenants.sql', '744d448ad9ffb9dbecd7bd553beb2387a0b2b425c3c844084f8a7f891c1b6256'),
    ('003_audit.sql', 'b8fbf8b5922a71cf1cdd7e4c56becc5f860b08873f908f78e5bff619801f3c2e'),
    ('004_tariff_staging.sql', '0048a3c02374b4b47c0f499cc6fbab50faa3192ec64b0bc1d27bf0373aa52bb4'),
    ('005_tariff_provenance.sql', '8f30848f5d1cc4b2302c534bfac1ad109424593f10afe8f27fea5783e4b625f3'),
)
BASELINES = {'v0.4': 1, 'v1.2': 4, 'current': 5}


def load_migrations(directory):
    """Fail closed on unknown, missing, reordered or byte-modified migration files."""
    paths = sorted(directory.glob('*.sql'))
    if [p.name for p in paths] != [name for name, _ in MANIFEST]:
        raise ValueError('Migration files do not match the release manifest')
    result = []
    for path, (name, digest) in zip(paths, MANIFEST):
        data = path.read_bytes()
        if len(data) > 1024 * 1024 or hashlib.sha256(data).hexdigest() != digest:
            raise ValueError('Migration checksum mismatch: ' + name)
        result.append(data.decode('utf-8'))
    return result


def validation():
    """Validate every stored row against the immutable ordered release manifest."""
    values = ','.join("(%d,'%s','%s')" % (i, name, digest)
                      for i, (name, digest) in enumerate(MANIFEST, 1))
    return f"""
DO $check$
BEGIN
 IF EXISTS (SELECT 1 FROM public.gtt_schema_migrations m
   FULL JOIN (VALUES {values}) AS e(version,name,checksum) USING (version)
   WHERE m.version IS NOT NULL AND (e.version IS NULL OR m.name IS DISTINCT FROM e.name
       OR m.checksum IS DISTINCT FROM e.checksum OR m.recorded_at IS NULL
       OR m.origin IS NULL OR m.origin NOT IN ('applied','adopted'))) OR
    EXISTS (SELECT version FROM public.gtt_schema_migrations GROUP BY version HAVING count(*) <> 1) OR
    EXISTS (SELECT 1 FROM public.gtt_schema_migrations WHERE version IS NULL OR version < 1) OR
    (SELECT count(*) FROM public.gtt_schema_migrations) <>
       COALESCE((SELECT max(version) FROM public.gtt_schema_migrations),0) OR
    EXISTS (SELECT 1 FROM public.gtt_schema_migrations a JOIN public.gtt_schema_migrations b
       ON a.version < b.version WHERE a.origin='applied' AND b.origin='adopted') THEN
   RAISE EXCEPTION 'Migration history is inconsistent or has drifted';
 END IF;
END $check$;
"""


# Compare structural catalog metadata in a transaction-created reference schema. The
# search path makes pg_get_* output independent of schema qualification.
SIGNATURE = r"""
CREATE FUNCTION pg_temp.schema_signature(ns text) RETURNS TABLE(kind text, identity text, definition text)
LANGUAGE sql AS $signature$
 SELECT 'relation', c.relname, concat_ws('|',c.relkind,c.relpersistence,c.relrowsecurity,c.relforcerowsecurity)
 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname=ns AND c.relname <> 'gtt_schema_migrations'
 AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class t ON t.oid=i.indrelid WHERE i.indexrelid=c.oid AND t.relname='gtt_schema_migrations')
 UNION ALL
 SELECT 'column', c.relname||'.'||a.attname,
 concat_ws('|',a.attnum,pg_catalog.format_type(a.atttypid,a.atttypmod),a.attnotnull,a.attidentity,a.attgenerated,
   COALESCE(pg_catalog.pg_get_expr(d.adbin,d.adrelid),''),a.attcollation)
 FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid=a.attrelid
 JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
 LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid=c.oid AND d.adnum=a.attnum
 WHERE n.nspname=ns AND c.relname <> 'gtt_schema_migrations' AND a.attnum>0 AND NOT a.attisdropped
 AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class t ON t.oid=i.indrelid WHERE i.indexrelid=c.oid AND t.relname='gtt_schema_migrations')
 UNION ALL
 SELECT 'constraint', c.relname||'.'||k.conname, pg_catalog.pg_get_constraintdef(k.oid)
 FROM pg_catalog.pg_constraint k JOIN pg_catalog.pg_class c ON c.oid=k.conrelid
 JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname=ns AND c.relname <> 'gtt_schema_migrations'
 UNION ALL
 SELECT 'index', c.relname, concat_ws('|',pg_catalog.pg_get_indexdef(c.oid),i.indisvalid,i.indisready,i.indislive)
 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
 JOIN pg_catalog.pg_index i ON i.indexrelid=c.oid
 WHERE n.nspname=ns AND c.relkind IN ('i','I')
 AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class t ON t.oid=i.indrelid WHERE i.indexrelid=c.oid AND t.relname='gtt_schema_migrations')
 UNION ALL
 SELECT 'sequence', c.relname, concat_ws('|',s.seqtypid,s.seqstart,s.seqincrement,s.seqmax,s.seqmin,s.seqcache,s.seqcycle)
 FROM pg_catalog.pg_sequence s JOIN pg_catalog.pg_class c ON c.oid=s.seqrelid
 JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=ns
 UNION ALL
 SELECT 'type', t.typname, t.typtype::text FROM pg_catalog.pg_type t
 JOIN pg_catalog.pg_namespace n ON n.oid=t.typnamespace
 WHERE n.nspname=ns AND t.typtype IN ('d','e','r','m')
 UNION ALL
 SELECT 'extension', e.extname, e.extversion FROM pg_catalog.pg_extension e
 JOIN pg_catalog.pg_namespace n ON n.oid=e.extnamespace WHERE n.nspname=ns
 UNION ALL
 SELECT 'function', p.proname, pg_catalog.pg_get_function_identity_arguments(p.oid)
 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname=ns
 UNION ALL
 SELECT 'trigger', c.relname||'.'||t.tgname, pg_catalog.pg_get_triggerdef(t.oid)
 FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid=t.tgrelid
 JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=ns AND NOT t.tgisinternal
 UNION ALL
 SELECT 'policy', c.relname||'.'||p.polname, p.polcmd::text
 FROM pg_catalog.pg_policy p JOIN pg_catalog.pg_class c ON c.oid=p.polrelid
 JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=ns
$signature$;
"""


COMPARE = """DO $compare$ BEGIN
 IF EXISTS ((SELECT kind, identity, replace(definition,'public.','') FROM pg_temp.schema_signature('public')
             EXCEPT SELECT kind, identity, replace(definition,'gtt_migration_reference.','') FROM pg_temp.schema_signature('gtt_migration_reference'))
            UNION ALL
            (SELECT kind, identity, replace(definition,'gtt_migration_reference.','') FROM pg_temp.schema_signature('gtt_migration_reference')
             EXCEPT SELECT kind, identity, replace(definition,'public.','') FROM pg_temp.schema_signature('public'))) THEN
   RAISE EXCEPTION 'Schema does not match recorded or requested migration baseline';
 END IF;
END $compare$;
DROP SCHEMA gtt_migration_reference CASCADE;
SET LOCAL search_path = pg_catalog, public;"""


def reference_check(migrations, baseline=None):
    """Compare the current catalog with a reference built from the selected prefix."""
    sql = ["CREATE SCHEMA gtt_migration_reference;", "SET LOCAL search_path = gtt_migration_reference, pg_catalog;"]
    for i, migration in enumerate(migrations, 1):
        if baseline is None:
            sql += [f"SELECT EXISTS (SELECT 1 FROM public.gtt_schema_migrations WHERE version={i}) AS present \\gset",
                    r'\if :present', migration, r'\endif']
        elif i <= BASELINES[baseline]:
            sql.append(migration)
    sql += ["SET LOCAL search_path = pg_catalog;", SIGNATURE, COMPARE]
    return sql


def build_sql(migrations, mode, baseline=None):
    """Build one locked transaction; all schema changes and records commit together."""
    sql = [r'\set ON_ERROR_STOP on', 'BEGIN;', "SET LOCAL lock_timeout = '30s';",
           "SET LOCAL statement_timeout = '5min';", 'SELECT pg_advisory_xact_lock(1196708932, 3);',
           "SET LOCAL search_path = pg_catalog, public;",
           "SELECT to_regclass('public.gtt_schema_migrations') IS NOT NULL AS ledger_exists \\gset",
           r'\if :ledger_exists', validation()]
    if mode == 'apply':
        if baseline:
            sql.append("DO $$ BEGIN RAISE EXCEPTION 'Database already has a ledger; omit adoption option'; END $$;")
        else:
            sql += reference_check(migrations)
    sql.append(r'\else')
    if mode == 'status':
        sql += ["SELECT 'No ledger: explicit adoption is required for an existing schema' AS status;"]
    else:
        if not baseline:
            sql += ["""DO $guard$ BEGIN
 IF EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public') OR
   EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public') OR
   EXISTS (SELECT 1 FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='public') OR
   EXISTS (SELECT 1 FROM pg_catalog.pg_extension e JOIN pg_catalog.pg_namespace n ON n.oid=e.extnamespace WHERE n.nspname='public') THEN
   RAISE EXCEPTION 'Untracked schema: choose an explicit adoption baseline';
 END IF;
END $guard$;"""]
        if baseline:
            sql += reference_check(migrations, baseline)
        sql += ["""CREATE TABLE public.gtt_schema_migrations (
 version integer PRIMARY KEY CHECK(version > 0), name text NOT NULL UNIQUE,
 checksum text NOT NULL CHECK(checksum ~ '^[0-9a-f]{64}$'),
 origin text NOT NULL CHECK(origin IN ('applied','adopted')),
 recorded_at timestamptz NOT NULL DEFAULT clock_timestamp());"""]
        if baseline:
            for i, (name, digest) in enumerate(MANIFEST[:BASELINES[baseline]], 1):
                sql.append(f"INSERT INTO public.gtt_schema_migrations(version,name,checksum,origin) VALUES ({i},'{name}','{digest}','adopted');")
    sql.append(r'\endif')
    if mode == 'apply':
        for i, ((name, digest), body) in enumerate(zip(MANIFEST, migrations), 1):
            sql += [f"SELECT NOT EXISTS (SELECT 1 FROM public.gtt_schema_migrations WHERE version={i}) AS pending \\gset",
                    r'\if :pending', 'SET LOCAL search_path = public, pg_catalog;', body,
                    f"INSERT INTO public.gtt_schema_migrations(version,name,checksum,origin) VALUES ({i},'{name}','{digest}','applied');", r'\endif']
        sql.append(validation())
    if mode == 'status':
        values = ','.join("(%d,'%s')" % (i, name) for i, (name, _) in enumerate(MANIFEST, 1))
        sql += [r'\if :ledger_exists', f"SELECT e.version,e.name,CASE WHEN m.version IS NULL THEN 'pending' ELSE m.origin END AS state,m.recorded_at FROM (VALUES {values}) AS e(version,name) LEFT JOIN public.gtt_schema_migrations m USING(version) ORDER BY e.version;", r'\endif']
    sql += [r'\if :ledger_exists', "SELECT version,name,checksum,origin,recorded_at FROM public.gtt_schema_migrations ORDER BY version;", r'\else']
    if mode == 'apply':
        sql.append("SELECT version,name,checksum,origin,recorded_at FROM public.gtt_schema_migrations ORDER BY version;")
    sql += [r'\endif', 'COMMIT;']
    return '\n'.join(sql) + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['status', 'apply'])
    parser.add_argument('--adopt', choices=BASELINES)
    args = parser.parse_args()
    if args.mode == 'status' and args.adopt:
        parser.error('--adopt requires apply')
    try:
        print(build_sql(load_migrations(ROOT / 'db'), args.mode, args.adopt), end='')
    except (OSError, UnicodeError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
