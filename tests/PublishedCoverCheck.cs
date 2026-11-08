using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Web.Script.Serialization;

internal static class PublishedCoverCheck {
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Assembly assembly;
    static readonly List<string> checks = new List<string>();

    static object Invoke(string type, object instance, string method, params object[] args) {
        try {
            return assembly.GetType(type, true).GetMethods(Flags)
                .Single(m => m.Name == method && m.GetParameters().Length == args.Length)
                .Invoke(instance, args);
        } catch (TargetInvocationException error) {
            throw error.InnerException;
        }
    }

    static object Property(object value, string name) {
        return value.GetType().GetProperty(name, Flags).GetValue(value, null);
    }

    static void Need(bool value, string name) {
        if (!value) throw new InvalidDataException(name);
        checks.Add(name);
    }

    static int Main(string[] args) {
        int exit = 1;
        var serializer = new JavaScriptSerializer();
        try {
            assembly = Assembly.LoadFrom(args[0]);
            string data = Path.GetFullPath(args[1]);
            if (Directory.Exists(data)) throw new IOException("New explicit isolated data root required");
            Directory.CreateDirectory(data);
            var common = assembly.GetType("Common", true);
            common.GetField("Root", Flags).SetValue(null, data);
            common.GetField("AppRoot", Flags).SetValue(null, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[0]), "..")));
            string source = Path.Combine(data, "video.mp4");
            File.Copy(args[2], source, false);
            string hash = (string)Invoke("Common", null, "Hash", source);
            string token = Guid.NewGuid().ToString("N");
            string run = Path.Combine((string)common.GetProperty("LogRoot", Flags).GetValue(null, null), "run-capture-check");
            Directory.CreateDirectory(run);
            Invoke("Common", null, "SaveNew", Path.Combine(run, "request.json"),
                new {mode = "preview", runToken = token, expectedSha256 = hash, selectedCatalogId = "video", cwp = source});
            object lease = Invoke("RuntimeInputs", null, "Begin", run, source, hash, CancellationToken.None);
            using ((IDisposable)lease) {
                Invoke("RuntimeInputLease", lease, "Copy", source, CancellationToken.None);
                string input = (string)lease.GetType().GetField("Path", Flags).GetValue(lease);
                Invoke("Common", null, "SaveNew", Path.Combine(run, "load-input.json"),
                    new {sourceSha256 = hash, runtimeSha256 = hash, runtimeCwp = input});
                object target = Activator.CreateInstance(assembly.GetType("PreviewFrameTarget", true), true);
                var fields = new Dictionary<string, object> {
                    {"Run", run}, {"Token", token}, {"ResourceSha256", hash},
                    {"CatalogId", "video"}, {"RuntimeCwp", input}
                };
                foreach (var pair in fields) target.GetType().GetProperty(pair.Key).SetValue(target, pair.Value, null);
                Invoke("PreviewFrame", null, "ValidateTarget", target);
                Need(true, "published-capture-validator-accepts-owned-input");
                string image = Path.Combine(data, "synthetic.jpg");
                using (var bitmap = new Bitmap(120, 80)) {
                    using (var graphics = Graphics.FromImage(bitmap)) {
                        graphics.Clear(Color.SteelBlue);
                        graphics.FillEllipse(Brushes.Yellow, 10, 10, 60, 60);
                    }
                    bitmap.Save(image, ImageFormat.Jpeg);
                }
                object binding = Invoke("PreviewCovers", null, "Publish", image, target,
                    Invoke("Common", null, "Hash", image), 120, 80, "synthetic-published-processor-check",
                    "isolated-headless-helper", false, (Action)(() => {}));
                string saved = (string)Invoke("PreviewCovers", null, "ImagePath", binding);
                Need(File.Exists(saved), "published-cover-publisher-commits-image");
                Need(Invoke("PreviewCovers", null, "Resolve", hash) != null, "published-cover-binding-readable");
                Invoke("RuntimeInputLease", lease, "End", true, false, "test", "normal");
                bool rejected = false;
                try { Invoke("PreviewFrame", null, "ValidateTarget", target); }
                catch (InvalidDataException) { rejected = true; }
                Need(rejected, "published-capture-validator-rejects-ended-input");
            }
            Need(assembly.GetType("PreviewCovers").GetMethod("TestForgetIdentities", Flags) == null,
                "published-test-cover-hook-absent");
            File.WriteAllText(args[3], serializer.Serialize(new {
                passed = true, checks = checks, publishedAssembly = args[0],
                guiEntryStarted = false, engineLoaded = false, actualCaptureExecuted = false,
                evidenceKind = "Actual release assembly static processors via reflection; synthetic JPEG; no rendering"
            }));
            exit = 0;
        } catch (Exception error) {
            File.WriteAllText(args[3], serializer.Serialize(new {passed = false, checks = checks, error = error.ToString()}));
        }
        return exit;
    }
}
