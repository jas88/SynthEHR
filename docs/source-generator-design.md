# Source Generator Migration Design Document

## Executive Summary

This document outlines the comprehensive strategy for replacing runtime CSV parsing with compile-time C# source generators in the SynthEHR project. This migration will eliminate the embedded Aggregates.zip file, remove runtime CsvHelper dependency from SynthEHR.Core, and generate strongly-typed, efficient in-memory data structures.

## 1. Current Architecture Analysis

### 1.1 Current State

**Embedded Resources:**
- `Aggregates.zip` containing:
  - `Prescribing.csv` (4.5 MB, ~71k rows)
  - `Biochemistry.csv` (638 KB, ~9k rows)
  - `HospitalAdmissions.csv` (1.5 MB, ~24k rows)
  - `HospitalAdmissionsOperations.csv` (1.1 MB, ~17k rows)
- `Maternity.csv` (direct embedded resource)

**Key Components:**
1. `DataGenerator.EmbeddedCsvToDataTable()` - Runtime CSV parsing from embedded resources
2. `PrescribingRecord` - Uses weighted distribution based on frequency column
3. `BiochemistryRecord` - Uses BucketList with weighted sampling
4. `HospitalAdmissionsRecord` - Complex month-based distribution with operations mapping
5. `MaternityRecord` - Location and demographic code distributions

**Runtime Dependencies:**
- CsvHelper for parsing at runtime
- System.Data.DataTable for holding parsed data
- System.IO.Compression for zip extraction

### 1.2 Performance Bottlenecks

Current approach incurs:
- ZIP decompression overhead on first access
- CSV parsing overhead (CsvHelper processing)
- DataTable allocation and population
- Dictionary/BucketList construction from parsed data

## 2. Proposed Architecture

### 2.1 Source Generator Project Structure

```
SynthEHR.SourceGenerators/
├── SynthEHR.SourceGenerators.csproj
├── CsvDataGenerator.cs              # Main generator
├── Analyzers/
│   ├── CsvStructureAnalyzer.cs      # Analyzes CSV structure
│   └── DataDistributionAnalyzer.cs  # Analyzes data distributions
├── Models/
│   ├── CsvSchema.cs                 # Schema model
│   ├── ColumnDescriptor.cs          # Column metadata
│   └── DistributionMetadata.cs      # Distribution statistics
├── Generators/
│   ├── WeightedDistributionGenerator.cs   # For Prescribing
│   ├── BucketListGenerator.cs             # For Biochemistry
│   ├── MonthHashMapGenerator.cs           # For HospitalAdmissions
│   └── SimpleDistributionGenerator.cs     # For Maternity
└── Templates/
    ├── RecordClassTemplate.cs       # T4-like template for records
    └── DataAccessorTemplate.cs      # T4-like template for accessors
```

### 2.2 Project Configuration

**SynthEHR.SourceGenerators.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <IncludeBuildOutput>false</IncludeBuildOutput>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>

  <!-- Include CSV files as AdditionalFiles for the generator -->
  <ItemGroup>
    <AdditionalFiles Include="../SynthEHR.Core/Datasets/Maternity.csv" />
  </ItemGroup>

  <!-- Extract and include CSV files from Aggregates.zip -->
  <Target Name="ExtractAggregatesZip" BeforeTargets="BeforeBuild">
    <Unzip SourceFiles="../SynthEHR.Core/Datasets/Aggregates.zip"
           DestinationFolder="$(IntermediateOutputPath)ExtractedCsvs" />
    <ItemGroup>
      <AdditionalFiles Include="$(IntermediateOutputPath)ExtractedCsvs/*.csv" />
    </ItemGroup>
  </Target>
</Project>
```

**SynthEHR.Core.csproj modifications:**
```xml
<ItemGroup>
  <!-- Remove embedded resources -->
  <None Remove="Datasets\Aggregates.zip" />
  <None Remove="Datasets\Maternity.csv" />

  <!-- Add CSV files as AdditionalFiles for source generator -->
  <AdditionalFiles Include="Datasets\Maternity.csv" />
  <AdditionalFiles Include="Datasets\Prescribing.csv" Visible="false" />
  <AdditionalFiles Include="Datasets\Biochemistry.csv" Visible="false" />
  <AdditionalFiles Include="Datasets\HospitalAdmissions.csv" Visible="false" />
  <AdditionalFiles Include="Datasets\HospitalAdmissionsOperations.csv" Visible="false" />

  <!-- Reference the source generator -->
  <ProjectReference Include="..\SynthEHR.SourceGenerators\SynthEHR.SourceGenerators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />

  <!-- Remove CsvHelper from Core, move to CLI only -->
  <PackageReference Remove="CsvHelper" />
