namespace AiController.Services;

/// <summary>
/// Analog-stick deadzone + rest-drift correction, plus a pre-click cursor
/// "settle" nudge -- ports two related Linux fixes:
///
/// 1. settle_wiggle.sh's tiny mousemove-and-back immediately before a native
///    click: forces the window manager to recompute hover state so the click
///    lands on what's actually under the cursor rather than a stale hover
///    target left over from wherever the cursor was moved from last. Ported
///    here as the static Settle() helper -- call it right before
///    InputInjector.GuardedMouse for a button bound to a MouseAction.
///
/// 2. A rolling per-axis center correction for sticks whose physical rest
///    position isn't exactly (0, 0). A fixed XInput deadzone alone doesn't fix
///    a stick that idles at, say, (400, -150) instead of (0, 0) -- it just
///    makes the drift ship *inside* the deadzone until it doesn't. Observe()
///    only folds a sample into the learned center while the raw stick is
///    already within the nominal deadzone (i.e. presumed at rest); an active
///    push is never averaged in, so calibration can't chase real input.
/// </summary>
public sealed class DriftCalibrator
{
    private const int HistorySize = 60; // ~1s of samples at the 60Hz XInput poll rate
    private const short DeadzoneRadius = 7849; // matches XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE

    private readonly Queue<(short x, short y)> _restHistory = new();
    // Running sums -- Observe() used to call Queue.Average() over the whole
    // window every single tick (an O(HistorySize) rescan 60 times a second for
    // no reason); maintaining a running total makes each call O(1) instead.
    private double _sumX;
    private double _sumY;
    private double _centerX;
    private double _centerY;

    /// <summary>Feed one raw stick sample per poll tick.</summary>
    public void Observe(short rawX, short rawY)
    {
        if (Math.Abs(rawX) > DeadzoneRadius || Math.Abs(rawY) > DeadzoneRadius) return;

        _restHistory.Enqueue((rawX, rawY));
        _sumX += rawX;
        _sumY += rawY;
        if (_restHistory.Count > HistorySize)
        {
            var (oldX, oldY) = _restHistory.Dequeue();
            _sumX -= oldX;
            _sumY -= oldY;
        }

        _centerX = _sumX / _restHistory.Count;
        _centerY = _sumY / _restHistory.Count;
    }

    /// <summary>Apply the learned center offset and deadzone to a raw sample,
    /// returning a corrected (x, y) each in [-1, 1], or (0, 0) if still within
    /// the deadzone once re-centered.</summary>
    public (double x, double y) Correct(short rawX, short rawY)
    {
        var x = rawX - _centerX;
        var y = rawY - _centerY;
        var magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude < DeadzoneRadius) return (0, 0);

        var scale = Math.Min(1.0, (magnitude - DeadzoneRadius) / (short.MaxValue - DeadzoneRadius));
        return (x / magnitude * scale, y / magnitude * scale);
    }

    /// <summary>Nudge the cursor 1px and back -- the same fix as
    /// settle_wiggle.sh's paired mousemove_relative calls. Call immediately
    /// before synthesizing a click.</summary>
    public static void Settle()
    {
        var pos = System.Windows.Forms.Cursor.Position;
        System.Windows.Forms.Cursor.Position = new System.Drawing.Point(pos.X + 1, pos.Y);
        System.Windows.Forms.Cursor.Position = pos;
    }
}
