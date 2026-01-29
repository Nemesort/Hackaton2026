using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

class SystemMapIssue
{
    public string Title;
    public string Details;
}

class SystemMapGraph
{
    public List<SystemMapNode> Nodes = new List<SystemMapNode>();
    public Dictionary<Type, HashSet<Type>> Edges = new Dictionary<Type, HashSet<Type>>();
    public List<SystemMapIssue> Issues = new List<SystemMapIssue>();
    public HashSet<(Type From, Type To)> CyclicEdges = new HashSet<(Type, Type)>();
}

class SystemMapNode
{
    public Type Type;
    public MapNodeAttribute Attr;
    public string Comment;
    public List<string> Exposed = new List<string>();
    public List<SystemMapLink> Outgoing = new List<SystemMapLink>();
    public List<SystemMapLink> Incoming = new List<SystemMapLink>();
}

class SystemMapLink
{
    public SystemMapNode Target;
    public List<string> Uses = new List<string>();
}

public class Node
{
    public Type Type { get; set; }
    public MapNodeAttribute Attr { get; set; }
}

public class MapWindow : EditorWindow
{
    [MenuItem("Tools/Nemad System Viewer")]
    public static void Open()
    {
        GetWindow<MapWindow>("Nemad System Viewer");
    }

    private enum Tab
    {
        Hierarchy = 0,
        Problems = 1
    }

    private SystemMapGraph _graph;
    private bool _cacheBuilt;

    private Vector2 _scrollHierarchy;
    private Vector2 _scrollProblems;

    private MapTag _viewFilter = MapTag.Manager;
    private Tab _tab = Tab.Hierarchy;

    private HashSet<string> _expandedPaths = new HashSet<string>();
    private bool _reverseView = false;

    private IEnumerable<Type> GetAllTypesSafely()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Assembly assembly = assemblies[i];