</ItemGroup>
```

## 3. Source Generator Design

### 3.1 Main Generator Implementation

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace SynthEHR.SourceGenerators;

[Generator]
public class CsvDataGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get all CSV files from AdditionalFiles
        var csvFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".csv"))
            .Collect();

        // Register source output
        context.RegisterSourceOutput(csvFiles, (spc, files) =>
        {
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file.Path);
                var content = file.GetText(spc.CancellationToken)?.ToString();

                if (string.IsNullOrEmpty(content)) continue;

                // Analyze CSV structure and generate appropriate code
                var analyzer = new CsvStructureAnalyzer(fileName, content);
                var schema = analyzer.Analyze();

                // Select appropriate generator based on dataset
                var generator = SelectGenerator(fileName, schema);
                var source = generator.Generate(schema);

                // Add generated source
                spc.AddSource($"{fileName}GeneratedData.g.cs",
                    SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static IDataGenerator SelectGenerator(string fileName, CsvSchema schema)
    {
        return fileName switch
        {
            "Prescribing" => new WeightedDistributionGenerator(),
            "Biochemistry" => new BucketListGenerator(),
            "HospitalAdmissions" => new MonthHashMapGenerator(),
            "HospitalAdmissionsOperations" => new OperationMappingGenerator(),
            "Maternity" => new SimpleDistributionGenerator(),
            _ => new SimpleDistributionGenerator()
        };
    }
}
```

### 3.2 CSV Structure Analyzer

```csharp
public class CsvStructureAnalyzer
{
    private readonly string _fileName;
    private readonly string _content;

    public CsvStructureAnalyzer(string fileName, string content)
    {
        _fileName = fileName;
        _content = content;
    }

    public CsvSchema Analyze()
    {
        var lines = _content.Split('\n');
        var headers = ParseCsvLine(lines[0]);

        var columns = new List<ColumnDescriptor>();
        var rowCount = lines.Length - 1;

        // Analyze each column
        for (int i = 0; i < headers.Length; i++)
        {
            var columnType = InferColumnType(lines, i);
            var statistics = CalculateStatistics(lines, i, columnType);

            columns.Add(new ColumnDescriptor
            {
                Name = headers[i],
                Index = i,
                Type = columnType,
                Statistics = statistics
            });
        }

        return new CsvSchema
        {
            FileName = _fileName,
            Columns = columns.ToImmutableArray(),
            RowCount = rowCount
        };
    }

    private ColumnType InferColumnType(string[] lines, int columnIndex)
    {
        // Sample first 100 rows to infer type
        var samples = lines.Skip(1).Take(100)
            .Select(line => ParseCsvLine(line)[columnIndex])
            .Where(val => !string.IsNullOrEmpty(val) && val != "NULL");

        if (samples.All(IsInt32)) return ColumnType.Int32;
        if (samples.All(IsDouble)) return ColumnType.Double;
        if (samples.All(IsDateTime)) return ColumnType.DateTime;
        return ColumnType.String;
    }

    private DistributionMetadata CalculateStatistics(string[] lines, int columnIndex, ColumnType type)
    {
        var values = lines.Skip(1)
            .Select(line => ParseCsvLine(line)[columnIndex])
            .Where(val => !string.IsNullOrEmpty(val) && val != "NULL")
            .ToList();

        return new DistributionMetadata
        {
            UniqueValueCount = values.Distinct().Count(),
            NullCount = lines.Length - 1 - values.Count,
            TotalCount = lines.Length - 1,
            // Additional statistics based on type...
        };
    }

    private string[] ParseCsvLine(string line)
    {
        // Implement RFC 4180 CSV parsing
        // Handle quoted fields, escaped quotes, etc.
        var result = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        result.Add(field.ToString());
        return result.ToArray();
    }
}
```

### 3.3 Data Model Classes

```csharp
public class CsvSchema
{
    public string FileName { get; init; }
    public ImmutableArray<ColumnDescriptor> Columns { get; init; }
    public int RowCount { get; init; }
}

public class ColumnDescriptor
{
    public string Name { get; init; }
    public int Index { get; init; }
    public ColumnType Type { get; init; }
    public DistributionMetadata Statistics { get; init; }
}

public enum ColumnType
{
    String,
    Int32,
    Double,
    DateTime,
    Boolean
}

public class DistributionMetadata
{
    public int UniqueValueCount { get; init; }
    public int NullCount { get; init; }
    public int TotalCount { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public double? Average { get; init; }
    public double? StandardDeviation { get; init; }
}
```

## 4. Generated Code Patterns

### 4.1 Prescribing - Weighted Distribution Pattern

**Generated Code:**
```csharp
// SynthEHR.Core/Generated/PrescribingGeneratedData.g.cs
namespace SynthEHR.Datasets.Generated;

internal static class PrescribingData
{
    // Compact storage using parallel arrays
    private static readonly string[] ResSeqNoData = new[]
    {
        "1234567", "2345678", "3456789", // ... ~71k entries
    };

    private static readonly string[] NameData = new[]
    {
        "Aspirin", "Paracetamol", "Ibuprofen", // ...
    };

    // ... other field arrays ...

    private static readonly int[] FrequencyData = new[]
    {
        5432, 4321, 3210, // ... frequencies
    };

    // Pre-computed cumulative frequency array for O(log n) lookup
    private static readonly int[] CumulativeFrequency;
    private static readonly int TotalWeight;

    static PrescribingData()
    {
        // Pre-compute cumulative frequencies
        CumulativeFrequency = new int[FrequencyData.Length];
        var cumulative = 0;

        for (int i = 0; i < FrequencyData.Length; i++)
        {
            cumulative += FrequencyData[i];
            CumulativeFrequency[i] = cumulative;
        }

        TotalWeight = cumulative;
    }

    // Fast weighted lookup using binary search
    public static int GetWeightedRandomIndex(Random random)
    {
        var target = random.Next(TotalWeight);
        return Array.BinarySearch(CumulativeFrequency, target);
    }

    public static PrescribingRecordData GetRecord(int index)
    {
        return new PrescribingRecordData
        {
            ResSeqNo = ResSeqNoData[index],
            Name = NameData[index],
            // ... other fields
        };
    }
}

// Immutable struct for zero-allocation access
public readonly struct PrescribingRecordData
{
    public string ResSeqNo { get; init; }
    public string Name { get; init; }
    public string FormulationCode { get; init; }
    // ... other fields
}
```

