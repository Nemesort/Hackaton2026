using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

public class Map0Window : EditorWindow
{
    class CachedNode
    {
        public Type Type;
        public MapNodeAttribute Attr;
        public string Comment;
        public List<string> Exposed = new();

        public List<NodeLink> Outgoing = new();
        public List<NodeLink> Incoming = new();
    }

    class NodeLink
    {
        public CachedNode Target;
        public List<string> Uses = new();
    }

    [MenuItem("Tools/System Map 0")]
    public static void Open() => GetWindow<Map0Window>("System Map 0");

    private List<CachedNode> _cachedNodes;
    private bool _cacheBuilt = false;

    private Vector2 _scroll;
    private MapTag _viewFilter = MapTag.Manager;

    private IEnumerable<Type> GetAllTypesSafely()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (Assembly a in assemblies)
        {
            IEnumerable<Type> types;
            try { types = a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null); }
            foreach (Type t in types) yield return t;
        }
    }

    private static List<string> GetExposed(Type type)
        => type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<ExposedAttribute>() != null)
            .Select(m =>
            {
                ExposedAttribute ex = m.GetCustomAttribute<ExposedAttribute>();
                return string.IsNullOrWhiteSpace(ex.Alias) ? m.Name : ex.Alias;
            })
            .Distinct()
            .ToList();

    private void BuildCache()
    {
        _cachedNodes = new List<CachedNode>();

        IEnumerable<Type> allTypes = GetAllTypesSafely();

        List<CachedNode> nodes = allTypes
            .Select(t => new CachedNode { Type = t, Attr = t.GetCustomAttribute<MapNodeAttribute>(), Comment = t.GetCustomAttribute<MapNodeComment>()?.Comment })
            .Where(x => x.Attr != null)
            .ToList();

        Dictionary<Type, CachedNode> nodeByType = nodes.ToDictionary(
            n => n.Type,
            n => new CachedNode
            {
                Type = n.Type,
                Attr = n.Attr,
                Comment = n.Comment,
                Exposed = GetExposed(n.Type)
            });

        _cachedNodes = nodeByType.Values.ToList();

        foreach (CachedNode node in nodeByType.Values)
        {
            IEnumerable<DependsAttribute> deps = node.Type.GetCustomAttributes<DependsAttribute>();

            foreach (DependsAttribute dep in deps)
            {
                if (!nodeByType.TryGetValue(dep.TargetType, out var target))
                    continue;

                NodeLink link = new NodeLink
                {
                    Target = target,
                    Uses = dep.Uses?
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct()
                        .ToList() ?? new List<string>()
                };

                node.Outgoing.Add(link);
                target.Incoming.Add(new NodeLink
                {
                    Target = node,
                    Uses = link.Uses
                });
            }
        }

        _cacheBuilt = true;
    }

    void DrawNode(CachedNode node, int indent, HashSet<CachedNode> visited, string path)
    {
        if (visited.Contains(node)) return;
        visited.Add(node);


        GUIContent label = new GUIContent(
            $"{node.Type.FullName}  [{node.Attr.Tags}]",
            node.Comment ?? ""
        );
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

        int previousIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = indent + 1;

        if (node.Exposed.Count > 0)
            EditorGUILayout.LabelField($"Exposes: {string.Join(", ", node.Exposed)}");

        List<NodeLink> links = node.Outgoing.OrderBy(l => l.Target.Type.FullName).ToList();

        foreach (NodeLink link in links)
        {
            string usesTxt = link.Uses.Count > 0
                ? $" (uses: {string.Join(", ", link.Uses)})"
                : "";

            EditorGUILayout.LabelField($"Uses: {link.Target.Type.FullName}{usesTxt}");
        }

        foreach (NodeLink link in links)
        {
            DrawNode(link.Target, indent + 2, visited, path + "/" + link.Target.Type.FullName);
        }

        EditorGUI.indentLevel = previousIndent;
    }

    private void WriteNodeRecursive(
    System.Text.StringBuilder sb,
    CachedNode node,
    HashSet<CachedNode> visited,
    int indent,
    bool markdown)
    {
        if (visited.Contains(node)) return;
        visited.Add(node);

        string indentStr = new string(' ', indent * 2);
        string title = $"{node.Type.FullName} [{node.Attr.Tags}]";

        if (markdown)
        {
            sb.AppendLine($"{indentStr}- **{title}**");
            if (!string.IsNullOrEmpty(node.Comment))
                sb.AppendLine($"{indentStr}  - _{node.Comment}_");
        }
        else
        {
            sb.AppendLine($"{indentStr}{title}");
            if (!string.IsNullOrEmpty(node.Comment))
                sb.AppendLine($"{indentStr}  Comment: {node.Comment}");
        }

        if (node.Exposed.Count > 0)
        {
            sb.AppendLine($"{indentStr}  Exposes: {string.Join(", ", node.Exposed)}");
        }

        foreach (var link in node.Outgoing.OrderBy(l => l.Target.Type.FullName))
        {
            string usesTxt = link.Uses.Count > 0
                ? $" (uses: {string.Join(", ", link.Uses)})"
                : "";

            sb.AppendLine($"{indentStr}  -> Uses {link.Target.Type.FullName}{usesTxt}");
        }

        foreach (var link in node.Outgoing.OrderBy(l => l.Target.Type.FullName))
        {
            WriteNodeRecursive(sb, link.Target, visited, indent + 1, markdown);
        }
    }

    private void ExportMap(bool markdown)
    {
        if (!_cacheBuilt) BuildCache();

        string path = EditorUtility.SaveFilePanel(
            "Export System Map",
            "",
            "SystemMap",
            markdown ? "md" : "txt");

        if (string.IsNullOrEmpty(path))
            return;

        var roots = _cachedNodes
            .Where(n => n.Incoming.Count == 0)
            .Where(n => _viewFilter == MapTag.None || (n.Attr.Tags & _viewFilter) != 0)
            .OrderBy(n => n.Type.FullName)
            .ToList();

        if (roots.Count == 0)
            roots = _cachedNodes
                .Where(n => _viewFilter == MapTag.None || (n.Attr.Tags & _viewFilter) != 0)
                .OrderBy(n => n.Type.FullName)
                .ToList();

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        sb.AppendLine(markdown ? "# System Architecture Map\n" : "SYSTEM ARCHITECTURE MAP\n");

        foreach (var root in roots)
        {
            WriteNodeRecursive(sb, root, new HashSet<CachedNode>(), 0, markdown);
            sb.AppendLine();
        }

        System.IO.File.WriteAllText(path, sb.ToString());
        EditorUtility.RevealInFinder(path);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("View Filter (tags)", EditorStyles.boldLabel);
        _viewFilter = (MapTag)EditorGUILayout.EnumFlagsField(_viewFilter);

        if (GUILayout.Button("Refresh") || !_cacheBuilt) BuildCache();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Export as .txt"))
        {
            ExportMap(false);
        }

        if (GUILayout.Button("Export as .md"))
        {
            ExportMap(true);
        }

        EditorGUILayout.EndHorizontal();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        List<CachedNode> roots = _cachedNodes
            .Where(n => n.Incoming.Count == 0)
            .Where(n => _viewFilter == MapTag.None || (n.Attr.Tags & _viewFilter) != 0)
            .OrderBy(n => n.Type.FullName)
            .ToList();

        if (roots.Count == 0)
            roots = _cachedNodes
                .Where(n => _viewFilter == MapTag.None || (n.Attr.Tags & _viewFilter) != 0)
                .OrderBy(n => n.Type.FullName)
                .ToList();

        foreach (CachedNode root in roots)
        {
            DrawNode(root, 0, new HashSet<CachedNode>(), root.Type.FullName);
        }

        EditorGUILayout.EndScrollView();
    }
    void OnEnable()
    {
        BuildCache();
    }
}
