// Copyright (c) The University of Dundee 2018-2019
// This file is part of the Research Data Management Platform (RDMP).
// RDMP is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
// RDMP is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
// You should have received a copy of the GNU General Public License along with RDMP. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;
using SynthEHR.Core.Data;

namespace SynthEHR.Datasets;

/// <summary>
/// Random record for when a <see cref="SynthEHR.Person"/> entered hospital.  Basic logic is implemented here to ensure that <see cref="DischargeDate"/>
/// is after <see cref="AdmissionDate"/> and that the person was alive at the time.
/// </summary>
public sealed class HospitalAdmissionsRecord
{
    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="AdmissionDate"]'/>
    public DateTime AdmissionDate { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="DischargeDate"]'/>
    public DateTime DischargeDate { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="MainCondition"]'/>
    public string MainCondition { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherCondition1"]'/>
    public string OtherCondition1 { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherCondition2"]'/>
    public string OtherCondition2 { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherCondition3"]'/>
    public string OtherCondition3 { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="MainOperation"]'/>
    public string MainOperation { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="MainOperationB"]'/>
    public string MainOperationB { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherOperation1"]'/>
    public string OtherOperation1 { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherOperation1B"]'/>
    public string OtherOperation1B { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherOperation2"]'/>
    public string OtherOperation2 { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherOperation2B"]'/>
    public string OtherOperation2B { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherOperation3"]'/>
    public string OtherOperation3 { get; private set; }

    /// <include file='../../Datasets.doc.xml' path='Datasets/HospitalAdmissions/Field[@name="OtherOperation3B"]'/>
    public string OtherOperation3B { get; private set; }

    /// <summary>
    /// The <see cref="Person"/> being admitted to hospital
    /// </summary>
    public Person Person { get; set; }

    /// <summary>
    /// Pre-computed cumulative weights and indices for each field/month combination.
    /// Enables O(log n) binary search instead of O(n) linear scan.
    /// Maps ColumnAppearingIn -> Month -> (CumulativeWeights array, Indices into ICD10Rows array)
    /// </summary>
    private static readonly FrozenDictionary<string, FrozenDictionary<int, (int[] CumulativeWeights, int[] Indices)>> ICD10MonthLookup;

    /// <summary>
    /// All ICD10 codes with their cumulative weights for binary search.
    /// Each entry contains (cumulative weight up to this point, ICD10 code).
    /// </summary>
    private static readonly (int CumulativeWeight, string Code)[] ICD10Rows;

    /// <summary>
    /// Maps a given MAIN_CONDITION code (doesn't cover other conditions) to popular operations for that condition.  The string array is always length 8 and corresponds to
    /// MAIN_OPERATION,MAIN_OPERATION_B,OTHER_OPERATION_1,OTHER_OPERATION_1B,OTHER_OPERATION_2,OTHER_OPERATION_2B,OTHER_OPERATION_3,OTHER_OPERATION_3B
    /// </summary>
    private static readonly FrozenDictionary<string, BucketList<string[]>> ConditionsToOperationsMap;

    /// <summary>
    /// The earliest date from which to generate records (matches HIC aggregate data collected)
    /// </summary>
    public static readonly DateTime MinimumDate = new(1983, 1, 1);

    /// <summary>
    /// The latest date to which to generate records (matches HIC aggregate data collected)
    /// </summary>
    public static readonly DateTime MaximumDate = new(2018, 1, 1);

    /// <summary>
    /// Creates a new record for the given <paramref name="person"/>
    /// </summary>
    /// <param name="person"></param>
    /// <param name="afterDateX"></param>
    /// <param name="r"></param>
    public HospitalAdmissionsRecord(Person person, DateTime afterDateX, Random r)
    {
        Person = person;
        if (person.DateOfBirth > afterDateX)
            afterDateX = person.DateOfBirth;

        AdmissionDate = DataGenerator.GetRandomDate(afterDateX.Max(MinimumDate), MaximumDate, r);

        DischargeDate = AdmissionDate.AddHours(r.Next(240));//discharged after random number of hours between 0 and 240 = 10 days

        //Condition 1 always populated
        MainCondition = GetRandomICDCode("MAIN_CONDITION", r);

        //50% chance of condition 2 as well as 1
        if (r.Next(2) == 0)
        {
            OtherCondition1 = GetRandomICDCode("OTHER_CONDITION_1", r);

            //25% chance of condition 3 too
            if (r.Next(2) == 0)
            {
                OtherCondition2 = GetRandomICDCode("OTHER_CONDITION_2", r);

                //12.5% chance of all conditions
                if (r.Next(2) == 0)
                    OtherCondition3 = GetRandomICDCode("OTHER_CONDITION_3", r);

                //1.25% chance of dirty data = the text 'Nul'
                if (r.Next(10) == 0)
                    OtherCondition3 = "Nul";
            }
        }

        //if the condition is one that is often treated in a specific way
        if (!ConditionsToOperationsMap.TryGetValue(MainCondition, out var operationsList)) return;

        var operations = operationsList.GetRandom(r);

        MainOperation = operations[0];
        MainOperationB = operations[1];
        OtherOperation1 = operations[2];
        OtherOperation1B = operations[3];
        OtherOperation2 = operations[4];
        OtherOperation2B = operations[5];
        OtherOperation3 = operations[6];
        OtherOperation3B = operations[7];
    }

    static HospitalAdmissionsRecord()
    {
        // Use compile-time generated data instead of runtime CSV parsing
        var rows = HospitalAdmissionsData.AllRows;

        // Step 1: Build ICD10Rows array with cumulative weights
        var rowsList = new List<(int CumulativeWeight, string Code)>(rows.Count);
        var cumulativeWeight = 0;

        foreach (var row in rows)
        {
            var weight = int.Parse(row.CountAppearances);
            cumulativeWeight += weight;
            rowsList.Add((cumulativeWeight, row.TestCode));
        }

        ICD10Rows = [.. rowsList];

        // Step 2: Build month-based index mappings (which rows are valid for each month)
        var tempICD10MonthHashMap = new Dictionary<string, Dictionary<int, List<int>>>
            {
                {"MAIN_CONDITION", new Dictionary<int, List<int>>()},
                {"OTHER_CONDITION_1", new Dictionary<int,  List<int>>()},
                {"OTHER_CONDITION_2", new Dictionary<int,  List<int>>()},
                {"OTHER_CONDITION_3", new Dictionary<int,  List<int>>()}
            };

        // Get all the months we might be asked for
        var from = (MinimumDate.Year - 1900) * 12 + MinimumDate.Month;
        var to = (MaximumDate.Year - 1900) * 12 + MaximumDate.Month;

        // Initialize all month buckets
        foreach (var columnKey in tempICD10MonthHashMap.Keys)
        {
            for (var i = from; i <= to; i++)
            {
                tempICD10MonthHashMap[columnKey].Add(i, []);
            }
        }

        // For each row in the sample data, determine which months it's valid for
        var rowCount = 0;
        foreach (var row in rows)
        {
            var avgMonth = double.Parse(row.AverageMonthAppearing);
            var stdDev = double.Parse(row.StandardDeviationMonthAppearing);

            // Calculate 2 standard deviations in months
            var monthFrom = Convert.ToInt32(avgMonth - 2 * stdDev);
            var monthTo = Convert.ToInt32(avgMonth + 2 * stdDev);

            // Clamp to our valid range
            monthFrom = Math.Max(monthFrom, from);
            monthTo = Math.Min(monthTo, to);

            // Add this row index to all months in its valid range
            for (var i = monthFrom; i <= monthTo; i++)
            {
                if (monthFrom < from)
                    continue;

                if (monthTo > to)
                    break;

                tempICD10MonthHashMap[row.ColumnAppearingIn][i].Add(rowCount);
            }

            rowCount++;
        }

        // Step 3: Convert index lists to cumulative weight arrays for binary search
        var tempICD10MonthLookup = new Dictionary<string, Dictionary<int, (int[] CumulativeWeights, int[] Indices)>>();

        foreach (var field in tempICD10MonthHashMap.Keys)
        {
            tempICD10MonthLookup[field] = new Dictionary<int, (int[] CumulativeWeights, int[] Indices)>();

            foreach (var (month, indices) in tempICD10MonthHashMap[field])
            {
                if (indices.Count == 0)
                {
                    // Empty month - store empty arrays
                    tempICD10MonthLookup[field][month] = ([], []);
                    continue;
                }

                // Build cumulative weights for this month's subset
                var indicesArray = indices.ToArray();
                var cumulativeWeights = new int[indicesArray.Length];
                var cumulative = 0;

                for (var i = 0; i < indicesArray.Length; i++)
                {
                    var idx = indicesArray[i];
                    // Get the weight for this specific item
                    var itemWeight = i == 0
                        ? ICD10Rows[idx].CumulativeWeight
                        : ICD10Rows[idx].CumulativeWeight - ICD10Rows[idx - 1].CumulativeWeight;

                    cumulative += itemWeight;
                    cumulativeWeights[i] = cumulative;
                }

                tempICD10MonthLookup[field][month] = (cumulativeWeights, indicesArray);
            }
        }

        // Freeze the lookup structure
        ICD10MonthLookup = tempICD10MonthLookup.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToFrozenDictionary()
        );

        // Use compile-time generated data for operations
        var operationsRows = HospitalAdmissionsOperationsData.AllRows;

        var tempConditionsToOperationsMap = new Dictionary<string, BucketList<string[]>>();

        foreach (var r in operationsRows)
        {
            var key = r.MAINCONDITION;
            if (!tempConditionsToOperationsMap.TryGetValue(key, out var conditionOps))
                tempConditionsToOperationsMap.Add(key, conditionOps = []);

            conditionOps.Add(int.Parse(r.CountOfRecords), [
                    r.MAINOPERATION,
                r.MAINOPERATIONB,
                r.OTHEROPERATION1,
                r.OTHEROPERATION1B,
                r.OTHEROPERATION2,
                r.OTHEROPERATION2B,
                r.OTHEROPERATION3,
                r.OTHEROPERATION3B
                    ]);
        }

        // Freeze the ConditionsToOperationsMap
        ConditionsToOperationsMap = tempConditionsToOperationsMap.ToFrozenDictionary();
    }


    private string GetRandomICDCode(string field, Random random)
    {
        // The number of months since 1/1/1900 (this is the measure of field AverageMonthAppearing)
        var monthsSinceZeroDay = (AdmissionDate.Year - 1900) * 12 + AdmissionDate.Month;

        var (cumulativeWeights, indices) = ICD10MonthLookup[field][monthsSinceZeroDay];

        if (cumulativeWeights.Length == 0)
        {
            // No valid codes for this month/field combination
            return string.Empty;
        }

        // Generate random value in range [0, totalWeight)
        var target = random.Next(cumulativeWeights[^1]);

        // Binary search for the target weight
        var index = BinarySearchCumulativeWeights(cumulativeWeights, target);

        // Return the code at the found index
        return ICD10Rows[indices[index]].Code;
    }

    /// <summary>
    /// Binary search to find the index where cumulative weight first exceeds target.
    /// This maintains the same weighted random distribution as the original linear scan.
    /// </summary>
    /// <param name="cumulativeWeights">Array of cumulative weights (must be sorted ascending)</param>
    /// <param name="target">Random value to search for</param>
    /// <returns>Index of the first element where cumulative weight > target</returns>
    private static int BinarySearchCumulativeWeights(int[] cumulativeWeights, int target)
    {
        var left = 0;
        var right = cumulativeWeights.Length - 1;

        // Find the first index where cumulativeWeights[index] > target
        while (left < right)
        {
            var mid = left + (right - left) / 2;

            if (cumulativeWeights[mid] <= target)
            {
                left = mid + 1;
            }
            else
            {
                right = mid;
            }
        }

        return left;
    }

}