### 4.2 Biochemistry - BucketList Pattern

**Generated Code:**
```csharp
// SynthEHR.Core/Generated/BiochemistryGeneratedData.g.cs
namespace SynthEHR.Datasets.Generated;

internal static class BiochemistryData
{
    // Sorted by RecordCount descending for efficient BucketList
    private static readonly BiochemistryRecordData[] Records = new[]
    {
        new BiochemistryRecordData
        {
            LocalClinicalCodeValue = "GLUC",
            SampleName = "Blood",
            RecordCount = 45678,
            QVAverage = 5.2,
            QVStandardDev = 1.3,
            // ... other fields
        },
        // ... ~9k records
    };

    private static readonly int TotalRecordCount;
    private static readonly int[] CumulativeCounts;

    static BiochemistryData()
    {
        CumulativeCounts = new int[Records.Length];
        var cumulative = 0;

        for (int i = 0; i < Records.Length; i++)
        {
            cumulative += Records[i].RecordCount;
            CumulativeCounts[i] = cumulative;
        }

        TotalRecordCount = cumulative;
    }

    public static BiochemistryRecordData GetWeightedRandom(Random random)
    {
        var target = random.Next(TotalRecordCount);
        var index = Array.BinarySearch(CumulativeCounts, target);
        if (index < 0) index = ~index;
        return Records[index];
    }
}

public readonly struct BiochemistryRecordData
{
    public string LocalClinicalCodeValue { get; init; }
    public string ReadCodeValue { get; init; }
    public string SampleName { get; init; }
    public string HbExtract { get; init; }
    public int RecordCount { get; init; }
    public double? QVAverage { get; init; }
    public double? QVStandardDev { get; init; }
    public string ArithmeticComparator { get; init; }
    public string Interpretation { get; init; }
    public string QuantityUnit { get; init; }
    public double? RangeHighValue { get; init; }
    public double? RangeLowValue { get; init; }
}
```

### 4.3 HospitalAdmissions - Month-Based Distribution

