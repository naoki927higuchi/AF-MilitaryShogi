using System.Runtime.CompilerServices;

// Only the .NET test runner may call the internal Judge directly (to prove the help screen's
// combat chart equals the referee). The CPU assembly is never granted access.
[assembly: InternalsVisibleTo("MilitaryShogi.Tests")]
