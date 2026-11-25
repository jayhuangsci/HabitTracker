using System.Windows;
using PromptCrew.Desktop.ViewModels;

namespace PromptCrew.Desktop;
public partial class CatalogWindow : Window
{
    public CatalogWindow(CatalogEditor model)
    {
        InitializeComponent(); DataContext = model;
        model.Saved += () => { DialogResult = true; };
    }
}