**Generated Code:**
```csharp
// SynthEHR.Core/Generated/HospitalAdmissionsGeneratedData.g.cs
namespace SynthEHR.Datasets.Generated;

internal static class HospitalAdmissionsData
{
    // ICD10 codes with appearance metadata
    private static readonly HospitalAdmissionCode[] Codes = new[]
    {
        new HospitalAdmissionCode
        {
            TestCode = "I21.0",
            ColumnAppearingIn = "MAIN_CONDITION",
            AverageMonthAppearing = 1200.5,
            StandardDeviationMonthAppearing = 24.3,
            CountAppearances = 8765
        },
        // ... ~24k codes
    };

    // Pre-computed month-to-indices mapping
    // Dictionary<ColumnName, Dictionary<MonthIndex, int[]>>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, int[]>> MonthHashMap;

    // Pre-computed weighted indices for fast lookup
    private static readonly int[] CumulativeWeights;
    private static readonly int TotalWeight;

    static HospitalAdmissionsData()
    {
        // Build month hash map at startup
        var mainCondition = new Dictionary<int, List<int>>();
        var otherCondition1 = new Dictionary<int, List<int>>();
        var otherCondition2 = new Dictionary<int, List<int>>();
        var otherCondition3 = new Dictionary<int, List<int>>();

        for (int month = 996; month <= 1416; month++) // 1983-2018
        {
            mainCondition[month] = new List<int>();
            otherCondition1[month] = new List<int>();
            otherCondition2[month] = new List<int>();
            otherCondition3[month] = new List<int>();
        }

        for (int i = 0; i < Codes.Length; i++)
        {
            var code = Codes[i];
            var monthFrom = (int)(code.AverageMonthAppearing - 2 * code.StandardDeviationMonthAppearing);
            var monthTo = (int)(code.AverageMonthAppearing + 2 * code.StandardDeviationMonthAppearing);

            monthFrom = Math.Max(monthFrom, 996);
            monthTo = Math.Min(monthTo, 1416);

            var targetDict = code.ColumnAppearingIn switch
            {
                "MAIN_CONDITION" => mainCondition,
                "OTHER_CONDITION_1" => otherCondition1,
                "OTHER_CONDITION_2" => otherCondition2,
                "OTHER_CONDITION_3" => otherCondition3,
                _ => null
            };

            if (targetDict != null)
            {
                for (int month = monthFrom; month <= monthTo; month++)
                {
                    targetDict[month].Add(i);
                }
            }
        }

        // Convert to arrays for better performance
        MonthHashMap = new Dictionary<string, IReadOnlyDictionary<int, int[]>>
        {
            ["MAIN_CONDITION"] = ConvertToArrayDict(mainCondition),
            ["OTHER_CONDITION_1"] = ConvertToArrayDict(otherCondition1),
            ["OTHER_CONDITION_2"] = ConvertToArrayDict(otherCondition2),
            ["OTHER_CONDITION_3"] = ConvertToArrayDict(otherCondition3)
        };

        // Pre-compute cumulative weights
        CumulativeWeights = new int[Codes.Length];
        var cumulative = 0;
        for (int i = 0; i < Codes.Length; i++)
        {
            cumulative += Codes[i].CountAppearances;
            CumulativeWeights[i] = cumulative;
        }
        TotalWeight = cumulative;
    }

    private static IReadOnlyDictionary<int, int[]> ConvertToArrayDict(Dictionary<int, List<int>> dict)
    {
        return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
    }

    public static string GetRandomICD10Code(string field, int monthIndex, Random random)
    {
        if (!MonthHashMap.TryGetValue(field, out var monthDict))
            throw new ArgumentException($"Invalid field: {field}");

        if (!monthDict.TryGetValue(monthIndex, out var indices))
            throw new ArgumentException($"Invalid month index: {monthIndex}");

        if (indices.Length == 0)
            return GetRandomCodeFromAll(random);

        // Get weighted random from available indices
        var totalWeight = indices.Sum(i => Codes[i].CountAppearances);
        var target = random.Next(totalWeight);
        var cumulative = 0;

        foreach (var index in indices)
        {
            cumulative += Codes[index].CountAppearances;
            if (cumulative > target)
                return Codes[index].TestCode;
        }

        return Codes[indices[^1]].TestCode;
    }

    private static string GetRandomCodeFromAll(Random random)
    {
        var target = random.Next(TotalWeight);
        var index = Array.BinarySearch(CumulativeWeights, target);
        if (index < 0) index = ~index;
        return Codes[index].TestCode;
    }
}

public readonly struct HospitalAdmissionCode
{
    public string TestCode { get; init; }
    public string ColumnAppearingIn { get; init; }
    public double AverageMonthAppearing { get; init; }
    public double StandardDeviationMonthAppearing { get; init; }
    public int CountAppearances { get; init; }
}
```

### 4.4 Operations Mapping Generator

```csharp
// SynthEHR.Core/Generated/HospitalAdmissionsOperationsGeneratedData.g.cs
namespace SynthEHR.Datasets.Generated;

internal static class HospitalAdmissionsOperationsData
{
    // Dictionary mapping condition codes to operation sets
    private static readonly IReadOnlyDictionary<string, OperationSet[]> ConditionToOperations;
    private static readonly IReadOnlyDictionary<string, int[]> ConditionCumulativeWeights;

    static HospitalAdmissionsOperationsData()
    {
        var tempDict = new Dictionary<string, List<OperationSet>>();

        // Pre-grouped by condition at compile time
        AddOperationSet(tempDict, "I21.0", new OperationSet
        {
            MainOperation = "K45.1",
            MainOperationB = "K46.2",
            OtherOperation1 = "K47.3",
            // ... other operations
            CountOfRecords = 234
        });
        // ... thousands more entries, pre-grouped

        // Convert to efficient lookup structure
        ConditionToOperations = tempDict.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToArray()
        );

        // Pre-compute cumulative weights for each condition
        var weightsDict = new Dictionary<string, int[]>();
        foreach (var (condition, operations) in ConditionToOperations)
        {
            var weights = new int[operations.Length];
            var cumulative = 0;
            for (int i = 0; i < operations.Length; i++)
            {
                cumulative += operations[i].CountOfRecords;
                weights[i] = cumulative;
            }
            weightsDict[condition] = weights;
        }
        ConditionCumulativeWeights = weightsDict;
    }

    public static bool TryGetOperations(string mainCondition, Random random, out OperationSet operations)
    {
        if (!ConditionToOperations.TryGetValue(mainCondition, out var operationSets))
        {
            operations = default;
            return false;
        }

        var weights = ConditionCumulativeWeights[mainCondition];
        var target = random.Next(weights[^1]);
        var index = Array.BinarySearch(weights, target);
        if (index < 0) index = ~index;

        operations = operationSets[index];
        return true;
    }
}

public readonly struct OperationSet
{
    public string MainOperation { get; init; }
    public string MainOperationB { get; init; }
    public string OtherOperation1 { get; init; }
    public string OtherOperation1B { get; init; }
    public string OtherOperation2 { get; init; }
    public string OtherOperation2B { get; init; }
    public string OtherOperation3 { get; init; }
    public string OtherOperation3B { get; init; }
    public int CountOfRecords { get; init; }
}
```

## 5. Migration Strategy for DataGenerator.cs

### 5.1 Update Record Classes

