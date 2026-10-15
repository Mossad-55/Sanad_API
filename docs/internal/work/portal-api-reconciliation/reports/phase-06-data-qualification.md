# PORTAL-06 — remaining data qualification attempt

Owner requested completion on 2026-10-15. Intake: clean main/origin `4537af3`.
Scope: offline candidate/evidence/scripts/docs only; public-source reads allowed.
No importer, app change, database access, provider, Production, publish or deploy.
The mastermind performed the bounded research directly; no additional worker
or repeated app review was needed. All-Egypt scope remains unchanged.

## Completed: attributable historical backbone comparison

The publisher's [country source note](https://openadmindata.org/eg/) now points
to [OCHA/HDX COD-AB Egypt](https://data.humdata.org/dataset/cod-ab-egy).
The [HDX metadata API](https://data.humdata.org/api/3/action/package_show?id=cod-ab-egy)
was fetched directly: CAPMAS attributed source, OCHA/FISS publisher, stated
CC BY-IGO terms. The resource was downloaded into memory, hashed, and parsed
as data; no downloaded code or package was executed and no raw dump was saved.

Independent exact comparison, not fuzzy matching:

| Candidate level | Included / reference | Arabic name/key correspondence | Differences |
| --- | --- | --- | --- |
| Governorates | 27 / 27 | All exact | None |
| Cities/districts | 347 / 365 | All included exact | 18 documented English disambiguations; 2 intentional parent corrections |
| Areas | 5712 / 5716 | All included exact; all immediate parents exact | 4 documented excluded customs children; reference English blank on all 5712 included rows |

This qualifies the historical Arabic backbone's correspondence and attribution.
It does not prove the GitHub publisher's full transformation process or the
origin/quality of its added Area English names. No legal conclusion is made.
Do not undo the Armant/Esna correction to make an outdated reference match.

Reproduce explicitly (network needed; public reference only):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/internal/work/portal-api-reconciliation/reference-data/egypt/Compare-HdxReference.ps1 -AllowNetwork
```

Execution succeeded, exit 0. Zero denotes execution, NOT readiness acceptance.
The input ZIP is pinned by SHA-256; drift fails closed. Recorded counts, key
differences and source dates are in
[hdx-reference-evidence.json](../reference-data/egypt/hdx-reference-evidence.json).
No API/test database fixtures required: the existing candidate was read first.

## Why the remaining gaps cannot honestly be marked Done

HDX explicitly describes ADM3 coverage as partial. Its dates distinguish 2017
boundary creation/validity from a 2024 review and 2026 resource publication.
Repackaging the same records does not establish current national completeness.
The direct HDX reference has no Area English names to validate the additions.

Official GOPP references also demonstrate a taxonomy/vintage issue:
[strategic plan](https://gopp.gov.eg/luxor-strategic-plan/) describes six markaz,
two new cities and an urban section, whereas its
[2023 inventory report](https://gopp.gov.eg/محافظ-الأقصر-يستقبل-وفد-الهيئة-العامة/)
lists seven named centres including Luxor. The
[urban map](https://gopp.gov.eg/الخريطة-العمرانية-لمحافظة-الاقصر/)
distinguishes five levels down to hamlets. These pages support the existence
of missing entities, not a complete current parent/child reconciliation. Do not
split historical `EG2902`/Tiba children into new centres by guessing from names.
The 2023 report link is the exact indexed primary URL; pages were indexed but
some direct page fetches timed out, so no new per-row boundary claim is made.

Smouha is a real consumer locality: the Alexandria governorate's
[tourism directory](https://www.alexandria.gov.eg/services/daleel/touristvillages.aspx)
uses it as an area. This does not prove equivalence to the legacy seed City or
an exact administrative polygon. Likewise Cairo's
[April 2025 bulletin](https://cairo.gov.eg/ar/Monthly%20Publication/april2025.pdf)
includes the New Administrative Capital among new cities. Existence is no
longer the question; full selectable locality hierarchy and legacy-ID mapping
remain to be established. Neither record was invented or auto-reparented.

The CAPMAS [population estimate index](https://old.capmas.gov.eg/Pages/StaticPages.aspx?page_id=7188)
advertises 2025 district tables; direct access timed out in web and one bounded
20-second HTTP attempt. The CEDEJ/CAPMAS map returned a redirect-loop fetch
failure. These are access limitations, not proof that no better reference exists.
No attempt was made to bypass access restrictions. Population totals alone
would not establish missing residential Areas or approve English translations.

## Exact dependency to finish (not another backend-code phase)

Obtain a current, reusable locality catalogue/export with an as-of date,
stable IDs, Arabic names and governorate/district/locality parent links,
including current Luxor, the New Capital and consumer-locality supplements.
English labels may be supplied or separately reviewed transliterations; they
need not falsely be called official translations. A mapped, versioned set of
current change records plus the qualified historical backbone is also viable.
It must cover nationwide branches, not only the named sample gaps.

An owner-designated geography/data steward or source provider must supply or
validate that missing factual input and the consumer-to-administrative mapping.
This is not a request to approve known errors or reduce All-Egypt scope.
No external message, purchase, account signup or source-provider contact was
authorized or performed. Once supplied, reconcile against the existing stable
keys, rerun structural/readiness gates, then return for catalogue acceptance.
Importer implementation and live target/backup/write approval remain later.

## Closeout state

Research/comparison completed; national release qualification remains **Blocked**
on the source/steward evidence above. Candidate bytes and all readiness flags
remain unchanged. Existing structural fixtures stay applicable. The separate
readiness gate must continue to fail; no blanket provisional-to-verified flip.
No solution build or app suite is needed for these script/document-only inputs.

Final verification: comparison script parses; no-network-by-default guard
rejects invocation before any request; all four local JSON files parse and the
evidence candidate hash matches. Relative links in the new report/provenance/
coverage resolve. Structural validation rerun: exit 0, 12/12 fixtures, zero
candidate failures; readiness rerun: expected exit 2, ten reported reasons
(four unresolved groups plus provisional Areas, three flags and five gaps).
The readiness failure is the disclosed data finding, not a failing code test.
Diff whitespace check clean; no compiled inputs changed.