            IEnumerable<Type> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null);
            }

            foreach (Type type in types)
            {
                yield return type;
            }
        }
    }

    private static List<string> GetExposed(Type type)
    {
        return type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<ExposedAttribute>() != null)
            .Select(m =>
            {
                ExposedAttribute ex = m.GetCustomAttribute<ExposedAttribute>();
                return string.IsNullOrWhiteSpace(ex.Alias) ? m.Name : ex.Alias;
            })
            .Distinct()
            .ToList();
    }

    private void BuildCache()
    {
        _graph = BuildGraph();
        _cacheBuilt = true;

        if (_graph != null && _graph.Issues.Count == 0 && _tab == Tab.Problems)
            _tab = Tab.Hierarchy;
    }

    private SystemMapGraph BuildGraph()
    {
        SystemMapGraph graph = new SystemMapGraph();

        IEnumerable<Type> allTypes = GetAllTypesSafely();

        List<Node> nodes = allTypes
            .Select(t => new Node
            {
                Type = t,
                Attr = t.GetCustomAttribute<MapNodeAttribute>() ?? new MapNodeAttribute(t.Name, MapTag.None)
            })
            .ToList();

        nodes = nodes
            .Where(n => n.Type.IsClass)
            .Where(n => !n.Type.IsAbstract)
            .Where(n => n.Type.Namespace == null || !n.Type.Namespace.StartsWith("Unity", StringComparison.Ordinal))
            .Where(n => n.Type.Namespace == null || !n.Type.Namespace.StartsWith("System", StringComparison.Ordinal))
            .Where(n => n.Type.Namespace == null || !n.Type.Namespace.StartsWith("Microsoft", StringComparison.Ordinal))
            .ToList();

        HashSet<Type> nodeTypes = new HashSet<Type>(nodes.Select(n => n.Type));

        Dictionary<Type, SystemMapNode> nodeByType = new Dictionary<Type, SystemMapNode>();

        for (int i = 0; i < nodes.Count; i++)
        {
            Node n = nodes[i];

            SystemMapNode node = new SystemMapNode();
            node.Type = n.Type;
            node.Attr = n.Attr;

            MapNodeComment commentAttr = n.Type.GetCustomAttribute<MapNodeComment>();
            node.Comment = commentAttr != null ? commentAttr.Comment : null;

            node.Exposed = GetExposed(n.Type);

            nodeByType[n.Type] = node;
            graph.Nodes.Add(node);
        }

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            SystemMapNode t = graph.Nodes[i];
            t.Outgoing.Clear();
            t.Incoming.Clear();
        }

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            SystemMapNode consumerNode = graph.Nodes[i];
            Type consumerType = consumerNode.Type;

            for (int j = 0; j < graph.Nodes.Count; j++)
            {
                SystemMapNode targetNode = graph.Nodes[j];
                Type targetType = targetNode.Type;

                if (consumerType == targetType)
                    continue;

                bool depends = DependsOnTypeByReflection(consumerType, targetType);
                if (!depends)
                    continue;

                AddEdge(graph.Edges, consumerType, targetType);

                SystemMapLink outLink = new SystemMapLink();
                outLink.Target = targetNode;
                outLink.Uses = new List<string>();

                SystemMapLink inLink = new SystemMapLink();
                inLink.Target = consumerNode;
                inLink.Uses = outLink.Uses;

                consumerNode.Outgoing.Add(outLink);
                targetNode.Incoming.Add(inLink);
            }
        }

        DetectMutualDependencies(graph);
        DetectCycles(graph);

        return graph;
    }

    private static void AddEdge(Dictionary<Type, HashSet<Type>> edges, Type from, Type to)
    {
        HashSet<Type> set;
        bool found = edges.TryGetValue(from, out set);
        if (!found)
        {
            set = new HashSet<Type>();
            edges[from] = set;
        }
        set.Add(to);
    }

    private static bool DependsOnTypeByReflection(Type consumerType, Type targetType)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        FieldInfo[] fields = consumerType.GetFields(flags);
        for (int i = 0; i < fields.Length; i++)
        {
            if (IsTargetOrAssignable(fields[i].FieldType, targetType))
                return true;
        }

        PropertyInfo[] props = consumerType.GetProperties(flags);
        for (int i = 0; i < props.Length; i++)
        {
            if (IsTargetOrAssignable(props[i].PropertyType, targetType))
                return true;
        }

        ConstructorInfo[] ctors = consumerType.GetConstructors(flags);
        for (int i = 0; i < ctors.Length; i++)
        {
            ParameterInfo[] ps = ctors[i].GetParameters();
            for (int k = 0; k < ps.Length; k++)
            {
                if (IsTargetOrAssignable(ps[k].ParameterType, targetType))
                    return true;
            }
        }

        MethodInfo[] methods = consumerType.GetMethods(flags);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo m = methods[i];
            if (m.IsSpecialName)
                continue;

            ParameterInfo[] ps = m.GetParameters();
            for (int k = 0; k < ps.Length; k++)
            {
                if (IsTargetOrAssignable(ps[k].ParameterType, targetType))
                    return true;
            }
        }

        return false;
    }

    private static bool IsTargetOrAssignable(Type candidate, Type targetType)
    {
        if (candidate == targetType)
            return true;
        if (targetType.IsAssignableFrom(candidate))
            return true;
        return false;
    }

    private static void DetectMutualDependencies(SystemMapGraph graph)
    {
        HashSet<string> seen = new HashSet<string>();

        foreach (KeyValuePair<Type, HashSet<Type>> kv in graph.Edges)
        {
            Type a = kv.Key;
            HashSet<Type> depsA = kv.Value;

            foreach (Type b in depsA)
            {
                HashSet<Type> depsB;
                bool ok = graph.Edges.TryGetValue(b, out depsB);
                if (!ok)
                    continue;

                if (!depsB.Contains(a))
                    continue;

                string key1 = a.FullName + "->" + b.FullName;
                string key2 = b.FullName + "->" + a.FullName;

                if (seen.Contains(key1) || seen.Contains(key2))
                    continue;

                seen.Add(key1);

                SystemMapIssue issue = new SystemMapIssue();
                issue.Title = "Mutual dependency (A ↔ B)";
                issue.Details = GetDisplayName(graph, a) + " ↔ " + GetDisplayName(graph, b);
                graph.Issues.Add(issue);
            }
        }
    }

    private static void DetectCycles(SystemMapGraph graph)
    {
        Dictionary<Type, int> state = new Dictionary<Type, int>();
        Stack<Type> stack = new Stack<Type>();
        HashSet<string> unique = new HashSet<string>();

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            Type t = graph.Nodes[i].Type;
            if (!state.ContainsKey(t))
                state[t] = 0;
        }

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            Type start = graph.Nodes[i].Type;
            int st;
            state.TryGetValue(start, out st);
            if (st != 0)
                continue;

            DfsCycle(graph, start, state, stack, unique);
        }
    }

    private static void DfsCycle(SystemMapGraph graph, Type node, Dictionary<Type, int> state, Stack<Type> stack, HashSet<string> unique)
    {
        state[node] = 1;
        stack.Push(node);

        HashSet<Type> next;
        bool ok = graph.Edges.TryGetValue(node, out next);
        if (ok)
        {
            foreach (Type to in next)
            {
                int st;
                bool has = state.TryGetValue(to, out st);
                if (!has)
                {
                    state[to] = 0;
                    st = 0;
                }

                if (st == 0)
                {
                    DfsCycle(graph, to, state, stack, unique);
                }
                else if (st == 1)
                {
                    List<Type> cycle = ExtractCycle(stack, to);
                    string sig = BuildCycleSignature(cycle);
                    if (!unique.Contains(sig))
                    {
                        unique.Add(sig);

                        SystemMapIssue issue = new SystemMapIssue();
                        issue.Title = "Cycle detected";
                        issue.Details = BuildCycleDisplay(graph, cycle);
                        graph.Issues.Add(issue);

                        for (int i = 0; i < cycle.Count - 1; i++)
                        {
                            graph.CyclicEdges.Add((cycle[i], cycle[i + 1]));
                        }
                    }
                }
            }
        }

        stack.Pop();
        state[node] = 2;
    }

    private static List<Type> ExtractCycle(Stack<Type> stack, Type entry)
    {
        Type[] arr = stack.ToArray();
        List<Type> cycle = new List<Type>();

        for (int i = 0; i < arr.Length; i++)
        {
            Type t = arr[i];
            cycle.Add(t);
            if (t == entry)
                break;
        }

        cycle.Reverse();
        cycle.Add(entry);
        return cycle;
    }

    private static string BuildCycleSignature(List<Type> cycle)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < cycle.Count; i++)
        {
            names.Add(cycle[i].FullName);
        }
        return string.Join("->", names);
    }

    private static string BuildCycleDisplay(SystemMapGraph graph, List<Type> cycle)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < cycle.Count; i++)
        {
            names.Add(GetDisplayName(graph, cycle[i]));
        }
        return string.Join(" → ", names);
    }

    private static string GetDisplayName(SystemMapGraph graph, Type type)
    {
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            SystemMapNode n = graph.Nodes[i];
            if (n.Type == type)
                return n.Attr.DisplayName;
        }
        return type.Name;
    }

    private void CollectPaths(SystemMapNode node, string path, HashSet<SystemMapNode> visited)
    {
        if (visited.Contains(node))
            return;

        visited.Add(node);
        _expandedPaths.Add(path);

        for (int i = 0; i < node.Outgoing.Count; i++)
        {
            SystemMapLink link = node.Outgoing[i];
            string childPath = path + "/" + link.Target.Type.FullName;
            CollectPaths(link.Target, childPath, new HashSet<SystemMapNode>(visited));
        }
    }

    private bool DrawFoldout(SystemMapNode node, string path)
    {
        bool isExpanded = _expandedPaths.Contains(path);

        GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldout);
        foldoutStyle.fontStyle = FontStyle.Bold;

        GUIContent label = new GUIContent(
            node.Type.FullName + "  [" + node.Attr.Tags + "]",
            node.Comment ?? ""
        );

        bool newExpanded = EditorGUILayout.Foldout(isExpanded, label, true, foldoutStyle);

        if (newExpanded != isExpanded)
        {
            if (newExpanded)
                _expandedPaths.Add(path);
            else
                _expandedPaths.Remove(path);
        }

        return newExpanded;
    }

    private void DrawNode(SystemMapNode node, int indent, HashSet<SystemMapNode> visited, string path)
    {
        if (visited.Contains(node))
            return;

        visited.Add(node);

        int previousIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = indent;

        bool expanded = DrawFoldout(node, path);
        if (!expanded)
        {
            EditorGUI.indentLevel = previousIndent;
            return;
        }

        if (node.Exposed.Count > 0)
            EditorGUILayout.LabelField("Exposes: " + string.Join(", ", node.Exposed));

        List<SystemMapLink> links = (!_reverseView ? node.Outgoing : node.Incoming)
            .OrderBy(l => l.Target.Type.FullName)
            .ToList();

        for (int i = 0; i < links.Count; i++)
        {
            SystemMapLink link = links[i];
            string usesTxt = link.Uses.Count > 0 ? " (uses: " + string.Join(", ", link.Uses) + ")" : "";

            bool isCycleEdge = _graph.CyclicEdges.Contains((node.Type, link.Target.Type));

            GUIStyle style = new GUIStyle(EditorStyles.label);
            if (isCycleEdge)
                style.normal.textColor = Color.red;

            string prefix = _reverseView ? "Used by: " : "Uses: ";
            EditorGUILayout.LabelField(prefix + link.Target.Type.FullName + usesTxt, style);
        }

        for (int i = 0; i < links.Count; i++)
        {
            SystemMapLink link = links[i];
            DrawNode(link.Target, indent + 2, new HashSet<SystemMapNode>(visited), path + "/" + link.Target.Type.FullName);
        }

        EditorGUI.indentLevel = previousIndent;
    }

    private void WriteNodeRecursive(System.Text.StringBuilder sb, SystemMapNode node, HashSet<SystemMapNode> visited, int indent)
    {
        if (visited.Contains(node))
            return;

        visited.Add(node);

        string indentStr = new string(' ', indent * 4);
        string title = node.Type.FullName + " [" + node.Attr.Tags + "]";

        sb.AppendLine(indentStr + "- **" + title + "**");

        if (!string.IsNullOrEmpty(node.Comment))
            sb.AppendLine(indentStr + "  - _" + node.Comment + "_");

        if (node.Exposed.Count > 0)
            sb.AppendLine(indentStr + "  Exposes: " + string.Join(", ", node.Exposed));

        List<SystemMapLink> links = node.Outgoing.OrderBy(l => l.Target.Type.FullName).ToList();

        for (int i = 0; i < links.Count; i++)
        {
            SystemMapLink link = links[i];
            string usesTxt = link.Uses.Count > 0 ? " (uses: " + string.Join(", ", link.Uses) + ")" : "";
            sb.AppendLine(indentStr + "  -> Uses " + link.Target.Type.FullName + usesTxt);
        }

        for (int i = 0; i < links.Count; i++)
        {
            WriteNodeRecursive(sb, links[i].Target, visited, indent + 1);
        }
    }

    private List<SystemMapNode> GetRoots()
    {
        IEnumerable<SystemMapNode> query;

        if (!_reverseView)
            query = _graph.Nodes.Where(n => n.Incoming.Count == 0);
        else
            query = _graph.Nodes.Where(n => n.Outgoing.Count == 0);

        List<SystemMapNode> roots = query
            .Where(n => _viewFilter == MapTag.None || (n.Attr.Tags & _viewFilter) != 0)
            .OrderBy(n => n.Type.FullName)
            .ToList();

        if (roots.Count == 0)
        {
            roots = _graph.Nodes
                .Where(n => _viewFilter == MapTag.None || (n.Attr.Tags & _viewFilter) != 0)
                .OrderBy(n => n.Type.FullName)
                .ToList();
        }

        return roots;
    }

    private void ExportMap()
    {
        if (!_cacheBuilt)
            BuildCache();

        if (_graph == null)
            return;

        string path = EditorUtility.SaveFilePanel(
            "Nemad Exported System",
            "",
            "Nemad Exported System",
            "md"
        );

        if (string.IsNullOrEmpty(path))
            return;

        List<SystemMapNode> roots = GetRoots();

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("# Nemad System Viewer\n");

        for (int i = 0; i < roots.Count; i++)
        {
            WriteNodeRecursive(sb, roots[i], new HashSet<SystemMapNode>(), 0);
            sb.AppendLine();
        }

        System.IO.File.WriteAllText(path, sb.ToString());
        EditorUtility.RevealInFinder(path);
    }

    private void OnGUI()
    {
        if (!_cacheBuilt)
            BuildCache();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("View Filter (tags)", EditorStyles.boldLabel);
        _viewFilter = (MapTag)EditorGUILayout.EnumFlagsField(_viewFilter);
        _reverseView = EditorGUILayout.ToggleLeft("Reverse view (start from parents, does not display cycles)", _reverseView);

        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh"))
            BuildCache();

        bool hasIssues = _graph != null && _graph.Issues.Count > 0;
        string problemsLabel = hasIssues ? "Problems (" + _graph.Issues.Count + ")" : "Problems";
        int selected = GUILayout.Toolbar((int)_tab, new string[] { "Hierarchy", problemsLabel });
        _tab = (Tab)selected;

        if (!hasIssues && _tab == Tab.Problems)
            _tab = Tab.Hierarchy;

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        if (_tab == Tab.Hierarchy)
        {
            EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);
            if (GUILayout.Button("Export as .md"))
                ExportMap();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Expand All"))
            {
                _expandedPaths.Clear();

                if (_graph != null)
                {
                    List<SystemMapNode> roots = GetRoots();
                    for (int i = 0; i < roots.Count; i++)
                    {
                        CollectPaths(roots[i], roots[i].Type.FullName, new HashSet<SystemMapNode>());
                    }
                }
            }

            if (GUILayout.Button("Collapse All"))
                _expandedPaths.Clear();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            DrawHierarchy();
        }
        else
        {
            DrawProblems();
        }
    }

    private void DrawHierarchy()
    {
        _scrollHierarchy = EditorGUILayout.BeginScrollView(_scrollHierarchy);

        if (_graph != null)
        {
            List<SystemMapNode> roots = GetRoots();

            for (int i = 0; i < roots.Count; i++)
            {
                DrawNode(roots[i], 0, new HashSet<SystemMapNode>(), roots[i].Type.FullName);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawProblems()
    {
        _scrollProblems = EditorGUILayout.BeginScrollView(_scrollProblems);

        if (_graph == null || _graph.Issues.Count == 0)
        {
            EditorGUILayout.LabelField("No issues detected.");
        }
        else
        {
            for (int i = 0; i < _graph.Issues.Count; i++)
            {
                SystemMapIssue issue = _graph.Issues[i];
                EditorGUILayout.LabelField(issue.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(issue.Details);
                EditorGUILayout.Space();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void OnEnable()
    {
        BuildCache();
    }
}
