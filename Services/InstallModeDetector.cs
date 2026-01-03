using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WindBoard.Services
{
    public enum InstallMode
    {
        Unknown = 0,
        InstallerPerMachine = 1,
        Portable = 2
    }

    public enum DeploymentRuntime
    {
        Unknown = 0,
        SelfContained = 1,
        FrameworkDependent = 2
    }

    public sealed class InstallEnvironment
    {
        public InstallMode InstallMode { get; }

        public DeploymentRuntime DeploymentRuntime { get; }

        public string? ExecutablePath { get; }

        public string? InstallRoot { get; }

        public InstallEnvironment(InstallMode installMode, DeploymentRuntime deploymentRuntime, string? executablePath, string? installRoot)
        {
            InstallMode = installMode;
            DeploymentRuntime = deploymentRuntime;
            ExecutablePath = executablePath;
            InstallRoot = installRoot;
        }
    }

    public static class InstallModeDetector
    {
        private const string UninstallRootPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static InstallEnvironment Detect()
        {
            string? exePath = null;
            try { exePath = Environment.ProcessPath; } catch { }
            exePath ??= TryGetProcessPathFallback();

            string? exeDir = null;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                try { exeDir = Path.GetDirectoryName(exePath); } catch { }
            }

            DeploymentRuntime runtime = DetectDeploymentRuntime(exeDir);
            (InstallMode installMode, string? installRoot) = DetectInstallMode(exePath, exeDir);

            return new InstallEnvironment(installMode, runtime, exePath, installRoot);
        }

        private static DeploymentRuntime DetectDeploymentRuntime(string? exeDir)
        {
            if (string.IsNullOrWhiteSpace(exeDir))
            {
                return DeploymentRuntime.Unknown;
            }

            try
            {
                string coreClr = Path.Combine(exeDir, "coreclr.dll");
                return File.Exists(coreClr) ? DeploymentRuntime.SelfContained : DeploymentRuntime.FrameworkDependent;
            }
            catch
            {
                return DeploymentRuntime.Unknown;
            }
        }

        private static (InstallMode InstallMode, string? InstallRoot) DetectInstallMode(string? exePath, string? exeDir)
        {
            if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(exeDir))
            {
                return (InstallMode.Unknown, null);
            }

            try
            {
                foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    {
                        string? installLocation = TryFindInnoInstallLocation(hive, view, exeDir);
                        if (!string.IsNullOrWhiteSpace(installLocation))
                        {
                            return (InstallMode.InstallerPerMachine, installLocation);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstallMode] Failed to detect install mode from registry: {ex}");
            }

            return (InstallMode.Portable, exeDir);
        }

        private static string? TryFindInnoInstallLocation(RegistryHive hive, RegistryView view, string exeDir)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? uninstallRoot = baseKey.OpenSubKey(UninstallRootPath, writable: false);
                if (uninstallRoot == null)
                {
                    return null;
                }

                foreach (string subKeyName in uninstallRoot.GetSubKeyNames())
                {
                    if (!subKeyName.EndsWith("_is1", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using RegistryKey? appKey = uninstallRoot.OpenSubKey(subKeyName, writable: false);
                    if (appKey == null)
                    {
                        continue;
                    }

                    string? displayName = appKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    if (!displayName.Contains("WindBoard", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? installLocation = appKey.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(installLocation))
                    {
                        continue;
                    }

                    if (IsPathPrefix(exeDir, installLocation))
                    {
                        return installLocation;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static bool IsPathPrefix(string childPath, string parentPath)
        {
            if (string.IsNullOrWhiteSpace(childPath) || string.IsNullOrWhiteSpace(parentPath))
            {
                return false;
            }

            try
            {
                string childFull = Path.GetFullPath(childPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + Path.DirectorySeparatorChar;
                string parentFull = Path.GetFullPath(parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + Path.DirectorySeparatorChar;
                return childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string? TryGetProcessPathFallback()
        {
            try
            {
                using Process p = Process.GetCurrentProcess();
                return p.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }
    }
}

