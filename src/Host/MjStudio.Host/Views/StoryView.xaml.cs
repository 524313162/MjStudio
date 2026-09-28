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

        /// <summary>角色区鼠标滚轮 → 横向滚动（滚轮是垂直方向，手动转成水平偏移）</summary>
        private void OnCharacterScrollWheel(object sender, MouseWheelEventArgs e)
        {
            if (CharacterScroll.ScrollableWidth <= 0) return;
            CharacterScroll.ScrollToHorizontalOffset(CharacterScroll.HorizontalOffset - e.Delta * 40);
            e.Handled = true;
        }
    }
}
