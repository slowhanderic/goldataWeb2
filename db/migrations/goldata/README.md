# migrations/goldata (legacy database)

Empty for now. **REQ-CMP-1:** GoldataWeb2 makes no destructive schema changes to `goldata`.

Only additive changes that the PHP app can't notice go here, for example an index needed for list performance (NFR-PERF-1). Each script needs:

* the owner's sign-off, noted in the header `Ref`;
* the specific grant `goldata_migrator` needs for it (e.g. `GRANT INDEX ON goldata.contracts`), added as a new `ops/` script;
* a PHP smoke test after it is applied.

The journal for this folder is `goldata_app.schemaversions_goldata`, so `goldata` gets no extra tables.
