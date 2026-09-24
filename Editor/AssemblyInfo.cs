using System.Runtime.CompilerServices;

// EditMode tests call ChatGuardBuildCheck's internals without running a build.
[assembly: InternalsVisibleTo("ChatGuard.Tests.EditMode")]
