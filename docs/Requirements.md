# Requirements

## Objective and scope

Build a .NET 9.0 console application named `PurlMaster` that enriches CycloneDX 1.2–1.6 JSON SBOMs with generic Package URLs derived exclusively from component CPE identifiers. Process a single file or a directory, optionally including subdirectories. Existing PURL values must remain unchanged.

Only JSON documents with `bomFormat: "CycloneDX"` and `specVersion` exactly equal to `"1.2"`, `"1.3"`, `"1.4"`, `"1.5"` or `"1.6"` are supported. XML, other CycloneDX versions, other BOM formats, CPE dictionary lookups, ecosystem inference and network services are out of scope. Processing does not upgrade or downgrade a document's version.

## Normative references

- [CycloneDX 1.6 / ECMA-424](specifications/ECMA-424_1st_edition_june_2024.md) and the supplied [JSON schema](schemas/bom-1.6.schema.json).
- Supplied JSON schemas for [CycloneDX 1.2](schemas/bom-1.2.schema.json), [1.3](schemas/bom-1.3.schema.json), [1.4](schemas/bom-1.4.schema.json), and [1.5](schemas/bom-1.5.schema.json).
- [PURL / ECMA-427](specifications/ECMA-427_1st_edition_december_2025.md), especially clauses 5.3–5.6 on delimiters, encoding and individual fields.
- [CPE Naming Specification / NIST IR 7695](specifications/nistir7695.md), especially sections 6.1 and 6.2 on URI and formatted-string bindings. Matching, dictionaries and applicability specifications provide context but are not needed for enrichment.

The following CPE-to-PURL mapping is an application policy, not a standardized conversion prescribed by those specifications. A generic PURL does not assert membership in any package ecosystem.

## Functional requirements

### FR-01: Input validation

1. Read UTF-8 JSON, with or without a UTF-8 BOM. Reject malformed JSON, duplicate object property names and unsupported BOM formats or versions before writing output.
2. Validate input and changed output against the supplied Draft-07 schema selected by `specVersion`. Use the regular `bom-1.2.schema.json` for 1.2; the optional strict variant is not used. Do not load both 1.2 variants, which share a schema ID. Resolve all external schema references using bundled local resources, independently of the input's `$schema` value. Validation must not fetch schemas from the network. Enforce each version's required fields, including root BOM `version` in 1.2–1.4 and component `version` in 1.2/1.3.
3. Report the file and JSON location of validation failures. Schema format annotations need not be asserted beyond Draft-07 defaults. CPE parsing is checked separately because the CycloneDX schema does not constrain CPE syntax.

### FR-02: Component discovery

Visit every component position defined by the schema of the document's version:

- Top-level `components` and `metadata.component`.
- From 1.5: component lists in `metadata.tools` and vulnerability `tools` when using the object form, `formulation[].components`, and `annotations[].annotator.component`.
- In 1.6: `declarations.targets.components`.
- Recursively, nested `components` and `pedigree.ancestors`, `pedigree.descendants`, and `pedigree.variants` within every discovered component.

Do not treat arbitrary objects containing `cpe`, including evidence identity records and properties, as components. Preserve additional properties allowed by older schemas without interpreting them as component locations introduced in later versions.

### FR-03: Existing identifiers

1. If a component already has a `purl` property, retain it exactly as a JSON value, without normalization, replacement or CPE parsing. This includes an empty string accepted by the supplied schema.
2. Components without CPE remain unchanged. Empty, malformed or unsupported CPE strings are skipped with a component-specific diagnostic.
3. Keep every original CPE, component field, BOM reference, dependency, metadata field and other JSON value. JSON formatting may change when enrichment occurs. Do not increment BOM `version` or change timestamps as part of this narrowly scoped transformation.

### FR-04: CPE parsing

1. Support CPE 2.3 formatted strings (`cpe:2.3:`) with exactly eleven attributes and escape-aware colon splitting.
2. Support the backward-compatible URI binding (`cpe:/`) with up to seven fields, omitted trailing fields, percent decoding, and packed edition attributes.
3. Distinguish logical ANY and NA from quoted/percent-encoded literal characters. Distinguish unquoted wildcard patterns from literal `*` and `?`. Check attribute syntax, part and language.
4. Normalize ASCII CPE attribute values to lowercase using invariant rules. Keep punctuation, underscores and literal characters; do not infer names or versions from other component fields.
5. Support printable ASCII URI escapes specified by NIST, plus `%01` and `%02` wildcard forms. Other legacy URI escapes are explicitly unsupported and produce a diagnostic; the application does not claim full CPE Naming implementation conformance.

### FR-05: Generic PURL mapping

| CPE attribute | PURL destination | Policy |
| --- | --- | --- |
| `vendor` | namespace | One namespace segment; omit for ANY/NA. Reject a concrete vendor containing `/`, which cannot form one valid namespace segment. |
| `product` | name | Required concrete value. Skip a component if product is ANY/NA. |
| `version` | version | Use a concrete value; omit `@version` for ANY/NA. |
| `part` | `cpe_part` qualifier | Retain concrete `a`, `o`, or `h`; omit ANY/NA. |
| `update`, `edition`, `language`, `sw_edition`, `target_sw`, `target_hw`, `other` | Corresponding `cpe_<attribute>` qualifiers | Retain concrete values; omit ANY/NA. |

