using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

class CachedNode
{
    public Type Type;
    public MapNodeAttribute Attr;
    public List<string> Exposed = new List<string>();
    public List<CachedConsumer> Consumers = new List<CachedConsumer>();
}

class CachedConsumer
{
    public string Name;
    public List<string> Uses = new List<string>();
}

class SystemMapIssue
{
    public string Title;
    public string Details;
}

class SystemMapGraph
{
    public List<CachedNode> Nodes = new List<CachedNode>();
    public Dictionary<Type, HashSet<Type>> Edges = new Dictionary<Type, HashSet<Type>>();
    public List<SystemMapIssue> Issues = new List<SystemMapIssue>();
}

public class Node
{
    public Type Type { get; set; }
    public MapNodeAttribute Attr { get; set; }
}

public class MapWindow : EditorWindow
{
    [MenuItem("Tools/System Map")]
    public static void Open()
    {
        GetWindow<MapWindow>("System Map");
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

    private HashSet<Type> _expandedNodes = new HashSet<Type>();

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

    private static bool HasAnyTag(MapTag tags, MapTag filter)
    {
        return filter == MapTag.None || (tags & filter) != 0;
    }

    private void BuildCache()
    {
        _graph = BuildGraph();
        _cacheBuilt = true;
        if (_graph != null && _graph.Issues.Count == 0 && _tab == Tab.Problems)
        {
            _tab = Tab.Hierarchy;
        }
    }

    private SystemMapGraph BuildGraph()
    {
        SystemMapGraph graph = new SystemMapGraph();

        IEnumerable<Type> allTypes = GetAllTypesSafely();

        List<Node> nodes = allTypes
            .Select(t => new Node { Type = t, Attr = t.GetCustomAttribute<MapNodeAttribute>() })
            .Where(n => n.Attr != null)
            .ToList();

        HashSet<Type> nodeTypes = new HashSet<Type>(nodes.Select(n => n.Type));

        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];

            CachedNode cached = new CachedNode();
            cached.Type = node.Type;
            cached.Attr = node.Attr;

            List<string> exposed = node.Type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<ExposedAttribute>() != null)
                .Select(m =>
                {
                    ExposedAttribute ex = m.GetCustomAttribute<ExposedAttribute>();
                    return string.IsNullOrWhiteSpace(ex.Alias) ? m.Name : ex.Alias;
                })
                .Distinct()
                .ToList();

            cached.Exposed = exposed;

            graph.Nodes.Add(cached);
        }

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            graph.Nodes[i].Consumers.Clear();
        }

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            CachedNode target = graph.Nodes[i];
            Type targetType = target.Type;

            for (int j = 0; j < graph.Nodes.Count; j++)
            {
                CachedNode consumer = graph.Nodes[j];
                Type consumerType = consumer.Type;

                if (consumerType == targetType)
                    continue;

                bool depends = DependsOnTypeByReflection(consumerType, targetType, nodeTypes);
                if (!depends)
                    continue;

                CachedConsumer c = new CachedConsumer();
                c.Name = consumer.Attr.DisplayName;
                c.Uses = new List<string>();

                target.Consumers.Add(c);

                AddEdge(graph.Edges, consumerType, targetType);
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

    private static bool DependsOnTypeByReflection(Type consumerType, Type targetType, HashSet<Type> nodeTypes)
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
            CachedNode n = graph.Nodes[i];
            if (n.Type == type)
                return n.Attr.DisplayName;
        }
        return type.Name;
    }

    private void OnGUI()
    {
        if (!_cacheBuilt)
            BuildCache();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("View Filter (tags)", EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        _viewFilter = (MapTag)EditorGUILayout.EnumFlagsField(_viewFilter);

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh"))
        {
            BuildCache();
        }

<<<<<<< HEAD
        bool hasIssues = _graph != null && _graph.Issues.Count > 0;
        string problemsLabel = hasIssues ? "Problems (" + _graph.Issues.Count + ")" : "Problems";
        int selected = GUILayout.Toolbar((int)_tab, new string[] { "Hierarchy", problemsLabel });
        _tab = (Tab)selected;
        if (!hasIssues && _tab == Tab.Problems)
        {
            _tab = Tab.Hierarchy;
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        if (_tab == Tab.Hierarchy)
        {
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
            IEnumerable<CachedNode> filtered = _graph.Nodes
                .Where(n => HasAnyTag(n.Attr.Tags, _viewFilter))
                .OrderBy(n => n.Attr.DisplayName);

            foreach (CachedNode node in filtered)
            {
                EditorGUILayout.LabelField(node.Attr.DisplayName + "  [" + node.Attr.Tags + "]", EditorStyles.boldLabel);
=======
                if (node.Exposed.Count > 0)
                    EditorGUILayout.LabelField("Exposes: " + string.Join(", ", node.Exposed));

                foreach (CachedConsumer c in node.Consumers.OrderBy(c => c.Name))
                {
                    string usesTxt = c.Uses.Count > 0 ? $" (uses: {string.Join(", ", c.Uses)})" : "";
                EditorGUILayout.LabelField($"   └ {c.Name}{usesTxt}");
            }
>>>>>>> origin/UI

                if (node.Exposed.Count > 0)
                    EditorGUILayout.LabelField("  Exposes: " + string.Join(", ", node.Exposed));

                IEnumerable<CachedConsumer> consumers = node.Consumers.OrderBy(c => c.Name);
                foreach (CachedConsumer c in consumers)
                    EditorGUILayout.LabelField("   └ " + c.Name);

                EditorGUILayout.Space();
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
}
