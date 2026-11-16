using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SynthEHR.SourceGenerators;

namespace SynthEHRTests;

/// <summary>
/// Runs <see cref="CsvDataSourceGenerator"/> over synthetic CSV content, compiles the generated source and
/// checks every cell reads back exactly as written. Covers storage paths the bundled datasets may not
/// exercise: multi-byte UTF-8 (2, 3 and 4 bytes, including surrogate pairs) before, inside and after
/// interned and non-interned values, numeric-looking text, CSV quoting, and non-invariant cultures.
/// </summary>
internal sealed class CsvDataSourceGeneratorTests
{
    private static readonly string[] Unicode =
    [
        "é", "«", "µ", "ß",            // 2-byte UTF-8
        "€", "中文", "→", "\u2028",     // 3-byte UTF-8
        "😀", "𝄞", "👩‍⚕️",              // 4-byte UTF-8 (surrogate pairs), ZWJ sequence
    ];

    [Test]
    public void MultiByteText_RoundTrips_InEveryPosition()
    {
        var headers = new[] { "Id", "Before", "Text", "After" };
        var rows = new List<string[]>();
        var i = 0;
        foreach (var u in Unicode)
        {
            // start, middle, end of a value; alone; repeated; adjacent to plain ASCII columns
            foreach (var text in new[] { u, $"{u}abc", $"abc{u}", $"a{u}b{u}c", string.Concat(Enumerable.Repeat(u, 5)) })
                rows.Add([$"r{i++}", "plain", text, "tail"]);
        }

        AssertRoundTrip(headers, rows);
    }

    /// <summary>
    /// Values seen 10+ times are interned (stored as constants, with only a marker in the blob); multi-byte
    /// characters in both interned and blob-stored values must not shift later offsets.
    /// </summary>
    [Test]
    public void MultiByteText_RoundTrips_AcrossInternedAndBlobValues()
    {
        var headers = new[] { "Id", "Interned", "Unique" };
        var rows = Enumerable.Range(0, 200)
            .Select(n => new[] { $"r{n}", Unicode[n % 3] + "_shared", $"{Unicode[n % Unicode.Length]}-{n}-unique" })
            .ToList();

        AssertRoundTrip(headers, rows);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(42)]
    [TestCase(1234)]
    public void RandomText_RoundTrips(int seed)
    {
        var r = new Random(seed);
        const string ascii = "abcXYZ019 _-.,\"'\\/\t";
        var pool = ascii.Select(c => c.ToString()).Concat(Unicode).Concat(["\r\n", "\n", "NULL", "\0"]).ToArray();

        string RandomValue()
        {
            // Mix empty, short repeated (so some get interned) and long unique values.
            switch (r.Next(6))
            {
                case 0: return string.Empty;
                case 1: return pool[r.Next(4)];
                default:
                    var sb = new StringBuilder();
                    var len = r.Next(1, 40);
                    for (var k = 0; k < len; k++)
                        sb.Append(pool[r.Next(pool.Length)]);
                    return sb.ToString();
            }
        }

        var headers = new[] { "Id", "A", "B", "C", "D", "E" };
        var rows = Enumerable.Range(0, 500)
            .Select(n => new[] { $"r{n}" }.Concat(Enumerable.Range(0, 5).Select(_ => RandomValue())).ToArray())
            .ToList();

        AssertRoundTrip(headers, rows);
    }

