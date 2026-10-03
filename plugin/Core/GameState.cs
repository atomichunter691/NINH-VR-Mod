using System;
using UnityEngine;
using UnityEngine.InputSystem;
using InputHandling = _Code.Player.InputHandling;
using WatcherManager = _Code.Player.WatcherManager;
using EWatcherState = _Code.Player.EWatcherState;

namespace NIVR.Core;

/// <summary>What the game is doing right now, read from its own input / state objects.</summary>
internal static class GameState
{
    private static InputHandling s_input;
    private static WatcherManager s_watcher;
    private static float s_nextScan;

    public static InputHandling Input { get { Scan(); return s_input; } }
    public static WatcherManager Watcher { get { Scan(); return s_watcher; } }

    private static _Code.Infrastructure.CloseUps.Views.Radio.RadioCloseUpView s_watcherOwner;

    private static void Scan()
    {
        // The watcher is a plain object: forget it once the scene object it came from is gone (back to the menu).
        if (s_watcher != null && s_watcherOwner == null) s_watcher = null;
        if ((s_input != null && s_watcher != null) || Time.unscaledTime < s_nextScan) return;
        s_nextScan = Time.unscaledTime + 1f;
        if (s_input == null)
            foreach (var ih in Resources.FindObjectsOfTypeAll<InputHandling>())
                if (ih != null && ih.gameObject.scene.IsValid()) { s_input = ih; break; }
        if (s_watcher == null)
            foreach (var r in Resources.FindObjectsOfTypeAll<_Code.Infrastructure.CloseUps.Views.Radio.RadioCloseUpView>())
                if (r != null && r.gameObject.scene.IsValid() && r._watcherManager != null) { s_watcher = r._watcherManager; s_watcherOwner = r; break; }
    }

    /// <summary>The game's current screen state (top of the watcher stack); null if unknown.</summary>
    public static EWatcherState? Top
    {
        get
        {
            try
            {
                var w = Watcher;
                if (w == null || w._states == null || w._states.Count == 0) return null;
                return w._states.Peek();
            }
            catch (Exception) { return null; }
        }
    }

    public static string Describe()
    {
        try
        {
            var ih = Input;
            string s = $"top={(Top.HasValue ? Top.Value.ToString() : "?")}";
            if (ih != null)
            {
                s += $" device={ih.CurrentDevice} scheme={(ih._playerInput != null ? ih._playerInput.currentControlScheme : "?")} inUi={ih._inUiCounter} gpCursorState={ih._gamepadCursorActiveState}";
                var gc = ih._gamepadCursor;
                s += $" radioKnob={ih.UIRadioKnob} look={ih.LookInput} move={ih.MoveInput}";
                if (gc != null) s += $" gpCursor=({gc._cursorPosition.x:F0},{gc._cursorPosition.y:F0}) enabled={gc._isEnabled} active={gc._isActive}";
            }
            var m = Mouse.current;
            if (m != null) s += $" mouse=({m.position.ReadValue().x:F0},{m.position.ReadValue().y:F0}) lmb={m.leftButton.isPressed}";
            s += $" cursor={Cursor.lockState}/{(Cursor.visible ? "visible" : "hidden")}";
            try { var lp = UnityEngine.Input.mousePosition; s += $" legacyMouse=({lp.x:F0},{lp.y:F0})"; }
            catch (Exception) { s += " legacyMouse=disabled"; }
            return s;
        }
        catch (Exception e) { return "game state unavailable: " + e.Message; }
    }
}
