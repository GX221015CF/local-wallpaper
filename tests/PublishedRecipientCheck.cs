using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// Separate headless verifier. Not compiled into or distributed with the application.
internal static class PublishedRecipientCheck {
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly assembly;
    static readonly List<string> checks = new List<string>();

    static object Call(string type, string method, params object[] arguments) {
        try {
            return assembly.GetType(type, true).GetMethods(Flags)
                .Single(m => m.IsStatic && m.Name == method && m.GetParameters().Length == arguments.Length)
                .Invoke(null, arguments);
        } catch (TargetInvocationException error) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    static void Set(string type, string field, object value) {
        assembly.GetType(type, true).GetField(field, Flags).SetValue(null, value);
    }
    static object Property(object value, string name) {
        return value.GetType().GetProperty(name, Flags).GetValue(value, null);
    }
    static void Need(bool condition, string description) {
        if (!condition) throw new InvalidDataException(description);
        checks.Add(description);
    }

    static int Main(string[] args) {
        var json = new JavaScriptSerializer();
        try {
            string mode = args[0], app = Path.GetFullPath(args[1]);
            string parent = Path.GetFullPath(args[2]), report = Path.GetFullPath(args[3]);
            assembly = Assembly.LoadFrom(Path.Combine(app, "build", "LocalWallpaper.exe"));
            Set("Common", "AppRoot", app);
            Set("Common", "Root", app);
            Call("VersionStore", "VerifyRelease", app, Call("Common", "Hash", Path.Combine(app, "build", "release.json")), 1);
            Need(true, "actual-release-verifier-accepts-complete-local-deployment");
            string binding = Path.Combine(app, "build", "data-location.json");
            if (mode == "prepare") {
                Need(!File.Exists(binding), "extracted-app-starts-without-user-binding");
                Directory.CreateDirectory(parent);
                Need((bool)Call("FirstUseSetup", "InitializeNew", parent, app, binding), "actual-first-use-initialization-completes");
            }
            object location = Call("DataLocation", "LoadBound", binding);
            Call("DataLocation", "ValidateStore", location, false);
            Set("DataLocation", "Current", location);
            Set("DataLocation", "Ready", true);
            string data = (string)Property(location, "DataRoot");
            Need(data.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase), "data-is-within-explicit-isolation");
            Set("Common", "Root", data);
            string source = (string)assembly.GetType("SourceLocation").GetProperty("DefaultRoot", Flags).GetValue(null, null);
            Need(source == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lxwp", "data"),
                "source-default-follows-current-windows-roaming-folder");
            string recommended = (string)assembly.GetType("SourceLocation").GetProperty("RecommendedDataParent", Flags).GetValue(null, null);
            Need(recommended == Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "data-recommendation-follows-current-user-local-folder");
            Call("RuntimeProvisioning", "Initialize", CancellationToken.None);
            Need(!(bool)assembly.GetType("RuntimeProvisioning").GetProperty("Available", Flags).GetValue(null, null),
                "no-component-configuration-reports-unavailable");
            Need(!File.Exists(Path.Combine(data, "data", "runtime-location.json")), "component-check-does-not-invent-configuration");
            var library = Activator.CreateInstance(assembly.GetType("WallpaperLibrary", true), true);
            var document = library.GetType().GetField("Doc", Flags).GetValue(library);
            var items = (System.Collections.IList)Property(document, "Items");
            Need(items.Count == 0 && Property(document, "SelectedId") == null, "new-user-library-is-empty-with-no-default-wallpaper-dependency");
            Need(!Directory.Exists(Path.Combine(data, "logs", "app")) && !Directory.Exists(Path.Combine(data, "runtime-inputs-r24")),
                "headless-preparation-creates-no-playback-session");
            Need(assembly.GetType("VideoFrameDecoder").GetField("TestBeforeDecode", Flags) == null
                && assembly.GetType("TestScenario") == null, "shipping-assembly-has-no-test-control-hooks");
            if (mode == "prepare") {
                string catalog = Path.Combine(data, "data", "catalog.json");
                string before = (string)Call("Common", "Hash", catalog);
                bool rejected = false;
                try { Call("FirstUseSetup", "InitializeNew", parent, app, binding); }
                catch (InvalidOperationException) { rejected = true; }
                Need(rejected && before == (string)Call("Common", "Hash", catalog), "repeat-initialization-preserves-existing-data");
                string marker = Path.Combine(data, "store.json"), held = marker + ".held";
                File.Move(marker, held);
                try {
                    bool missingRejected = false;
                    try { Call("DataLocation", "ValidateStore", location, false); }
                    catch { missingRejected = true; }
                    Need(missingRejected && !File.Exists(marker) && before == (string)Call("Common", "Hash", catalog),
                        "invalid-store-is-rejected-without-creating-a-replacement-library");
                } finally { File.Move(held, marker); }
            }
            File.WriteAllText(report, json.Serialize(new {
                passed = true, mode = mode, checks = checks, dataRoot = data,
                storeId = Property(location, "StoreId"), evidenceKind = "Actual published static setup, binding and verification handlers in a separate process; main GUI Entry not invoked",
                guiStarted = false, engineLoaded = false, playbackStarted = false
            }));
            return 0;
        } catch (Exception error) {
            File.WriteAllText(args[3], json.Serialize(new {passed = false, checks = checks, error = error.ToString()}));
            return 1;
        }
    }
}
