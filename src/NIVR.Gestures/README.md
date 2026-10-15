# NIVR.Gestures

Gesture recognition for the NIVR motion controls. Pure C# on `System.Numerics`: no Unity, no game types, no BepInEx.
That is the point of the project: it is the only part of the mod that can be built and tested without the game, so
CI (`.github/workflows/gestures.yml`) runs its tests on every change.

```
dotnet test src/NIVR.Gestures.Tests/NIVR.Gestures.Tests.csproj
```

## Conventions

Metres, seconds, Y up, angular velocity in radians per second. Nothing depends on handedness. A recognizer is a small
object fed once per frame with `dt` and the values it needs; it has no clock, no statics and no knowledge of what the
gesture will be used for. The caller decides what "in the zone" means (see `Zones`) and what to do when it fires.
Every threshold is a public property with a default; the defaults are first guesses that have **not** been tuned in a
headset.

## What is in it

| Type | Recognizes | Fires |
|---|---|---|
| `Zones` | Sphere, box (axis-aligned, padded, rotated) and plane distance tests | - |
| `VelocityFilter` | Smoothed velocity from positions, for trackers that report none | - |
| `PushPullRecognizer` | A hand moved 10 cm along a surface normal after touching it; sideways drift cancels | `Push` / `Pull`, once per engagement |
| `GrabHoldRecognizer` | Grip closed inside a zone and kept closed; a fist carried in does not grab; the grab survives leaving the zone | `Grabbed`, `HoldCompleted`, `Released` |
| `StrokeRecognizer` | A gentle slide along a surface, back and forth allowed; fast movement resets | `true` each 25 cm slid |
| `ThrowRecognizer` | Grip opened while moving fast; velocity averaged over the last 0.1 s | `true` + release velocity |
| `LeanInRecognizer` | Head close to a point while facing into it, with a dwell and a larger exit radius | `Entered` / `Exited` |
| `HeadGestureRecognizer` | Nod and shake as runs of swings on one axis; ignores jitter, single looks and slow looking around | `Nod` / `Shake`, then a cooldown |
| `RaiseHandsRecognizer` | Both hands at chest height or above, in front, apart, still | `Raised` / `Lowered` |

## Using it from the plugin (not done yet)

The plugin uses `UnityEngine.Vector3`; this library uses `System.Numerics.Vector3`. A motion module converts at the
boundary (three floats each way). The release zip is allow-listed to four files, so when the first module uses the
library, either compile these sources into `NIVR.Core` with a linked `<Compile Include>` (no new dll, recommended) or
add `NIVR.Gestures.dll` to the allow-list in `plugin/Core/package.ps1`.