**Before (PrescribingRecord.cs):**
```csharp
static PrescribingRecord()
{
    LookupTable = DataGenerator.EmbeddedCsvToDataTable(typeof(PrescribingRecord), "Prescribing.csv");
    // ... build WeightToRow dictionary
}

public PrescribingRecord(Random r)
{
    var row = GetRandomRowUsingWeight(r);
    ResSeqNo = row["res_seqno"].ToString();
    // ... populate from DataRow
}
```

**After (PrescribingRecord.cs):**
```csharp
using SynthEHR.Datasets.Generated;

// Remove static constructor - no longer needed

public PrescribingRecord(Random r)
{
    // Use generated data accessor
    var index = PrescribingData.GetWeightedRandomIndex(r);
    var data = PrescribingData.GetRecord(index);

    ResSeqNo = data.ResSeqNo;
    Name = data.Name;
    FormulationCode = data.FormulationCode;
    Strength = data.Strength;
    StrengthNumerical = data.StrengthNumerical;
    MeasureCode = data.MeasureCode;
    BnfCode = data.BnfCode;
    FormattedBnfCode = data.FormattedBnfCode;
    BnfDescription = data.BnfDescription;
    ApprovedName = data.ApprovedName;

    // Quantity calculation remains the same
    if (data.HasMinMaxQuantity)
        Quantity = ((int)(r.NextDouble() * (data.MaxQuantity - data.MinQuantity) + data.MinQuantity)).ToString();
    else
        Quantity = r.Next(0, 2) == 0 ? data.MinQuantityStr : data.MaxQuantityStr;
}
```

### 5.2 Update BiochemistryRecord

**After (BiochemistryRecord.cs):**
```csharp
using SynthEHR.Datasets.Generated;

// Remove static constructor and BucketList field

public BiochemistryRecord(Random r)
{
    // Use generated data accessor
    var data = BiochemistryData.GetWeightedRandom(r);

    LabNumber = GetRandomLabNumber(r);
    TestCode = data.LocalClinicalCodeValue;
    SampleType = data.SampleName;

    // Generate result from distribution
    Result = data.QVAverage.HasValue && data.QVStandardDev.HasValue
        ? new Normal(data.QVAverage.Value, data.QVStandardDev.Value, r).Sample().ToString(CultureInfo.CurrentCulture)
        : null;

    ArithmeticComparator = data.ArithmeticComparator;
    Interpretation = data.Interpretation;
    QuantityUnit = data.QuantityUnit;
    RangeHighValue = data.RangeHighValue?.ToString() ?? "NULL";
    RangeLowValue = data.RangeLowValue?.ToString() ?? "NULL";
    Healthboard = data.HbExtract;
    ReadCodeValue = data.ReadCodeValue;
}
```

### 5.3 Update HospitalAdmissionsRecord

**After (HospitalAdmissionsRecord.cs):**
```csharp
using SynthEHR.Datasets.Generated;

// Remove static constructors and hash map fields

public HospitalAdmissionsRecord(Person person, DateTime afterDateX, Random r)
{
    Person = person;
    if (person.DateOfBirth > afterDateX)
        afterDateX = person.DateOfBirth;

    AdmissionDate = DataGenerator.GetRandomDate(afterDateX.Max(MinimumDate), MaximumDate, r);
    DischargeDate = AdmissionDate.AddHours(r.Next(240));

    // Calculate month index
    var monthIndex = (AdmissionDate.Year - 1900) * 12 + AdmissionDate.Month;

    // Use generated data accessor
    MainCondition = HospitalAdmissionsData.GetRandomICD10Code("MAIN_CONDITION", monthIndex, r);

    if (r.Next(2) == 0)
    {
        OtherCondition1 = HospitalAdmissionsData.GetRandomICD10Code("OTHER_CONDITION_1", monthIndex, r);

        if (r.Next(2) == 0)
        {
            OtherCondition2 = HospitalAdmissionsData.GetRandomICD10Code("OTHER_CONDITION_2", monthIndex, r);

            if (r.Next(2) == 0)
                OtherCondition3 = HospitalAdmissionsData.GetRandomICD10Code("OTHER_CONDITION_3", monthIndex, r);

            if (r.Next(10) == 0)
                OtherCondition3 = "Nul";
        }
    }

    // Get operations using generated data
    if (HospitalAdmissionsOperationsData.TryGetOperations(MainCondition, r, out var operations))
    {
        MainOperation = operations.MainOperation;
        MainOperationB = operations.MainOperationB;
        OtherOperation1 = operations.OtherOperation1;
        OtherOperation1B = operations.OtherOperation1B;
        OtherOperation2 = operations.OtherOperation2;
        OtherOperation2B = operations.OtherOperation2B;
        OtherOperation3 = operations.OtherOperation3;
        OtherOperation3B = operations.OtherOperation3B;
    }
}
```

### 5.4 Remove EmbeddedCsvToDataTable Method

The `DataGenerator.EmbeddedCsvToDataTable()` and `GetResourceStream()` methods can be completely removed from DataGenerator.cs after migration.

## 6. Backward Compatibility Strategy

### 6.1 Compatibility Shim (Optional)

