using System.Globalization;
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
    public void NoCultureIsForced()
    {
        // Nothing in the app sets this; leaving it null is what lets Windows decide.
        Strings.Culture = null;

        Strings.ConvertToMarkdown.Should().NotBeNullOrWhiteSpace();
    }
}
