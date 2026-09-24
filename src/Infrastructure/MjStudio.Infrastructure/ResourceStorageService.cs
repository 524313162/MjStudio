using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;

namespace MjStudio.Infrastructure
{
    /// <summary>
    /// 资源存储服务：把二进制文件落盘到资源目录，并生成 Resource 记录。
    /// 数据库只存相对路径与元数据；RelativePath 一律相对「资源目录」（images/xxx.png），
    /// 资源目录默认 &lt;项目&gt;\resources，也可由全局设置 ResourcesRoot 统一存放。
    /// 兼容旧数据：旧库 RelativePath 相对项目根（resources/xxx.png），读取时自动回退。
    /// </summary>
    public class ResourceStorageService
    {
        private readonly ProjectStorageManager _storage;

        public ResourceStorageService(ProjectStorageManager storage) => _storage = storage;

        /// <summary>
        /// 保存资源文件到资源目录，返回相对资源目录的路径与元数据。
        /// </summary>
        /// <param name="projectName">项目名</param>
        /// <param name="bytes">文件字节</param>
        /// <param name="subDir">resources 下子目录（images/audio/video/voice/final）</param>
        /// <param name="fileName">文件名（含扩展名）</param>
        /// <param name="mediaType">媒体类型</param>
        /// <param name="purpose">用途</param>
        public (string RelativePath, string AbsolutePath, long Size) Save(
            string projectName, byte[] bytes, string subDir, string fileName,
            string mediaType, ResourcePurposeEnum purpose)
        {
            var resourcesDir = _storage.GetResourcesDir(projectName);
            var targetDir = Path.Combine(resourcesDir, subDir);
            Directory.CreateDirectory(targetDir);

            // 避免重名覆盖：若已存在则加时间戳
            var safeName = Sanitize(fileName);
            var absPath = Path.Combine(targetDir, safeName);
            if (File.Exists(absPath))
            {
                var ext = Path.GetExtension(safeName);
                var baseName = Path.GetFileNameWithoutExtension(safeName);
                safeName = $"{baseName}_{DateTime.Now:HHmmssfff}{ext}";
                absPath = Path.Combine(targetDir, safeName);
            }

            File.WriteAllBytes(absPath, bytes);

            var relativePath = Path.GetRelativePath(resourcesDir, absPath).Replace('\\', '/');
            return (relativePath, absPath, bytes.Length);
        }

        /// <summary>
        /// 读取资源文件字节（按相对路径）
        /// </summary>
        public byte[] Read(string projectName, string relativePath)
        {
            return File.ReadAllBytes(GetAbsolutePath(projectName, relativePath));
        }

        /// <summary>
        /// 获取资源绝对路径（按资源目录解析；回退旧版项目文件夹以兼容旧数据）
        /// </summary>
        public string GetAbsolutePath(string projectName, string relativePath)
        {
            var norm = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var byResources = Path.GetFullPath(Path.Combine(_storage.GetResourcesDir(projectName), norm));
            if (File.Exists(byResources)) return byResources;
            // 旧数据：资源在旧版项目文件夹内（&lt;项目&gt;\resources\xxx）
            return Path.GetFullPath(Path.Combine(_storage.GetLegacyProjectDir(projectName), norm));
        }

        /// <summary>
        /// 删除资源文件
        /// </summary>
        public void Delete(string projectName, string relativePath)
        {
            var absPath = GetAbsolutePath(projectName, relativePath);
            if (File.Exists(absPath)) File.Delete(absPath);
        }

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }
    }
}
