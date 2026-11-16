using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SynthEHR.Datasets;

/// <summary>
/// Finds Types and Creates instances of <see cref="IDataGenerator"/> implementations
/// </summary>
public static class DataGeneratorFactory
{
    /// <summary>
    /// Trivial type wrapper as workaround for https://github.com/dotnet/sdk/issues/27997
    /// </summary>
    public readonly struct GeneratorType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
    {
        /// <summary>
        /// Actual generator type
        /// </summary>
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type Type { get; } = type;
    }

    /// <summary>
    /// List of generator types. Add yourself to this if outside SynthEHR.Core, to avoid reliance on reflection breaking AOT.
    /// </summary>
    public static readonly List<GeneratorType> Generators =
    [
        new GeneratorType(typeof(Biochemistry)),
        new GeneratorType(typeof(CarotidArteryScan)),
        new GeneratorType(typeof(Demography)),
        new GeneratorType(typeof(HospitalAdmissions)),
        new GeneratorType(typeof(Maternity)),
        new GeneratorType(typeof(Prescribing)),
        new GeneratorType(typeof(UltraWide)),
        new GeneratorType(typeof(Wide))
    ];

    /// <summary>
    /// Finds all concrete implementations of <see cref="IDataGenerator"/>.
    /// </summary>
    /// <returns></returns>
    public static IEnumerable<GeneratorType> GetAvailableGenerators()
    {
        return Generators;
    }

    /// <summary>
    /// Creates a new instance of the generic <see cref="IDataGenerator"/> Type initialized with the given seed
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="seed"></param>
    /// <returns></returns>
    public static T Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Random seed) where T : IDataGenerator
    {
        return (T)Create(typeof(T), seed);
    }

    /// <summary>
    /// Creates a new instance of a <see cref="IDataGenerator"/> of Type <paramref name="type"/> initialized with the given seed
    /// </summary>
    /// <param name="type"></param>
    /// <param name="seed"></param>
    /// <returns></returns>
    public static IDataGenerator Create([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type, Random seed)
    {
        // CreateInstance only returns null for Nullable<T> value types, which can never implement IDataGenerator
        return (IDataGenerator?)Activator.CreateInstance(type, seed)
               ?? throw new InvalidOperationException($"Activator returned null when creating '{type}'");
    }
}