    /// <summary>
    /// Row properties are strings, so numeric-looking cells must come back as written: no reformatting,
    /// no culture-specific separators and no conversion between empty and "NULL".
    /// </summary>
    [TestCase("")]
    [TestCase("de-DE")]
    [TestCase("ar-SA")]
    [TestCase("fa-IR")]
    public void NumericLookingText_RoundTripsExactly(string culture)
    {
        var headers = new[] { "Id", "Int", "Double", "Bool", "Mixed" };
        var rows = new List<string[]>
        {
            new[] { "r0", "007", "13.0", "yes", "" },
            new[] { "r1", "-5", "480.000000", "No", "NULL" },
            new[] { "r2", "", "4.1595885622433E-09", "1", "1,5" },
            new[] { "r3", "NULL", "0.1", "0", " 12 " },
            new[] { "r4", "2147483647", "-0.0", "TRUE", "1e3" },
            new[] { "r5", "0     ", "1.5", "false", "∞" },
        };

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            AssertRoundTrip(headers, rows);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void QuotedFields_RoundTrip()
    {
        var headers = new[] { "Id", "Text" };
        var rows = new List<string[]>
        {
            new[] { "r0", "a,b" },
            new[] { "r1", "say \"hi\"" },
            new[] { "r2", "line1\nline2" },
            new[] { "r3", "line1\r\nline2 «»" },
            new[] { "r4", "\"" },
            new[] { "r5", "" },
        };

        AssertRoundTrip(headers, rows);
    }

    [Test]
    public void CountColumn_RowsArePreSortedDescending()
    {
        var headers = new[] { "Code", "RecordCount" };
        var rows = new List<string[]>
        {
            new[] { "«a", "5" }, new[] { "b€", "50" }, new[] { "c😀", "1" }, new[] { "d", "500" },
        };

        var data = Generate(headers, rows);
        var counts = data.Rows.Select(r => r[1]).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(counts, Is.EqualTo(new[] { "500", "50", "5", "1" }));
            Assert.That(data.Rows.Select(r => r[0]), Is.EqualTo(new[] { "d", "b€", "«a", "c😀" }));
        });
    }

    private static void AssertRoundTrip(string[] headers, List<string[]> rows)
    {
        var data = Generate(headers, rows);

        Assert.That(data.Rows, Has.Count.EqualTo(rows.Count));

        // No count column in these tests, so row order is preserved.
        Assert.Multiple(() =>
        {
            for (var i = 0; i < rows.Count; i++)
                for (var c = 0; c < headers.Length; c++)
                    Assert.That(data.Rows[i][c], Is.EqualTo(rows[i][c]), $"row {i} column {headers[c]}");
        });
    }

    /// <summary>
    /// Runs the generator over <paramref name="headers"/>/<paramref name="rows"/> written as a CSV file,
    /// compiles the output and returns every cell of TestData.AllRows, columns in header order.
    /// </summary>
    private static (List<string[]> Rows, ImmutableArray<Diagnostic> Diagnostics) Generate(string[] headers, List<string[]> rows)
    {
        var csv = new StringBuilder();
        csv.Append('﻿').Append(string.Join(',', headers.Select(Quote))).Append("\r\n");
        foreach (var row in rows)
            csv.Append(string.Join(',', row.Select(Quote))).Append("\r\n");

        var compilation = CSharpCompilation.Create(
            "GeneratorTest" + Guid.NewGuid().ToString("N"),
            [],
            ReferenceAssemblies.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CsvDataSourceGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText("/data/Test.csv", csv.ToString())],
            parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.That(generatorDiagnostics, Is.Empty, "generator diagnostics");
        Assert.That(driver.GetRunResult().GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)), Does.Contain("TestData.g.cs"));

        using var dll = new MemoryStream();
        var emit = output.Emit(dll);
        Assert.That(emit.Success, Is.True,
            () => "generated code does not compile:\n" + string.Join('\n', emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        dll.Position = 0;
        var context = new AssemblyLoadContext("GeneratorTest", isCollectible: true);
        try
        {
            var dataType = context.LoadFromStream(dll).GetType("SynthEHR.Core.Data.TestData")!;
            var rowType = dataType.GetNestedType("Row")!;
            var properties = headers.Select(h => rowType.GetProperty(h)!).ToArray();
            var allRows = (IEnumerable<object>)dataType.GetProperty("AllRows", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            var result = allRows.Select(row => properties.Select(p => (string)p.GetValue(row)!).ToArray()).ToList();
            return (result, generatorDiagnostics);
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static readonly Lazy<MetadataReference[]> ReferenceAssemblies = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal)
                        || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray(), LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content, Encoding.UTF8);
    }
}
