using System.Globalization;
using System.Reflection;
using FluentAssertions;
using MdPipe.Wpf.Resources;

namespace MdPipe.Wpf.Tests;

/// <summary>
/// The interface follows the machine's language, which is impossible to see from a screenshot on a
/// single machine, so both branches are pinned here instead.
/// </summary>
[Collection("ui-strings")]
public sealed class StringsTests : IDisposable
{
    private readonly CultureInfo? _previous = Strings.Culture;

    public void Dispose() => Strings.Culture = _previous;

    [Theory]
    [InlineData("en", "Convert to Markdown")]
    [InlineData("es", "Convertir a Markdown")]
    [InlineData("es-ES", "Convertir a Markdown")]
    [InlineData("fr", "Convert to Markdown")]   // no French satellite: falls back to the neutral one
    public void UiTextFollowsTheCulture(string culture, string expected)
    {
        Strings.Culture = new CultureInfo(culture);

        Strings.ConvertToMarkdown.Should().Be(expected);
    }

    [Fact]
    public void EverySpanishStringIsTranslated()
    {
        // A key missing from the Spanish file silently falls back to English, which would show up as
        // one stray English label in an otherwise Spanish window.
        var english = Strings.ResourceManager.GetResourceSet(new CultureInfo("en"), true, true)!;
        var spanish = Strings.ResourceManager.GetResourceSet(new CultureInfo("es"), true, false)!;

        var untranslated = english
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .Where(key => spanish.GetString(key) is null)
            .ToList();

        untranslated.Should().BeEmpty();
    }

    [Fact]
    public void EveryAccessorOnStringsResolvesToARealResource()
    {
        // Strings.cs is written by hand and committed, because the temporary project WPF uses to
        // compile XAML cannot see sources generated during the build. That makes it possible to add
        // an accessor whose key does not exist, and the lookup falls back to returning the key
        // itself, so the window shows "UpdateGetIt" instead of "Get it" and nothing fails.
        // Asking the resource manager by name rather than comparing the text, because some labels
        // legitimately read the same as their key.
        var english = new CultureInfo("en");

        var broken = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .Where(name => Strings.ResourceManager.GetString(name, english) is null)
            .ToList();

        broken.Should().BeEmpty();
    }

    [Fact]
    public void EveryResourceHasAnAccessor()
    {
        // The other direction: a key added to the .resx and never added to Strings.cs is a string
        // nothing can reach.
        var accessors = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name)
            .ToHashSet();

        var unreachable = Strings.ResourceManager
            .GetResourceSet(new CultureInfo("en"), true, true)!
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .Where(key => !accessors.Contains(key))
            .ToList();

        unreachable.Should().BeEmpty();
    }

    [Fact]
    public void NoCultureIsForced()
    {
        // Nothing in the app sets this; leaving it null is what lets Windows decide.
        Strings.Culture = null;

        Strings.ConvertToMarkdown.Should().NotBeNullOrWhiteSpace();
    }
}
