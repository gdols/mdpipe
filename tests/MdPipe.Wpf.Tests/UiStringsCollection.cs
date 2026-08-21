namespace MdPipe.Wpf.Tests;

/// <summary>
/// Keeps the tests that swap the interface language off each other's toes: the culture is a static
/// setting, so running them side by side would make either one flaky.
/// </summary>
[CollectionDefinition("ui-strings", DisableParallelization = true)]
public sealed class UiStringsCollection;
