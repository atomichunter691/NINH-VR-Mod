using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace NIVR;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "nivr.plugin";
    public const string Name = "NIVR";
    public const string Version = "0.1.0";

    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        Log.LogInfo($"NIVR hello world - Unity {Application.unityVersion}, game {Application.productName} {Application.version}");
    }
}
