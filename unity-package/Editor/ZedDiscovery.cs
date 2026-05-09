using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ZedUnity.Editor
{
    /// <summary>
    /// Discovers Zed Editor installations on the current platform.
    /// </summary>
    internal static class ZedDiscovery
    {
        public const string EditorName = "Zed";

        /// <summary>Returns all detected Zed executable paths on this platform.</summary>
        public static IEnumerable<string> GetInstallationPaths()
        {
#if UNITY_EDITOR_WIN
            return GetWindowsPaths();
#elif UNITY_EDITOR_OSX
            return GetMacOSPaths();
#else
            return GetLinuxPaths();
#endif
        }

        /// <summary>Returns true if the given path points to a Zed executable.</summary>
        public static bool IsZedPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var filename = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return filename == "zed";
        }

        /// <summary>
        /// Builds the CLI argument string to open a file at a specific line/column.
        /// Zed syntax: zed path:line:column
        /// </summary>
        public static string BuildOpenFileArgs(string filePath, int line, int column)
        {
            // Normalize path separators
            filePath = filePath.Replace('\\', '/');

            if (line < 1)
                return Quote(filePath);

            if (column < 1)
                return Quote($"{filePath}:{line}");

            return Quote($"{filePath}:{line}:{column}");
        }

        /// <summary>Builds args to open a project folder (pass as additional workspace).</summary>
        public static string BuildOpenProjectArgs(string projectPath)
        {
            return Quote(projectPath.Replace('\\', '/'));
        }

        private static string Quote(string value)
        {
            // Wrap in double quotes if the path contains spaces
            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

#if UNITY_EDITOR_WIN
        private static IEnumerable<string> GetWindowsPaths()
        {
            var paths = new List<string>();

            // Standard user install via installer
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                // Installed via installer: %LOCALAPPDATA%\Programs\Zed\zed.exe
                paths.Add(Path.Combine(localAppData, "Programs", "Zed", "zed.exe"));

                // Self-updating via bundle: %LOCALAPPDATA%\Zed\bin\zed.exe
                paths.Add(Path.Combine(localAppData, "Zed", "bin", "zed.exe"));
            }

            // Scoop install
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                paths.Add(Path.Combine(userProfile, "scoop", "apps", "zed", "current", "zed.exe"));
                paths.Add(Path.Combine(userProfile, "scoop", "shims", "zed.exe"));
            }

            // PATH fallback - resolved at runtime
            paths.Add("zed");

            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (p == "zed" || File.Exists(p))
                    yield return p;
            }
        }
#elif UNITY_EDITOR_OSX
        private static IEnumerable<string> GetMacOSPaths()
        {
            var paths = new[]
            {
                "/Applications/Zed.app/Contents/MacOS/zed",
                "/Applications/Zed Preview.app/Contents/MacOS/zed",
            };

            foreach (var p in paths)
            {
                if (File.Exists(p))
                    yield return p;
            }

            // Homebrew / PATH
            yield return "zed";
        }
#else
        private static IEnumerable<string> GetLinuxPaths()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var paths = new[]
            {
                Path.Combine(home, ".local", "bin", "zed"),
                "/usr/local/bin/zed",
                "/usr/bin/zed",
                "/snap/bin/zed",
                "zed",
            };

            foreach (var p in paths)
            {
                if (p == "zed" || File.Exists(p))
                    yield return p;
            }
        }
#endif
    }
}
