using UnityEngine.InputSystem;

namespace MiniParty
{
    /// <summary>
    /// Local 2-player mapping on one PC. P1 = WASD + Space + Left click, P2 = Arrows + Enter + Right click.
    /// In online play each client only drives its own player, so both would use Space / Left click.
    /// </summary>
    public static class Controls
    {
        public static bool Pressed(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;

        public static bool Left(int p) => Pressed(p == 0 ? Key.A : Key.LeftArrow);
        public static bool Right(int p) => Pressed(p == 0 ? Key.D : Key.RightArrow);
        public static bool Up(int p) => Pressed(p == 0 ? Key.W : Key.UpArrow);
        public static bool Down(int p) => Pressed(p == 0 ? Key.S : Key.DownArrow);
        public static bool Confirm(int p) => p == 0 ? Pressed(Key.Space) : Pressed(Key.Enter) || Pressed(Key.NumpadEnter);
        public static bool AnyConfirm => Confirm(0) || Confirm(1);

        public static bool Held(Key k) => Keyboard.current != null && Keyboard.current[k].isPressed;
        public static bool UpHeld(int p) => Held(p == 0 ? Key.W : Key.UpArrow);
        public static bool DownHeld(int p) => Held(p == 0 ? Key.S : Key.DownArrow);
        public static bool LeftHeld(int p) => Held(p == 0 ? Key.A : Key.LeftArrow);
        public static bool RightHeld(int p) => Held(p == 0 ? Key.D : Key.RightArrow);

        static readonly Key[] RowDigits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        static readonly Key[] PadDigits = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };

        /// <summary>Digit 1-9 pressed this frame, or -1. P1 = number row, P2 = numpad; p = -1 accepts both.</summary>
        public static int Digit(int p)
        {
            for (int i = 0; i < 9; i++)
            {
                if (p != 1 && Pressed(RowDigits[i])) return i + 1;
                if (p != 0 && Pressed(PadDigits[i])) return i + 1;
            }
            return -1;
        }

        public static UnityEngine.Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : UnityEngine.Vector2.zero;

        public static bool Click(int p)
        {
            var m = Mouse.current;
            if (m == null) return false;
            return p == 0 ? m.leftButton.wasPressedThisFrame : m.rightButton.wasPressedThisFrame;
        }
    }
}
