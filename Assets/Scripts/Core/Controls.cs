using UnityEngine.InputSystem;

namespace MiniParty
{
    /// <summary>
    /// Raw key sets. P1 set = WASD + Space + Left click, P2 set = Arrows + Enter + Right click.
    /// One PC drives one human player, who may use either set (see MiniGameBase input helpers).
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

        static readonly Key[] AllRow = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        static readonly Key[] AllPad = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };

        /// <summary>Digit 0-9 pressed this frame on the number row or numpad, or -1.</summary>
        public static int AnyDigit()
        {
            for (int i = 0; i < 10; i++)
                if (Pressed(AllRow[i]) || Pressed(AllPad[i])) return i;
            return -1;
        }

        public static bool Backspace => Pressed(Key.Backspace) || Pressed(Key.Delete);

        public static UnityEngine.Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : UnityEngine.Vector2.zero;

        public static bool Click(int p)
        {
            var m = Mouse.current;
            if (m == null) return false;
            return p == 0 ? m.leftButton.wasPressedThisFrame : m.rightButton.wasPressedThisFrame;
        }
    }
}
