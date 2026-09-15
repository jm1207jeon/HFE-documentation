using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlLint;

// WPF resolves data bindings and resource keys at RUNTIME: a wrong property name or a missing
// resource key does not fail the build, it silently leaves a control blank, dead or unstyled.
// This linter resolves them statically against the COMPILED assemblies (so source-generated
// members from CommunityToolkit.Mvvm are included) and fails the build instead.
//
//   xamllint <assembly.dll> <xaml-file>...   (extra probe dirs via --probe <dir>)

var assemblies = new List<string>();
var xamlFiles = new List<string>();
var probeDirs = new List<string>();
var rootContexts = new Dictionary<string, string>(StringComparer.Ordinal);

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--probe" && i + 1 < args.Length) { probeDirs.Add(args[++i]); continue; }
    if (args[i] == "--datacontext" && i + 1 < args.Length)
    {
        var pair = args[++i].Split('=', 2);
        if (pair.Length != 2)
        {
            Console.Error.WriteLine($"xamllint: --datacontext 는 <x:Class>=<타입> 형식이어야 합니다: '{args[i]}'");
            return 2;
        }
        rootContexts[pair[0]] = pair[1];
        continue;
    }
    if (args[i].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) { assemblies.Add(args[i]); continue; }
    if (args[i].EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) { xamlFiles.Add(args[i]); continue; }
    Console.Error.WriteLine($"xamllint: 알 수 없는 인자 '{args[i]}'");
    return 2;
}

if (assemblies.Count == 0 || xamlFiles.Count == 0)
{
    Console.Error.WriteLine(
        "사용법: xamllint <assembly.dll>... <file.xaml>... [--probe <dir>] [--datacontext <x:Class>=<타입>]");
    return 2;
}

TypeUniverse universe;
try
{
    universe = TypeUniverse.Create(assemblies, probeDirs);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"xamllint: 어셈블리 로드 실패 — {ex.Message}");
    return 2;
}

var problems = new List<Problem>();
foreach (var file in xamlFiles)
{
    try
    {
        problems.AddRange(new XamlChecker(universe, file, rootContexts).Check());
    }
    catch (Exception ex)
    {
        problems.Add(new Problem(file, 0, "lint", $"검사 중 예외: {ex.GetType().Name}: {ex.Message}"));
    }
}

foreach (var p in problems.OrderBy(p => p.File).ThenBy(p => p.Line))
    Console.Error.WriteLine($"{p.File}({p.Line}): error XAMLLINT-{p.Kind}: {p.Message}");

if (problems.Count > 0)
{
    Console.Error.WriteLine($"\nxamllint: {problems.Count}건의 XAML 결함 (실행 시 조용히 깨지는 바인딩·리소스)");
    return 1;
}

Console.WriteLine($"xamllint: OK — {xamlFiles.Count}개 XAML의 바인딩·리소스가 모두 해석됨");
return 0;

namespace XamlLint
{
    internal sealed record Problem(string File, int Line, string Kind, string Message);

    /// <summary>Metadata-only view of the built assemblies (no code executes, so this runs on Linux).</summary>
    internal sealed class TypeUniverse
    {
        private readonly MetadataLoadContext _mlc;
        private readonly List<Assembly> _roots = new();

        private TypeUniverse(MetadataLoadContext mlc) => _mlc = mlc;

