using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed class SettingFieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }

    public DataTemplate? SecretTemplate { get; set; }

    public DataTemplate? PathTemplate { get; set; }

    public DataTemplate? DropdownTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not SettingFieldViewModel field)
        {
            return base.SelectTemplate(item, container);
        }

        if (field.IsSecret)
        {
            return SecretTemplate;
        }

        if (field.IsFolderPath || field.IsFilePath)
        {
            return PathTemplate;
        }

        if (field.IsDropdown)
        {
            return DropdownTemplate;
        }

        return TextTemplate;
    }
}