If external code depends on `EmbeddedCsvToDataTable`, provide a compatibility layer:

```csharp
// Mark as obsolete to encourage migration
[Obsolete("Use generated data accessors instead. This method will be removed in a future version.")]
public static DataTable EmbeddedCsvToDataTable(Type requestingType, string resourceFileName, DataTable dt = null)
{
    // Delegate to generated data
    return resourceFileName switch
    {
        "Prescribing.csv" => PrescribingData.ToDataTable(dt),
        "Biochemistry.csv" => BiochemistryData.ToDataTable(dt),
        "HospitalAdmissions.csv" => HospitalAdmissionsData.ToDataTable(dt),
        "HospitalAdmissionsOperations.csv" => HospitalAdmissionsOperationsData.ToDataTable(dt),
        "Maternity.csv" => MaternityData.ToDataTable(dt),
        _ => throw new ArgumentException($"Unknown resource: {resourceFileName}")
    };
}
```

### 6.2 Generated ToDataTable Methods

Each generated data class can provide a `ToDataTable()` method for compatibility:

```csharp
// In generated code
internal static class PrescribingData
{
    // ... existing code ...

    [Obsolete("DataTable support is for backward compatibility only")]
    public static DataTable ToDataTable(DataTable dt = null)
    {
        var result = dt ?? new DataTable();

        // Add columns if not present
        if (!result.Columns.Contains("res_seqno"))
            result.Columns.Add("res_seqno", typeof(string));
        // ... add all columns ...

        // Populate rows
        for (int i = 0; i < ResSeqNoData.Length; i++)
        {
            var row = result.NewRow();
            row["res_seqno"] = ResSeqNoData[i];
            row["name"] = NameData[i];
            // ... populate all columns ...
            result.Rows.Add(row);
        }

        return result;
    }
}
```

## 7. Testing Strategy

### 7.1 Unit Tests for Source Generator

```csharp
// SynthEHR.SourceGenerators.Tests/CsvDataGeneratorTests.cs
[TestClass]
public class CsvDataGeneratorTests
{
    [TestMethod]
    public void Generator_ParsesCsvCorrectly()
    {
        // Arrange
        var source = @"
res_seqno,name,frequency
1234567,Aspirin,5432
2345678,Paracetamol,4321
";
        var generator = new CsvDataGenerator();

        // Act
        var result = RunGenerator(generator, source);

        // Assert
        Assert.IsTrue(result.Contains("ResSeqNoData"));
        Assert.IsTrue(result.Contains("GetWeightedRandomIndex"));
    }

    [TestMethod]
    public void WeightedDistribution_ProducesCorrectDistribution()
    {
        // Test generated weighted distribution matches expected probabilities
        var random = new Random(42);
        var counts = new Dictionary<int, int>();

        for (int i = 0; i < 100000; i++)
        {
            var index = PrescribingData.GetWeightedRandomIndex(random);
            counts[index] = counts.GetValueOrDefault(index, 0) + 1;
        }

        // Verify distribution matches weights (within statistical tolerance)
        // ...
    }
}
```

### 7.2 Integration Tests

```csharp
// SynthEHRTests/DataGeneratorIntegrationTests.cs
[TestClass]
public class DataGeneratorIntegrationTests
{
    [TestMethod]
    public void PrescribingRecord_GeneratedData_MatchesOriginal()
    {
        // Compare generated data behavior with original
        var random = new Random(42);
        var record = new PrescribingRecord(random);

        Assert.IsNotNull(record.ResSeqNo);
        Assert.IsNotNull(record.Name);
        Assert.IsNotNull(record.BnfCode);
        // ... verify all fields populated correctly
    }

    [TestMethod]
    public void BiochemistryRecord_Distribution_IsConsistent()
    {
        var random = new Random(42);
        var testCodes = new Dictionary<string, int>();

        for (int i = 0; i < 10000; i++)
        {
            var record = new BiochemistryRecord(random);
            testCodes[record.TestCode] = testCodes.GetValueOrDefault(record.TestCode, 0) + 1;
        }

        // Verify common codes appear frequently
        Assert.IsTrue(testCodes.Count > 100); // Many different test codes
        var topCode = testCodes.OrderByDescending(kvp => kvp.Value).First();
        Assert.IsTrue(topCode.Value > 100); // Most common code appears frequently
    }

    [TestMethod]
    public void HospitalAdmissions_MonthDistribution_WorksCorrectly()
    {
        var person = new Person(new Random(42));
        var random = new Random(42);

        var record = new HospitalAdmissionsRecord(person, new DateTime(2010, 1, 1), random);

        Assert.IsNotNull(record.MainCondition);
        Assert.IsTrue(record.AdmissionDate >= new DateTime(2010, 1, 1));
        Assert.IsTrue(record.DischargeDate > record.AdmissionDate);
    }
}
```

### 7.3 Performance Benchmarks

