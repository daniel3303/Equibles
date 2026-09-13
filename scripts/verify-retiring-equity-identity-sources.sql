\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL timezone = 'UTC';
SET LOCAL extra_float_digits = 3;
\ir audit-retiring-equity-identity-sources.sql
COMMIT;
