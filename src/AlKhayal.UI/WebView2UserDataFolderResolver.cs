using System;
using System.IO;

namespace AlKhayal.UI
{
    internal static class WebView2UserDataFolderResolver
    {
        internal static string Resolve(string configuredPath, string localApplicationDataPath)
        {
            if (string.IsNullOrWhiteSpace(localApplicationDataPath))
            {
                throw new ArgumentException("A local application data path is required.", nameof(localApplicationDataPath));
            }

            var defaultPath = Path.GetFullPath(Path.Combine(localApplicationDataPath, "AlKhayal", "WebView2"));
            if (configuredPath == null)
            {
                return defaultPath;
            }

            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new ArgumentException("The configured WebView2 user-data folder cannot be empty.", nameof(configuredPath));
            }

            if (!Path.IsPathRooted(configuredPath))
            {
                throw new ArgumentException("The configured WebView2 user-data folder must be an absolute path.", nameof(configuredPath));
            }

            var resolvedPath = Path.GetFullPath(configuredPath);
            if (PathsOverlap(resolvedPath, defaultPath))
            {
                throw new ArgumentException("The configured WebView2 user-data folder must be isolated from the default AlKhayal profile.", nameof(configuredPath));
            }

            return resolvedPath;
        }

        internal static void EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        private static bool PathsOverlap(string firstPath, string secondPath)
        {
            var first = NormalizeForComparison(firstPath);
            var second = NormalizeForComparison(secondPath);
            return IsSameOrDescendant(first, second) || IsSameOrDescendant(second, first);
        }

        private static bool IsSameOrDescendant(string path, string possibleParent)
        {
            if (string.Equals(path, possibleParent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var parentPrefix = possibleParent.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? possibleParent
                : possibleParent + Path.DirectorySeparatorChar;
            return path.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeForComparison(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
