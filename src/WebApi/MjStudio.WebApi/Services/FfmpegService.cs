using System.Diagnostics;
using MjStudio.Application.Shared.Services;

namespace MjStudio.WebApi.Services
{
    /// <summary>
    /// FFmpeg 封装：视频拼接（concat）、音视频拆分、音视频合成。
    /// FFmpeg 路径优先取设置 FfmpegPath，未配置则从系统 PATH 查找 ffmpeg。
    /// </summary>
    public class FfmpegService
    {
        private readonly ISettingsService _settings;

        public FfmpegService(ISettingsService settings)
        {
            _settings = settings;
        }

        /// <summary>解析 FFmpeg 可执行文件路径（设置优先，回退 PATH）</summary>
        public string ResolveFfmpeg()
        {
            var configured = _settings.Get().FfmpegPath;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(configured)) return configured;
                throw new InvalidOperationException($"FFmpeg 路径不存在: {configured}");
            }
            // 回退：从 PATH 查找
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
                var candidateNoExt = Path.Combine(dir.Trim(), "ffmpeg");
                if (File.Exists(candidateNoExt)) return candidateNoExt;
            }
            throw new InvalidOperationException("未找到 FFmpeg：请在设置中配置 FFmpeg 路径，或将 ffmpeg 加入系统 PATH。");
        }

        /// <summary>
        /// 按顺序拼接多个视频（concat demuxer，要求编码参数一致）。
        /// </summary>
        /// <param name="inputPaths">输入视频绝对路径列表（按顺序）</param>
        /// <param name="outputPath">输出视频绝对路径</param>
        public void ConcatVideos(List<string> inputPaths, string outputPath)
        {
            if (inputPaths.Count == 0)
                throw new InvalidOperationException("没有可拼接的视频");

            // 生成 concat 列表文件
            var listFile = Path.Combine(Path.GetTempPath(), $"mjstudio_concat_{Guid.NewGuid():N}.txt");
            var lines = inputPaths.Select(p => $"file '{p.Replace('\\', '/')}'");
            File.WriteAllLines(listFile, lines);

            try
            {
                RunFfmpeg($"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{outputPath}\"");
            }
            finally
            {
                if (File.Exists(listFile)) File.Delete(listFile);
            }
        }

        /// <summary>
        /// 从视频中提取音频（wav）。
        /// </summary>
        public void ExtractAudio(string videoPath, string audioPath)
        {
            RunFfmpeg($"-y -i \"{videoPath}\" -vn -acodec pcm_s16le \"{audioPath}\"");
        }

        /// <summary>
        /// 从视频中提取无声视频流（保留视频，去掉音频）。
        /// </summary>
        public void ExtractVideoOnly(string videoPath, string videoOnlyPath)
        {
            RunFfmpeg($"-y -i \"{videoPath}\" -an -c:v copy \"{videoOnlyPath}\"");
        }

        /// <summary>
        /// 将无声视频与新音频合成为新视频。
        /// </summary>
        public void MergeVideoAudio(string videoOnlyPath, string audioPath, string outputPath)
        {
            RunFfmpeg($"-y -i \"{videoOnlyPath}\" -i \"{audioPath}\" -c:v copy -c:a aac -shortest \"{outputPath}\"");
        }

        /// <summary>
        /// 执行 FFmpeg 命令，失败时抛出含 stderr 的异常。
        /// </summary>
        private void RunFfmpeg(string args)
        {
            var ffmpeg = ResolveFfmpeg();
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 FFmpeg 进程");
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg 执行失败 (exit {proc.ExitCode}): {Tail(stderr, 2000)}");
        }

        private static string Tail(string s, int max)
            => s.Length <= max ? s : s[^max..];
    }
}
