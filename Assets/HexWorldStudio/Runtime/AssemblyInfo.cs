using System.Runtime.CompilerServices;

// Exposes internal types in HexWorldStudio.Runtime to the EditMode and
// PlayMode test assemblies so the test code can construct and inspect
// internal services (e.g., RngService) directly. Production code outside
// the test assemblies sees only the public API surface.
[assembly: InternalsVisibleTo("HexWorldStudio.Tests.Editor")]
[assembly: InternalsVisibleTo("HexWorldStudio.Tests.Runtime")]
