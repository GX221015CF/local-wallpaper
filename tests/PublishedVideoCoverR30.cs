using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;

internal static class PublishedVideoCoverR30 {
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly assembly;
    static object Call(string type, string method, params object[] args) {
        try {
            return assembly.GetType(type, true).GetMethods(Flags)
                .Single(x => x.IsStatic && x.Name == method && x.GetParameters().Length == args.Length)
                .Invoke(null, args);
        } catch (TargetInvocationException error) { throw error.InnerException; }
    }
    static object Property(object value, string name) {
        return value.GetType().GetProperty(name).GetValue(value, null);
    }
    static void Set(string type, string field, object value) {
        assembly.GetType(type, true).GetField(field, Flags).SetValue(null, value);
    }
    static int Main(string[] args) {
        var json = new JavaScriptSerializer();
        try {
            string mode = args[0], app = Path.GetFullPath(args[1]), boundary = Path.GetFullPath(args[2]);
            string fixture = Path.GetFullPath(args[3]), report = Path.GetFullPath(args[4]);
            if (!fixture.StartsWith(boundary + "\\", StringComparison.OrdinalIgnoreCase)
                || !report.StartsWith(boundary + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Explicit isolated boundary required");
            assembly = Assembly.LoadFrom(Path.Combine(app, "build", "LocalWallpaper.exe"));
            Set("Common", "AppRoot", app);
            Set("Common", "Root", app);
            Call("VersionStore", "VerifyRelease", app, Call("Common", "Hash", Path.Combine(app, "build", "release.json")), 1);
            string data;
            object location = null;
            {
                string binding = Path.Combine(app, "build", "data-location.json");
                if (File.Exists(binding)) throw new IOException("Existing observation binding protected");
                string parent = Path.Combine(boundary, "data-choice");
                Directory.CreateDirectory(parent);
                if (!(bool)Call("FirstUseSetup", "InitializeNew", parent, app, binding))
                    throw new IOException("Initialization failed");
                location = Call("DataLocation", "LoadBound", binding);
                Call("DataLocation", "ValidateStore", location, false);
                data = (string)Property(location, "DataRoot");
                Set("DataLocation", "Current", location);
                Set("DataLocation", "Ready", true);
            }
            Set("Common", "Root", data);
            Call("SourceLocation", "SaveRoot", fixture);
            var library = Activator.CreateInstance(assembly.GetType("WallpaperLibrary", true), true);
            var document = library.GetType().GetField("Doc", Flags).GetValue(library);
            var items = (System.Collections.IList)Property(document, "Items");
            if (items.Count != 0) throw new Exception("Expected empty new isolated catalog");
            var covers = new List<object>();
            if (mode == "verify") {
                foreach (string id in new[] {"402242", "398417", "403178"}) {
                    string operation = Path.Combine((string)assembly.GetType("Common").GetProperty("WorkRoot", Flags).GetValue(null, null), Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(operation);
                    Set("CoverCache", "WorkFolder", Path.Combine(operation, "cover-temp"));
                    var candidate = Call("LocalImport", "ReadRecord", Path.Combine(fixture, "cache_info", id + "_fixture.dat"));
                    var watch = Stopwatch.StartNew();
                    Action check = () => { if (watch.Elapsed.TotalSeconds > 20) throw new TimeoutException("Published check budget"); };
                    Call("CandidateChecks", "Run", candidate, check, null, true);
                    object checkedResult = Property(candidate, "Check");
                    string image = (string)Property(candidate, "Cover");
                    if ((string)Property(checkedResult, "State") != "passed" || image == null)
                        throw new Exception("Published candidate image absent: " + Property(checkedResult, "Reason")
                            + " / " + Property(candidate, "CoverReason"));
                    using (var bitmap = Image.FromFile(image))
                        covers.Add(new {id = id, hash = Property(checkedResult, "Sha256"),
                            path = image, imageSha256 = Call("Common", "Hash", image),
                            width = bitmap.Width, height = bitmap.Height, bytes = new FileInfo(image).Length});
                    Call("LocalImport", "CleanPayload", operation);
                }
                if (items.Count != 0) throw new Exception("Candidate check imported unexpectedly");
                if (assembly.GetType("VideoFrameDecoder").GetField("TestBeforeDecode", Flags) != null)
                    throw new Exception("Test decoder hook reached release");
            }
            File.WriteAllText(report, json.Serialize(new {
                passed = true, mode = mode, dataRoot = data, sourceRoot = fixture,
                entry = Path.Combine(app, "build", "LocalWallpaper.exe"),
                storeId = location == null ? null : Property(location, "StoreId"),
                covers = covers, catalogItems = items.Count, componentsRequired = false,
                systemFrameDecoderExecuted = mode == "verify", thirdPartyEngineLoaded = false,
                guiEntryStarted = false, playbackStarted = false, testHooksAbsent = mode == "verify"
            }));
            return 0;
        } catch (Exception error) {
            File.WriteAllText(args[4], json.Serialize(new {passed = false, error = error.ToString()}));
            return 1;
        }
    }
}
