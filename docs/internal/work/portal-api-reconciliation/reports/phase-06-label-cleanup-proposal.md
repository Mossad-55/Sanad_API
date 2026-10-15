# V1 location display cleanup — approved on 2026-10-15

Base: `f932c46d826f758837398242ea370b6521c75e0e`, clean main/origin at intake.
The owner approved the exact 16 replacements below. They are applied locally;
final validation/report is required before separate commit/push approval.
No commit, push, publish, import, deployment, or database action is approved.
Owner explicitly required findings/corrections to be reviewed before the final
commit/push. That supersedes the older automatic commit/push workflow here.

## Recommended scope

Approved: all 16 display-only rows below. Preserve every source key, sourceId,
parent, row count and provisional verification status. Preserve original Arabic
and English names in an explicit before/after correction manifest. Do not split
compound records, merge locations, invent missing numbers, change boundaries,
activate/deactivate rows, or overwrite existing database IDs.

These labels are readable editorial proposals, not newly certified geographic
identities, official translations, or independent boundary evidence.

## Four ambiguous source labels

| Key | Current Arabic | Proposed Arabic | Proposed English |
| --- | --- | --- | --- |
| EG190806 | الاسماعيليةشرق?رقم?سابقا? | الإسماعيلية شرق | Ismailia East |
| EG190809 | السلامالجديدة?تشملالبحيراتالمرة? | السلام الجديدة (تشمل البحيرات المرة) | Al-Salam Al-Jadida (Tashmal Al-Buhayrat Al-Murra) |
| EG190810 | بوابةالقنطرة?شرقالجمرك? | بوابة القنطرة (شرق الجمرك) | Bawabet El-Qantara Sharq El-Gomrok |
| EG310205 | منجمأبوتندب?قبليوبحري? | منجم أبو تندب (قبلي وبحري) | Mangam Abu Tandab (Qibli wa Bahari) |

EG190806 is a naming decision, not just punctuation: remove the unrecoverable
former-number fragment from the display label, retaining it in provenance.
Do not supply a guessed number. The other three retain the source words and
qualifiers; parentheses are editorial display punctuation. CAPMAS core-name
references in the full scan support the core names, not every qualifier or an
automatic identity match to newer CAPMAS codes.

## Twelve delimiter / spacing-only proposals

English names without underscores are deliberately retained unchanged. Do not
normalize transliteration variants or infer extra English aliases in this pass.

| Key | Current Arabic | Proposed Arabic | Proposed English |
| --- | --- | --- | --- |
| EG020507 | ابيس 7_8_9_10 | ابيس 7 / 8 / 9 / 10 | Abis 7 / 8 / 9 / 10 |
| EG170410 | الخضرة_قلتيالصغريسابقا | الخضرة - قلتي الصغري سابقا | El-Khadra Qelti El-Saghira Sabeqan |
| EG150305 | الربع_سوق التلات | الربع - سوق التلات | Al-Rob' Souq al-Talāt |
| EG150306 | الساحلالبحري_بلوش | الساحل البحري - بلوش | Al-Sahel Al-Bahari - Baloush |
| EG150307 | الساحلالقبلي_الوهابية | الساحل القبلي - الوهابية | Al-Sahel Al-Qibli - Al-Wahhabiya |
| EG020105 | السيوف قبلى_تشمل عزبة درباله | السيوف قبلى (تشمل عزبة درباله) | Al-Siyouf Qibli - Tashmal Ezbet Dirbala |
| EG020110 | المندره قبلى_تشمل عزبة الجبل | المندره قبلى (تشمل عزبة الجبل) | Al-Mandara Qibli |
| EG030601 | قرى الجنوب - جزء_ينبعه توابع تسمى الصديق | قرى الجنوب - جزء (ينبعه توابع تسمى الصديق) | Qura El-Ganoub - Juz' Yanba'u Tawabe' Tusamma El-Siddiq |
| EG150721 | كومالذهب_زبيدةالبحريةسابقا | كوم الذهب - زبيدة البحرية سابقا | Koum El Dahab - Zubeida El Bahariya Sabeqan |
| EG181350 | منشأةالجوربجي_الشوربجي | منشأة الجوربجي - الشوربجي | Manshaat al-Gourbagy - al-Shourbagy |
| EG170227 | منشأةعصام_طوخالبراغتةسابقا | منشأة عصام - طوخ البراغتة سابقا | Manshaat Essam Toukh El-Baraghta Sabeqan |
| EG122119 | نشا_تشملكفرالبشابشةوكفرالحوارني | نشا (تشمل كفر البشابشة وكفر الحوارني) | Nesha Tashmal Kafr El-Bashabsha wa Kafr El-Hawarni |

The unusual `ينبعه` spelling is kept verbatim rather than silently replaced by
an assumed correction. Existing short English labels remain short where their
Arabic labels include an extra source scope description; no geographic scope
is inferred or removed from the preserved source history.

## After approval, before any commit/push

1. Apply exactly these before/after edits and record a 16-key correction manifest.
   Check that every original value and parent matches the pinned candidate.
2. Adapt publisher name comparison to accept only exact approved before/after
   pairs; do not disable source checks or allow arbitrary exceptions.
3. Recheck all 6086 records, duplicates, lengths, source correspondence and
   relationships; assert zero changes to non-owned rows/keys/parents/counts.
4. Rerun the isolated 90-case lookup compatibility filter against the edited
   candidate. No API startup, live database, PostgreSQL reset or provider send.
5. Regenerate the affected human-readable inventory, candidate hash/provenance
   and reports. Keep historical evidence pinned to its old hash, not overwritten
   as though earlier executions tested the edited bytes.
6. Present final diff/test evidence to the owner. Wait for explicit commit/push
   approval; publish and deployment require their own explicit order.

Catalogue cleanup does not install any locations in the API: importer work is
still a later, separately pinned task, followed by preview/reconciliation and
exact target/backup/write authorization. Publishing alone will not load JSON.
