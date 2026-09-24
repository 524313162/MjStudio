using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MjStudio.Host.Views
{
    public partial class StoryView : UserControl
    {
        public StoryView()
        {
            InitializeComponent();
        }

        /// <summary>搜索框回车 → 触发搜索</summary>
        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is ViewModels.StoryViewModel vm)
                    vm.SearchCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
