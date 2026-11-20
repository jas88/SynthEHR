# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [3.1.0] - 2026-11-16

### Changed

- **BREAKING** (target frameworks): the library now targets `net10.0` and `net11.0`; `net8.0` and `net9.0` reached end of support on 10 November 2026 and are dropped. The CLI still targets `net10.0`
- Update the build-standards submodule, which sets these target frameworks
- CI: CodeQL runs as a separate, parallel job, and the NuGet package and CLI binaries are built from the tested Release output instead of being recompiled
- Symbol package settings moved from the CI command line into `SynthEHR.Core.csproj`: local Release builds now produce the same separate portable PDBs as the published package (`.snupkg`); the package contents are otherwise unchanged

## [3.0.2] - 2026-11-16

### Fixed

- Generated data classes (`SynthEHR.Core.Data.*Data`) now return exactly the text of every CSV cell. In 3.0.0 and 3.0.1:
  - String offsets into the UTF-8 data blob were counted in UTF-16 chars, so every value after the first non-ASCII character was read from the wrong position. `Prescribing.csv` contains `«`, which corrupted about half of all prescribing `Name`s and most `BNF_Description`s, and produced whitespace-only names
  - Numeric-looking columns were stored as numbers and formatted back with the current culture, so values were reformatted (`13.0` became `13`, `480.000000` became `480`) and, under cultures with a comma decimal separator, became unparseable or were mis-parsed by consumers (`13.5` read back as `135`)
  - Empty cells in numeric-looking columns came back as the literal `NULL`, changing generated Maternity marital status values
- The source generator formats numbers in generated code with the invariant culture, so building under locales such as `ar-SA` no longer produces uncompilable source, and column names no longer depend on the build machine's casing rules

### Changed

- Every column is stored as text (with the existing string interning), increasing `SynthEHR.Core.dll` from about 17 MB to 20 MB per target framework

### Added

- Tests that compare every generated row against an independent parse of each dataset CSV, in several cultures, and tests that run the source generator over synthetic CSVs with multi-byte UTF-8, quoting, numeric-looking text and randomised content

## [3.0.1] - 2026-11-16

Completes the 3.0 API clean-up. 3.0.0 had no consumers yet, so these source-breaking changes ship as a patch release.

### Breaking

- `IDataGenerator.GenerateTestDataRow` / `DataGenerator.GenerateTestDataRow` return `object?[]`, since rows legitimately contain null cells; the parameter is renamed from `p` to `person`
- Removed `DataGenerator.EmbeddedCsvToDataTable`, which always threw in 3.0.0
- Public fields are now properties: `BiochemistryRecord.*`, `PrescribingRecord.*`, `Person.DateOfBirth`, `Person.DateOfDeath` and `DataGeneratorFactory.GeneratorType.Type`
- The protected field `DataGenerator.r` is now a protected property; `DataGenerator` subclasses that pass it by `ref` or `out` must change
- `RowsGeneratedEventArgs` now derives from `EventArgs`; `IPersonCollection` and `PersonCollection` implement `IReadOnlyCollection<Person>` (enumerating `People`)
- Nullable annotations corrected to match actual behaviour: `BiochemistryRecord.Result`, `DemographyAddress.Line4`, the optional diagnosis/operation properties on `HospitalAdmissionsRecord`, `Descriptions.Get*` and the `RowsGenerated` event are nullable

### Fixed

- `DataGeneratorFactory` is annotated for trimming, so generator constructors are preserved in trimmed and Native AOT applications
- `PersonCollection.People` is initialised to an empty array, so using it before `GeneratePeople` gives a clear `ArgumentException` rather than a `NullReferenceException`
- The CLI reports a clear error when `ConnectionString` or `DatabaseName` is missing from the configuration file
- The CLI no longer uses the obsolete FAnsiSql `ImplementationManager.Load<T>()`

### Changed

- The build is now warning-free and treats warnings as errors
- Version is set once in `Directory.Build.props`

## [3.0.0] - 2026-11-15

First release from the jas88/SynthEHR fork.

### Breaking

- NuGet package renamed from `HIC.SynthEHR` to `SynthEHR.Core`
- Library now multi-targets net8.0, net9.0 and net10.0 (previously net8.0 only); CLI targets net10.0
- Seeded runs now produce deterministic GUIDs, so output differs from 2.x for the same seed
- CLI database support switched from `HIC.FAnsiSql` 3.2.x to `FAnsiSql.Legacy` 3.7.0
- CsvHelper is no longer a dependency, so it no longer flows transitively; projects that used CsvHelper via `HIC.SynthEHR` must now reference it directly
- Nullable reference types are enabled across the public API; consumers and `DataGenerator` subclasses with nullable enabled will see new warnings (errors under `TreatWarningsAsErrors`)
- `DataGenerator.EmbeddedCsvToDataTable` now throws `NotSupportedException`; dataset lookups come from compile-time generated data classes instead
- `DataGenerator.GenerateTestDataFile` writes CSV with a built-in writer rather than CsvHelper

