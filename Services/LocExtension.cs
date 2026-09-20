using WpfBinding = System.Windows.Data.Binding;
using WpfBindingMode = System.Windows.Data.BindingMode;
using System.Windows.Markup;

namespace NODR.Services;

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new WpfBinding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = WpfBindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
