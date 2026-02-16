using System;
using System.Collections.Generic;
using System.Linq;

namespace WindBoard.Updates
{
    /// <summary>
    /// 从 latest.json 的 assets 中为“当前架构 + 安装形态”选择推荐下载项。
    /// </summary>
    internal static class UpdateAssetSelector
    {
        internal static UpdateAssetRecommendation Select(
            IReadOnlyList<LatestReleaseAsset> assets,
            string? currentArch,
            AppInstallProbeResult install)
        {
            string arch = (currentArch ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(arch))
            {
                arch = "x64";
            }

            List<UpdateAssetPick> picks = assets
                .Where(a => a is not null && string.Equals((a.Arch ?? string.Empty).Trim(), arch, StringComparison.OrdinalIgnoreCase))
                .Select(a => new UpdateAssetPick(a, Classify(a)))
                .Where(p => p.Kind != UpdateAssetKind.Unknown)
                .ToList();

            if (picks.Count == 0)
            {
                return new UpdateAssetRecommendation
                {
                    Recommended = null,
                    Alternatives = Array.Empty<UpdateAssetPick>(),
                    Reason = $"no assets for arch '{arch}'",
                };
            }

            UpdateAssetKind preferred = GetPreferredKind(install);

            UpdateAssetPick? recommended = preferred == UpdateAssetKind.Unknown
                ? null
                : picks.FirstOrDefault(p => p.Kind == preferred);

            IReadOnlyList<UpdateAssetPick> alternatives = SortAlternatives(picks, install);

            return new UpdateAssetRecommendation
            {
                Recommended = recommended,
                Alternatives = alternatives,
                Reason = recommended is null
                    ? $"no recommended kind (install={install.Kind}/{install.Variant}, arch={arch})"
                    : $"recommended={recommended.Kind} (install={install.Kind}/{install.Variant}, arch={arch})",
            };
        }

        internal static UpdateAssetKind Classify(LatestReleaseAsset asset)
        {
            if (asset is null)
            {
                return UpdateAssetKind.Unknown;
            }

            string name = asset.FileName ?? string.Empty;
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return UpdateAssetKind.PortableZip;
            }

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                // 约定：安装包 -fd 变体文件名以 -fd.exe 结尾。
                if (name.EndsWith("-fd.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return UpdateAssetKind.InstallerFrameworkDependent;
                }

                return UpdateAssetKind.InstallerSelfContained;
            }

            return UpdateAssetKind.Unknown;
        }

        private static UpdateAssetKind GetPreferredKind(AppInstallProbeResult install)
        {
            if (install is null)
            {
                return UpdateAssetKind.Unknown;
            }

            if (install.Kind == AppInstallKind.Portable)
            {
                return UpdateAssetKind.PortableZip;
            }

            if (install.Kind == AppInstallKind.Installer)
            {
                return install.Variant switch
                {
                    AppInstallVariant.FrameworkDependent => UpdateAssetKind.InstallerFrameworkDependent,
                    AppInstallVariant.SelfContained => UpdateAssetKind.InstallerSelfContained,
                    _ => UpdateAssetKind.Unknown,
                };
            }

            return UpdateAssetKind.Unknown;
        }

        private static IReadOnlyList<UpdateAssetPick> SortAlternatives(
            List<UpdateAssetPick> picks,
            AppInstallProbeResult install)
        {
            // 排序目标：
            // - 若为便携版：zip 在前
            // - 若为安装版：优先列出两类安装包，再列出 zip
            // - 最后做稳定排序：按 Kind + 文件名
            int GetRank(UpdateAssetPick p)
            {
                if (install.Kind == AppInstallKind.Portable)
                {
                    return p.Kind switch
                    {
                        UpdateAssetKind.PortableZip => 0,
                        UpdateAssetKind.InstallerSelfContained => 1,
                        UpdateAssetKind.InstallerFrameworkDependent => 2,
                        _ => 9,
                    };
                }

                if (install.Kind == AppInstallKind.Installer && install.Variant == AppInstallVariant.FrameworkDependent)
                {
                    // 当前为 -fd 安装版：优先把 -fd 安装包放在最前。
                    return p.Kind switch
                    {
                        UpdateAssetKind.InstallerFrameworkDependent => 0,
                        UpdateAssetKind.InstallerSelfContained => 1,
                        UpdateAssetKind.PortableZip => 2,
                        _ => 9,
                    };
                }

                // 默认：自包含安装包最通用，放在最前。
                return p.Kind switch
                {
                    UpdateAssetKind.InstallerSelfContained => 0,
                    UpdateAssetKind.InstallerFrameworkDependent => 1,
                    UpdateAssetKind.PortableZip => 2,
                    _ => 9,
                };
            }

            return picks
                .OrderBy(GetRank)
                .ThenBy(p => p.Kind)
                .ThenBy(p => p.Asset.FileName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    internal sealed class UpdateAssetRecommendation
    {
        internal UpdateAssetPick? Recommended { get; init; }

        internal IReadOnlyList<UpdateAssetPick> Alternatives { get; init; } = Array.Empty<UpdateAssetPick>();

        internal string Reason { get; init; } = string.Empty;
    }

    internal sealed class UpdateAssetPick
    {
        internal LatestReleaseAsset Asset { get; }

        internal UpdateAssetKind Kind { get; }

        internal UpdateAssetPick(LatestReleaseAsset asset, UpdateAssetKind kind)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            Kind = kind;
        }
    }

    internal enum UpdateAssetKind
    {
        Unknown,
        InstallerSelfContained,
        InstallerFrameworkDependent,
        PortableZip,
    }
}
