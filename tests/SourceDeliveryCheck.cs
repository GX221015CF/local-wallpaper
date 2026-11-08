#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

internal static class SourceDeliveryCheck {
    static readonly List<string> checks = new List<string>();
    static void Need(bool condition, string description) {
        if (!condition) throw new InvalidDataException(description);
        checks.Add(description);
    }
    static int Main(string[] args) {
        try {
            Need(LocalImport.Under(Common.Root, Path.Combine(Common.AppRoot, "..", "..")), "explicit-isolated-root");
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lxwp", "data");
            Need(SourceLocation.DefaultRoot == expected, "source-default-follows-current-windows-user");
            Need(VideoPackage.ResourceExtension(VideoPackage.Format) == ".mp4", "known-video-keeps-resource-type");
            Need(VideoCovers.Resolve(new string('a', 64)) == null, "empty-isolated-data-has-no-inherited-cover");
            string fixture = Path.Combine(Common.Root, "synthetic-fixture");
            Directory.CreateDirectory(fixture);
            var method = typeof(MultiWallpaperRegression).GetMethod("Fixture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            string resource = (string)method.Invoke(null, new object[] {fixture, "portable", (byte)5});
            CoverCache.ValidateV2Directory(resource, () => {});
            Need(true, "synthetic-v2-uses-actual-directory-validator");
            Need(File.Exists(PackageAnalysis.RulesPath) && LocalImport.Under(PackageAnalysis.RulesPath, Common.AppRoot),
                "structure-rules-resolve-within-isolated-copy");
            var library = new WallpaperLibrary();
            library.Doc.Items.Clear();
            library.Save();
            bool duplicate;
            var item = library.Add(resource, out duplicate);
            Need(!duplicate && library.Doc.Items.Count == 1, "synthetic-resource-registered-in-isolated-library");
            Need(item.PreviewConfirmedSha256 == null && item.DesktopConfirmedSha256 == null, "registration-does-not-create-playback-confirmation");
            Common.SaveNew(args[0], new {passed=true, checks=checks, evidenceKind="TEST_BUILD portable source assembly and synthetic CWP handler; no rendering claim", windowCreated=false, engineLoaded=false});
            return 0;
        } catch (Exception error) {
            Common.SaveNew(args[0], new {passed=false, checks=checks, error=error.ToString()});
            return 1;
        }
    }
}
#endif
