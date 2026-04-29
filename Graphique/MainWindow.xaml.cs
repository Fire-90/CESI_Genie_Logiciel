using EasySave.Services;
using EasySave.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace Graphique
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Avoid running runtime-only code in the Visual Studio designer
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            // Defer heavy initialization to runtime (after window is created)
            var configManager = new ConfigManager();
            var jobs = configManager.LoadConfig();
            var stateTracker = new StateTracker(jobs);
            var engine = new BackupEngine(stateTracker);

            var mainViewModel = new MainViewModel(configManager, stateTracker, engine);

            this.DataContext = mainViewModel;

            this.Loaded -= MainWindow_Loaded;
        }
    }
}