        public static TypeUniverse Create(List<string> assemblies, List<string> probeDirs)
        {
            var paths = new List<string>();
            foreach (var a in assemblies)
            {
                paths.Add(Path.GetFullPath(a));
                var dir = Path.GetDirectoryName(Path.GetFullPath(a));
                if (dir is not null) probeDirs.Add(dir);
            }

            probeDirs.AddRange(FrameworkProbeDirectories());

            // WPF assemblies live in BOTH shared frameworks on Windows: the copy of WindowsBase in
            // Microsoft.NETCore.App is a facade without System.Windows.Rect, so the desktop framework
            // has to be searched first or resolving a converter's output type fails.
            var ordered = probeDirs.Distinct()
                .OrderByDescending(d => d.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var dir in ordered)
                if (Directory.Exists(dir))
                    paths.AddRange(Directory.GetFiles(dir, "*.dll"));

            // One path per simple assembly name, earliest wins: the probe directories overlap
            // (the shared runtime, the SDK targeting packs and the NuGet ref packs all carry
            // System.Runtime), and a MetadataLoadContext refuses the same identity twice.
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
                byName.TryAdd(Path.GetFileNameWithoutExtension(path), path);

            var resolver = new PathAssemblyResolver(byName.Values.ToList());
            var mlc = new MetadataLoadContext(resolver, "System.Runtime");
            var universe = new TypeUniverse(mlc);
            foreach (var a in assemblies)
                universe._roots.Add(mlc.LoadFromAssemblyPath(Path.GetFullPath(a)));
            return universe;
        }

        /// <summary>
        /// Where the BCL and WPF assemblies live, discovered rather than passed in: the caller
        /// should not have to know the layout of the SDK on each machine (that knowledge is exactly
        /// what made this check fail on a clean CI runner).
        /// </summary>
        private static IEnumerable<string> FrameworkProbeDirectories()
        {
            var dirs = new List<string>();

            // the runtime this tool itself is running on — always has System.*
            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (!string.IsNullOrEmpty(runtimeDir)) dirs.Add(runtimeDir);

            // sibling shared frameworks, e.g. .../shared/Microsoft.WindowsDesktop.App/8.0.x (Windows)
            var shared = Directory.GetParent(runtimeDir?.TrimEnd(Path.DirectorySeparatorChar) ?? "")?.Parent;
            if (shared is not null && shared.Exists)
                foreach (var framework in shared.GetDirectories())
                    foreach (var version in framework.GetDirectories())
                        dirs.Add(version.FullName);

            // reference packs from the NuGet cache (how a non-Windows machine gets WPF metadata)
            foreach (var root in new[]
                     {
                         Environment.GetEnvironmentVariable("NUGET_PACKAGES"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
                     })
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                foreach (var name in new[] { "microsoft.windowsdesktop.app.ref", "microsoft.netcore.app.ref" })
                {
                    var packageDir = Path.Combine(root, name);
                    if (!Directory.Exists(packageDir)) continue;
                    foreach (var version in Directory.GetDirectories(packageDir))
                        dirs.AddRange(Directory.GetDirectories(version, "net*", SearchOption.AllDirectories));
                }
            }

            // targeting packs shipped with the SDK
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrEmpty(dotnetRoot) && !string.IsNullOrEmpty(runtimeDir))
                dotnetRoot = Directory.GetParent(runtimeDir.TrimEnd(Path.DirectorySeparatorChar))?.Parent?.Parent?.FullName;
            var packs = string.IsNullOrEmpty(dotnetRoot) ? null : Path.Combine(dotnetRoot, "packs");
            if (packs is not null && Directory.Exists(packs))
                foreach (var pack in Directory.GetDirectories(packs, "*.Ref"))
                    foreach (var version in Directory.GetDirectories(pack))
                        dirs.AddRange(Directory.GetDirectories(version, "net*", SearchOption.AllDirectories));

            return dirs;
        }

        public Type? FindType(string fullName)
        {
            foreach (var asm in _roots)
            {
                var t = asm.GetType(fullName, throwOnError: false);
                if (t is not null) return t;
            }
            foreach (var asm in _mlc.GetAssemblies())
            {
                var t = asm.GetType(fullName, throwOnError: false);
                if (t is not null) return t;
            }
            return null;
        }

        public Type? FindTypeBySimpleName(string ns, string name)
            => FindType($"{ns}.{name}");

