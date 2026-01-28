using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class Map2Window : EditorWindow
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

    [MenuItem("Tools/System Map 2")]
    public static void Open() => GetWindow<Map2Window>("System Map 2");

    private List<CachedNode> _cachedNodes;
    private bool _cacheBuilt = false;

    private Vector2 _scroll;
    private MapTag _viewFilter = MapTag.Manager;

    private HashSet<string> _expandedPaths = new HashSet<string>();

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
    private void CollectPaths(CachedNode node, string path, HashSet<CachedNode> visited)
    {
        if (visited.Contains(node)) return;
        visited.Add(node);

        _expandedPaths.Add(path);

        foreach (var link in node.Outgoing)
        {
            string childPath = path + "/" + link.Target.Type.FullName;
            CollectPaths(link.Target, childPath, new HashSet<CachedNode>(visited));
        }
    }

    private bool DrawFoldout(CachedNode node, string path)
    {
        bool isExpanded = _expandedPaths.Contains(path);

        GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldout)
        {
            fontStyle = FontStyle.Bold
        };

        GUIContent label = new GUIContent(
            $"{node.Type.FullName}  [{node.Attr.Tags}]",
            node.Comment ?? ""
        );

        bool newExpanded = EditorGUILayout.Foldout(
            isExpanded,
            label,
            true,
            foldoutStyle
        );

        if (newExpanded != isExpanded)
        {
            if (newExpanded) _expandedPaths.Add(path);
            else _expandedPaths.Remove(path);
        }

        return newExpanded;
    }

    void DrawNode(CachedNode node, int indent, HashSet<CachedNode> visited, string path)
    {
        if (visited.Contains(node)) return;
        visited.Add(node);

        int previousIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = indent;

        bool expanded = DrawFoldout(node, path);

        if (!expanded) return;

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
    int indent)
    {
        if (visited.Contains(node)) return;
        visited.Add(node);

        string indentStr = new string(' ', indent * 4);
        string title = $"{node.Type.FullName} [{node.Attr.Tags}]";

        sb.AppendLine($"{indentStr} - **{title}**");
        if (!string.IsNullOrEmpty(node.Comment))
            sb.AppendLine($"{indentStr}  - _{node.Comment}_");

        if (node.Exposed.Count > 0)
        {
            sb.AppendLine($"{indentStr}  Exposes: {string.Join(", ", node.Exposed)}");
        }

        foreach (NodeLink link in node.Outgoing.OrderBy(l => l.Target.Type.FullName))
        {
            string usesTxt = link.Uses.Count > 0
                ? $" (uses: {string.Join(", ", link.Uses)})"
                : "";

            sb.AppendLine($"{indentStr}  -> Uses {link.Target.Type.FullName}{usesTxt}");
        }

        foreach (NodeLink link in node.Outgoing.OrderBy(l => l.Target.Type.FullName))
        {
            WriteNodeRecursive(sb, link.Target, visited, indent + 1);
        }
    }

    private void ExportMap()
    {
        if (!_cacheBuilt) BuildCache();

        string path = EditorUtility.SaveFilePanel(
            "Nemad Viewer Exported Map",
            "",
            "Nemad Viewer Exported Map",
             "md");

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

        sb.AppendLine("# Nemad Viewer Map\n");

        foreach (var root in roots)
        {
            WriteNodeRecursive(sb, root, new HashSet<CachedNode>(), 0);
            sb.AppendLine();
        }

        System.IO.File.WriteAllText(path, sb.ToString());
        EditorUtility.RevealInFinder(path);
    }

    void OnGUI()
    {
        List<CachedNode> roots;

        //Refresh
        EditorGUILayout.Space(10);
        if (GUILayout.Button("Refresh") || !_cacheBuilt) BuildCache();

        // Exports
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);
        if (GUILayout.Button("Export as .md"))
        {
            ExportMap();
        }

        // Filters
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("View Filter (tags)", EditorStyles.boldLabel);
        _viewFilter = (MapTag)EditorGUILayout.EnumFlagsField(_viewFilter);

        // Accordions
        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Expand All"))
        {
            _expandedPaths.Clear();

           roots = _cachedNodes.Where(n => n.Incoming.Count == 0).ToList();
            if (roots.Count == 0)
                roots = _cachedNodes;

            foreach (CachedNode root in roots)
            {
                CollectPaths(root, root.Type.FullName, new HashSet<CachedNode>());
            }
        }

        if (GUILayout.Button("Collapse All"))
            _expandedPaths.Clear();
        EditorGUILayout.EndHorizontal();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        roots = _cachedNodes
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
