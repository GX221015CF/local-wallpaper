using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// Local preparation harness, never shipped or exposed as an application command.
internal static class PublishedPersonalPreparation {
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Assembly assembly;
    static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16777216 };
    static object Call(string type, string name, params object[] args) {
        try {
            return assembly.GetType(type, true).GetMethods(Flags).Single(method => method.IsStatic && method.Name == name
                && method.GetParameters().Length == args.Length
                && method.GetParameters().Select((parameter, index) => args[index] == null || parameter.ParameterType.IsInstanceOfType(args[index])).All(match => match))
                .Invoke(null, args);
        } catch (TargetInvocationException error) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw;
        }
    }
    static void Set(string type, string field, object value) { assembly.GetType(type, true).GetField(field, Flags).SetValue(null, value); }
    static object Get(object value, string property) { return value.GetType().GetProperty(property, Flags).GetValue(value, null); }
    static string Text(object value, string property) { return (string)Get(value, property); }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

    static int Main(string[] args) {
        if (args.Length > 3 && args[0] == "bind-upgrade") {
            var domain = AppDomain.CreateDomain("personal-target-binding", null,
                new AppDomainSetup { ApplicationBase = Path.Combine(Path.GetFullPath(args[3]), "build") });
            try {
                var worker = (PublishedPersonalDomain)domain.CreateInstanceFromAndUnwrap(
                    Assembly.GetExecutingAssembly().Location, typeof(PublishedPersonalDomain).FullName);
                return worker.Execute(args);
            } finally { AppDomain.Unload(domain); }
        }
        return Execute(args);
    }

    internal static int Execute(string[] args) {
        var rows = new List<object>();
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(120))) {
        try {
            string mode = args[0], app = Path.GetFullPath(args[1]), parent = Path.GetFullPath(args[2]), plan = args[3], report = args[4];
            assembly = Assembly.LoadFrom(Path.Combine(app, "build", "LocalWallpaper.exe"));
            Set("Common", "AppRoot", app); Set("Common", "Root", app);
            Call("VersionStore", "VerifyRelease", app, Call("Common", "Hash", Path.Combine(app, "build", "release.json")), 1);
            string binding = Path.Combine(app, "build", "data-location.json");
            if (mode == "bind-upgrade") {
                // The target verifier carries the target's exact generated host/contract pins.
                // Create its binding through the existing formal mechanism before using its tool.
                string previous = Path.GetFullPath(plan);
                object previousLocation = Call("DataLocation", "LoadBound", Path.Combine(previous, "build", "data-location.json"));
                Set("Common", "AppRoot", previous);
                Call("FirstUseSetup", "EnsureVersionBinding", app, previousLocation);
                File.WriteAllText(report, json.Serialize(new { passed = true, mode = mode, targetVerified = true, dataRoot = Get(previousLocation, "DataRoot"), storeId = Get(previousLocation, "StoreId"), engineLoaded = false, guiStarted = false }));
                return 0;
            }
            if (mode == "prepare") Call("FirstUseSetup", "InitializeNew", parent, app, binding);
            object location = Call("DataLocation", "LoadBound", binding);
            Call("DataLocation", "ValidateStore", location, false);
            Set("DataLocation", "Current", location); Set("DataLocation", "Ready", true);
            string dataRoot = Text(location, "DataRoot");
            Need(dataRoot.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase), "Personal data is outside deployment and inside authorized parent");
            Set("Common", "Root", dataRoot);
            if (mode == "prepare") Call("SourceLocation", "SaveRoot", assembly.GetType("SourceLocation").GetProperty("DefaultRoot", Flags).GetValue(null, null));
            var library = Activator.CreateInstance(assembly.GetType("WallpaperLibrary", true), true);
            var document = library.GetType().GetField("Doc", Flags).GetValue(library);
            var items = (System.Collections.IList)Get(document, "Items");
            if (mode == "prepare" || mode == "append") {
                var sourceRows = json.DeserializeObject(File.ReadAllText(plan)) as object[];
                foreach (var row in sourceRows) {
                    string encoded = json.Serialize(row);
                    object source = json.Deserialize(encoded, assembly.GetType("WallpaperItem", true));
                    try {
                        budget.Token.ThrowIfCancellationRequested();
                        object[] invokeArgs = { library, source, budget.Token, false };
                        object item = assembly.GetType("PersonalResourceImport", true).GetMethod("Import", Flags).Invoke(null, invokeArgs);
                        rows.Add(new { source = Text(source, "SourcePath"), resourceId = Text(source, "ResourceId"), result = "imported", duplicate = invokeArgs[3], id = Text(item, "Id"), target = Text(item, "SourcePath"), hash = Text(item, "RegisteredSha256"), cover = Text(item, "CoverPath") });
                    } catch (TargetInvocationException error) {
                        rows.Add(new { source = Text(source, "SourcePath"), resourceId = Text(source, "ResourceId"), result = "rejected", error = error.InnerException.ToString() });
                    }
                    File.WriteAllText(report + ".progress.json", json.Serialize(rows));
                }
                if (mode == "prepare" && items.Count > 0) library.GetType().GetMethod("Select", Flags).Invoke(library, new[] { items[0] });
            }
            foreach (object item in items) {
                budget.Token.ThrowIfCancellationRequested();
                Need(File.Exists(Text(item, "SourcePath")), "Personal resource exists");
                Need((string)Call("LocalImport", "HashCancelable", Text(item, "SourcePath"), (Action)(() => budget.Token.ThrowIfCancellationRequested())) == Text(item, "RegisteredSha256"), "Personal resource identity matches catalog");
                Need(Text(item, "PreviewConfirmedSha256") == null && Text(item, "DesktopConfirmedSha256") == null, "No old confirmation transferred");
                Need(Text(item, "SourcePath").StartsWith(dataRoot + "\\library\\", StringComparison.OrdinalIgnoreCase), "Catalog uses owned persistent library");
            }
            object discovery = Call("RuntimeDiscovery", "Check", budget.Token);
            Need(!File.Exists(Path.Combine(dataRoot, "data", "runtime-location.json")), "First component adoption waits for user UI confirmation");
            Need(assembly.GetType("RuntimeDiscovery").GetField("TestBeforeVerify", Flags) == null
                && assembly.GetType("PersonalResourceImport").GetField("TestBeforeCommit", Flags) == null, "No test hooks in release");
            Need(!Directory.Exists(Path.Combine(dataRoot, "runtime-inputs-r24")), "Preparation started no playback");
            File.WriteAllText(report, json.Serialize(new {
                passed = true, mode = mode, count = items.Count, selectedId = Get(document, "SelectedId"), dataRoot = dataRoot,
                storeId = Get(location, "StoreId"), source = Call("SourceLocation", "ResolveRoot"), runtimeDiscovery = discovery,
                rows = rows, evidenceKind = "Actual release assembly handlers in separate process; GUI Entry never invoked", engineLoaded = false, guiStarted = false
            }));
            return 0;
        } catch (Exception error) {
            File.WriteAllText(args[4], json.Serialize(new { passed = false, rows = rows, error = error.ToString() }));
            return 1;
        }
        }
    }
}

public sealed class PublishedPersonalDomain : MarshalByRefObject {
    public int Execute(string[] args) { return PublishedPersonalPreparation.Execute(args); }
}
