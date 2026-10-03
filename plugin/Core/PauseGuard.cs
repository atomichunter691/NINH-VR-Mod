using System;
using UnityEngine;
using EWatcherState = _Code.Player.EWatcherState;
using PauseMenuView = _Code.Infrastructure.Pause.PauseMenuView;

namespace NIVR.Core;

/// <summary>
/// Keeps the pause screen from trapping the player. Observed with a gamepad (verified in the simulator): B on the
/// bare pause screen half-closes it (cursor re-locked, pause still shown) and the next "Continue" or Menu then leaves
/// the game's UI-state counter above zero with the pause screen up, so nothing can be moved, looked at or used.
///   * B is not passed on while the pause screen itself is on top (B still leaves Settings and sub-screens).
///   * Menu on the pause screen closes it; if the game has not done so shortly after, it is closed directly.
///   * If the UI-state counter stays above zero in the hallway with no pause screen, or a "Pause" state stays on the
///     watcher stack after the screen has closed, the leftovers are cleared.
/// </summary>
internal static class PauseGuard
{
    private static PauseMenuView s_view;
    private static float s_nextScan, s_menuAt, s_uiStuckSince, s_pauseStaleSince;

    /// <summary>B must not reach the game right now.</summary>
    public static bool SwallowEast { get; private set; }

    private static PauseMenuView View()
    {
        if (s_view != null && s_view.gameObject.scene.IsValid()) return s_view;
        s_view = null;
        if (Time.unscaledTime < s_nextScan) return null;
        s_nextScan = Time.unscaledTime + 1f;
        foreach (var v in Resources.FindObjectsOfTypeAll<PauseMenuView>())
            if (v != null && v.gameObject.scene.IsValid() && v._inputHandler != null) { s_view = v; break; }
        return s_view;
    }

    /// <summary>Once per frame, before the virtual gamepad is fed.</summary>
    public static void Tick(bool active)
    {
        SwallowEast = false;
        try
        {
            var view = View();
            var top = GameState.Top;
            var ih = GameState.Input;
            if (!active || view == null) { s_menuAt = s_uiStuckSince = s_pauseStaleSince = 0f; return; }
            float now = Time.unscaledTime;
            bool paused = view.IsPaused;

            SwallowEast = top == EWatcherState.Pause && paused;

            // Menu on the pause screen: the game closes it; close it directly if it did not.
            if (top == EWatcherState.Pause && paused && !view.IsChanging)
            {
                if (ButtonGate.Down(VRRig.LeftController, VRButton.Menu) && s_menuAt == 0f) s_menuAt = now;
                if (s_menuAt != 0f && now - s_menuAt > 0.7f)
                {
                    s_menuAt = 0f;
                    CorePlugin.Log.LogInfo("Pause guard: the pause screen ignored Menu; closing it directly.");
                    view.Switch(false);
                }
            }
            else s_menuAt = 0f;

            // A "Pause" state left on the watcher stack after the screen closed.
            bool staleState = top == EWatcherState.Pause && !paused && !view.IsChanging;
            if (staleState)
            {
                if (s_pauseStaleSince == 0f) s_pauseStaleSince = now;
                else if (now - s_pauseStaleSince > 2.5f)
                {
                    s_pauseStaleSince = 0f;
                    CorePlugin.Log.LogWarning("Pause guard: stale Pause state after the screen closed; clearing it.");
                    GameState.Watcher?.LeaveState(EWatcherState.Pause);
                }
            }
            else s_pauseStaleSince = 0f;

            // UI-state counter stuck above zero in the hallway.
            bool uiStuck = ih != null && top == EWatcherState.World3d && !paused && !view.IsChanging && ih._inUiCounter > 0
                           && Cursor.lockState == CursorLockMode.Locked;
            if (uiStuck)
            {
                if (s_uiStuckSince == 0f) s_uiStuckSince = now;
                else if (now - s_uiStuckSince > 2.5f)
                {
                    s_uiStuckSince = 0f;
                    CorePlugin.Log.LogWarning($"Pause guard: UI-state counter stuck at {ih._inUiCounter} in the hallway; releasing it.");
                    for (int i = 0; i < 4 && ih._inUiCounter > 0; i++) ih.SetIsInUIState(false);
                }
            }
            else s_uiStuckSince = 0f;
        }
        catch (Exception e) { CorePlugin.LogThrottled("pause-guard", "Pause guard: " + e.Message); }
    }

    /// <summary>Debug: raise the UI counter without a screen to see the recovery work.</summary>
    internal static void DebugTrap() { GameState.Input?.SetIsInUIState(true); GameState.Input?.SetIsInUIState(true); }
}
