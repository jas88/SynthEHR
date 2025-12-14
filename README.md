# SynthEHR (Previously BadMedicine)

[![Build](https://github.com/jas88/SynthEHR/actions/workflows/build.yml/badge.svg)](https://github.com/jas88/SynthEHR/actions/workflows/build.yml) [![codecov](https://codecov.io/gh/jas88/SynthEHR/graph/badge.svg)](https://codecov.io/gh/jas88/SynthEHR) [![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=jas88_SynthEHR&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=jas88_SynthEHR) [![NuGet Badge](https://buildstats.info/nuget/HIC.SynthEHR)](https://www.nuget.org/packages/HIC.SynthEHR/)

Library and CLI for randomly generating medical data like you might get out of an Electronic Health Records (EHR) system.  It is intended for generating data for demos and testing ETL / cohort generation/ data management tools.

SynthEHR differs from other random data generators e.g. Mockaroo, SQL Data Generator etc in that data generated is based on (simple) models generated from live EHR datasets collected for over 30 years in Tayside and Fife (UK).  This makes the data generated recognisable (codes used, frequency of codes etc) from a clinical perspective and representative of the problems (ontology mapping etc) that data analysts would encounter working with real medical data.

Datasets generated are not suitable for training AI algorithms etc (See [What is Modelled?](#what-is-modelled))

## Rename
As of v2.0.0 BadMedicine was renamed to SynthEHR. Previous versions of the software can be found at [nuget.org](https://www.nuget.org/packages/HIC.BadMedicine).

## Datasets

The following synthetic datasets can be produced.

| Dataset        | Description           |
| ------------- |:-------------:|
| Demography      | Address and patient details as might appear in the CHI register |
| Biochemistry      | Lab test codes as might appear in Sci Store lab system extracts |
| Prescribing      | Prescription data of prescribed drugs |
| Carotid Artery Scan      | Scan results for Carotid Artery |
| Hospital Admissions | ICD9 and ICD10 codes for admission to hospital |
| Maternity | Records of births etc |

## Usage

### CLI

SynthEHR is available as a [nuget package](https://www.nuget.org/packages/HIC.SynthEHR/) for linking as a library

The CLI can be run using `dotnet run`:

```bash
# Generate default amount of data (500 patients, 2000 records per dataset)
dotnet run --project SynthEHR/SynthEHR.csproj c:/temp/

# Specify number of patients and records per dataset
dotnet run --project SynthEHR/SynthEHR.csproj c:/temp/ 500 10000

# Generate only a single dataset
dotnet run --project SynthEHR/SynthEHR.csproj c:/temp 5000 200000 -l -d CarotidArteryScan

# Seed the generator for reproducible results (deterministic GUIDs included)
dotnet run --project SynthEHR/SynthEHR.csproj c:/temp 5000 200000 -l -d CarotidArteryScan -s 5000
```

Or you can build and run the executable directly:

```bash
# Build the application
dotnet publish SynthEHR/SynthEHR.csproj -c Release -o ./publish

# Run the executable (platform-dependent name)
./publish/SynthEHR c:/temp/  # Linux/macOS
./publish/SynthEHR.exe c:/temp/  # Windows
```

### Deterministic GUID Generation

When using the `-s` (seed) parameter, SynthEHR now generates deterministic GUIDs. This means that:

- With the same seed, all generated data (including GUIDs) will be identical across runs
- GUIDs are generated using the seeded random number generator with `stackalloc` for efficient memory usage
- This enables fully reproducible test data scenarios

## Building

Building requires .NET 8.0 SDK or later.

To build the solution:
```bash
dotnet build
```

To create a self-contained executable for a specific platform:
```bash
# Windows x64
dotnet publish SynthEHR/SynthEHR.csproj -c Release -r win-x64 --self-contained

# Linux x64
dotnet publish SynthEHR/SynthEHR.csproj -c Release -r linux-x64 --self-contained

# macOS x64
dotnet publish SynthEHR/SynthEHR.csproj -c Release -r osx-x64 --self-contained
```

To create a framework-dependent executable (smaller, requires .NET runtime installed):
```bash
dotnet publish SynthEHR/SynthEHR.csproj -c Release -o ./publish
```
## Direct to Database

You can generate data directly into a relational database (instead of onto disk).

To turn this mode on rename the file `SynthEHR.template.yaml` to `SynthEHR.yaml` and provide the connection strings to your database e.g.:

```yaml
Database:
  # Set to true to drop and recreate tables described in the Template
  DropTables: false
  # The connection string to your database
  ConnectionString: server=(localdb)\MSSQLLocalDB;Integrated Security=true;
  # Your DBMS provider ('MySql', 'PostgreSql','Oracle' or 'MicrosoftSQLServer')
  DatabaseType: MicrosoftSQLServer
  # Database to create/use on the server
  DatabaseName: SynthEHRTestData
```

## Library Usage

You can generate test data for your program yourself by referencing the [nuget package](https://www.nuget.org/packages/HIC.SynthEHR/):

```csharp
// Seed the random generator for reproducible results (including GUIDs)
var r = new Random(100);

// Create a new person
var person = new Person(r);

// Create test data for that person
var a = new HospitalAdmissionsRecord(person, person.DateOfBirth, r);

Assert.IsNotNull(a.Person.CHI);
Assert.IsNotNull(a.Person.DateOfBirth);
Assert.IsNotNull(a.Person.Address.Line1);
Assert.IsNotNull(a.Person.Address.Postcode);
Assert.IsNotNull(a.AdmissionDate);
Assert.IsNotNull(a.DischargeDate);
Assert.IsNotNull(a.Condition1);
```

Note: When using a seeded `Random` instance, all generated data including GUIDs in datasets like Appointments and Maternity will be deterministic, ensuring reproducible test scenarios.

## What is Modelled?

Data generated by SynthEHR is driven by Aggregate distributions of real health data collected in Tayside (UK).  This means that codes appear in data with the frequency that match real data.  For example in the Hospital Admissions data we can see that ICD9 codes (denoted by dash) cease being recorded in ~1997 in favour of ICD10 codes and we can see the most common admission conditions are sensible:

![alt text](./Images/MainConditionDistribution.png)

*ICD 9 and ICD 10 codes in Condition1 (the main condition) upon Hospital Admission*

## What is not Modelled?

No inter dataset / inter record level randomisation model exists.  For example the following would **not** be modelled:

- If a patient is on Drug A they are more likely to also be on Drug B
- Hospitalisations are more likely to be at the beginning/end of a patients life
- Drug A is likely to be given to patients discharged having been treated for condition Y
