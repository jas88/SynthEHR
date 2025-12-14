// Copyright (c) The University of Dundee 2018-2024
// Stub implementations for CodeQL analysis - provides minimal dummy data
// to allow code structure analysis without the massive source-generated files.
// This file is only compiled when DISABLE_SOURCE_GENERATOR is true.

#if DISABLE_SOURCE_GENERATOR

using System.Collections.Generic;

namespace SynthEHR.Core.Data;

/// <summary>Stub for CodeQL - provides minimal dummy data for static analysis.</summary>
internal static class BiochemistryData
{
    public sealed class Row
    {
        public string LocalClinicalCodeValue { get; init; } = "TEST";
        public string ReadCodeValue { get; init; } = "44J3.";
        public string HbExtract { get; init; } = "T";
        public string SampleName { get; init; } = "Serum";
        public string ArithmeticComparator { get; init; } = "=";
        public string Interpretation { get; init; } = "Normal";
        public string QuantityUnit { get; init; } = "mmol/L";
        public string RangeHighValue { get; init; } = "10.0";
        public string RangeLowValue { get; init; } = "1.0";
        public string RecordCount { get; init; } = "100";
        public string QVAverage { get; init; } = "5.5";
        public string QVStandardDev { get; init; } = "1.2";
    }

    private static readonly Row[] _rows = [new Row()];
    public static IReadOnlyList<Row> AllRows => _rows;
    public static int Count => 1;
}

/// <summary>Stub for CodeQL - provides minimal dummy data for static analysis.</summary>
internal static class PrescribingData
{
    public sealed class Row
    {
        public string ResSeqno { get; init; } = "1";
        public string Name { get; init; } = "Paracetamol";
        public string FormulationCode { get; init; } = "TAB";
        public string Strength { get; init; } = "500mg";
        public string OrigStrength { get; init; } = "500";
        public string MeasureCode { get; init; } = "mg";
        public string BNFCode { get; init; } = "0407010H0";
        public string FormattedBNFCode { get; init; } = "04.07.01";
        public string BNFDescription { get; init; } = "Analgesics";
        public string ApprovedName { get; init; } = "Paracetamol";
        public string MinQuantity { get; init; } = "1";
        public string MaxQuantity { get; init; } = "100";
        public string Frequency { get; init; } = "1000";
    }

    private static readonly Row[] _rows = [new Row()];
    public static IReadOnlyList<Row> AllRows => _rows;
    public static int Count => 1;
}

/// <summary>Stub for CodeQL - provides minimal dummy data for static analysis.</summary>
internal static class HospitalAdmissionsData
{
    public sealed class Row
    {
        public string TestCode { get; init; } = "A00";
        public string CountAppearances { get; init; } = "100";
        public string AverageMonthAppearing { get; init; } = "1200";
        public string StandardDeviationMonthAppearing { get; init; } = "100";
        public string ColumnAppearingIn { get; init; } = "MAIN_CONDITION";
    }

    private static readonly Row[] _rows = [new Row()];
    public static IReadOnlyList<Row> AllRows => _rows;
    public static int Count => 1;
}

/// <summary>Stub for CodeQL - provides minimal dummy data for static analysis.</summary>
internal static class HospitalAdmissionsOperationsData
{
    public sealed class Row
    {
        public string MAINCONDITION { get; init; } = "A00";
        public string MAINOPERATION { get; init; } = "X00";
        public string MAINOPERATIONB { get; init; } = "";
        public string OTHEROPERATION1 { get; init; } = "";
        public string OTHEROPERATION1B { get; init; } = "";
        public string OTHEROPERATION2 { get; init; } = "";
        public string OTHEROPERATION2B { get; init; } = "";
        public string OTHEROPERATION3 { get; init; } = "";
        public string OTHEROPERATION3B { get; init; } = "";
        public string CountOfRecords { get; init; } = "100";
    }

    private static readonly Row[] _rows = [new Row()];
    public static IReadOnlyList<Row> AllRows => _rows;
    public static int Count => 1;
}

/// <summary>Stub for CodeQL - provides minimal dummy data for static analysis.</summary>
internal static class MaternityData
{
    public sealed class Row
    {
        public string Location { get; init; } = "T101H";
        public string LocationRecordCount { get; init; } = "100";
        public string MaritalStatusNumeric { get; init; } = "1";
        public string MaritalStatusNumericRecordCount { get; init; } = "100";
        public string MaritalStatusAlpha { get; init; } = "M";
        public string MaritalStatusAlphaRecordCount { get; init; } = "100";
        public string Specialty { get; init; } = "AA";
        public string SpecialtyRecordCount { get; init; } = "100";
    }

    private static readonly Row[] _rows = [new Row()];
    public static IReadOnlyList<Row> AllRows => _rows;
    public static int Count => 1;
}

#endif
