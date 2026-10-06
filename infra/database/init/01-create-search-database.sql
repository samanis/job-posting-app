-- Runs once, on first initialization of the shared PostgreSQL volume.
-- The posting role/database come from POSTGRES_USER/POSTGRES_DB; this adds the
-- search read model as a separate database with its own least-privilege owner.
\getenv search_password SEARCH_POSTGRES_PASSWORD

CREATE ROLE jobsearch LOGIN PASSWORD :'search_password';
CREATE DATABASE job_search OWNER jobsearch;
