namespace AiController.Models;

/// <summary>
/// Discrete controller inputs this app can bind an action to. Named to match
/// the vocabulary already established in the Android app's ControllerInput
/// (~/projects/ai-controller-android/.../models/ControllerInput.kt) --
/// same product concept, independently implemented per platform, but no
/// reason to invent different names for the same physical buttons.
/// </summary>
public enum ControllerInput
{
    ButtonA, ButtonB, ButtonX, ButtonY,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    ButtonStart, ButtonBack,
    LeftShoulder, RightShoulder,
    LeftThumb, RightThumb,
    LeftTrigger, RightTrigger,
}

/// <summary>Mirrors the Android app's ActionType -- the set of things a bound input can trigger.</summary>
public enum ActionType
{
    KeyEvent,
    MouseClick,
    VoiceTrigger,
    ShowKeyboard,
    LaunchApp,
    None,
}

/// <summary>A single configured action bound to a controller input.</summary>
public record ButtonAction(ActionType Type, string? KeyText = null)
{
    public static ButtonAction None() => new(ActionType.None);
    public static ButtonAction VoiceTrigger() => new(ActionType.VoiceTrigger);
    public static ButtonAction ShowKeyboard() => new(ActionType.ShowKeyboard);
    public static ButtonAction LaunchApp() => new(ActionType.LaunchApp);
    public static ButtonAction MouseClick() => new(ActionType.MouseClick);
    public static ButtonAction KeyEvent(string keyText) => new(ActionType.KeyEvent, keyText);
}

/// <summary>Button-to-action bindings. Not yet backed by a settings UI (Phase 2) -- this is a plain in-memory default for now.</summary>
public class ControllerProfile
{
    public Dictionary<ControllerInput, ButtonAction> Mappings { get; } = new()
    {
        [ControllerInput.ButtonA] = ButtonAction.MouseClick(),
        [ControllerInput.ButtonY] = ButtonAction.LaunchApp(),
        [ControllerInput.RightTrigger] = ButtonAction.VoiceTrigger(),
        [ControllerInput.ButtonBack] = ButtonAction.ShowKeyboard(),
    };

    public ButtonAction Resolve(ControllerInput input) =>
        Mappings.TryGetValue(input, out var action) ? action : ButtonAction.None();
}
