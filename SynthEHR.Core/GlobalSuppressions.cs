using System.Diagnostics.CodeAnalysis;

// CA1711: these names follow established .NET conventions (EventArgs subclasses, collection types)
// and are part of the published public API, so they are kept as-is.
[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Conventional EventArgs/collection naming; public API", Scope = "type",
    Target = "~T:SynthEHR.RowsGeneratedEventArgs")]
[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Conventional EventArgs/collection naming; public API", Scope = "type",
    Target = "~T:SynthEHR.PersonCollection")]
[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Conventional EventArgs/collection naming; public API", Scope = "type",
    Target = "~T:SynthEHR.IPersonCollection")]
