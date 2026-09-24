namespace MjStudio.Application.Shared.Services
{
    /// <summary>
    /// 当前项目上下文（UI/CLI 启动时选定项目后写入，服务据此解析对应项目的数据库）。
    /// </summary>
    public class CurrentProject
    {
        /// <summary>当前项目名（null 表示未加载）</summary>
        public string? Name { get; set; }

        /// <summary>是否已加载项目</summary>
        public bool IsLoaded => !string.IsNullOrEmpty(Name);

        /// <summary>切换当前项目</summary>
        public void Set(string name) => Name = name;

        /// <summary>清空</summary>
        public void Clear() => Name = null;

        /// <summary>取当前项目名，未加载则抛异常</summary>
        public string Require()
            => Name ?? throw new InvalidOperationException("尚未加载项目，请先选择/创建项目。");
    }
}
