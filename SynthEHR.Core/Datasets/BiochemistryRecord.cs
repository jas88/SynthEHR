// Copyright (c) The University of Dundee 2018-2019
// This file is part of the Research Data Management Platform (RDMP).
// RDMP is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
// RDMP is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
// You should have received a copy of the GNU General Public License along with RDMP. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Normal = SynthEHR.Statistics.Distributions.Normal;
using SynthEHR.Core.Data;

namespace SynthEHR.Datasets;

/// <summary>
/// Data class representing a single row in <see cref="Biochemistry"/> (use if you want to use randomly generated data directly
/// rather than generate it into a file).
/// </summary>
public sealed class BiochemistryRecord
{
    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="LabNumber"]'/>
    public readonly string LabNumber;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="SampleType"]'/>
    public readonly string SampleType;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="TestCode"]'/>
    public readonly string TestCode;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="Result"]'/>
    public readonly string Result;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="ReadCodeValue"]'/>
    public readonly string ReadCodeValue;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="Healthboard"]'/>
    public readonly string Healthboard;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="ArithmeticComparator"]'/>
    public readonly string ArithmeticComparator;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="Interpretation"]'/>
    public readonly string Interpretation;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="QuantityUnit"]'/>
    public readonly string QuantityUnit;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="RangeHighValue"]'/>
    public readonly string RangeHighValue;

    /// <include file='../../Datasets.doc.xml' path='Datasets/Biochemistry/Field[@name="RangeLowValue"]'/>
    public readonly string RangeLowValue;

    /// <summary>
    /// Sorted array of (cumulative weight, data row) tuples for O(log n) binary search lookup.
    /// Data is pre-sorted by RecordCount (descending) by the source generator.
    /// </summary>
    private static readonly (int CumulativeWeight, BiochemistryRandomDataRow Data)[] WeightedRows;
    private static readonly int MaxWeight;

    static BiochemistryRecord()
    {
        // Use compile-time generated data instead of runtime CSV parsing
        // Data is already pre-sorted by RecordCount (descending) by the source generator
        var rows = BiochemistryData.AllRows;

        var weights = new List<(int, BiochemistryRandomDataRow)>();
        var currentWeight = 0;

        foreach (var row in rows)
        {
            var count = int.Parse(row.RecordCount);
            currentWeight += count;
            weights.Add((currentWeight, new BiochemistryRandomDataRow(row)));
        }

        MaxWeight = currentWeight;
        // Array is already sorted by construction (cumulative sum is monotonically increasing)
        WeightedRows = weights.ToArray();
    }

    /// <summary>
    /// Generates a new random biochemistry test.
    /// </summary>
    /// <param name="r"></param>
    public BiochemistryRecord(Random r)
    {
        //get a random row from the lookup table - based on its representation within our biochemistry dataset
        var row = GetRandomRowUsingWeight(r);
        LabNumber = GetRandomLabNumber(r);
        TestCode = row.LocalClinicalCodeValue;
        SampleType = row.SampleName;

        Result = row.GetQVResult(r);

        ArithmeticComparator = row.ArithmeticComparator;
        Interpretation = row.Interpretation;
        QuantityUnit = row.QuantityUnit;
        RangeHighValue = row.RangeHighValue.HasValue ? row.RangeHighValue.ToString() : "NULL";
        RangeLowValue = row.RangeLowValue.HasValue ? row.RangeLowValue.ToString() : "NULL";

        Healthboard = row.hb_extract;
        ReadCodeValue = row.ReadCodeValue;
    }

    private static BiochemistryRandomDataRow GetRandomRowUsingWeight(Random r)
    {
        var weightToGet = r.Next(MaxWeight);

        // Binary search to find first cumulative weight > weightToGet
        // O(log n) instead of O(n) linear scan
        int left = 0;
        int right = WeightedRows.Length - 1;

        while (left < right)
        {
            int mid = left + (right - left) / 2;

            if (WeightedRows[mid].CumulativeWeight <= weightToGet)
                left = mid + 1;
            else
                right = mid;
        }

        return WeightedRows[left].Data;
    }



    private static string GetRandomLabNumber(Random r)
    {
        return r.Next(0, 2) == 0 ? $"CC{r.Next(0, 1000000)}" : $"BC{r.Next(0, 1000000)}";
    }

    private sealed class BiochemistryRandomDataRow(BiochemistryData.Row row)
    {
        public readonly string LocalClinicalCodeValue = row.LocalClinicalCodeValue;
        public readonly string ReadCodeValue = row.ReadCodeValue;
        public readonly string hb_extract = row.HbExtract;
        public readonly string SampleName = row.SampleName;
        public readonly string ArithmeticComparator = row.ArithmeticComparator;
        public readonly string Interpretation = row.Interpretation;
        public readonly string QuantityUnit = row.QuantityUnit;
        public double? RangeHighValue = double.TryParse(row.RangeHighValue, out var rangeLow) ? rangeLow : null;
        public double? RangeLowValue = double.TryParse(row.RangeLowValue, out var rangeHigh) ? rangeHigh : null;
        private readonly double? QVAverage = double.TryParse(row.QVAverage, out var min) ? min : null;
        private readonly double? QVStandardDev = double.TryParse(row.QVStandardDev, out var dev) ? dev : null;

        /// <summary>
        /// Returns a new QV value using the <see cref="QVAverage"/> and <see cref="QVStandardDev"/> seeded with the provided
        /// <paramref name="r"/>.  Returns null if <see cref="QVAverage"/> or <see cref="QVStandardDev"/> are null.
        /// </summary>
        /// <param name="r"></param>
        /// <returns></returns>
        internal string GetQVResult(Random r)
        {
            return !QVAverage.HasValue || !QVStandardDev.HasValue
                ? null
                : new Normal(QVAverage.Value, QVStandardDev.Value, r).Sample().ToString(CultureInfo.CurrentCulture);
        }
    }
}