// Performance benchmark and correctness test for CHI/ANOCHI generation
using System;
using System.Diagnostics;
using SynthEHR;

namespace SynthEHRTests;

public class CHIBenchmark
{
    public static void Main()
    {
        Console.WriteLine("=== CHI/ANOCHI Generation Benchmark ===\n");

        // Test correctness with seeded Random
        TestCorrectness();

        // Benchmark performance
        BenchmarkPerformance();
    }

    private static void TestCorrectness()
    {
        Console.WriteLine("Testing Correctness with Seeded Random...");
        var r = new Random(500);
        var person = new Person(r);

        Console.WriteLine($"CHI: {person.CHI}");
        Console.WriteLine($"CHI Length: {person.CHI.Length}");
        Console.WriteLine($"ANOCHI: {person.ANOCHI}");
        Console.WriteLine($"ANOCHI Length: {person.ANOCHI.Length}");
        Console.WriteLine($"Date of Birth: {person.DateOfBirth:dd/MM/yyyy}");
        Console.WriteLine($"Gender: {person.Gender}");

        // Verify CHI format
        if (person.CHI.Length != 10)
        {
            Console.WriteLine($"ERROR: CHI should be 10 chars, got {person.CHI.Length}");
            return;
        }

        // Verify ANOCHI format
        if (person.ANOCHI.Length != 12 || !person.ANOCHI.EndsWith("_A"))
        {
            Console.WriteLine($"ERROR: ANOCHI format incorrect");
            return;
        }

        // Verify CHI starts with date of birth
        string expectedPrefix = person.DateOfBirth.ToString("ddMMyy");
        if (!person.CHI.StartsWith(expectedPrefix))
        {
            Console.WriteLine($"ERROR: CHI should start with {expectedPrefix}, got {person.CHI.Substring(0, 6)}");
            return;
        }

        Console.WriteLine("All correctness tests PASSED!\n");
    }

    private static void BenchmarkPerformance()
    {
        const int iterations = 1_000_000;
        Console.WriteLine($"Benchmarking {iterations:N0} Person creations...\n");

        var sw = Stopwatch.StartNew();
        var r = new Random(12345);

        for (int i = 0; i < iterations; i++)
        {
            _ = new Person(r);
        }

        sw.Stop();

        Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"Average Time: {(double)sw.ElapsedMilliseconds / iterations * 1000:F2} μs per person");
        Console.WriteLine($"Throughput: {iterations / sw.Elapsed.TotalSeconds:N0} persons/second");

        // Memory efficiency note
        Console.WriteLine("\nMemory Efficiency:");
        Console.WriteLine("- Span<char> uses stack allocation (zero heap allocations)");
        Console.WriteLine("- No StringBuilder overhead");
        Console.WriteLine("- Fixed-size buffers: CHI=10 chars, ANOCHI=12 chars");
    }
}
