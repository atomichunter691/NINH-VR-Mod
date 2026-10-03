using System;
using UnityEngine;
using PauseMenuView = _Code.Infrastructure.Pause.PauseMenuView;

namespace NIVR.Core;

internal static class SessionFocus
{
    public static void Lost()
    {
        if (!VRConfig.PauseOnFocusLoss.Value) return;
        try
        {
            foreach (var view in Resources.FindObjectsOfTypeAll<PauseMenuView>())
            {
                if (view == null || !view.gameObject.scene.IsValid() || view._inputHandler == null) continue;
                if (!view.IsPaused && !view.IsChanging) { view.Switch(true); CorePlugin.Log.LogInfo("Headset focus lost: opened game's pause menu (resume manually)."); }
                return;
            }
        }
        catch (Exception e) { CorePlugin.LogThrottled("focus-pause", "Could not open pause on focus loss: " + e.Message); }
    }
}
