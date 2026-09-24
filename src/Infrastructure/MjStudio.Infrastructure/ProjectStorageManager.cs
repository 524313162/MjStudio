using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MjStudio.Domain.Shared;

namespace MjStudio.Infrastructure
{
    /// <summary>
    /// 存储管理器：管理单一全局数据库（DbRoot）与资源目录（ResourcesRoot）。
    /// 全局库：&lt;DbRoot&gt;\mjstudio.db（所有项目 + 全部资产/镜头/资源等数据都在其中）；
    /// 媒体资源统一存放：&lt;ResourcesRoot&gt;\&lt;项目名&gt;\{images,audio,video,voice,final}。
    /// 资产与项目为多对多关系（AssetProjectRef），通用资产可被多个项目引用。
    /// 兼容旧版「每项目一个 &lt;项目名&gt;.db」结构：启动时自动合并进全局库。
    /// </summary>
    public class ProjectStorageManager
    {
        private readonly MjStudioOptions _options;

        /// <summary>全局数据库文件名</summary>
        public const string GlobalDbFileName = "mjstudio.db";

        public ProjectStorageManager(MjStudioOptions options) => _options = options;

        /// <summary>数据库目录</summary>
        public string DbRoot => _options.DbRoot;

        /// <summary>资源目录</summary>
        public string ResourcesRoot => _options.ResourcesRoot;

        /// <summary>全局数据库文件路径：&lt;DbRoot&gt;\mjstudio.db</summary>
        public string GetGlobalDbPath()
            => Path.Combine(DbRoot, GlobalDbFileName);

        /// <summary>项目资源目录：&lt;ResourcesRoot&gt;\&lt;项目名&gt;</summary>
        public string GetResourcesDir(string projectName)
            => Path.Combine(ResourcesRoot, Sanitize(projectName));

        /// <summary>旧版项目文件夹路径（&lt;DbRoot&gt;\&lt;项目名&gt;\，用于兼容迁移）</summary>
        public string GetLegacyProjectDir(string projectName)
            => Path.Combine(DbRoot, Sanitize(projectName));

        /// <summary>
        /// 初始化项目资源目录结构（images/audio/video/voice/final）。
        /// 全局库由 DbContextFactory 负责创建。
        /// </summary>
        public void InitializeProject(string projectName)
        {
            var resDir = GetResourcesDir(projectName);
            Directory.CreateDirectory(resDir);
            foreach (var sub in new[] { "images", "audio", "video", "voice", "final" })
                Directory.CreateDirectory(Path.Combine(resDir, sub));
        }

