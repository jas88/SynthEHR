using System;
using System.Collections.Generic;
using System.Data;
using SynthEHR.Core.Data;

namespace SynthEHR.Datasets;

/// <summary>
/// Describes a single maternity event for a specific <see cref="Person"/>
/// </summary>
public sealed class MaternityRecord
{
    /// <summary>
    /// Youngest age of mother to generate
    /// </summary>
    public const int MinAge = 18;

    /// <summary>
    /// Oldest age of mother to generate
    /// </summary>
    public const int MaxAge = 55;

    /// <summary>
    /// Sorted array of (cumulative weight, value) tuples for O(log n) binary search lookup.
    /// </summary>
    private static readonly (int CumulativeWeight, string Value)[] _locations;
    private static int _locationsMaxWeight;

    private static readonly (int CumulativeWeight, string Value)[] _maritalStatusOld;
    private static int _maritalStatusOldMaxWeight;

    private static readonly (int CumulativeWeight, string Value)[] _maritalStatusNew;
    private static int _maritalStatusNewMaxWeight;

    private static readonly (int CumulativeWeight, string Value)[] _specialties;
    private static int _specialtiesMaxWeight;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Maternity/Field[@name="Location"]'/>
    public string Location { get; set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/Maternity/Field[@name="SendingLocation"]'/>
    public string SendingLocation { get; set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/Maternity/Field[@name="Date"]'/>
    public DateTime Date { get; set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/Maternity/Field[@name="MaritalStatus"]'/>
    public object MaritalStatus { get; set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/Maternity/Field[@name="Specialty"]'/>
    public string Specialty { get; internal set; }

    /// <summary>
    /// The person on whom the maternity action is performed
    /// </summary>
    public Person Person { get; set; }

    /// <summary>
    /// Chi numbers of up to 3 babies involved.  Always contains 3 elements with nulls e.g. if twins then first 2 elements are populated and third is null.
    /// </summary>
    public string[] BabyChi { get; } = new string[3];


    /// <summary>
    /// The date at which the data collector stopped using numeric marital status codes (in favour of alphabetical)
    /// </summary>
    private static readonly DateTime MaritalStatusSwitchover = new(2001, 1, 1);

    /// <summary>
    /// Generates a new random biochemistry test.
    /// </summary>
    /// <param name="p">The person who is undergoing maternity activity.  Should be Female and of a sufficient age that the operation could have taken place during their lifetime (see <see cref="Maternity.IsEligible(SynthEHR.Person)"/></param>
    /// <param name="r"></param>
    public MaternityRecord(Person p, Random r)
    {
        Person = p;

        var youngest = p.DateOfBirth.AddYears(MinAge);
        var oldest = p.DateOfDeath ?? p.DateOfBirth.AddYears(MaxAge);

        // No future dates
        oldest = oldest > DataGenerator.Now ? DataGenerator.Now : oldest;

        // If they died younger than 18 or were born less than 18 years into the past
        Date = youngest > oldest ? oldest : DataGenerator.GetRandomDate(youngest, oldest, r);

        Location = GetRandomFromWeightedArray(_locations, _locationsMaxWeight, r);
        SendingLocation = GetRandomFromWeightedArray(_locations, _locationsMaxWeight, r);
        MaritalStatus = Date < MaritalStatusSwitchover
            ? GetRandomFromWeightedArray(_maritalStatusOld, _maritalStatusOldMaxWeight, r)
            : GetRandomFromWeightedArray(_maritalStatusNew, _maritalStatusNewMaxWeight, r);

        BabyChi[0] = new Person(r) { DateOfBirth = Date }.GetRandomCHI(r);

        // One in 30 are twins
        if (r.Next(30) == 0)
        {
            BabyChi[1] = new Person(r) { DateOfBirth = Date }.GetRandomCHI(r);

            // One in 1000 are triplets (1/30 * 1/34)
            if (r.Next(34) == 0)
                BabyChi[2] = new Person(r) { DateOfBirth = Date }.GetRandomCHI(r);
        }

        Specialty = GetRandomFromWeightedArray(_specialties, _specialtiesMaxWeight, r);
    }

    static MaternityRecord()
    {
        // Use compile-time generated data instead of runtime CSV parsing
        var rows = MaternityData.AllRows;

        // Build cumulative weight arrays for binary search
        var locationsBuilder = new List<(int, string)>();
        var maritalStatusOldBuilder = new List<(int, string)>();
        var maritalStatusNewBuilder = new List<(int, string)>();
        var specialtiesBuilder = new List<(int, string)>();

        foreach (var row in rows)
        {
            AddRow(row.Location, row.LocationRecordCount, locationsBuilder);
            AddRow(row.MaritalStatusNumeric, row.MaritalStatusNumericRecordCount, maritalStatusOldBuilder);
            AddRow(row.MaritalStatusAlpha, row.MaritalStatusAlphaRecordCount, maritalStatusNewBuilder);
            AddRow(row.Specialty, row.SpecialtyRecordCount, specialtiesBuilder);
        }

        // Convert to arrays and store max weights
        _locations = locationsBuilder.ToArray();
        _locationsMaxWeight = locationsBuilder.Count > 0 ? locationsBuilder[^1].Item1 : 0;

        _maritalStatusOld = maritalStatusOldBuilder.ToArray();
        _maritalStatusOldMaxWeight = maritalStatusOldBuilder.Count > 0 ? maritalStatusOldBuilder[^1].Item1 : 0;

        _maritalStatusNew = maritalStatusNewBuilder.ToArray();
        _maritalStatusNewMaxWeight = maritalStatusNewBuilder.Count > 0 ? maritalStatusNewBuilder[^1].Item1 : 0;

        _specialties = specialtiesBuilder.ToArray();
        _specialtiesMaxWeight = specialtiesBuilder.Count > 0 ? specialtiesBuilder[^1].Item1 : 0;
    }

    private static void AddRow(string val, string freqStr, List<(int CumulativeWeight, string Value)> builder)
    {
        if (string.IsNullOrWhiteSpace(freqStr) || freqStr == "NULL")
            return;

        var frequency = Convert.ToInt32(freqStr);
        if (frequency == 0)
            return;

        var cumulativeWeight = (builder.Count > 0 ? builder[^1].Item1 : 0) + frequency;
        builder.Add((cumulativeWeight, val));
    }

    /// <summary>
    /// Binary search to find a random value from a weighted array.
    /// O(log n) complexity instead of O(n) linear scan.
    /// </summary>
    private static string GetRandomFromWeightedArray((int CumulativeWeight, string Value)[] array, int maxWeight, Random r)
    {
        if (array.Length == 0)
            return null;

        var weight = r.Next(maxWeight);

        // Binary search to find first cumulative weight > weight
        int left = 0;
        int right = array.Length - 1;

        while (left < right)
        {
            int mid = left + (right - left) / 2;

            if (array[mid].CumulativeWeight <= weight)
                left = mid + 1;
            else
                right = mid;
        }

        return array[left].Value;
    }
}