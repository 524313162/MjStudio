using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using MjStudio.WebApi.Dtos;

namespace MjStudio.WebApi.Services
{
    /// <summary>
    /// 批量生成服务：按模块（image/audio/video）列出项目内全部资产/分镜及其状态，
    /// 支持「全部生成（跳过已存在）」「单项生成」「单项清理」。
    /// 供 WPF 弹窗（共享 DI 直接调用）与 <see cref="Controllers.BatchGenController"/>（API）共用。
    /// </summary>
    public class BatchGenService
    {
        private readonly IAssetService _assets;
        private readonly IShotService _shots;
        private readonly IResourceService _resources;
        private readonly GenerationEngine _engine;

        public BatchGenService(
            IAssetService assets,
            IShotService shots,
            IResourceService resources,
            GenerationEngine engine)
        {
            _assets = assets;
            _shots = shots;
            _resources = resources;
            _engine = engine;
        }

        /// <summary>模块 → 资产类型映射（video 模块走分镜，不在此表）</summary>
        public static readonly Dictionary<string, AssetTypeEnum[]> ModuleAssetTypes = new()
        {
            ["image"] = new[] { AssetTypeEnum.Character, AssetTypeEnum.Scene, AssetTypeEnum.Prop },
            ["audio"] = new[] { AssetTypeEnum.Bgm, AssetTypeEnum.Music, AssetTypeEnum.SoundEffect, AssetTypeEnum.Voice }
        };

        /// <summary>列出某模块下当前项目用到的全部资产/分镜（含是否已有内容）</summary>
        public async Task<List<BatchGenItem>> GetItemsAsync(string moduleType)
        {
            var pid = await _resources.GetProjectIdAsync();
            var list = new List<BatchGenItem>();

            if (moduleType == "video")
            {
                var episodes = await _shots.GetEpisodesAsync(pid);
                foreach (var ep in episodes.OrderBy(e => e.EpisodeNo))
                {
                    var shots = await _shots.GetShotsAsync(ep.Id);
                    foreach (var s in shots.OrderBy(x => x.ShotNo))
                    {
                        // GetShotAsync 会加载 AssetRefs→Asset→Resource 与 VideoResource
                        var full = await _shots.GetShotAsync(s.Id);
                        var refImages = full?.AssetRefs
                            .Where(r => r.RefType is not "bgm" and not "sfx")
                            .OrderBy(r => r.MediaIndex)
                            .Select(r => r.Asset?.Resource?.RelativePath)
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Select(p => p!)
                            .ToList() ?? new List<string>();

                        list.Add(new BatchGenItem
                        {
                            Id = s.Id,
                            Name = s.Title ?? "",
                            TypeDisplay = $"镜头#{s.ShotNo}",
                            Description = s.VideoContent,
                            ModuleType = "video",
                            ShotNo = s.ShotNo,
                            EpisodeNo = ep.EpisodeNo,
                            HasContent = s.VideoResourceId is not null,
                            Prompt = s.VideoPromptCn ?? s.VideoContent,
                            NegativePrompt = s.VideoNegativePromptEn,
                            Duration = s.Duration,
                            ReferenceImagePaths = refImages,
                            RelativePath = full?.VideoResource?.RelativePath
                        });
                    }
                }
                return list;
            }

            if (!ModuleAssetTypes.TryGetValue(moduleType, out var types))
                throw new InvalidOperationException($"未知模块: {moduleType}");

            // 收集本项目相关资产（按 Id 去重）：
            //  1) 项目引用的资产（AssetProjectRefs）
            //  2) 分镜绑定的资产（ShotAssetRefs）—— 修复：原实现只取项目引用，
            //     导致「分镜下绑定的资产」在图片/声音页不显示（弹窗为空）
            var assetMap = new Dictionary<long, Asset>();

            foreach (var a in await _assets.GetByProjectAsync(pid))
                assetMap.TryAdd(a.Id, a);

            var shotEps = await _shots.GetEpisodesAsync(pid);
            var shotAssetIds = new HashSet<long>();
            foreach (var ep in shotEps)
            {
                foreach (var s in await _shots.GetShotsAsync(ep.Id))
                    foreach (var r in s.AssetRefs)
                        if (r.Asset is not null) shotAssetIds.Add(r.Asset.Id);
            }
            foreach (var aid in shotAssetIds)
            {
                var a = await _assets.GetByIdAsync(aid);
                if (a is not null) assetMap.TryAdd(a.Id, a);
            }

            foreach (var a in assetMap.Values.Where(a => types.Contains(a.AssetType))
                         .OrderBy(a => a.AssetType).ThenBy(a => a.Order).ThenBy(a => a.Name))
            {
                list.Add(new BatchGenItem
                {
                    Id = a.Id,
                    Name = a.Name ?? "",
                    TypeDisplay = TypeDisplay(a.AssetType),
                    Description = a.Description,
                    ModuleType = moduleType,
                    AssetType = a.AssetType,
                    HasContent = a.ResourceId is not null && a.Resource is not null,
                    Prompt = a.Prompt,
                    NegativePrompt = a.NegativePrompt,
                    RelativePath = a.Resource?.RelativePath
                });
            }
            return list;
        }

