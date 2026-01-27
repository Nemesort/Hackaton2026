using System;

[MapNode("HudView", MapTag.UI)]
[Depends(typeof(StatsManager), "pv", "pm")]
[Depends(typeof(UIManager), "currentScreen", "isPaused")]
public class HudView
{
}
