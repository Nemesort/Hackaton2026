using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

public class MapWindow : EditorWindow
{
    [MenuItem("Tools/System Map")]
    public static void Open() => GetWindow<MapWindow>("System Map");

    private Vector2 _scroll;
    private MapTag _viewFilter = MapTag.Manager;

    private IEnumerable<Type> GetAllTypesSafely()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var a in assemblies)
        {
            Type[] types = null;
            try { types = a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in types) yield return t;
        }
    }

    private static bool HasAnyTag(MapTag tags, MapTag filter)
        => filter == MapTag.None ? true : (tags & filter) != 0;

    void OnGUI()
    {
        EditorGUILayout.LabelField("View Filter (tags)", EditorStyles.boldLabel);
        _viewFilter = (MapTag)EditorGUILayout.EnumFlagsField(_viewFilter);

        if (GUILayout.Button("Refresh")) Repaint();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        var allTypes = GetAllTypesSafely().ToArray();

        var nodes = allTypes
            .Select(t => new { Type = t, Attr = t.GetCustomAttribute<MapNodeAttribute>() })
            .Where(x => x.Attr != null)
            .ToList();

        var filteredNodes = nodes
            .Where(n => HasAnyTag(n.Attr.Tags, _viewFilter))
            .ToList();

        var nodeByType = filteredNodes.ToDictionary(n => n.Type, n => n.Attr);

        foreach (var node in filteredNodes.OrderBy(n => n.Attr.DisplayName))
        {
            var t = node.Type;
            var a = node.Attr;

            EditorGUILayout.LabelField($"{a.DisplayName}  [{a.Tags}]", EditorStyles.boldLabel);

            var exposed = t.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<ExposedAttribute>() != null)
                .Select(m =>
                {
                    var ex = m.GetCustomAttribute<ExposedAttribute>();
                    return string.IsNullOrWhiteSpace(ex.Alias) ? m.Name : ex.Alias;
                })
                .Distinct()
                .ToList();

            if (exposed.Count > 0)
                EditorGUILayout.LabelField("  Exposes: " + string.Join(", ", exposed));

            var consumers = filteredNodes
                .Select(n => new
                {
                    ConsumerType = n.Type,
                    ConsumerName = n.Attr.DisplayName,
                    Deps = n.Type.GetCustomAttributes<DependsAttribute>()
                        .Where(d => d.TargetType == t)
                        .ToArray()
                })
                .Where(x => x.Deps.Length > 0)
                .ToList();

            foreach (var c in consumers.OrderBy(x => x.ConsumerName))
            {
                var uses = c.Deps.SelectMany(d => d.Uses).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
                var usesTxt = uses.Count > 0 ? $" (uses: {string.Join(", ", uses)})" : "";
                EditorGUILayout.LabelField($"   └ {c.ConsumerName}{usesTxt}");
            }

            EditorGUILayout.Space();
        }

        EditorGUILayout.EndScrollView();
    }
}