        /// <summary>资产类型 → 中文显示</summary>
        public static string TypeDisplay(AssetTypeEnum t) => t switch
        {
            AssetTypeEnum.Character => "角色",
            AssetTypeEnum.Scene => "场景",
            AssetTypeEnum.Prop => "道具",
            AssetTypeEnum.Bgm => "BGM",
            AssetTypeEnum.Music => "音乐",
            AssetTypeEnum.SoundEffect => "音效",
            AssetTypeEnum.Voice => "声线",
            _ => t.ToString()
        };

        /// <summary>为单项构建生成请求（返回引擎类型 + 请求体）</summary>
        public (string Type, object Request) BuildRequest(BatchGenItem item)
        {
            switch (item.ModuleType)
            {
                case "image":
                    var imgType = item.AssetType switch
                    {
                        AssetTypeEnum.Character => "character",
                        AssetTypeEnum.Scene => "scene",
                        _ => "prop"
                    };
                    return (imgType, new ImageGenRequest
                    {
                        Prompt = item.Prompt ?? "",
                        NegativePrompt = item.NegativePrompt,
                        FileName = $"gen_{imgType}_{item.Id}.png"
                    });

                case "audio":
                    var audioType = item.AssetType == AssetTypeEnum.SoundEffect ? "sfx" : "bgm";
                    return (audioType, new AudioGenRequest
                    {
                        Prompt = item.Prompt ?? "",
                        Duration = 150,
                        FileName = $"gen_{audioType}_{item.Id}.mp3"
                    });

                case "video":
                    return ("video", new VideoGenRequest
                    {
                        Prompt = item.Prompt ?? "",
                        Images = item.ReferenceImagePaths,
                        Seconds = (int)(item.Duration ?? 10),
                        FileName = $"gen_video_{item.Id}.mp4"
                    });

                default:
                    throw new InvalidOperationException($"未知模块: {item.ModuleType}");
            }
        }

        /// <summary>提交单项生成任务（立即返回 taskId，后台异步执行）</summary>
        public GenerationTask SubmitForItem(BatchGenItem item)
        {
            var (type, request) = BuildRequest(item);
            return _engine.Submit(type, request);
        }

        /// <summary>任务完成后，把产物资源挂载到对应资产/分镜</summary>
        public async Task AttachResultAsync(BatchGenItem item, GenerationTask task)
        {
            if (string.IsNullOrEmpty(task.RelativePath)) return;
            switch (item.ModuleType)
            {
                case "image":
                    await _assets.AttachResourceAsync(item.Id, task.RelativePath,
                        task.MediaType ?? "image/png", ResourcePurposeEnum.AssetImage);
                    break;
                case "audio":
                    await _assets.AttachResourceAsync(item.Id, task.RelativePath,
                        task.MediaType ?? "audio/mpeg", ResourcePurposeEnum.AssetAudio);
                    break;
                case "video":
                    await _shots.AttachVideoAsync(item.Id, task.RelativePath, ResourcePurposeEnum.ShotVideo);
                    break;
            }
        }

        /// <summary>清理单项内容（删文件 + 清资源记录 + 重置为未生成状态）</summary>
        public async Task ClearAsync(BatchGenItem item)
        {
            switch (item.ModuleType)
            {
                case "image":
                case "audio":
                    await _assets.ClearResourceAsync(item.Id);
                    break;
                case "video":
                    await _shots.ClearVideoAsync(item.Id);
                    break;
                default:
                    throw new InvalidOperationException($"未知模块: {item.ModuleType}");
            }
        }
    }
}