        /// <summary>Public instance property (walks the base chain, which GetProperty does not for MLC types).</summary>
        public static PropertyInfo? Property(Type type, string name)
        {
            for (Type? t = type; t is not null; t = SafeBase(t))
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (p is not null) return p;
            }
            return null;
        }

        public static MethodInfo? Method(Type type, string name)
        {
            for (Type? t = type; t is not null; t = SafeBase(t))
            {
                var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m is not null) return m;
            }
            return null;
        }

        private static Type? SafeBase(Type t)
        {
            try { return t.BaseType; } catch { return null; }
        }

        /// <summary>Element type of IEnumerable&lt;T&gt; (for ItemsSource → item DataContext).</summary>
        public static Type? ItemTypeOf(Type collectionType)
        {
            if (collectionType.IsArray) return collectionType.GetElementType();
            foreach (var i in Enumerable.Concat(new[] { collectionType }, SafeInterfaces(collectionType)))
            {
                if (!i.IsGenericType) continue;
                var def = i.GetGenericTypeDefinition().FullName ?? "";
                if (def.StartsWith("System.Collections.Generic.IEnumerable`1") ||
                    def.StartsWith("System.Collections.ObjectModel.ObservableCollection`1") ||
                    def.StartsWith("System.Collections.Generic.IList`1") ||
                    def.StartsWith("System.Collections.Generic.IReadOnlyList`1") ||
                    def.StartsWith("System.Collections.Generic.List`1"))
                    return i.GetGenericArguments()[0];
            }
            return null;
        }

        private static IEnumerable<Type> SafeInterfaces(Type t)
        {
            try { return t.GetInterfaces(); } catch { return Array.Empty<Type>(); }
        }
    }

    internal sealed class XamlChecker
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        private readonly TypeUniverse _u;
        private readonly string _path;
        private readonly string _display;
        private readonly XDocument _doc;
        private readonly List<Problem> _problems = new();
        private readonly HashSet<string> _declaredKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _clrNamespaces = new(StringComparer.Ordinal);

        /// <summary>Root DataContext per window class — the code-behind assigns it, XAML cannot express it.</summary>
        private readonly IReadOnlyDictionary<string, string> _rootDataContext;

        /// <summary>Controls whose ItemTemplate/ItemContainerStyle re-scope the DataContext to the item type.</summary>
        private static readonly HashSet<string> ItemsControls = new(StringComparer.Ordinal)
        { "ItemsControl", "ListBox", "ListView", "ComboBox", "DataGrid", "TreeView", "TabControl" };

        public XamlChecker(TypeUniverse universe, string path,
            IReadOnlyDictionary<string, string> rootDataContext)
        {
            _u = universe;
            _path = path;
            _display = path;
            _rootDataContext = rootDataContext;
            _doc = XDocument.Load(path, LoadOptions.SetLineInfo);
        }

        public IEnumerable<Problem> Check()
        {
            var root = _doc.Root!;
            foreach (var attr in root.Attributes())
            {
                // xmlns:app="clr-namespace:HfeLayoutSim.App"
                if (attr.IsNamespaceDeclaration && attr.Value.StartsWith("clr-namespace:"))
                {
                    var ns = attr.Value.Substring("clr-namespace:".Length).Split(';')[0];
                    _clrNamespaces[attr.Name.LocalName] = ns;
                }
            }

            CollectResourceKeys(root);

            var rootClass = (string?)root.Attribute(X + "Class");
            Type? rootContext = null;
            if (rootClass is not null && _rootDataContext.TryGetValue(rootClass, out var vmName))
            {
                rootContext = _u.FindType(vmName);
                if (rootContext is null)
                    Add(root, "DC", $"루트 DataContext 타입을 찾을 수 없습니다: {vmName}");
            }

            Walk(root, rootContext);
            return _problems;
        }

        private void CollectResourceKeys(XElement root)
        {
            foreach (var el in root.DescendantsAndSelf())
            {
                var key = (string?)el.Attribute(X + "Key");
                if (key is not null) _declaredKeys.Add(key);
            }
            // App.xaml sits next to the window and holds the shared resource dictionary.
            var appXaml = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!, "App.xaml");
            if (File.Exists(appXaml) && !string.Equals(Path.GetFullPath(appXaml), Path.GetFullPath(_path), StringComparison.Ordinal))
            {
                try
                {
                    foreach (var el in XDocument.Load(appXaml).Descendants())
                    {
                        var key = (string?)el.Attribute(X + "Key");
                        if (key is not null) _declaredKeys.Add(key);
                    }
                }
                catch { /* App.xaml unreadable — keys stay unresolved and get reported */ }
            }
        }

        private void Walk(XElement element, Type? dataContext)
        {
            var scoped = dataContext;

            // <DataTemplate DataType="{x:Type vm:Foo}"> rescopes to Foo
            var dataType = (string?)element.Attribute("DataType");
            if (element.Name.LocalName == "DataTemplate" && dataType is not null)
                scoped = ResolveXTypeRef(dataType) ?? scoped;

            // DataContext="{Binding X}" rescopes this element and everything under it
            var explicitContext = (string?)element.Attribute("DataContext");
            if (explicitContext is not null && dataContext is not null)
            {
                var contextPath = ExtractBindingPath(explicitContext);
                if (contextPath is not null)
                {
                    var resolved = ResolvePath(dataContext, contextPath, out var failed);
                    if (resolved is null)
                        Add(element, "BIND",
                            $"DataContext: '{contextPath}' 를 {Short(dataContext)} 에서 찾을 수 없습니다 (막힌 구간: '{failed}').");
                    else
                        scoped = resolved;
                }
            }

            foreach (var attr in element.Attributes())
            {
                if (attr.Name.LocalName == "DataContext") continue; // handled above
                CheckAttributeValue(element, attr.Name.LocalName, attr.Value, scoped);
            }

            // Property-element syntax: <Border.Style>, <ItemsControl.ItemTemplate>, ...
            foreach (var child in element.Elements())
            {
                var local = child.Name.LocalName;
                var dot = local.IndexOf('.');
                var childScope = scoped;

                if (dot > 0)
                {
                    var owner = local.Substring(0, dot);
                    var prop = local.Substring(dot + 1);
                    if (ItemsControls.Contains(owner) &&
                        (prop == "ItemTemplate" || prop == "ItemContainerStyle" || prop == "CellTemplate" || prop == "RowStyle"))
                        childScope = ItemScopeFor(element) ?? scoped;
                }
                else if (local == "DataTemplate" || local == "Style")
                {
                    // handled by the parent property element
                }

                Walk(child, childScope);
            }
        }

        /// <summary>Item DataContext of an ItemsControl: element type of its ItemsSource binding.</summary>
        private Type? ItemScopeFor(XElement itemsControl)
        {
            var itemsSource = (string?)itemsControl.Attribute("ItemsSource");
            if (itemsSource is null) return null;
            var path = ExtractBindingPath(itemsSource);
            if (path is null) return null;

            var owner = NearestDataContext(itemsControl);
            if (owner is null) return null;

            var resolved = ResolvePath(owner, path, out _);
            return resolved is null ? null : TypeUniverse.ItemTypeOf(resolved);
        }

        private readonly Dictionary<XElement, Type?> _contextCache = new();

        private Type? NearestDataContext(XElement element)
        {
            // Rebuild the scope by walking from the root — cheap for files of this size.
            if (_contextCache.TryGetValue(element, out var cached)) return cached;

            var chain = new List<XElement>();
            for (XElement? e = element; e is not null; e = e.Parent) chain.Add(e);
            chain.Reverse();

            Type? ctx = null;
            var rootClass = (string?)_doc.Root!.Attribute(X + "Class");
            if (rootClass is not null && _rootDataContext.TryGetValue(rootClass, out var vm))
                ctx = _u.FindType(vm);

            for (var i = 1; i < chain.Count; i++)
            {
                var node = chain[i];
                var local = node.Name.LocalName;
                var dot = local.IndexOf('.');
                if (dot > 0)
                {
                    var owner = local.Substring(0, dot);
                    var prop = local.Substring(dot + 1);
                    if (ItemsControls.Contains(owner) &&
                        (prop == "ItemTemplate" || prop == "ItemContainerStyle" || prop == "CellTemplate" || prop == "RowStyle"))
                    {
                        var scope = ItemScopeFor(chain[i - 1]);
                        if (scope is not null) ctx = scope;
                    }
                }
                if (local == "DataTemplate")
                {
                    var dt = (string?)node.Attribute("DataType");
                    var t = dt is null ? null : ResolveXTypeRef(dt);
                    if (t is not null) ctx = t;
                }

                var explicitContext = (string?)node.Attribute("DataContext");
                if (explicitContext is not null && ctx is not null)
                {
                    var contextPath = ExtractBindingPath(explicitContext);
                    if (contextPath is not null)
                    {
                        var resolved = ResolvePath(ctx, contextPath, out _);
                        if (resolved is not null) ctx = resolved;
                    }
                }
            }

            _contextCache[element] = ctx;
            return ctx;
        }

        private void CheckAttributeValue(XElement element, string attrName, string value, Type? dataContext)
        {
            foreach (var key in ExtractStaticResourceKeys(value))
                if (!_declaredKeys.Contains(key))
                    Add(element, "RES", $"{attrName}: StaticResource '{key}' 가 선언되어 있지 않습니다 (실행 시 리소스 예외).");

            if (!value.TrimStart().StartsWith("{Binding", StringComparison.Ordinal) &&
                !value.TrimStart().StartsWith("{ Binding", StringComparison.Ordinal))
                return;

            if (value.Contains("RelativeSource") || value.Contains("ElementName") || value.Contains("Source="))
                return; // resolved against something this linter does not model

            var path = ExtractBindingPath(value);
            if (path is null) return; // {Binding} — whole DataContext

            if (dataContext is null)
            {
                Add(element, "DC", $"{attrName}: DataContext를 알 수 없어 '{path}' 바인딩을 검증할 수 없습니다.");
                return;
            }

            var resolved = ResolvePath(dataContext, path, out var failedSegment);
            if (resolved is null)
                Add(element, "BIND",
                    $"{attrName}: '{path}' 를 {Short(dataContext)} 에서 찾을 수 없습니다 " +
                    $"(막힌 구간: '{failedSegment}') — 실행 시 바인딩이 조용히 무시됩니다.");
            else
                CheckConverterCompatibility(element, attrName, value, resolved);
        }

        /// <summary>
        /// A converter declares what it emits with [ValueConversion(sourceType, targetType)].
        /// If the XAML property cannot take that target type, WPF drops the binding at runtime.
        /// </summary>
        private void CheckConverterCompatibility(XElement element, string attrName, string value, Type sourceType)
        {
            var m = Regex.Match(value, @"Converter\s*=\s*\{StaticResource\s+(?<k>[A-Za-z0-9_]+)\s*\}");
            if (!m.Success) return;
            var key = m.Groups["k"].Value;

            var converterType = FindConverterTypeByKey(key);
            if (converterType is null) return;

            var conv = converterType.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.Name == "ValueConversionAttribute");
            if (conv is null || conv.ConstructorArguments.Count < 2) return;

            var outType = conv.ConstructorArguments[1].Value as Type;
            if (outType is null) return;

            var targetType = TargetPropertyType(element, attrName);
            if (targetType is null) return;

            if (!IsAssignable(targetType, outType))
                Add(element, "CONV",
                    $"{attrName}: 변환기 '{key}' 는 {Short(outType)} 를 돌려주는데 속성 타입은 {Short(targetType)} 입니다 — " +
                    "WPF가 변환에 실패해 바인딩을 버립니다(값이 적용되지 않음).");
        }

        private Type? FindConverterTypeByKey(string key)
        {
            // <app:HexBrushConverter x:Key="HexBrush" /> — in this window or in App.xaml
            foreach (var file in new[] { _path, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!, "App.xaml") })
            {
                if (!File.Exists(file)) continue;
                XDocument doc;
                try { doc = string.Equals(file, _path, StringComparison.Ordinal) ? _doc : XDocument.Load(file); }
                catch { continue; }

                foreach (var el in doc.Descendants())
                {
                    if ((string?)el.Attribute(X + "Key") != key) continue;
                    var prefix = el.Name.Namespace.NamespaceName;
                    if (!prefix.StartsWith("clr-namespace:")) return null;
                    var ns = prefix.Substring("clr-namespace:".Length).Split(';')[0];
                    return _u.FindTypeBySimpleName(ns, el.Name.LocalName);
                }
            }
            return null;
        }

        /// <summary>Type of the WPF dependency property an attribute maps to (only the ones worth policing).</summary>
        private Type? TargetPropertyType(XElement element, string attrName) => attrName switch
        {
            "Visibility" => _u.FindType("System.Windows.Visibility"),
            "IsEnabled" or "IsChecked" or "IsHitTestVisible" or "IsReadOnly" => _u.FindType("System.Boolean"),
            "Background" or "Foreground" or "BorderBrush" or "Fill" or "Stroke" => _u.FindType("System.Windows.Media.Brush"),
            "FontWeight" => _u.FindType("System.Windows.FontWeight"),
            "FontSize" or "Width" or "Height" or "Opacity" => _u.FindType("System.Double"),
            "Text" or "Content" or "ToolTip" or "Header" or "Title" => null, // object/string — anything goes
            _ => null,
        };

        private static bool IsAssignable(Type target, Type provided)
        {
            if (target.FullName == provided.FullName) return true;
            if (target.FullName == "System.Object") return true;
            for (Type? t = provided; t is not null; t = SafeBase(t))
                if (t.FullName == target.FullName) return true;
            return false;

            static Type? SafeBase(Type t) { try { return t.BaseType; } catch { return null; } }
        }

        private Type? ResolvePath(Type root, string path, out string failedSegment)
        {
            failedSegment = "";
            var current = root;
            foreach (var rawSegment in path.Split('.'))
            {
                var segment = rawSegment.Trim();
                if (segment.Length == 0) continue;
                var bracket = segment.IndexOf('[');
                if (bracket >= 0) segment = segment.Substring(0, bracket);
                if (segment.Length == 0) continue;

                var prop = TypeUniverse.Property(current!, segment);
                if (prop is null)
                {
                    failedSegment = segment;
                    return null;
                }
                current = prop.PropertyType;
            }
            return current;
        }

        private Type? ResolveXTypeRef(string value)
        {
            var m = Regex.Match(value, @"\{x:Type\s+(?<p>[A-Za-z0-9_]+):(?<n>[A-Za-z0-9_]+)\s*\}");
            if (!m.Success) return null;
            return _clrNamespaces.TryGetValue(m.Groups["p"].Value, out var ns)
                ? _u.FindTypeBySimpleName(ns, m.Groups["n"].Value)
                : null;
        }

        private static string? ExtractBindingPath(string value)
        {
            var v = value.Trim();
            if (!v.StartsWith("{Binding", StringComparison.Ordinal)) return null;
            var inner = v.Substring("{Binding".Length).TrimEnd('}').Trim();
            if (inner.Length == 0) return null;

            var explicitPath = Regex.Match(inner, @"(^|,)\s*Path\s*=\s*(?<p>[^,}]+)");
            if (explicitPath.Success) return explicitPath.Groups["p"].Value.Trim();

            var first = inner.Split(',')[0].Trim();
            if (first.Length == 0 || first.Contains('=')) return null;
            return first;
        }

        private static IEnumerable<string> ExtractStaticResourceKeys(string value)
        {
            foreach (Match m in Regex.Matches(value, @"\{StaticResource\s+(?<k>[A-Za-z0-9_\.]+)\s*\}"))
                yield return m.Groups["k"].Value;
        }

        private static string Short(Type t) => t.Name;

        private void Add(XElement element, string kind, string message)
        {
            var line = (element as System.Xml.IXmlLineInfo)?.LineNumber ?? 0;
            _problems.Add(new Problem(_display, line, kind, message));
        }
    }
}
