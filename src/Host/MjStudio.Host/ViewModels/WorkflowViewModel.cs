using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using MjStudio.WebApi.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 工作流页 ViewModel：上传/删除/编辑工作流模板，点击左侧工作流在右侧显示 JSON，
    /// 可保存修改，也可直接发起请求并显示 ComfyUI 返回的 JSON。
    /// </summary>
    public class WorkflowViewModel : ViewModelBase
    {
        private readonly IServiceProvider _services;
        private readonly WorkflowEngine _engine;

        public ObservableCollection<string> Workflows { get; } = new();

        private string? _selectedWorkflow;
        public string? SelectedWorkflow
        {
            get => _selectedWorkflow;
            set
            {
                if (SetProperty(ref _selectedWorkflow, value))
                    LoadSelected();
            }
        }

        // ===== 右侧 JSON 编辑区 =====
        private string _editorJson = "";
        public string EditorJson { get => _editorJson; set => SetProperty(ref _editorJson, value); }

        private bool _isDirty;
        public bool IsDirty { get => _isDirty; set => SetProperty(ref _isDirty, value); }

        private string _statusText = "";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        // ===== 发起请求 =====
        private bool _isSubmitting;
        public bool IsSubmitting { get => _isSubmitting; set => SetProperty(ref _isSubmitting, value); }

        private string _resultJson = "";
        public string ResultJson { get => _resultJson; set => SetProperty(ref _resultJson, value); }

        private string _resultStatus = "";
        public string ResultStatus { get => _resultStatus; set => SetProperty(ref _resultStatus, value); }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand UploadCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand RunCommand { get; }

        public WorkflowViewModel(IServiceProvider services)
        {
            _services = services;
            _engine = services.GetRequiredService<WorkflowEngine>();

            RefreshCommand = new RelayCommand(Refresh);
            UploadCommand = new RelayCommand(Upload);
            DeleteCommand = new RelayCommand(Delete);
            SaveCommand = new RelayCommand(Save);
            RunCommand = new RelayCommand(Run);

            // 编辑内容变化 → 标记未保存
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EditorJson))
                    IsDirty = true;
            };

            Refresh();
        }

        /// <summary>刷新工作流列表（保留当前选中项）</summary>
        private void Refresh()
        {
            var prev = SelectedWorkflow;
            Workflows.Clear();
            try
            {
                foreach (var w in _engine.ListWorkflows()) Workflows.Add(w);
            }
            catch { /* 忽略 */ }

            if (prev is not null && Workflows.Contains(prev))
                SelectedWorkflow = prev;
            else if (Workflows.Count > 0)
                SelectedWorkflow = Workflows[0];
            else
            {
                SelectedWorkflow = null;
                EditorJson = "";
                IsDirty = false;
                ResultJson = "";
                ResultStatus = "";
                StatusText = "暂无工作流，点击「上传工作流」添加";
            }
        }

        /// <summary>选中工作流 → 加载 JSON 到编辑器</summary>
        private void LoadSelected()
        {
            if (string.IsNullOrEmpty(SelectedWorkflow)) return;
            try
            {
                var content = _engine.ReadWorkflow(SelectedWorkflow);
                EditorJson = PrettyJson(content);
                IsDirty = false;
                ResultJson = "";
                ResultStatus = "";
                StatusText = $"已加载：{SelectedWorkflow}";
            }
            catch (Exception ex)
            {
                StatusText = "加载失败：" + ex.Message;
            }
        }

        /// <summary>上传工作流（选择 .json 文件）</summary>
        private void Upload()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择工作流 JSON 文件",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var content = System.IO.File.ReadAllText(dlg.FileName);
                var name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                _engine.SaveWorkflow(name, content);
                Refresh();
                SelectedWorkflow = name;
                StatusText = $"已上传：{name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("上传失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>删除当前工作流</summary>
        private void Delete()
        {
            if (string.IsNullOrEmpty(SelectedWorkflow)) return;
            var confirm = MessageBox.Show(
                $"确定删除工作流「{SelectedWorkflow}」吗？",
                "MjStudio", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                _engine.DeleteWorkflow(SelectedWorkflow);
                StatusText = $"已删除：{SelectedWorkflow}";
                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>保存当前 JSON 到工作流文件</summary>
        private void Save()
        {
            if (string.IsNullOrEmpty(SelectedWorkflow))
            {
                MessageBox.Show("请先选择工作流", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var normalized = _engine.SaveWorkflow(SelectedWorkflow, EditorJson);
                EditorJson = normalized;
                IsDirty = false;
                StatusText = $"已保存：{SelectedWorkflow}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>发起请求：直接提交整个 JSON 内容，显示返回的 JSON</summary>
        private async void Run()
        {
            if (string.IsNullOrEmpty(SelectedWorkflow))
            {
                MessageBox.Show("请先选择工作流", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(EditorJson))
            {
                MessageBox.Show("工作流 JSON 内容为空", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 校验 JSON 合法性
            try
            {
                using var doc = JsonDocument.Parse(EditorJson);
            }
            catch (Exception ex)
            {
                MessageBox.Show("JSON 格式错误：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsSubmitting = true;
            ResultStatus = "提交中…";
            ResultJson = "";
            try
            {
                var result = await _engine.SubmitRawAsync(EditorJson);
                ResultJson = result?.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }) ?? "(空响应)";
                ResultStatus = "已返回";
                StatusText = $"请求完成：{SelectedWorkflow}";
            }
            catch (Exception ex)
            {
                ResultStatus = "失败";
                ResultJson = ex.Message;
                StatusText = "请求失败：" + ex.Message;
            }
            finally
            {
                IsSubmitting = false;
            }
        }

        /// <summary>JSON 美化（失败则原样返回）</summary>
        private static string PrettyJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch
            {
                return json;
            }
        }
    }
}

