using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MiniParty.EditorTools
{
    /// <summary>Checks the in-game 두벌식 composer against qwerty key sequences (uppercase = Shift).</summary>
    static class HangulSelfTest
    {
        [MenuItem("MiniParty/Test/한글 입력 자체 테스트")]
        static void Run()
        {
            var cases = new (string keys, string expect)[]
            {
                ("tkrhk", "사과"), ("EjrqhRdl", "떡볶이"), ("Ejrqhrdl", "떡복이"), ("dkdltmzmfla", "아이스크림"),
                ("xpffpqlwjs", "텔레비전"), ("rlacl", "김치"), ("Wlro", "찌개"), ("chzhfflt", "초콜릿"), ("chzhffltm", "초콜리스"),
                ("ekfr", "닭"), ("ekfrdl", "닭이"), ("dkswdk", "앉아"), ("rhoscksgdk", "괜찮아"), ("dmlwk", "의자"),
                ("gkqkfkrl", "하바라기"), ("godqkfkrl", "행바라기"), ("tmakxmvhs", "스마트폰"), ("vldksh", "피아노"),
            };
            int fail = 0;
            foreach (var (keys, expect) in cases)
            {
                var list = new List<char>();
                foreach (char c in keys) list.Add(Hangul.ToJamo(c, char.IsUpper(c)));
                Hangul.Compose(list, out var done, out var composing);
                string got = done + composing;
                if (got != expect) { fail++; Debug.LogError($"[한글 테스트] {keys} → {got} (기대: {expect})"); }
            }
            Debug.Log($"[한글 테스트] {cases.Length - fail}/{cases.Length} 통과");
        }
    }
}
