# PurlMaster

A .NET 9 console application that adds generic Package URLs to CycloneDX 1.2–1.6 JSON components with a CPE identifier and no `purl` property. Existing PURLs, the document's CycloneDX version and other values are preserved. Both CPE 2.3 formatted strings and the backward-compatible `cpe:/` URI binding are supported.

## Disclaimer
This application was created with help of AI, namely GPT-6.1 Sol with reasoning effort set to High.

**Use at your own risk.**

## Build and test

Install a .NET 9 SDK; `global.json` selects .NET 9. Restore requires access to NuGet or an existing package cache. Lock files record direct and transitive dependency versions.

```sh
dotnet restore PurlMaster.sln --locked-mode
dotnet build PurlMaster.sln -c Release --no-restore
dotnet test PurlMaster.sln -c Release --no-build
```

The regular build output includes the executable, application dependencies, configuration files and these schema files in a `Schemas/` subdirectory:

- `bom-1.2.schema.json`, `bom-1.3.schema.json`, `bom-1.4.schema.json`, `bom-1.5.schema.json`, and `bom-1.6.schema.json`.
- `spdx.schema.json` (referenced license identifiers).
- `jsf-0.82.schema.json` (referenced signature definitions).

Output is under `src/PurlMaster/bin/<configuration>/net9.0/`. Schemas remain embedded as well; validation uses the embedded copies, so editing output schema files does not change validation behavior. Copy the entire output directory to run elsewhere with the .NET 9 runtime installed:

```sh
dotnet ./src/PurlMaster/bin/Release/net9.0/purl-master.dll --help
```

## Run

Convert a single file to `docs/test-bom.1.6.purl.json`:

```sh
dotnet run --project src/PurlMaster -c Release -- docs/test-bom.1.6.json
```

Specify an output file:

```sh
dotnet run --project src/PurlMaster -c Release -- docs/test-bom.1.6.json --output ./output/test-bom.json
```

Process the `.json` files directly inside a directory, writing to the sibling `./sboms.purl` directory:

```sh
dotnet run --project src/PurlMaster -c Release -- ./sboms
```

Process an entire directory tree and preserve relative paths in the output:

```sh
dotnet run --project src/PurlMaster -c Release -- ./sboms --recursive --output ./enriched-sboms
```

Explicitly modify input files:

```sh
dotnet run --project src/PurlMaster -c Release -- ./sboms --recursive --in-place
```

Add `--overwrite` to replace existing separate outputs. It cannot be combined with `--in-place`. Output directories must be outside the input directory tree. Symbolic links/reparse points in input directory trees are skipped. Quote paths containing spaces. Use `--help` for all options.

Input and changed output are validated against the supplied schema selected by the exact `specVersion` value: `1.2`, `1.3`, `1.4`, `1.5`, or `1.6`. Files of different supported versions can be processed in the same directory batch. The input's `$schema` URL is preserved as data and does not select the validator. Schema validation and enrichment work offline after restore, including in published applications. Malformed JSON, duplicate properties, schema-invalid documents and unsupported versions produce errors without output. A failing file does not stop a directory batch.

For 1.2, validation uses the regular `bom-1.2.schema.json`; the additional strict variant is not used or bundled. Version-specific requirements apply: a root BOM `version` is required in 1.2–1.4, and component `version` is required in 1.2/1.3. Additional properties allowed by the supplied 1.2/1.3 schemas are preserved.

## CPE mapping

The mapping is deterministic application policy; it does not identify a package ecosystem:

```text
cpe:2.3:a:busybox:busybox:1.36.1:*:*:*:*:*:*:*
pkg:generic/busybox/busybox@1.36.1?cpe_part=a
```

CPE vendor becomes the namespace, product the name, and version the optional version. Additional concrete attributes become sorted `cpe_*` qualifiers, including `cpe_part`. CPE escapes are decoded before PURL encoding. CPE values are normalized to lowercase. ANY/NA vendor or version values are omitted; product must be concrete. The original CPE remains in the component.

