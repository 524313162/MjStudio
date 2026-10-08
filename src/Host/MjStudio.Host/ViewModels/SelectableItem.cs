using System.ComponentModel;

namespace MjStudio.Host.ViewModels;

/// <summary>
/// 多选下拉里的单个可选项（带选中状态），用于目标平台 / 题材。
/// </summary>
public class SelectableItem : INotifyPropertyChanged
{
    public string Text { get; init; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
