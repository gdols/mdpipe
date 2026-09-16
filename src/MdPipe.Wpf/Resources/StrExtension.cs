using System.Windows.Markup;

namespace MdPipe.Wpf.Resources;

/// <summary>
/// Looks interface text up by name so XAML can say <c>{res:Str DropTitle}</c>. The generated
/// Strings class is internal and XAML cannot reach it. A missing key shows as the key itself.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension() { }

    public StrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Strings.ResourceManager.GetString(Key, Strings.Culture) ?? Key;
}
