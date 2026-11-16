using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using NUnit.Framework;
using SynthEHR.Core.Data;

namespace SynthEHRTests;

/// <summary>
/// Checks that the compile-time generated data classes (SynthEHR.Core.Data.*Data) return exactly the text
/// of every cell in the CSV files they were generated from. The CSVs are re-read here with an independent
/// parser (<see cref="TextFieldParser"/>), so a fault in the generator's own parser or storage shows up as a
/// mismatch.
/// </summary>
internal sealed class GeneratedDataRoundTripTests
{
    private static readonly string DataDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, "data");

    private static IEnumerable<string> CsvFiles() =>
        Directory.GetFiles(DataDirectory, "*.csv").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!;

    [Test]
    public void DataDirectory_ContainsEveryDataset()
    {
        Assert.That(CsvFiles(), Is.SupersetOf(new[]
        {
            "Biochemistry", "HospitalAdmissions", "HospitalAdmissionsOperations", "Maternity", "Prescribing"
        }));
    }

    [TestCaseSource(nameof(CsvFiles))]
    public void GeneratedRows_MatchCsvExactly_InvariantCulture(string dataset) =>
        WithCulture(CultureInfo.InvariantCulture, () => AssertRoundTrip(dataset));

    /// <summary>
    /// Cultures with a comma decimal separator, a non-ASCII minus sign or non-Latin digits must not change
    /// the text returned for any cell.
    /// </summary>
    [TestCaseSource(nameof(CsvFiles))]
    public void GeneratedRows_MatchCsvExactly_InOtherCultures(string dataset)
    {
        foreach (var culture in new[] { "de-DE", "fr-FR", "sv-SE", "ar-SA", "fa-IR" })
            WithCulture(CultureInfo.GetCultureInfo(culture), () => AssertRoundTrip(dataset));
    }

    /// <summary>
    /// Regression test for UTF-8 byte offsets being computed in UTF-16 chars: rows after the first
    /// non-ASCII character ('«' in Prescribing.csv) were read from the wrong place in the string blob.
    /// </summary>
    [Test]
    public void Prescribing_RowsAfterNonAsciiText_AreNotShifted()
    {
        // AllRows materialises (and decodes) each Row on enumeration, so read it once.
        // res_seqno is not unique across the file, but is for these rows.
        string[] wanted = ["45164", "83773", "54028"];
        var found = new Dictionary<string, List<PrescribingData.Row>>(StringComparer.Ordinal);
        var blankNames = 0;
        foreach (var row in PrescribingData.AllRows)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
                blankNames++;
            if (wanted.Contains(row.ResSeqno, StringComparer.Ordinal))
                (found.TryGetValue(row.ResSeqno, out var list) ? list : found[row.ResSeqno] = []).Add(row);
        }

        Assert.That(wanted.Select(id => found.TryGetValue(id, out var l) ? l.Count : 0), Is.All.EqualTo(1), "each sample row appears once");
        var (paroven, vitajoule, phenol) = (found["45164"][0], found["83773"][0], found["54028"][0]);

        Assert.Multiple(() =>
        {
            Assert.That(paroven.Name, Is.EqualTo("PAROVEN"));
            Assert.That(paroven.BNFCode, Is.EqualTo("0206040AHBBAAAA"));
            Assert.That(paroven.BNFDescription, Is.EqualTo("PAROVEN_CAP 250MG"));
            Assert.That(paroven.ApprovedName, Is.EqualTo("OXERUTINS"));
            Assert.That(vitajoule.Name, Is.EqualTo("VITAJOULE"));
            Assert.That(phenol.MaxQuantity, Is.EqualTo("7«G   ")); // trailing spaces are in the CSV
            Assert.That(blankNames, Is.Zero, "rows with a blank Name");
        });
    }

    /// <summary>
    /// An empty cell must come back empty, not as the literal "NULL" (and vice versa).
    /// </summary>
    [Test]
    public void Maternity_EmptyCells_StayEmpty()
    {
        var rows = MaternityData.AllRows.ToList(); // enumerate (and decode) once
        Assert.Multiple(() =>
        {
            Assert.That(rows.Any(r => r.MaritalStatusNumeric.Length == 0 && r.MaritalStatusNumericRecordCount == "44724"),
                "the MaritalStatusNumeric category with an empty value");
            Assert.That(rows.Select(r => r.MaritalStatusNumeric), Has.None.EqualTo("NULL"));
        });
    }

    private static void AssertRoundTrip(string dataset)
    {
        var (headers, expectedRows) = ReadCsv(Path.Combine(DataDirectory, dataset + ".csv"));

        var dataType = typeof(PrescribingData).Assembly.GetType($"SynthEHR.Core.Data.{SanitizeIdentifier(dataset)}Data");
        Assert.That(dataType, Is.Not.Null, $"no generated class for {dataset}.csv");

        var rowType = dataType!.GetNestedType("Row")!;
        var properties = headers.Select(h => rowType.GetProperty(SanitizeIdentifier(h))).ToArray();
        Assert.That(properties, Has.None.Null, $"{dataset}: a CSV column has no matching Row property");

        var allRows = (IEnumerable<object>)dataType.GetProperty("AllRows", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var actualRows = allRows.Select(row => properties.Select(p => (string)p!.GetValue(row)!).ToArray()).ToList();

        Assert.That(actualRows, Has.Count.EqualTo(expectedRows.Count), $"{dataset}: row count");

        // The generator may reorder rows (pre-sorting by a count column), so compare as multisets.
        var remaining = expectedRows.GroupBy(Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var unexpected = new List<string[]>();
        foreach (var row in actualRows)
        {
            var key = Key(row);
            if (remaining.TryGetValue(key, out var n) && n > 0)
                remaining[key] = n - 1;
            else
                unexpected.Add(row);
        }

        if (unexpected.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{dataset} ({CultureInfo.CurrentCulture.Name}): {unexpected.Count} of {actualRows.Count} generated rows do not match any CSV row. First few:");
        foreach (var row in unexpected.Take(5))
        {
            var closest = expectedRows.MaxBy(e => e.Where((cell, c) => cell == row[c]).Count());
            sb.AppendLine();
            for (var c = 0; c < headers.Length; c++)
                if (closest is null || closest[c] != row[c])
                    sb.Append(CultureInfo.InvariantCulture, $"  [{headers[c]}] generated '{row[c]}' expected '{closest?[c]}'");
        }

        Assert.Fail(sb.ToString());
    }

    private static string Key(string[] row) => string.Join('\u001F', row);

    private static (string[] Headers, List<string[]> Rows) ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields() ?? throw new InvalidDataException($"{path} has no header row");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null || fields.All(string.IsNullOrWhiteSpace))
                continue;

            // Missing trailing cells read as empty, matching the generator.
            rows.Add(fields.Length >= headers.Length ? fields : [.. fields, .. Enumerable.Repeat(string.Empty, headers.Length - fields.Length)]);
        }

        return (headers, rows);
    }

    /// <summary>
    /// The generator's column-to-property naming rule: keep letters and digits, capitalise the first character
    /// and any character following '_' or ' ', prefix '_' if the result starts with a digit.
    /// </summary>
    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder();
        var upperNext = true;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            else if (c is '_' or ' ' && sb.Length > 0)
            {
                upperNext = true;
            }
        }

        var result = sb.Length == 0 ? "Field" : sb.ToString();
        return char.IsDigit(result[0]) ? "_" + result : result;
    }

    private static void WithCulture(CultureInfo culture, Action action)
    {
        var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
            action();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (previous, previousUi);
        }
    }
}
