-- Standalone psql verification; do not include this wrapper inside another transaction.
\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
\ir audit-original-directory-evidence.sql
COMMIT;