1. Use the constant scheme `pkg` and type `generic`. Do not add a subpath.
2. Skip a CPE containing a wildcard pattern in any attribute rather than assert a concrete package identity for a pattern. This is a warning, not a file validation failure.
3. Decode CPE syntax first, then apply PURL UTF-8 percent encoding once. Preserve unreserved characters and literal `:` as required by ECMA-427; encode data delimiters such as `/`, `@`, `?`, `#`, `&`, `=`, backslash and literal `%`. Use uppercase hexadecimal escapes. Sort qualifier keys ordinally.
4. The conversion is deterministic and enrichment is idempotent. Missing/NA values are not synthesized, and the original CPE retains distinctions omitted from the PURL.

Example:

```text
cpe:2.3:a:busybox:busybox:1.36.1:*:*:*:*:*:*:*
→ pkg:generic/busybox/busybox@1.36.1?cpe_part=a
```

### FR-06: Command-line interface and output

```text
purl-master <file-or-directory> [--output <path>] [--recursive] [--overwrite]
purl-master <file-or-directory> --in-place [--recursive]
purl-master --help
```

1. Accept one input path per invocation, including paths containing spaces. Provide `-o` as an alias for `--output` and `-h` for `--help`. Reject unknown, repeated or incompatible options and missing values with usage guidance.
2. Single-file input defaults to a sibling `<stem>.purl.json` file. A supplied output path is a file path.
3. Directory input processes files with case-insensitive `.json` extensions. Default output is a sibling `<directory-name>.purl` directory; preserve relative file paths under the output directory. Include subdirectories only with `--recursive`.
4. Skip symbolic links/reparse points during discovery; do not follow directory links. Snapshot candidates before processing and visit them in deterministic ordinal order.
5. Prevent an output directory equal to or inside the input directory; reject aliased input/output paths. `--in-place` is the explicit mode for rewriting input files and cannot be combined with `--output` or `--overwrite`.
6. Existing output files are not overwritten unless `--overwrite` is set. Input modification requires `--in-place`.
7. Write through a temporary file beside the destination and rename only after validation and successful serialization. Never leave a partially written destination. Clean up temporary files on failure.
8. If no component changes, preserve the original bytes in separate output, or leave the source file untouched in place. Verify that an input has not changed concurrently before replacing it in place.

### FR-07: Diagnostics and batch behavior

1. Print per-file results and a final summary including successful/failed files, added PURLs, existing PURLs, components without CPE and skipped CPEs. Send warnings and errors to standard error.
2. Include a file path and JSON pointer for component diagnostics. Do not dump complete SBOMs to the console.
3. Continue processing independent files after a read, validation, CPE or write problem. Invalid or unsupported files produce no output. Invalid/unsupported CPEs only skip the affected component, allowing valid components in that file to be enriched.
4. Exit `0` for successful processing, including documented component skips; `1` for one or more file/discovery failures or no JSON candidates; `2` for invalid command-line arguments. Help exits `0` without processing.

## Quality requirements

- Target `net9.0` with nullable reference types enabled. Keep parsing, mapping, document traversal, validation and filesystem/CLI work separate.
- Process one complete SBOM at a time; memory consumption may scale with the largest file. No streaming or parallel processing is required.
- Bundle the supplied schemas for published execution outside the repository. Use pinned dependencies; after restore the application works offline.
- Regular builds and publish must copy the five regular `bom-1.2.schema.json` through `bom-1.6.schema.json` files, `spdx.schema.json` and `jsf-0.82.schema.json` into a `Schemas/` subdirectory of the output. Retain their filenames so relative schema references can be resolved by consumers. Validation continues to use embedded copies. The default deployment requires an installed .NET 9 runtime.
- Add meaningful automated tests for CPE binding/encoding, existing PURL preservation, every component location, malformed/unsupported input, schema validation, idempotence, directory recursion, collisions, atomic output behavior and CLI exit codes.
- Include build, test, publish and usage instructions. Keep `Roadmap.md` current with implementation status, decisions, commands and remaining work so another Codex instance can resume without reconstructing the conversation.

## Acceptance criteria

1. Enriching `docs/test-bom.json` adds `pkg:generic/busybox/busybox@1.36.1?cpe_part=a` to BusyBox, preserves both existing PURLs and every other JSON value, and produces a schema-valid CycloneDX 1.6 document.
2. Equivalent supported URI/formatted-string CPEs generate identical PURLs, including packed editions and escaped punctuation.
3. A second enrichment leaves the output byte-for-byte unchanged.
4. A recursive directory invocation mirrors eligible files into a separate output tree and processes valid files despite failures in other files.
5. Failed parsing/validation and output collisions never modify source files or pre-existing destination files. No-op in-place processing does not rewrite a file.
6. Release build and automated tests pass on the installed .NET 9 SDK/runtime; published execution resolves schemas without the repository or network access.
7. Each of CycloneDX 1.2–1.6 supports enrichment, preservation and idempotence for its schema-defined component locations. Mixed-version directory batches retain each file's `specVersion` and BOM `version`, while rejecting unsupported versions and invalid required fields.
