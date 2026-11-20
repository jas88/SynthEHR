// Copyright (c) The University of Dundee 2018-2019
// This file is part of the Research Data Management Platform (RDMP).
// RDMP is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
// RDMP is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
// You should have received a copy of the GNU General Public License along with RDMP. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections;
using System.Collections.Generic;

namespace SynthEHR;

/// <summary>
/// A factory and collection for <see cref="Person"/>.  Pass around to ensure all datasets share the same
/// patient identifiers.
/// </summary>
public interface IPersonCollection : IReadOnlyCollection<Person>
{
    /// <summary>
    /// The last set of randomly generated patients
    /// </summary>
    Person[] People { get; }

    /// <summary>
    /// Generates <paramref name="numberOfUniqueIndividuals"/> random people (populating <see cref="People"/>).
    /// </summary>
    /// <param name="numberOfUniqueIndividuals">Number of <see cref="People"/> to generate</param>
    /// <param name="random">The seed upon which to generate data</param>
    void GeneratePeople(int numberOfUniqueIndividuals, Random random);

    /// <summary>
    /// The number of <see cref="People"/> currently generated
    /// </summary>
    int IReadOnlyCollection<Person>.Count => People.Length;

    /// <summary>
    /// Enumerates <see cref="People"/>
    /// </summary>
    IEnumerator<Person> IEnumerable<Person>.GetEnumerator() => ((IEnumerable<Person>)People).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => People.GetEnumerator();

}