        /// <summary>
        /// 列出所有项目（查询全局库 Projects 表）。
        /// </summary>
        public List<string> ListProjects()
        {
            EnsureGlobalDb();
            try
            {
                using var conn = new SqliteConnection(ConnStr(GetGlobalDbPath()));
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Name FROM Projects ORDER BY Name";
                using var rd = cmd.ExecuteReader();
                var list = new List<string>();
                while (rd.Read()) list.Add(rd.GetString(0));
                return list;
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>项目是否存在（全局库 Projects 表）</summary>
        public bool ProjectExists(string projectName)
        {
            EnsureGlobalDb();
            try
            {
                using var conn = new SqliteConnection(ConnStr(GetGlobalDbPath()));
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM Projects WHERE Name = $n";
                cmd.Parameters.AddWithValue("$n", projectName);
                return (long)cmd.ExecuteScalar()! > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 确保全局库存在（建库 + 建表 + 迁移旧版每项目库）。
        /// 由 DbContextFactory 在创建上下文前调用。
        /// </summary>
        public void EnsureGlobalDb()
        {
            Directory.CreateDirectory(DbRoot);
            var globalPath = GetGlobalDbPath();

            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ProjectDbContext>()
                .UseSqlite(ConnStr(globalPath))
                .Options;
            using (var db = new ProjectDbContext(options))
            {
                db.Database.EnsureCreated();
            }

            // 幂等 schema 升级：EnsureCreated 不会给已存在的库加新列，这里手动补列
            EnsureSchemaUpgrades(globalPath);

            // EF Core 的 Sqlite 有独立连接池（不受连接字符串 Pooling=False 控制），
            // 其池化连接会持有全局库句柄，导致后续原生连接合并/重命名失败。迁移前清空所有池。
            SqliteConnection.ClearAllPools();

            // 幂等迁移：每次启动都尝试合并旧版每项目库（已迁移的已重命名为 .migrated，不再匹配 *.db）
            MigrateLegacyFolders();
            MigrateLegacyDbs();
        }

        /// <summary>
        /// 幂等 schema 升级：给已存在的库补充新增列（EnsureCreated 只建新库）。
        /// 每次启动执行，列已存在则跳过。
        /// </summary>
        private void EnsureSchemaUpgrades(string globalPath)
        {
            try
            {
                using var conn = new SqliteConnection(ConnStr(globalPath));
                conn.Open();
                var cols = GetTableColumns(conn, "Assets");
                // 资产提示词简化：CN/EN 双字段 → 单个正向/反向（Prompt / NegativePrompt）
                if (!cols.Contains("Prompt"))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "ALTER TABLE Assets ADD COLUMN Prompt TEXT NULL";
                    cmd.ExecuteNonQuery();
                }
                if (!cols.Contains("NegativePrompt"))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "ALTER TABLE Assets ADD COLUMN NegativePrompt TEXT NULL";
                    cmd.ExecuteNonQuery();
                }
                if (!cols.Contains("ParentAssetId"))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "ALTER TABLE Assets ADD COLUMN ParentAssetId INTEGER NULL";
                    cmd.ExecuteNonQuery();
                }

                // 项目状态（进行中/已完结）
                var projCols = GetTableColumns(conn, "Projects");
                if (!projCols.Contains("Status"))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "ALTER TABLE Projects ADD COLUMN Status INTEGER NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                }

                // 分镜：BGM/音效播放时间段
                var shotCols = GetTableColumns(conn, "Shots");
                if (!shotCols.Contains("BgmRange"))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "ALTER TABLE Shots ADD COLUMN BgmRange TEXT NULL";
                    cmd.ExecuteNonQuery();
                }
                if (!shotCols.Contains("SfxRange"))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "ALTER TABLE Shots ADD COLUMN SfxRange TEXT NULL";
                    cmd.ExecuteNonQuery();
                }
            }
            catch { /* 表不存在或已升级则忽略 */ }
        }

        /// <summary>SQLite 连接字符串（禁用连接池，避免文件句柄被池化连接占用导致迁移/重命名失败）</summary>
        private static string ConnStr(string path) => $"Data Source={path};Pooling=False";

