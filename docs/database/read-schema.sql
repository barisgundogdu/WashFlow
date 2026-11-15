-- Run with psql -X -qAt -v ON_ERROR_STOP=1. Returns one JSON document.
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL search_path = pg_catalog;

WITH relations AS (
    SELECT c.oid, c.relname, c.relkind
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
),
columns AS (
    SELECT a.attrelid, a.attnum, jsonb_build_object(
        'name', a.attname, 'type', format_type(a.atttypid, a.atttypmod),
        'nullable', NOT a.attnotnull,
        'default', pg_get_expr(d.adbin, d.adrelid),
        'identity', nullif(a.attidentity, ''),
        'generated', nullif(a.attgenerated, '')
    ) AS entry
    FROM pg_attribute a
    JOIN relations t ON t.oid = a.attrelid
    LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
    WHERE a.attnum > 0 AND NOT a.attisdropped
),
constraints AS (
    SELECT k.conrelid, k.conname, jsonb_build_object(
        'name', k.conname,
        'kind', k.contype,
        'definition', pg_get_constraintdef(k.oid),
        'validated', k.convalidated,
        'columns', ARRAY(
            SELECT a.attname
            FROM unnest(k.conkey) WITH ORDINALITY AS key(attnum, position)
            JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = key.attnum
            ORDER BY key.position
        ),
        'references', CASE WHEN k.contype = 'f' THEN jsonb_build_object(
            'schema', pn.nspname, 'table', p.relname,
            'columns', ARRAY(
                SELECT a.attname
                FROM unnest(k.confkey) WITH ORDINALITY AS key(attnum, position)
                JOIN pg_attribute a ON a.attrelid = k.confrelid AND a.attnum = key.attnum
                ORDER BY key.position
            )
        ) END
    ) AS entry
    FROM pg_constraint k
    JOIN relations t ON t.oid = k.conrelid
    LEFT JOIN pg_class p ON p.oid = k.confrelid
    LEFT JOIN pg_namespace pn ON pn.oid = p.relnamespace
),
indexes AS (
    SELECT i.indrelid, c.relname, jsonb_build_object(
        'name', c.relname, 'definition', pg_get_indexdef(i.indexrelid),
        'unique', i.indisunique, 'valid', i.indisvalid,
        'predicate', pg_get_expr(i.indpred, i.indrelid),
        'key_columns', ARRAY(
            SELECT a.attname
            FROM unnest(i.indkey) WITH ORDINALITY AS key(attnum, position)
            LEFT JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = key.attnum
            WHERE key.position <= i.indnkeyatts
            ORDER BY key.position
        )
    ) AS entry
    FROM pg_index i
    JOIN relations t ON t.oid = i.indrelid
    JOIN pg_class c ON c.oid = i.indexrelid
),
triggers AS (
    SELECT g.tgrelid, g.tgname, jsonb_build_object(
        'name', g.tgname,
        'enabled', g.tgenabled,
        'definition', pg_get_triggerdef(g.oid),
        'function', pg_get_functiondef(g.tgfoid)
    ) AS entry
    FROM pg_trigger g
    JOIN relations t ON t.oid = g.tgrelid
    WHERE NOT g.tgisinternal
)
SELECT jsonb_pretty(jsonb_build_object(
    'schema', 'public',
    'migrations', coalesce(
        (SELECT jsonb_agg(jsonb_build_object('id', "MigrationId", 'product_version', "ProductVersion") ORDER BY "MigrationId")
         FROM public."__EFMigrationsHistory"), '[]'::jsonb
    ),
    'tables', jsonb_agg(jsonb_build_object(
        'name', t.relname, 'relation_kind', t.relkind,
        'classification', CASE WHEN t.relname = '__EFMigrationsHistory' THEN 'ef_metadata' ELSE 'application' END,
        'columns', coalesce((SELECT jsonb_agg(c.entry ORDER BY c.attnum) FROM columns c WHERE c.attrelid = t.oid), '[]'::jsonb),
        'constraints', coalesce((SELECT jsonb_agg(c.entry ORDER BY c.conname) FROM constraints c WHERE c.conrelid = t.oid), '[]'::jsonb),
        'indexes', coalesce((SELECT jsonb_agg(i.entry ORDER BY i.relname) FROM indexes i WHERE i.indrelid = t.oid), '[]'::jsonb),
        'triggers', coalesce((SELECT jsonb_agg(g.entry ORDER BY g.tgname) FROM triggers g WHERE g.tgrelid = t.oid), '[]'::jsonb)
    ) ORDER BY t.relname)
))
FROM relations t;

ROLLBACK;