```csharp
// SynthEHR.Benchmarks/DataGeneratorBenchmarks.cs
[MemoryDiagnoser]
public class DataGeneratorBenchmarks
{
    private Random _random;

    [GlobalSetup]
    public void Setup()
    {
        _random = new Random(42);
    }

    [Benchmark(Baseline = true)]
    public PrescribingRecord PrescribingRecord_Original()
    {
        // Benchmark original implementation (with CSV parsing)
        return new PrescribingRecord(_random);
    }

    [Benchmark]
    public PrescribingRecord PrescribingRecord_Generated()
    {
        // Benchmark with generated data
        return new PrescribingRecord(_random);
    }

    [Benchmark]
    public BiochemistryRecord BiochemistryRecord_Generated()
    {
        return new BiochemistryRecord(_random);
    }

    [Benchmark]
    public HospitalAdmissionsRecord HospitalAdmissions_Generated()
    {
        var person = new Person(new Random(42));
        return new HospitalAdmissionsRecord(person, DateTime.Now.AddYears(-5), _random);
    }
}
```

### 7.4 Validation Tests

```csharp
[TestMethod]
public void GeneratedData_MatchesOriginalData()
{
    // Extract CSV files
    // Parse with both old and new methods
    // Verify data is identical

    var originalData = LoadOriginalData("Prescribing.csv");
    var generatedData = LoadGeneratedData();

    Assert.AreEqual(originalData.RowCount, generatedData.Length);

    for (int i = 0; i < originalData.RowCount; i++)
    {
        Assert.AreEqual(originalData.Rows[i]["res_seqno"], generatedData[i].ResSeqNo);
        // ... verify all fields
    }
}
```

## 8. Performance Considerations

### 8.1 Memory Optimization

**Parallel Arrays vs Objects:**
- Use parallel arrays instead of array of objects for better cache locality
- Reduces memory overhead (no object headers per record)
- Improves iteration performance

**String Interning:**
```csharp
// For frequently repeated strings
private static readonly string[] InternedStrings;

static PrescribingData()
{
    // Intern repeated values like formulation codes, measure codes
    InternedStrings = FormulationCodeData.Distinct().Select(string.Intern).ToArray();
}
```

**ReadOnlySpan for Temporary Data:**
```csharp
public static ReadOnlySpan<BiochemistryRecordData> GetRecordsSpan()
{
    return Records.AsSpan();
}
```

### 8.2 Startup Performance

**Lazy Initialization (if needed):**
```csharp
private static class LazyHolder
{
    public static readonly int[] CumulativeFrequency = BuildCumulativeFrequency();

    private static int[] BuildCumulativeFrequency()
    {
        // Expensive initialization only happens if accessed
        // ...
    }
}
```

**Parallel Initialization:**
```csharp
static HospitalAdmissionsData()
{
    // For large datasets, use parallel processing
    Parallel.For(0, Codes.Length, i =>
    {
        // Pre-process code[i]
    });
}
```

### 8.3 Compilation Time

**Incremental Code Generation:**
- Only regenerate if CSV files change
- Use content-based hashing to detect changes
- Cache parsed CSV structure

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    var csvFiles = context.AdditionalTextsProvider
        .Where(static file => file.Path.EndsWith(".csv"))
        .Select(static (file, ct) =>
        {
            var content = file.GetText(ct)?.ToString() ?? "";
            var hash = ComputeHash(content);
            return (file.Path, Content: content, Hash: hash);
        })
        .Collect();

    context.RegisterSourceOutput(csvFiles, (spc, files) =>
    {
        // Only regenerate if hash changed
        // ...
    });
}
```

### 8.4 Binary Search Optimization

For large datasets, use optimized binary search:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static int BinarySearchWeighted(ReadOnlySpan<int> weights, int target)
{
    int left = 0;
    int right = weights.Length - 1;

    while (left <= right)
    {
        int mid = left + ((right - left) >> 1);
        int midValue = weights[mid];

        if (midValue <= target)
            left = mid + 1;
        else
            right = mid - 1;
    }

    return left;
}
```

## 9. Implementation Roadmap

### Phase 1: Foundation (Week 1-2)
1. Create SynthEHR.SourceGenerators project
2. Implement CsvDataGenerator skeleton
3. Implement CsvStructureAnalyzer
4. Create data model classes (CsvSchema, ColumnDescriptor, etc.)
5. Write unit tests for CSV parsing

### Phase 2: Simple Generators (Week 3)
1. Implement SimpleDistributionGenerator (for Maternity)
2. Test end-to-end with Maternity.csv
3. Update MaternityRecord to use generated data
4. Verify functionality with existing tests

### Phase 3: Weighted Distribution (Week 4)
1. Implement WeightedDistributionGenerator (for Prescribing)
2. Generate code with parallel arrays and cumulative frequencies
3. Update PrescribingRecord
4. Performance benchmarking

### Phase 4: BucketList Pattern (Week 5)
1. Implement BucketListGenerator (for Biochemistry)
2. Update BiochemistryRecord
3. Test weighted sampling correctness

### Phase 5: Complex Patterns (Week 6-7)
1. Implement MonthHashMapGenerator (for HospitalAdmissions)
2. Implement OperationMappingGenerator (for HospitalAdmissionsOperations)
3. Update HospitalAdmissionsRecord
4. Comprehensive testing

