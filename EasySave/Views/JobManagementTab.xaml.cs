using System.Windows.Controls;
using System.Windows.Input;
using EasySave.ViewModels;

namespace Graphic.Views
{
    public partial class JobManagementTab : UserControl
    {
        public JobManagementTab()
        {
            InitializeComponent();
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.Item is JobViewModel job)
            {
                job.IsSelected = !job.IsSelected;

                e.Handled = true;
            }
        }
    }
}