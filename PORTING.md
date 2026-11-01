# Required carry-forward policy

Every SPT 4.1.6 update must include the separately added fixes and compatibility integrations. Use the latest corrected native source as the port base; merge upstream 4.0.13 changes into it. Do not replace the native tree with the upstream source and only change the target/version labels.

Preserve and verify:

- Use Items Anywhere: optional dependency, explicit query-patch order, configured grenade/player reload slots, native bot reload rules, exact collector signatures, clean failure fallback and per-copy wheel/quick-select availability.
- HollywoodFX: optional visual adapter, native explosion scaling, deferred dispatch and normal pool reuse.
- APBS: automatic detection, native tables, final-load timing, exclusive standalone/APBS registration and shared rates.
- Native inspection: typed item/label fields, Examined gating, guarded presentation fallback and backpack/grid behavior.
- Medical/recoil/night vision: typed native effect callbacks, food/drink eligibility guards, native surgery/interruption members and repaired recoil/audio fields.
- Grenades/quests: live native packing cooldowns, retained item identities, independent charge ledgers, base-item quest equivalence and protection from destructive hand-ins/placement.

Adopt intentional upstream feature changes while preserving these native repairs. New status readers must use the repaired native members/helpers as well as gameplay code. Check new reflection calls against the actual 4.1.6 export.

Run the current regression suite, real-server/native metadata audits, UIA-present and UIA-absent audits, audio/layout checks, version contracts and tools/audit_carry_forward.py. Update the guards deliberately when a feature requires changing a protected file, and document why. Record exact tested input hashes and include current evidence in Source.

Each release uses a new MAJOR.MINOR.PATCH version consistently in declarations, binary file/product metadata, docs and archive names. Produce only Source and fresh-install Runtime downloads. Runtime installs under BepInEx/plugins and SPT_Runtime/user/mods. Keep game/optional DLLs, SDKs, build output and caches out of Source. Distinguish reference-based tests from live game verification.
