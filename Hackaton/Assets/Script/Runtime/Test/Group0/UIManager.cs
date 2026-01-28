using System;

[MapNode("UIManager", MapTag.Manager | MapTag.UI)]
public class UIManager
{
    [Exposed] public string currentScreen;
    [Exposed] public bool isPaused;
}
