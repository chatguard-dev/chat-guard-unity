#nullable enable
using System.Runtime.CompilerServices;

// Lets the EditMode tests use internals, such as the UTF-8 request writer and the response reader.
[assembly: InternalsVisibleTo("ChatGuard.Tests.EditMode")]
