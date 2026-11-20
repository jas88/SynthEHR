using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CommandLine;
using FAnsi.Discovery;
using FAnsi.Implementations.MicrosoftSQL;
using FAnsi.Implementations.MySql;
using FAnsi.Implementations.Oracle;
using FAnsi.Implementations.PostgreSql;
using SynthEHR.Configuration;
using SynthEHR.Datasets;
using YamlDotNet.Serialization;
using YamlException = YamlDotNet.Core.YamlException;

namespace SynthEHR;

internal static class Program
{
    private static int _returnCode;
    private const string ConfigFile = "./SynthEHR.yaml";

    public static int Main(string[] args)
    {
        _returnCode = 0;

        Parser.Default.ParseArguments<ProgramOptions>(args)
            .WithParsed(static opts => RunOptionsAndReturnExitCode(opts))
            .WithNotParsed(static _ => HandleParseError());

        return _returnCode;
    }

    private static void HandleParseError()
    {
        _returnCode = 500;
    }

    private static void RunOptionsAndReturnExitCode(ProgramOptions opts)
    {

        if (opts.NumberOfPatients <= 0)
            opts.NumberOfPatients = 500;
        if (opts.NumberOfRows <= 0)
            opts.NumberOfRows = 2000;

        Config? config = null;

        if (File.Exists(ConfigFile))
        {
            try
            {
                var d = new Deserializer();
                config = d.Deserialize<Config>(File.ReadAllText(ConfigFile));
            }
            // The file can't be read, or isn't valid YAML for Config (YamlDotNet wraps every other
            // deserialization failure in a YamlException)
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlException)
            {
                Console.WriteLine($"Error deserializing '{ConfigFile}'");
                Console.Write(e);
                _returnCode = -1;
                return;
            }
        }


        var dir = Directory.CreateDirectory(opts.OutputDirectory);

        try
        {
            //if user wants to write out the lookups generate those too
            if (opts.Lookups)
                DataGenerator.WriteLookups(dir);

            var r = opts.Seed == -1 ? new Random() : new Random(opts.Seed);

            //create a cohort of people
            var identifiers = new PersonCollection();
            identifiers.GeneratePeople(opts.NumberOfPatients, r);

            var generators = DataGeneratorFactory.GetAvailableGenerators().ToList();

            //if the user only wants to extract a single dataset
            if (!string.IsNullOrEmpty(opts.Dataset))
            {
                var match = generators.FirstOrDefault(g => g.Type.Name.Equals(opts.Dataset, StringComparison.Ordinal));
                if (match.Type == null)
                {
                    Console.WriteLine($"Could not find dataset called '{opts.Dataset}'");
                    Console.WriteLine(
                        $"Generators found were:{Environment.NewLine}{string.Join(Environment.NewLine, generators.Select(static g => g.Type.Name))}");
                    _returnCode = 2;
                    return;
                }

                generators = [.. new[] { match }];
            }

            // if we are not going to write out to a database
            if (config?.Database != null)
            {
                try
                {
                    //for each generator
                    foreach (var instance in generators.Select(([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] g) => DataGeneratorFactory.Create(g.Type, r)))
                    {
                        _returnCode = Math.Min(RunDatabaseTarget(identifiers, config.Database, instance, opts.NumberOfRows), _returnCode);
                    }

                    return;
                }
                catch (Exception e)
                {

                    Console.WriteLine(e);
                    _returnCode = 3;
                    return;
                }
            }
            // we are writing out to CSV

            //for each generator
            foreach (var g in generators)
            {
                var instance = DataGeneratorFactory.Create(g.Type, r);

                var targetFile = new FileInfo(Path.Join(dir.FullName, $"{g.Type.Name}.csv"));
                instance.GenerateTestDataFile(identifiers, targetFile, opts.NumberOfRows);
            }

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            _returnCode = 2;
            return;
        }

        _returnCode = 0;
    }

    private static int RunDatabaseTarget(IPersonCollection cohort, TargetDatabase configDatabase, IDataGenerator generator, int numberOfRows)
    {

        // Explicitly register each implementation rather than relying on auto-registration,
        // so they are guaranteed to be loaded in single-file published builds.
        MySqlImplementation.EnsureLoaded();
        PostgreSqlImplementation.EnsureLoaded();
        OracleImplementation.EnsureLoaded();
        MicrosoftSQLImplementation.EnsureLoaded();

        if (string.IsNullOrWhiteSpace(configDatabase.ConnectionString))
            throw new InvalidOperationException($"'{nameof(TargetDatabase.ConnectionString)}' must be set in the Database section of '{ConfigFile}'");
        if (string.IsNullOrWhiteSpace(configDatabase.DatabaseName))
            throw new InvalidOperationException($"'{nameof(TargetDatabase.DatabaseName)}' must be set in the Database section of '{ConfigFile}'");

        var server = new DiscoveredServer(configDatabase.ConnectionString, configDatabase.DatabaseType);

        try
        {
            server.TestConnection(10000);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Could not reach target server '{server.Name}'");
            Console.WriteLine(e);
            return -2;
        }


        var db = server.ExpectDatabase(configDatabase.DatabaseName);

        if (!db.Exists())
        {
            Console.WriteLine($"Creating Database '{db.GetRuntimeName()}'");
            db.Create();
            Console.WriteLine("Database Created");
        }
        else
        {
            Console.WriteLine($"Found Database '{db.GetRuntimeName()}'");
        }

        var tblName = generator.GetType().Name;

        if (db.ExpectTable(tblName).Exists())
        {

            Console.WriteLine($"Found Existing Table '{tblName}'");

            if (configDatabase.DropTables)
                db.ExpectTable(tblName).Drop();
            else
            {
                Console.WriteLine("Skipping table because it already existed and DropTables is false");
                return -3;
            }
        }

        Console.WriteLine($"Creating Table '{tblName}'");

        var dt = generator.GetDataTable(cohort, numberOfRows);
        var tbl = db.CreateTable(tblName, dt);

        Console.WriteLine($"Finished Creating '{tblName}'.  It had '{tbl.GetRowCount()}' rows");
        return 0;
    }
}
