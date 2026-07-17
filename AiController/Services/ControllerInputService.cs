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

    private readonly ControllerProfile _profile;
    private readonly Action<ButtonAction> _onAction;
    private readonly System.Threading.Timer _timer;
    private ushort _prevButtons;
    private bool _prevLeftTrigger;
    private bool _prevRightTrigger;

    public ControllerInputService(ControllerProfile profile, Action<ButtonAction> onAction)
    {
        _profile = profile;
        _onAction = onAction;
        // ~60Hz poll, matching typical game-loop cadence for XInput consumers.
        _timer = new System.Threading.Timer(Poll, null, 0, 16);
    }

    private void Poll(object? state)
    {
        if (XInputGetState(0, out var s) != 0) return; // 0 = ERROR_SUCCESS; nonzero = no controller connected

        var buttons = s.Gamepad.wButtons;
        var pressed = (ushort)(buttons & ~_prevButtons); // edges that just went down
        _prevButtons = buttons;

        DispatchIfPressed(pressed, A, ControllerInput.ButtonA);
        DispatchIfPressed(pressed, B, ControllerInput.ButtonB);
        DispatchIfPressed(pressed, X, ControllerInput.ButtonX);
        DispatchIfPressed(pressed, Y, ControllerInput.ButtonY);
        DispatchIfPressed(pressed, DPAD_UP, ControllerInput.DPadUp);
        DispatchIfPressed(pressed, DPAD_DOWN, ControllerInput.DPadDown);
        DispatchIfPressed(pressed, DPAD_LEFT, ControllerInput.DPadLeft);
        DispatchIfPressed(pressed, DPAD_RIGHT, ControllerInput.DPadRight);
        DispatchIfPressed(pressed, START, ControllerInput.ButtonStart);
        DispatchIfPressed(pressed, BACK, ControllerInput.ButtonBack);
        DispatchIfPressed(pressed, LEFT_SHOULDER, ControllerInput.LeftShoulder);
        DispatchIfPressed(pressed, RIGHT_SHOULDER, ControllerInput.RightShoulder);
        DispatchIfPressed(pressed, LEFT_THUMB, ControllerInput.LeftThumb);
        DispatchIfPressed(pressed, RIGHT_THUMB, ControllerInput.RightThumb);

        // Triggers are analog (0-255), not bitmask buttons -- edge-detect
        // against a threshold the same way XInput's own sample code does.
        bool leftTrigger = s.Gamepad.bLeftTrigger > TRIGGER_THRESHOLD;
        bool rightTrigger = s.Gamepad.bRightTrigger > TRIGGER_THRESHOLD;
        if (leftTrigger && !_prevLeftTrigger) Dispatch(ControllerInput.LeftTrigger);
        if (rightTrigger && !_prevRightTrigger) Dispatch(ControllerInput.RightTrigger);
        _prevLeftTrigger = leftTrigger;
        _prevRightTrigger = rightTrigger;
    }

    private void DispatchIfPressed(ushort pressedMask, ushort bit, ControllerInput input)
    {
        if ((pressedMask & bit) != 0) Dispatch(input);
    }

    private void Dispatch(ControllerInput input)
    {
        var action = _profile.Resolve(input);
        if (action.Type != ActionType.None) _onAction(action);
    }

    public void Dispose() => _timer.Dispose();
}