### Added

- Deterministic GUID generation in Appointment and Maternity datasets when using a seeded random number generator
- Compile-time source generator turns the dataset CSVs into generated data classes, replacing runtime CSV parsing
- Standardised build configuration through the build-standards submodule

### Changed

- The library has no runtime package dependencies
- Dataset CSVs are no longer also embedded as unused resources, reducing SynthEHR.Core.dll from about 25 MB to 17 MB per target framework
- Performance improvements: Span-based CHI/ANOCHI generation, binary-search weighted lookups, SearchValues CSV escaping, string interning
- Updated README with modern .NET CLI usage instructions

## [2.0.1] - 2024-08-15

- Remove reliance on Equ
- Embed trivial code from MathNet.Numerics rather than using as dependency
- Bump CsvHelper from 32.0.3 to 33.0.1

## [2.0.0] - 2024-05-16

- Rename package to SynthEHR

## [1.2.2] - 2024-05-16

-Add warning about naming deprecation, see [README](./README.md#Deprecation)

## [1.2.1] - 2024-03-18

- Bump HIC.FAnsiSql from 3.2.0 to 3.2.2

## [1.2.0] - 2024-03-06

- Now targets .Net 8.0
- Some bugfixes change random data generation, cross-version consistency not preserved
- SynthEHR itself now AOT/trim clean, but dependencies are not
- Improve BucketList performance
- Add Equ 2.3.0
- Bump HIC.FAnsiSql from 3.0.1 to 3.2.0
- Bump CvsHelper from 30.0.1 to 31.0.2
- Bump YamlDotNet from 12.0.2 to 15.1.2

## [1.1.2] - 2022-11-22

### Dependencies

- Bump CvsHelper from 30.0.0 to 30.0.1

## [1.1.1] - 2022-10-31

### Dependencies

- Bump CvsHelper from 28.0.0 to 30.0.0
- Bump HIC.FAnsiSql from 2.0.4 to 3.0.1
- Bump YamlDotNet from 11.2.1 to 12.0.2

## [1.1.0] - 2022-07-11

### Added

- Added support for creating to database
- Added the Wide and UltraWide datasets
- Added Microsoft.SourceLink.GitHub 1.1.1 (build time only) for easier debugging

### Dependencies

- Bump CsvHelper from 24.0.1 to 28.0.0
- Bump MathNet.Numerics from 4.15.0 to 5.0.0
- Build nupkg using csproj metadata instead of separate nuspec

### Fixed

- Fixed formatting and missing headers in lookup z\_chiStatus.csv
- Fixed `--help` not rendering correctly for some command line arguments

## [1.0.0] - 2021-02-16

### Changed

- Bump MathNet.Numerics from 4.13.0 to 4.14.0
- Bump CsvHelper from 19.0.0 to 24.0.1

## [0.1.6] - 2020-05-20

### Changed

- Updated CsvHelper to 15.0.5
- Updated MathNet.Numerics to 4.9.1

## [0.1.5] - 2019-07-12

### Fixed

- Fixed seed randomisation bug (caused by static initialization) when generating multiple copies of a dataset (e.g. generating Biochemistry twice within the same process)

## [0.1.4] - 2019-07-02

### Changed

- CI changes only (no changes to codebase)

## [0.1.3] - 2019-07-02

### Changed

- Patient birth dates now go from 1914 (Person.MinimumYearOfBirth) allowing for patients aged up to 100 years

[Unreleased]: https://github.com/jas88/SynthEHR/compare/v3.1.0...main
[3.1.0]: https://github.com/jas88/SynthEHR/compare/v3.0.2...v3.1.0
[3.0.2]: https://github.com/jas88/SynthEHR/compare/v3.0.1...v3.0.2
[3.0.1]: https://github.com/jas88/SynthEHR/compare/v3.0.0...v3.0.1
[3.0.0]: https://github.com/jas88/SynthEHR/compare/v2.0.1...v3.0.0
[2.0.1]: https://github.com/HicServices/SynthEHR/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/HicServices/SynthEHR/compare/v1.2.2...v2.0.0
[1.2.2]: https://github.com/HicServices/SynthEHR/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/HicServices/SynthEHR/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/HicServices/SynthEHR/compare/v1.1.2...v1.2.0
[1.1.2]: https://github.com/HicServices/SynthEHR/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/HicServices/SynthEHR/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/HicServices/SynthEHR/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/HicServices/SynthEHR/compare/v0.1.6...v1.0.0
[0.1.6]: https://github.com/HicServices/SynthEHR/compare/v0.1.5...v0.1.6
[0.1.5]: https://github.com/HicServices/SynthEHR/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/HicServices/SynthEHR/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/HicServices/SynthEHR/compare/0.0.1.2...v0.1.3