        /// <summary>读取表列名（PRAGMA table_info）</summary>
        private static List<string> GetTableColumns(SqliteConnection conn, string table)
        {
            var cols = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) cols.Add(rd.GetString(1));
            return cols;
        }

        /// <summary>
        /// 旧版结构迁移：&lt;DbRoot&gt;\&lt;项目名&gt;\mjstudio.db → 合并进全局库（仅 Projects 行）。
        /// </summary>
        private void MigrateLegacyFolders()
        {
            foreach (var dir in Directory.GetDirectories(DbRoot))
            {
                var name = Path.GetFileName(dir);
                var oldDb = Path.Combine(dir, "mjstudio.db");
                if (!File.Exists(oldDb)) continue;
                if (ProjectExistsInGlobalDb(name) || MergeLegacyDb(oldDb))
                {
                    try
                    {
                        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() == false)
                            Directory.Delete(dir);
                    }
                    catch { /* 旧文件夹内有其他文件则保留 */ }
                }
            }
        }

        /// <summary>
        /// 旧版「每项目一个 &lt;项目名&gt;.db」迁移：把 Projects 行合并进全局库。
        /// 合并成功才改名/清理（.db → .migrated 备份；.migrated 直接删除）；失败保留原文件下次重试。
        /// </summary>
        private void MigrateLegacyDbs()
        {
            var files = Directory.GetFiles(DbRoot, "*.db")
                .Concat(Directory.GetFiles(DbRoot, "*.db.migrated"))
                .Where(f => Path.GetFileName(f) != GlobalDbFileName)
                .ToList();
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file).Replace(".migrated", "");
                if (ProjectExistsInGlobalDb(name))
                {
                    // 项目已在全局库中，清理旧文件
                    try { File.Delete(file); } catch { /* 忽略 */ }
                    continue;
                }
                if (!MergeLegacyDb(file)) continue; // 合并失败：保留原文件，下次重试

                try
                {
                    if (file.EndsWith(".migrated"))
                        File.Delete(file);
                    else
                    {
                        var backup = file + ".migrated";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(file, backup);
                    }
                }
                catch { /* 重命名失败则保留，下次重试 */ }
            }
        }

        /// <summary>直接查全局库判断项目是否存在（不触发 EnsureGlobalDb，避免递归）</summary>
        private bool ProjectExistsInGlobalDb(string projectName)
        {
            try
            {
                using var conn = new SqliteConnection(ConnStr(GetGlobalDbPath()));
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM Projects WHERE Name = $n";
                cmd.Parameters.AddWithValue("$n", projectName);
                return (long)cmd.ExecuteScalar()! > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 把旧库中的 Projects 行合并进全局库（ID 冲突时重映射）。
        /// 旧库 schema 可能较旧（缺新列/含已删除列），故只插入新旧表共有的列。
        /// 返回是否合并成功（失败时调用方应保留原文件以便重试）。
        /// </summary>
        private bool MergeLegacyDb(string legacyPath)
        {
            try
            {
                using var src = new SqliteConnection(ConnStr(legacyPath));
                src.Open();

                var srcCols = GetTableColumns(src, "Projects");
                if (srcCols.Count == 0) return true; // 无 Projects 表，视为无需迁移

                using var dst = new SqliteConnection(ConnStr(GetGlobalDbPath()));
                dst.Open();
                var dstCols = GetTableColumns(dst, "Projects");

                using var srcCmd = src.CreateCommand();
                srcCmd.CommandText = "SELECT Id, Name FROM Projects";
                var rows = new List<(long Id, string Name)>();
                using (var rd = srcCmd.ExecuteReader())
                {
                    while (rd.Read()) rows.Add((rd.GetInt64(0), rd.GetString(1)));
                }

                using var tx = dst.BeginTransaction();
                foreach (var (id, name) in rows)
                {
                    using var chk = dst.CreateCommand();
                    chk.Transaction = tx;
                    chk.CommandText = "SELECT COUNT(1) FROM Projects WHERE Name = $n";
                    chk.Parameters.AddWithValue("$n", name);
                    if ((long)chk.ExecuteScalar()! > 0) continue;

                    long newId;
                    using (var idCmd = dst.CreateCommand())
                    {
                        idCmd.Transaction = tx;
                        idCmd.CommandText = "SELECT COALESCE(MAX(Id),0)+1 FROM Projects";
                        newId = (long)idCmd.ExecuteScalar()!;
                    }

                    // 只插入新旧表共有的列（跳过旧库中已删除的列，新库新增列取默认值）
                    var common = srcCols.Where(c => c != "Id" && dstCols.Contains(c)).ToList();
                    using var sel = src.CreateCommand();
                    sel.CommandText = "SELECT " + string.Join(",", common) + " FROM Projects WHERE Id = $id";
                    sel.Parameters.AddWithValue("$id", id);
                    using var srd = sel.ExecuteReader();
                    if (!srd.Read()) continue;
                    var values = new object?[common.Count];
                    for (int i = 0; i < common.Count; i++)
                        values[i] = srd.IsDBNull(i) ? (object?)null : srd.GetValue(i);
                    srd.Close();

                    var sql = $"INSERT INTO Projects (Id, {string.Join(",", common)}) " +
                              $"VALUES ($id, {string.Join(",", common.Select((_, i) => "$v" + i))})";
                    using var ins = dst.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = sql;
                    ins.Parameters.AddWithValue("$id", newId);
                    for (int i = 0; i < values.Length; i++)
                        ins.Parameters.AddWithValue("$v" + i, values[i] ?? DBNull.Value);
                    ins.ExecuteNonQuery();
                }
                tx.Commit();
                try
                {
                    File.AppendAllText(Path.Combine(DbRoot, "migrate.log"),
                        $"\n[{DateTime.Now:O}] MergeLegacyDb({Path.GetFileName(legacyPath)}) OK, rows={rows.Count}");
                }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(DbRoot, "migrate.log"),
                        $"\n[{DateTime.Now:O}] MergeLegacyDb({Path.GetFileName(legacyPath)}) FAILED:\n{ex}");
                }
                catch { }
                return false; // 合并失败：保留原文件，下次启动重试
            }
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
