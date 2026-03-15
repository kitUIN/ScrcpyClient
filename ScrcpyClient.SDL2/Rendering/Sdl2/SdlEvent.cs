using System.Runtime.InteropServices;

namespace ScrcpyClient.Rendering.Sdl2;

/// <summary>
/// Overlapping layout covering SDL_Event union (56 bytes).
/// Fields are valid only when 'type' matches the corresponding SDL event type.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 56)]
public struct SdlEvent
{
    [FieldOffset(0)] public uint type;

    // SDL_MouseButtonEvent / SDL_MouseMotionEvent fields
    [FieldOffset(12)] public uint which;
    [FieldOffset(16)] public byte button;
    [FieldOffset(17)] public byte state;
    [FieldOffset(18)] public byte clicks;
    [FieldOffset(20)] public int mouseX;
    [FieldOffset(24)] public int mouseY;

    // SDL_KeyboardEvent fields (overlapping with mouse fields at same offsets)
    // keysym.sym (Sint32) is at offset 20, same as mouseX
    [FieldOffset(12)] public byte keyState;    // SDL_PRESSED=1 / SDL_RELEASED=0
    [FieldOffset(13)] public byte keyRepeat;   // nonzero = auto-repeat
    [FieldOffset(20)] public int  sdlKeycode;  // keysym.sym
    [FieldOffset(24)] public ushort keyMod;    // keysym.mod (SDL_Keymod)

    // SDL_TextInputEvent: text[32] (UTF-8) starts at offset 12.
    // Read via MemoryMarshal.AsBytes on a span of this struct, then slice [12..44].
}
