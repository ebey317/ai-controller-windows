using System.Runtime.InteropServices;
using AiController.Models;

namespace AiController.Services;

/// <summary>
/// XInput polling loop -- the Windows equivalent of antimicrox+uinput on the
/// Linux build (~/ai-controller/profiles/dont delete .gamecontroller.amgp)
/// and InputMapper.kt+ControllerAccessibilityService.kt on Android. XInput
/// is Xbox-controller-specific, matching this app's primary target hardware
/// (the Linux app's own README: "wired Xbox Series X/S controller"); broader
/// PlayStation/DualShock/generic-USB support the Linux app claims would need
/// DirectInput/HID on top of this later -- not attempted in this pass.
///
/// This is a plain polling loop, not an event-driven hook: XInput has no
/// native "subscribe to button events" API, so the standard pattern (used by
/// essentially every XInput consumer on Windows) is to poll at a fixed
/// interval and diff against the previous state to find press/release edges.
///
/// W4 fix: this used to only ever compute the pressed-edge mask
/// (`buttons & ~prevButtons`) and never the released-edge mask
/// (`prevButtons & ~buttons`), so nothing downstream could tell when a held
/// button came back up. That's the one signal a hold-to-talk PTT flow needs
/// (PttController.OnRelease) and this now fires it for every button and both
/// analog triggers, not just presses.
/// </summary>
public sealed class ControllerInputService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetState(int dwUserIndex, out XINPUT_STATE pState);

    // XINPUT_GAMEPAD_* button bitmasks (documented, stable Win32 constants).
    private const ushort DPAD_UP = 0x0001;
    private const ushort DPAD_DOWN = 0x0002;
    private const ushort DPAD_LEFT = 0x0004;
    private const ushort DPAD_RIGHT = 0x0008;
    private const ushort START = 0x0010;
    private const ushort BACK = 0x0020;
    private const ushort LEFT_THUMB = 0x0040;
    private const ushort RIGHT_THUMB = 0x0080;
    private const ushort LEFT_SHOULDER = 0x0100;
    private const ushort RIGHT_SHOULDER = 0x0200;
    private const ushort A = 0x1000;
    private const ushort B = 0x2000;
    private const ushort X = 0x4000;
    private const ushort Y = 0x8000;

    private const byte TRIGGER_THRESHOLD = 30; // XINPUT_GAMEPAD_TRIGGER_THRESHOLD

    private readonly Action<ButtonAction> _onPress;
    private readonly Action<ButtonAction> _onRelease;
    private readonly DriftCalibrator _drift = new();
    private readonly System.Threading.Timer _timer;

    // F3 fix: Dispatch used to re-resolve ActiveProfile on release, so a
    // profile/context switch between a press and its release fired whatever the
    // NEW profile mapped that input to, not what was actually pressed. The
    // resolved action is now latched on press and reused on release.
    private readonly Dictionary<ControllerInput, ButtonAction> _latchedActions = new();
    private ushort _prevButtons;
    private bool _prevLeftTrigger;
    private bool _prevRightTrigger;

    /// <summary>The profile currently resolving button->action. Settable so
    /// ContextSwitcher can hot-swap it when the foreground app's context changes,
    /// without tearing down and recreating the whole polling loop.</summary>
    public ControllerProfile ActiveProfile { get; set; }

    public ControllerInputService(ControllerProfile initialProfile, Action<ButtonAction> onPress, Action<ButtonAction> onRelease)
    {
        ActiveProfile = initialProfile;
        _onPress = onPress;
        _onRelease = onRelease;
        // ~60Hz poll, matching typical game-loop cadence for XInput consumers.
        _timer = new System.Threading.Timer(Poll, null, 0, 16);
    }

    // Cursor speed for the left-stick-as-mouse feature DriftCalibrator's Correct()
    // exists to serve -- pixels per poll tick (~60Hz) at full deflection.
    private const double CursorSpeedPxPerTick = 8.0;

    private void Poll(object? state)
    {
        if (XInputGetState(0, out var s) != 0) return; // 0 = ERROR_SUCCESS; nonzero = no controller connected

        // F6 fix: Observe() fed the calibrator's learned-center history, but
        // nothing ever called Correct() to actually use it -- the left stick
        // could never move the cursor at all. Observe-then-correct, in that
        // order, so the correction always reflects samples already folded in
        // and never a sample from the future.
        _drift.Observe(s.Gamepad.sThumbLX, s.Gamepad.sThumbLY);
        var (cx, cy) = _drift.Correct(s.Gamepad.sThumbLX, s.Gamepad.sThumbLY);
        if (cx != 0 || cy != 0) MoveCursorBy(cx, cy);

        var buttons = s.Gamepad.wButtons;
        var pressed = (ushort)(buttons & ~_prevButtons); // edges that just went down
        var released = (ushort)(_prevButtons & ~buttons); // edges that just came up
        _prevButtons = buttons;

        DispatchIfSet(pressed, A, ControllerInput.ButtonA, isPress: true);
        DispatchIfSet(pressed, B, ControllerInput.ButtonB, isPress: true);
        DispatchIfSet(pressed, X, ControllerInput.ButtonX, isPress: true);
        DispatchIfSet(pressed, Y, ControllerInput.ButtonY, isPress: true);
        DispatchIfSet(pressed, DPAD_UP, ControllerInput.DPadUp, isPress: true);
        DispatchIfSet(pressed, DPAD_DOWN, ControllerInput.DPadDown, isPress: true);
        DispatchIfSet(pressed, DPAD_LEFT, ControllerInput.DPadLeft, isPress: true);
        DispatchIfSet(pressed, DPAD_RIGHT, ControllerInput.DPadRight, isPress: true);
        DispatchIfSet(pressed, START, ControllerInput.ButtonStart, isPress: true);
        DispatchIfSet(pressed, BACK, ControllerInput.ButtonBack, isPress: true);
        DispatchIfSet(pressed, LEFT_SHOULDER, ControllerInput.LeftShoulder, isPress: true);
        DispatchIfSet(pressed, RIGHT_SHOULDER, ControllerInput.RightShoulder, isPress: true);
        DispatchIfSet(pressed, LEFT_THUMB, ControllerInput.LeftThumb, isPress: true);
        DispatchIfSet(pressed, RIGHT_THUMB, ControllerInput.RightThumb, isPress: true);

        DispatchIfSet(released, A, ControllerInput.ButtonA, isPress: false);
        DispatchIfSet(released, B, ControllerInput.ButtonB, isPress: false);
        DispatchIfSet(released, X, ControllerInput.ButtonX, isPress: false);
        DispatchIfSet(released, Y, ControllerInput.ButtonY, isPress: false);
        DispatchIfSet(released, DPAD_UP, ControllerInput.DPadUp, isPress: false);
        DispatchIfSet(released, DPAD_DOWN, ControllerInput.DPadDown, isPress: false);
        DispatchIfSet(released, DPAD_LEFT, ControllerInput.DPadLeft, isPress: false);
        DispatchIfSet(released, DPAD_RIGHT, ControllerInput.DPadRight, isPress: false);
        DispatchIfSet(released, START, ControllerInput.ButtonStart, isPress: false);
        DispatchIfSet(released, BACK, ControllerInput.ButtonBack, isPress: false);
        DispatchIfSet(released, LEFT_SHOULDER, ControllerInput.LeftShoulder, isPress: false);
        DispatchIfSet(released, RIGHT_SHOULDER, ControllerInput.RightShoulder, isPress: false);
        DispatchIfSet(released, LEFT_THUMB, ControllerInput.LeftThumb, isPress: false);
        DispatchIfSet(released, RIGHT_THUMB, ControllerInput.RightThumb, isPress: false);

        // Triggers are analog (0-255), not bitmask buttons -- edge-detect
        // against a threshold the same way XInput's own sample code does.
        bool leftTrigger = s.Gamepad.bLeftTrigger > TRIGGER_THRESHOLD;
        bool rightTrigger = s.Gamepad.bRightTrigger > TRIGGER_THRESHOLD;
        if (leftTrigger && !_prevLeftTrigger) Dispatch(ControllerInput.LeftTrigger, isPress: true);
        if (!leftTrigger && _prevLeftTrigger) Dispatch(ControllerInput.LeftTrigger, isPress: false);
        if (rightTrigger && !_prevRightTrigger) Dispatch(ControllerInput.RightTrigger, isPress: true);
        if (!rightTrigger && _prevRightTrigger) Dispatch(ControllerInput.RightTrigger, isPress: false);
        _prevLeftTrigger = leftTrigger;
        _prevRightTrigger = rightTrigger;
    }

    private static void MoveCursorBy(double correctedX, double correctedY)
    {
        var pos = System.Windows.Forms.Cursor.Position;
        var dx = (int)Math.Round(correctedX * CursorSpeedPxPerTick);
        // Screen Y+ is down; XInput's stick Y+ is "pushed up" -- invert to match.
        var dy = (int)Math.Round(-correctedY * CursorSpeedPxPerTick);
        if (dx == 0 && dy == 0) return;
        System.Windows.Forms.Cursor.Position = new System.Drawing.Point(pos.X + dx, pos.Y + dy);
    }

    private void DispatchIfSet(ushort edgeMask, ushort bit, ControllerInput input, bool isPress)
    {
        if ((edgeMask & bit) != 0) Dispatch(input, isPress);
    }

    private void Dispatch(ControllerInput input, bool isPress)
    {
        if (isPress)
        {
            var action = ActiveProfile.Resolve(input);
            if (action is NullAction) return;
            _latchedActions[input] = action;
            _onPress(action);
        }
        else
        {
            // Reuse whatever was latched on the matching press -- never re-resolve
            // against whatever profile happens to be active now. If nothing was
            // latched (e.g. the press resolved to NullAction, or press happened
            // before this service started), there is nothing to release.
            if (!_latchedActions.Remove(input, out var action)) return;
            _onRelease(action);
        }
    }

    public void Dispose() => _timer.Dispose();
}
