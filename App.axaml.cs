using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VisualDataDiff.Services;
using VisualDataDiff.ViewModels;
using VisualDataDiff.Views;

namespace VisualDataDiff;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var sourceFactory = new TabularDataSourceFactory();
            var diffEngine = new DataDiffEngine();
            var searchEngine = new SearchEngine();
            var columnMatcher = new ColumnMatcher();
            Window? mainWindow = null;
            var filePickerService = new AvaloniaFilePickerService(() => mainWindow);

            var viewModel = new MainWindowViewModel(sourceFactory, diffEngine, filePickerService, searchEngine, columnMatcher);
            mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}