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
    public List<string> Exposed = new();
    public List<CachedConsumer> Consumers = new();
}

class CachedConsumer
{
    public string Name;
    public List<string> Uses = new();
}


public class Node
{
    public Type Type { get; set; }
    public MapNodeAttribute Attr { get; set; }
}

public class Consummer
{
    public Type ConsumerType { get; set; }
    public string ConsumerName { get; set; }
    public IEnumerable<DependsAttribute> Deps { get; set; }
}

public class MapWindow : EditorWindow
{
    [MenuItem("Tools/System Map")]
    public static void Open() => GetWindow<MapWindow>("System Map");

    private List<CachedNode> _cachedNodes;
    private bool _cacheBuilt = false;

    private Vector2 _scroll;
    private MapTag _viewFilter = MapTag.Manager;

    private HashSet<Type> _expandedNodes = new HashSet<Type>();

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

    private static bool HasAnyTag(MapTag tags, MapTag filter)
        => filter == MapTag.None || (tags & filter) != 0;

    private void BuildCache()
    {
        _cachedNodes = new List<CachedNode>();

        IEnumerable<Type> allTypes = GetAllTypesSafely();

        List<Node> nodes = allTypes
            .Select(t => new Node { Type = t, Attr = t.GetCustomAttribute<MapNodeAttribute>() })
            .Where(x => x.Attr != null)
            .ToList();

        Dictionary<Type, MapNodeAttribute> nodeByType = nodes.ToDictionary(n => n.Type, n => n.Attr);

        foreach (Node node in nodes)
        {
            CachedNode cached = new CachedNode
            {
                Type = node.Type,
                Attr = node.Attr
            };

            // Exposed
            cached.Exposed = node.Type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<ExposedAttribute>() != null)
                .Select(m =>
                {
                    ExposedAttribute ex = m.GetCustomAttribute<ExposedAttribute>();
                    return string.IsNullOrWhiteSpace(ex.Alias) ? m.Name : ex.Alias;
                })
                .Distinct()
                .ToList();

            _cachedNodes.Add(cached);
        }

        // Consumers (fait une seule fois, pas N² par frame)
        foreach (CachedNode cached in _cachedNodes)
        {
            Type targetType = cached.Type;

            foreach (CachedNode other in _cachedNodes)
            {
                DependsAttribute[] deps = other.Type
                    .GetCustomAttributes<DependsAttribute>()
                    .Where(d => d.TargetType == targetType)
                    .ToArray();

                if (deps.Length == 0) continue;

                CachedConsumer consumer = new CachedConsumer
                {
                    Name = other.Attr.DisplayName,
                    Uses = deps.SelectMany(d => d.Uses)
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .Distinct()
                               .ToList()
                };

                cached.Consumers.Add(consumer);
            }
        }

        _cacheBuilt = true;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("View Filter (tags)", EditorStyles.boldLabel);
        _viewFilter = (MapTag)EditorGUILayout.EnumFlagsField(_viewFilter);

        if (GUILayout.Button("Refresh") || !_cacheBuilt) BuildCache();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        foreach (CachedNode node in _cachedNodes
             .Where(n => HasAnyTag(n.Attr.Tags, _viewFilter))
             .OrderBy(n => n.Attr.DisplayName))
        {
            EditorGUILayout.LabelField($"{node.Attr.DisplayName}  [{node.Attr.Tags}]", EditorStyles.boldLabel);

                if (node.Exposed.Count > 0)
                    EditorGUILayout.LabelField("Exposes: " + string.Join(", ", node.Exposed));

                foreach (CachedConsumer c in node.Consumers.OrderBy(c => c.Name))
                {
                    string usesTxt = c.Uses.Count > 0 ? $" (uses: {string.Join(", ", c.Uses)})" : "";
                EditorGUILayout.LabelField($"   └ {c.Name}{usesTxt}");
            }

            EditorGUILayout.Space();
        }

        EditorGUILayout.EndScrollView();
    }
    void OnEnable()
    {
        BuildCache();
    }
}
