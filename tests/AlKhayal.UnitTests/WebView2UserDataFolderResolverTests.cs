using System;
using System.IO;
using AlKhayal.UI;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class WebView2UserDataFolderResolverTests
    {
        [Fact]
        public void Missing_override_preserves_the_default_profile_path()
        {
            var resolved = WebView2UserDataFolderResolver.Resolve(null, @"C:\Users\operator\AppData\Local");

            Assert.Equal(
                Path.GetFullPath(@"C:\Users\operator\AppData\Local\AlKhayal\WebView2"),
                resolved);
        }

        [Fact]
        public void Absolute_override_is_canonicalized()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "alkhayal-profile-" + Guid.NewGuid().ToString("N"));
            var configuredPath = Path.Combine(testRoot, "profile", "..");

            var resolved = WebView2UserDataFolderResolver.Resolve(configuredPath, @"C:\Users\operator\AppData\Local");

            Assert.Equal(Path.GetFullPath(testRoot), resolved);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("relative-profile")]
        public void Invalid_override_fails_without_falling_back(string configuredPath)
        {
            Assert.Throws<ArgumentException>(() =>
                WebView2UserDataFolderResolver.Resolve(configuredPath, @"C:\Users\operator\AppData\Local"));
        }

        [Theory]
        [InlineData(@"C:\Users\operator\AppData\Local\AlKhayal\WebView2")]
        [InlineData(@"C:\Users\operator\AppData\Local\AlKhayal\WebView2\acceptance")]
        [InlineData(@"C:\Users\operator\AppData\Local\AlKhayal")]
        public void Override_cannot_overlap_the_default_profile(string configuredPath)
        {
            Assert.Throws<ArgumentException>(() =>
                WebView2UserDataFolderResolver.Resolve(configuredPath, @"C:\Users\operator\AppData\Local"));
        }

        [Fact]
        public void Directory_creation_failure_is_not_suppressed()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "alkhayal-profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            var filePath = Path.Combine(testRoot, "not-a-directory");
            File.WriteAllText(filePath, "test fixture");

            try
            {
                Assert.Throws<IOException>(() => WebView2UserDataFolderResolver.EnsureDirectory(filePath));
            }
            finally
            {
                Directory.Delete(testRoot, true);
            }
        }
    }
}