Wildcard patterns, malformed/unsupported CPEs and vendors containing `/` are skipped with a warning identifying the file and component JSON pointer. Literal escaped `*` and `?` are supported and are encoded as data. URI escapes are limited to the printable punctuation defined in NIST IR 7695 and its `%01`/`%02` wildcard markers; broader legacy CPE URI escapes are outside the supported subset. Versions are derived from the CPE, even if the component's `version` differs.

Every component position defined for the document's version is covered: top-level components, metadata, nested components and pedigree throughout 1.2–1.6; tools, annotations and formulation from 1.5; declaration targets in 1.6. Extension properties in older documents are preserved without being interpreted as newer component positions. If a component already has `purl`, its value is preserved and its CPE is not parsed.

Writes use a temporary file and an atomic rename beside the destination. In-place replacement checks that the input still has the bytes read for processing and preserves Unix permission bits. A no-op keeps original bytes in separate output and leaves the original file untouched in place. Changed documents are serialized as indented UTF-8 JSON; BOM version, timestamps and references retain their values.

## Output and exit codes

The application prints a result per successful file and a final count of successful/failed files, added/existing PURLs, components without CPE, and skipped CPEs. Warnings and errors go to standard error.

| Exit code | Meaning |
| --- | --- |
| `0` | Processing succeeded, possibly with component skip warnings; also used for help. |
| `1` | A file/discovery failed, input is missing, or no JSON files were found. Other valid files may have been written. |
| `2` | Invalid command-line arguments or output configuration. |

## Publish

```sh
dotnet publish src/PurlMaster -c Release --no-restore -o ./artifacts/publish
dotnet ./artifacts/publish/purl-master.dll --help
dotnet ./artifacts/publish/purl-master.dll ./sboms --recursive --output ./enriched-sboms
```

Publish also copies the seven schema files into `Schemas/`. Copy the entire publish directory to run elsewhere with the .NET 9 runtime installed. Processing holds one SBOM at a time in memory; usage scales with the largest file.

## GitHub releases

The [release workflow](.github/workflows/release.yml) restores locked dependencies, builds the Release configuration and runs the tests before packaging. It uses the .NET SDK specified in `global.json`. Push a version tag after committing the workflow to create a GitHub release:

```sh
git tag v1.0.0
git push origin v1.0.0
```

Supported tags have the form `vMAJOR.MINOR.PATCH`, optionally with a prerelease suffix such as `v1.0.0-rc.1`. Prerelease tags produce GitHub prereleases. The tag version is also embedded in the published application. Release creation uses the [GitHub CLI](https://cli.github.com/manual/gh_release_create) and the workflow's built-in token; no additional secrets are required.

Each release contains a Windows x64 ZIP and Linux/macOS x64 and ARM64 tar archives, with SHA-256 checksums. Archives include the application dependencies, seven schemas, README and MIT license; an installed .NET 9 runtime is required. Linux archives target glibc-based distributions. Extract the entire archive and run `purl-master` (`purl-master.exe` on Windows), or use `dotnet purl-master.dll`.

The release also includes `purl-master-<version>.cdx.json`, a CycloneDX 1.6 JSON SBOM with a SHA-256 checksum. The workflow uses the pinned [CycloneDX .NET generator](https://github.com/CycloneDX/cyclonedx-dotnet/tree/v5.5.0) to describe the application project's direct and transitive NuGet dependencies, excluding development dependencies. The SBOM records the release version and passes schema validation before upload. It covers application dependencies; the installed .NET runtime is supplied separately by the user.

You can also run **Release** manually from the GitHub Actions tab to test and build downloadable artifacts, including the SBOM. Manual runs do not create a GitHub release. Rerunning a tag workflow updates the assets of an existing release.

See [Requirements.md](docs/Requirements.md) for the complete behavior and acceptance criteria, and [Roadmap.md](docs/Roadmap.md) for implementation status and Codex handoff notes.

## License

Project source code is licensed under the [MIT License](LICENSE). Bundled third-party schemas, specifications and dependencies retain their respective licenses and notices.