### Phase 6: Cleanup and Optimization (Week 8)
1. Remove CsvHelper from SynthEHR.Core
2. Remove Aggregates.zip
3. Remove EmbeddedCsvToDataTable method
4. Extract and add individual CSV files as AdditionalFiles
5. Final performance optimization
6. Documentation updates

### Phase 7: Testing and Validation (Week 9)
1. Run full test suite
2. Performance benchmarking (before/after comparison)
3. Memory profiling
4. Integration testing with CLI

### Phase 8: Release (Week 10)
1. Code review
2. Update CHANGELOG
3. Create migration guide
4. Release notes
5. NuGet package update

## 10. Risk Mitigation

### 10.1 Data Accuracy Risks

**Risk:** Generated data might not exactly match original CSV data
**Mitigation:**
- Comprehensive validation tests comparing old vs new
- Hash-based verification of data content
- Statistical tests for distribution correctness

### 10.2 Performance Risks

**Risk:** Generated code might be slower than runtime parsing
**Mitigation:**
- Detailed benchmarking before finalizing design
- Multiple pattern implementations to choose optimal one
- Profile-guided optimization

### 10.3 Compilation Time Risks

**Risk:** Source generator might significantly increase build time
**Mitigation:**
- Incremental generation with caching
- Parallel processing where possible
- Only regenerate on CSV file changes
- Benchmark build times regularly

### 10.4 Maintenance Risks

**Risk:** CSV updates require manual intervention
**Mitigation:**
- Automated tests that verify generated code
- Clear documentation on update process
- Scripts to extract and update CSV files
- Version control for CSV files

## 11. Success Metrics

### 11.1 Performance Metrics

**Target Improvements:**
- 80% reduction in first-use latency (no ZIP decompression)
- 60% reduction in memory allocation per record
- 40% faster record generation after warmup
- 50% reduction in overall memory footprint

**Measurement:**
```csharp
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 10)]
public class PerformanceComparison
{
    [Benchmark(Baseline = true)]
    public void Original_10000Records() { /* ... */ }

    [Benchmark]
    public void Generated_10000Records() { /* ... */ }
}
```

### 11.2 Code Quality Metrics

- 100% test coverage of source generator code
- Zero regression in existing test suite
- All generated code passes AOT compatibility analysis
- No compiler warnings in generated code

### 11.3 Build Metrics

- Build time increase < 10%
- Incremental build time increase < 2%
- Generated code size < 2x CSV file size

## 12. Future Enhancements

### 12.1 Compression

For very large datasets, consider column compression:
```csharp
// Dictionary encoding for low-cardinality columns
private static readonly string[] HealthboardDictionary = new[] { "A", "B", "C", ... };
private static readonly byte[] HealthboardIndices = new byte[] { 0, 1, 2, ... };
```

### 12.2 SIMD Optimization

For numerical operations:
```csharp
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// Vectorized cumulative sum calculation
```

### 12.3 Native AOT Support

Ensure full compatibility with .NET Native AOT:
- No reflection in generated code
- All types known at compile time
- Trimmer-friendly design

### 12.4 Hot Reload Support

Support design-time CSV updates:
```csharp
#if DEBUG
[ModuleInitializer]
internal static void RegisterFileWatcher()
{
    // Watch CSV files for changes during development
    // Trigger rebuild when CSV changes
}
#endif
```

## 13. Documentation Updates

### 13.1 Developer Documentation

Create documentation covering:
- How source generators work
- How to update CSV data
- How to add new datasets
- Performance characteristics
- Troubleshooting guide

### 13.2 API Documentation

Update XML documentation:
```csharp
/// <summary>
/// Represents a prescribing record generated from pre-analyzed CSV data.
/// Data is loaded at compile-time for optimal runtime performance.
/// </summary>
/// <remarks>
/// This class uses data generated by the SynthEHR.SourceGenerators package.
/// The underlying data comes from Prescribing.csv and is embedded as compiled code.
/// </remarks>
public sealed class PrescribingRecord
{
    // ...
}
```

## 14. Appendix: Example Generated Code Size

**Estimated Generated Code Sizes:**
- Prescribing: ~8 MB C# source (71k records × ~120 bytes/record)
- Biochemistry: ~1.2 MB C# source (9k records)
- HospitalAdmissions: ~3 MB C# source (24k records + month hashmap)
- HospitalAdmissionsOperations: ~2 MB C# source (17k records)
- Maternity: ~200 KB C# source

**Total: ~15 MB of generated C# code**

After compilation:
- IL size: ~5-7 MB
- Runtime memory: ~8-10 MB (vs current ~15-20 MB with DataTable overhead)

## 15. Conclusion

This migration strategy provides a comprehensive path to eliminating runtime CSV parsing while maintaining backward compatibility and improving performance. The source generator approach leverages compile-time processing to create optimized, strongly-typed data structures that are faster, more memory-efficient, and better suited for modern .NET AOT scenarios.

Key benefits:
- **Performance**: 40-80% improvement in various metrics
- **Type Safety**: Strongly-typed access to all data
- **AOT Compatible**: Full Native AOT support
- **Maintainability**: Clear separation of data and logic
- **Testability**: Generator and data access separately testable

The phased implementation approach minimizes risk while allowing incremental validation at each step.
