namespace NIVR.Core;

/// <summary>
/// Buttons still held when the input mode changes (gamepad &lt;-&gt; virtual mouse / keyboard) are ignored until they
/// are let go, so one press is never seen twice: Menu opens the pause screen as gamepad Start and must not then
/// close it again as Escape; the trigger that clicks "Continue" must not then interact with a door.
/// </summary>
internal static class ButtonGate
{
    private const int Buttons = (int)VRButton.StickDown + 1;
    private static readonly bool[][] s_held = { new bool[Buttons], new bool[Buttons] };

    /// <summary>Call once per frame before anything reads buttons.</summary>
    public static void Update(bool modeChanged)
    {
        for (int h = 0; h < 2; h++)
        {
            var c = VRRig.Controller((VRHand)h);
            for (int b = 0; b < Buttons; b++)
            {
                bool down = c.GetButton((VRButton)b);
                if (modeChanged) s_held[h][b] = down;
                else if (!down) s_held[h][b] = false;
            }
        }
    }

    public static bool Down(VRController c, VRButton b) => c.GetButton(b) && !s_held[(int)c.Hand][(int)b];

    public static float Analog(VRController c, VRButton b, float value) => s_held[(int)c.Hand][(int)b] ? 0f : value;
}
