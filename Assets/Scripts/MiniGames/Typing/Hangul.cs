using System.Collections.Generic;
using System.Text;

namespace MiniParty
{
    /// <summary>
    /// Built-in 두벌식 Korean input, so typing doesn't depend on the Windows IME (whose 한/영 toggle often
    /// does nothing inside a Unity window). Keys are stored as a list of jamo / letters and the text is
    /// rebuilt from scratch each time, which makes Backspace remove one jamo just like a real IME.
    /// </summary>
    public static class Hangul
    {
        const string Cho = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        const string Jung = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
        const string Jong = "\0ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

        // qwerty letter → jamo (two-set layout); shift only changes ㅂㅈㄷㄱㅅㅐㅔ
        const string Plain = "ㅁㅠㅊㅇㄷㄹㅎㅗㅑㅓㅏㅣㅡㅜㅐㅔㅂㄱㄴㅅㅕㅍㅈㅌㅛㅋ"; // a..z
        static readonly Dictionary<char, char> Shifted = new Dictionary<char, char>
        {
            { 'q', 'ㅃ' }, { 'w', 'ㅉ' }, { 'e', 'ㄸ' }, { 'r', 'ㄲ' }, { 't', 'ㅆ' }, { 'o', 'ㅒ' }, { 'p', 'ㅖ' },
        };

        static readonly Dictionary<(char, char), char> Vowels = new Dictionary<(char, char), char>
        {
            { ('ㅗ', 'ㅏ'), 'ㅘ' }, { ('ㅗ', 'ㅐ'), 'ㅙ' }, { ('ㅗ', 'ㅣ'), 'ㅚ' },
            { ('ㅜ', 'ㅓ'), 'ㅝ' }, { ('ㅜ', 'ㅔ'), 'ㅞ' }, { ('ㅜ', 'ㅣ'), 'ㅟ' }, { ('ㅡ', 'ㅣ'), 'ㅢ' },
        };

        static readonly Dictionary<(char, char), char> Finals = new Dictionary<(char, char), char>
        {
            { ('ㄱ', 'ㅅ'), 'ㄳ' }, { ('ㄴ', 'ㅈ'), 'ㄵ' }, { ('ㄴ', 'ㅎ'), 'ㄶ' }, { ('ㄹ', 'ㄱ'), 'ㄺ' },
            { ('ㄹ', 'ㅁ'), 'ㄻ' }, { ('ㄹ', 'ㅂ'), 'ㄼ' }, { ('ㄹ', 'ㅅ'), 'ㄽ' }, { ('ㄹ', 'ㅌ'), 'ㄾ' },
            { ('ㄹ', 'ㅍ'), 'ㄿ' }, { ('ㄹ', 'ㅎ'), 'ㅀ' }, { ('ㅂ', 'ㅅ'), 'ㅄ' },
        };

        public static bool IsKorean(string s)
        {
            foreach (char c in s)
                if (c >= 0xAC00 && c <= 0xD7A3) return true;
            return false;
        }

        /// <summary>Jamo for a qwerty letter (a-z), or the letter itself if it has none.</summary>
        public static char ToJamo(char letter, bool shift)
        {
            letter = char.ToLowerInvariant(letter);
            if (letter < 'a' || letter > 'z') return letter;
            if (shift && Shifted.TryGetValue(letter, out var s)) return s;
            return Plain[letter - 'a'];
        }

        static bool IsVowel(char c) => Jung.IndexOf(c) >= 0;
        static bool IsConsonant(char c) => Cho.IndexOf(c) >= 0 || (c != '\0' && Jong.IndexOf(c) > 0);

        /// <summary>Builds text from keys. <paramref name="composing"/> is the syllable still being typed.</summary>
        public static void Compose(IList<char> keys, out string done, out string composing)
        {
            var sb = new StringBuilder();
            char cho = '\0', jung = '\0', jong = '\0';

            void Flush()
            {
                sb.Append(Build(cho, jung, jong));
                cho = jung = jong = '\0';
            }

            foreach (char c in keys)
            {
                if (IsVowel(c))
                {
                    if (jong != '\0')
                    {
                        // the final consonant moves to the next syllable (split compound finals)
                        char carry = jong;
                        jong = '\0';
                        foreach (var pair in Finals)
                            if (pair.Value == carry) { jong = pair.Key.Item1; carry = pair.Key.Item2; break; }
                        Flush();
                        cho = carry;
                        jung = c;
                    }
                    else if (jung != '\0')
                    {
                        if (Vowels.TryGetValue((jung, c), out var v)) jung = v;
                        else { Flush(); jung = c; }
                    }
                    else jung = c;
                }
                else if (IsConsonant(c))
                {
                    if (jung == '\0' || cho == '\0')
                    {
                        if (cho != '\0' || jung != '\0') Flush();
                        cho = c;
                    }
                    else if (jong == '\0')
                    {
                        if (Jong.IndexOf(c) > 0) jong = c;
                        else { Flush(); cho = c; }
                    }
                    else if (Finals.TryGetValue((jong, c), out var f)) jong = f;
                    else { Flush(); cho = c; }
                }
                else
                {
                    Flush();
                    sb.Append(c);
                }
            }
            done = sb.ToString();
            composing = Build(cho, jung, jong);
        }

        static string Build(char cho, char jung, char jong)
        {
            if (cho != '\0' && jung != '\0')
            {
                int code = 0xAC00 + (Cho.IndexOf(cho) * 21 + Jung.IndexOf(jung)) * 28 + (jong == '\0' ? 0 : Jong.IndexOf(jong));
                return ((char)code).ToString();
            }
            var sb = new StringBuilder();
            if (cho != '\0') sb.Append(cho);
            if (jung != '\0') sb.Append(jung);
            if (jong != '\0') sb.Append(jong);
            return sb.ToString();
        }
    }
}
