using System.Collections.ObjectModel;
using System.Windows;
using MjStudio.Domain.Models;

namespace MjStudio.Host.Views
{
    /// <summary>
    /// 选择角色声线对话框：从声线资产中单选一个，确定后返回选中的资产（含参考音频资源）。
    /// </summary>
    public partial class VoiceSelectDialog : Window
    {
        public ObservableCollection<Asset> Voices { get; }

        private Asset? _selectedVoice;
        public Asset? SelectedVoice
        {
            get => _selectedVoice;
            set => _selectedVoice = value;
        }

        public VoiceSelectDialog(IEnumerable<Asset> voices)
        {
            InitializeComponent();
            Voices = new ObservableCollection<Asset>(voices);
            DataContext = this;
            if (Voices.Count > 0) SelectedVoice = Voices[0];
        }

        private void OnOkClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (SelectedVoice is null)
            {
                System.Windows.MessageBox.Show("请选择一个声线", "MjStudio",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e)
            => DialogResult = false;
    }
}
