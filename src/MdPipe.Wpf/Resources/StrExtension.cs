using System.Windows.Markup;

namespace MdPipe.Wpf.Resources;

/// <summary>
/// Looks a piece of interface text up by name, so XAML can say <c>{res:Str DropTitle}</c> instead of
/// carrying the English in the markup.
/// </summary>
/// <remarks>
/// The generated <c>Strings</c> class is internal, which XAML can't reach directly, and mirroring
/// sixty properties into a public wrapper would be worse than this. A missing key shows as the key
/// itself: visible in the window, which is exactly where a typo should show up.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension() { }

    public StrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Strings.ResourceManager.GetString(Key, Strings.Culture) ?? Key;
}
