using System.Text.Json.Serialization;

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

public enum MouseButton { Left, Right, Middle }

public enum MouseActionKind { Click, DoubleClick, Down, Up }

/// <summary>Hold-to-talk mirrors the physical PTT gesture (press, speak, release);
/// Toggle mirrors this app's original press-once/press-again dictation flow.</summary>
public enum VoiceMode { PushToTalk, Toggle }

/// <summary>Simple = the plain grid keyboard (OnScreenKeyboardWindow); Slide = the
/// mode-chips + pinned-snippets keyboard (SlideKeyboard), mirroring slide_keyboard.py.</summary>
public enum KeyboardMode { Simple, Slide }

/// <summary>
/// A single configured action bound to a controller input.
///
/// W1 fix: this used to be a flat `record ButtonAction(ActionType Type, string?
/// KeyText)` -- a single optional string field that could hold a key name OR an
/// app target but never both, and had no field at all for a mouse button/kind or
/// a voice/keyboard mode. Saving a mapping to disk and reloading it silently
/// dropped whatever didn't fit that one field. This polymorphic hierarchy gives
/// each action kind its own strongly-typed data, and System.Text.Json's
/// [JsonDerivedType] discriminator ("type") round-trips the concrete subtype
/// through ProfileStore without a hand-rolled converter.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyAction), "key")]
[JsonDerivedType(typeof(MouseAction), "mouse")]
[JsonDerivedType(typeof(VoiceAction), "voice")]
[JsonDerivedType(typeof(KeyboardAction), "keyboard")]
[JsonDerivedType(typeof(LaunchAction), "launch")]
[JsonDerivedType(typeof(NullAction), "none")]
public abstract class ButtonAction
{
    public static NullAction None() => new();
}

/// <summary>Sends a named key (by virtual-key name, e.g. "Enter", "Tab") with an
/// optional modifier ("Shift", "Ctrl", "Alt").</summary>
public sealed class KeyAction : ButtonAction
{
    public string KeyText { get; set; }
    public string? Modifier { get; set; }

    public KeyAction() : this("") { }
    public KeyAction(string keyText, string? modifier = null)
    {
        KeyText = keyText;
        Modifier = modifier;
    }
}

/// <summary>Synthesizes a mouse button press of the given kind at the current cursor position.</summary>
public sealed class MouseAction : ButtonAction
{
    public MouseButton Button { get; set; }
    public MouseActionKind Kind { get; set; }

    public MouseAction() : this(MouseButton.Left, MouseActionKind.Click) { }
    public MouseAction(MouseButton button, MouseActionKind kind)
    {
        Button = button;
        Kind = kind;
    }
}

/// <summary>Drives PttController: starts/stops voice dictation.</summary>
public sealed class VoiceAction : ButtonAction
{
    public VoiceMode Mode { get; set; }

    public VoiceAction() : this(VoiceMode.Toggle) { }
    public VoiceAction(VoiceMode mode)
    {
        Mode = mode;
    }
}

/// <summary>Toggles one of the on-screen keyboard windows.</summary>
public sealed class KeyboardAction : ButtonAction
{
    public KeyboardMode Mode { get; set; }

    public KeyboardAction() : this(KeyboardMode.Simple) { }
    public KeyboardAction(KeyboardMode mode)
    {
        Mode = mode;
    }
}

/// <summary>Launches a named installed app directly, or opens the launcher search
/// UI when AppTarget is empty.</summary>
public sealed class LaunchAction : ButtonAction
{
    public string AppTarget { get; set; }

    public LaunchAction() : this("") { }
    public LaunchAction(string appTarget)
    {
        AppTarget = appTarget;
    }
}

/// <summary>No binding -- Dispatch() skips these entirely.</summary>
public sealed class NullAction : ButtonAction
{
}

/// <summary>Button-to-action bindings, persisted per context by ProfileStore (see
/// ContextSwitcher: desktop/browser/IPTV each load their own named profile).</summary>
public class ControllerProfile
{
    public Dictionary<ControllerInput, ButtonAction> Mappings { get; set; } = new()
    {
        [ControllerInput.ButtonA] = new MouseAction(MouseButton.Left, MouseActionKind.Click),
        [ControllerInput.ButtonY] = new LaunchAction(""),
        [ControllerInput.RightTrigger] = new VoiceAction(VoiceMode.Toggle),
        [ControllerInput.ButtonBack] = new KeyboardAction(KeyboardMode.Slide),
    };

    public ButtonAction Resolve(ControllerInput input) =>
        Mappings.TryGetValue(input, out var action) ? action : ButtonAction.None();
}
