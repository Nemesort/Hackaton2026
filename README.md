# Nemad System Viewer

Nemad System Viewer is an editor tool that analyzes code dependencies between annotated systems and displays them in a interactive hierarchy graph inside Unity.

It helps you:

- Visualize who depends on whom
- Detect cycles and mutual dependencies
- Understand large architectures faster
- Document system responsibilities


### System Requirements

Unity **6000.0** or later versions.


#### Installation

Using Unity's `Package Mannager`, you can install the package using the following git URL:
```
https://github.com/Nemesort/Hackaton2026.git#package
```

### Quick Start

#### Mark a class as a node

```csharp
[MapNode("Game Manager", MapTag.Manager)]
public class GameManager { }
```


#### Expose public variables

```csharp
[MapNode("Game Manager", MapTag.Manager)]
public class GameManager {
    [Exposed] public void StartGame() { }
    [Exposed] public int Health;
}
```


#### Add a comment

```csharp
[MapNode("Game Manager", MapTag.Manager)]
[MapNodeComment("Coordinator of the gameplay loop")]
public class GameManager {
    [Exposed] public void StartGame() { }
    [Exposed] public int Health;
}
```


### Using the Graph

To open the graph, go to Tools -> Nemad System Viewer


#### Understanding the Graph

Each node represents a class marked with `[MapNode]`.

Connections mean:
- **Uses ->** This system depends on another system
- **Used By <-** (Reverse view) Other systems depend on this one

Displayed information:
- Node name (from MapNode)
- Tags (Manager, System, etc.)
- Exposed members (public API marked with `[Exposed]`)

Comments are showed on label hover.

##### Window Controls

| Button | Description |
|-------|-------------|
| Refresh | Rebuilds the system graph |
| Expand All | Expands the full hierarchy |
| Collapse All | Collapses all nodes |
| Export as .md | Exports the current hierarchy as Markdown |

##### Display Cyclic Issues

In the`hierarchy` tab, class that have cyclic dependencies are highlighted in red.
The `Problem` tab offer more details about those issues in your architecture.

###### How Dependencies Are Detected

Nemad System Viewer uses reflection to detect references between systems.

A dependency is detected when a class uses another class as:

- A field
- A property
- A constructor parameter
- A method parameter

Only classes marked with `[MapNode]` are included in the graph.
