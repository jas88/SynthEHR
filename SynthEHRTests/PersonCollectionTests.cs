using System;
using System.Linq;
using NUnit.Framework;
using SynthEHR;

namespace SynthEHRTests;

internal sealed class PersonCollectionTests
{
    [Test]
    public void PersonCollection_EnumeratesPeople()
    {
        var people = new PersonCollection();
        Assert.That(people, Is.Empty);

        people.GeneratePeople(25, new Random(42));

        Assert.Multiple(() =>
        {
            Assert.That(people, Has.Count.EqualTo(25));
            Assert.That(people.ToArray(), Is.EqualTo(people.People));
        });
    }

    [Test]
    public void IPersonCollection_DefaultImplementation_EnumeratesPeople()
    {
        IPersonCollection people = new FixedPeople([new Person(new Random(1)), new Person(new Random(2))]);

        Assert.Multiple(() =>
        {
            Assert.That(people, Has.Count.EqualTo(2));
            Assert.That(people.ToArray(), Is.EqualTo(people.People));
        });
    }

    [Test]
    public void RowsGeneratedEventArgs_IsEventArgs()
    {
        EventArgs args = new RowsGeneratedEventArgs(10, TimeSpan.Zero, true);
        Assert.That(args, Is.InstanceOf<RowsGeneratedEventArgs>());
    }

    /// <summary>
    /// An external-style implementation that relies on the interface's default enumeration members.
    /// </summary>
    private sealed class FixedPeople(Person[] people) : IPersonCollection
    {
        public Person[] People { get; } = people;

        public void GeneratePeople(int numberOfUniqueIndividuals, Random random) =>
            throw new NotSupportedException();
    }
}
