using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Step 1: character select. One PC is always one human player. Two modes:
    ///  - CPU 대전: you vs the CPU (P1 can also be set to CPU to watch a CPU vs CPU test match).
    ///  - Online: pick your character, then play a friend:
    ///      빠른 매칭 (any open room) · 방 만들기 → share the code · 코드로 참가 · IP 직접 연결 (LAN / VPN).
    /// </summary>
    public class LobbyScreen : MonoBehaviour
    {
        public readonly bool[] Cpu = new bool[2];

        /// <summary>Set when an online match was found. Null for local play.</summary>
        public NetSession Session { get; private set; }

        enum View { Menu, EnterCode, IpMenu, EnterIp, Busy, Matched }

        readonly int[] selection = { 0, 1 };
        readonly bool[] ready = new bool[2];
        readonly RectTransform[] slots = new RectTransform[2];
        readonly CharacterView[] previews = new CharacterView[2];
        readonly Text[] names = new Text[2], statuses = new Text[2], tags = new Text[2], cpuLabels = new Text[2], hints = new Text[2];
        readonly GameObject[] cpuButtons = new GameObject[2];
        readonly Image[] modeButtons = new Image[2];

        bool online;
        View view;
        RectTransform onlinePanel, opponentSlot;
        Text onlineStatus, fieldText, placeholder;
        Image field;
        GameObject copyButton;
        readonly Dictionary<View, List<GameObject>> viewButtons = new Dictionary<View, List<GameObject>>();
        readonly StringBuilder entry = new StringBuilder();
        CancellationTokenSource cancel;
        NetSession pending;
        int selectedHost, selectedClient;
        bool hooked;

        public static LobbyScreen Create(Transform parent)
        {
            var rt = UIKit.Rect("Lobby", parent).Stretch();
            var lobby = rt.gameObject.AddComponent<LobbyScreen>();
            lobby.Build();
            return lobby;
        }

        void Build()
        {
            UIKit.Img("Bg", transform, Palette.Bg).rectTransform.Stretch();

            var title = UIKit.Label("Title", transform, "MINI PARTY BATTLE", 88, Palette.Gold);
            title.rectTransform.Place(new Vector2(0, 420), new Vector2(1200, 110));
            UIKit.Shadow(title, 6f);
            UIKit.Label("Sub", transform, "캐릭터를 고르고 준비하세요 — 미니게임에서 지면 벌칙! HP가 먼저 0이 되면 패배", 30, Palette.Muted, style: FontStyle.Normal)
                .rectTransform.Place(new Vector2(0, 340), new Vector2(1400, 44));

            for (int p = 0; p < 2; p++)
            {
                int idx = p;
                float x = p == 0 ? -420 : 420;
                var panel = UIKit.Img($"P{p + 1}Panel", transform, Palette.Panel, UIKit.Rounded).rectTransform;
                panel.Place(new Vector2(x, -30), new Vector2(640, 620));

                tags[p] = UIKit.Label("Tag", panel, $"P{p + 1}", 60, Color.white);
                tags[p].rectTransform.Place(new Vector2(0, 250), new Vector2(300, 80));

                slots[p] = UIKit.Rect("Slot", panel);
                slots[p].Place(new Vector2(0, -120), new Vector2(10, 10));

                UIKit.Button("Prev", panel, "◀", new Vector2(-230, 0), new Vector2(80, 80), 36, Palette.PanelLight, () => Change(idx, -1));
                UIKit.Button("Next", panel, "▶", new Vector2(230, 0), new Vector2(80, 80), 36, Palette.PanelLight, () => Change(idx, 1));

                names[p] = UIKit.Label("Name", panel, "", 46, Color.white);
                names[p].rectTransform.Place(new Vector2(0, -165), new Vector2(400, 60));

                hints[p] = UIKit.Label("Hint", panel, "", 24, Palette.Muted, style: FontStyle.Normal);
                hints[p].rectTransform.Place(new Vector2(0, -215), new Vector2(560, 36));

                statuses[p] = UIKit.Label("Status", panel, "", 34, Palette.Muted);
                statuses[p].rectTransform.Place(new Vector2(0, -265), new Vector2(560, 48));

                var cpuBtn = UIKit.Button("Cpu", transform, "", new Vector2(x, -385), new Vector2(240, 56), 26, Palette.PanelLight, () => ToggleCpu(idx));
                cpuLabels[p] = cpuBtn.GetComponentInChildren<Text>();
                cpuButtons[p] = cpuBtn.gameObject;
            }

            BuildOnlinePanel();

            modeButtons[0] = UIKit.Button("Local", transform, "CPU 대전", new Vector2(-170, -470), new Vector2(320, 64), 28, Palette.PanelLight, () => SetMode(false)).GetComponent<Image>();
            modeButtons[1] = UIKit.Button("Online", transform, "온라인 1:1", new Vector2(170, -470), new Vector2(320, 64), 28, Palette.PanelLight, () => SetMode(true)).GetComponent<Image>();
        }

        // ---------- online panel ----------

        void BuildOnlinePanel()
        {
            onlinePanel = UIKit.Img("OnlinePanel", transform, Palette.Panel, UIKit.Rounded).rectTransform;
            onlinePanel.Place(new Vector2(420, -30), new Vector2(640, 620));

            UIKit.Label("Tag", onlinePanel, "온라인 상대", 50, Color.white).rectTransform.Place(new Vector2(0, 255), new Vector2(500, 70));
            opponentSlot = UIKit.Rect("Slot", onlinePanel);
            opponentSlot.Place(new Vector2(0, 50), new Vector2(10, 10));
            placeholder = UIKit.Label("Question", onlinePanel, "?", 140, Color.white.WithAlpha(0.15f));
            placeholder.rectTransform.Place(new Vector2(0, 120), new Vector2(200, 180));

            onlineStatus = UIKit.Label("Status", onlinePanel, "", 26, Palette.Muted);
            onlineStatus.rectTransform.Place(new Vector2(0, -15), new Vector2(600, 70));

            field = UIKit.Img("Field", onlinePanel, Palette.Hex("#15182E"), UIKit.Rounded);
            field.rectTransform.Place(new Vector2(-60, -90), new Vector2(440, 64));
            fieldText = UIKit.Label("FieldText", field.transform, "", 32, Color.white);
            fieldText.rectTransform.Stretch();
            fieldText.supportRichText = true;
            copyButton = UIKit.Button("Copy", onlinePanel, "복사", new Vector2(225, -90), new Vector2(120, 60), 26, Palette.PanelLight, CopyShare).gameObject;

            var green = Palette.Hex("#3FB950");
            var blue = Palette.Hex("#4C8DFF");
            AddButton(View.Menu, "빠른 매칭", new Vector2(-150, -170), green, () => StartConnect(NetSession.Mode.QuickMatch, null));
            AddButton(View.Menu, "방 만들기 (초대코드)", new Vector2(150, -170), blue, () => StartConnect(NetSession.Mode.CreateRoom, null));
            AddButton(View.Menu, "코드로 참가", new Vector2(-150, -245), Palette.PanelLight, () => Show(View.EnterCode));
            AddButton(View.Menu, "IP 직접 연결", new Vector2(150, -245), Palette.PanelLight, () => Show(View.IpMenu));

            AddButton(View.EnterCode, "참가하기", new Vector2(-150, -210), green, SubmitEntry);
            AddButton(View.EnterCode, "뒤로", new Vector2(150, -210), Palette.PanelLight, () => Show(View.Menu));

            AddButton(View.IpMenu, "내가 방 열기", new Vector2(-150, -170), blue, () => StartConnect(NetSession.Mode.HostDirect, null));
            AddButton(View.IpMenu, "IP로 참가", new Vector2(150, -170), green, () => Show(View.EnterIp));
            AddButton(View.IpMenu, "뒤로", new Vector2(0, -245), Palette.PanelLight, () => Show(View.Menu));

            AddButton(View.EnterIp, "접속", new Vector2(-150, -210), green, SubmitEntry);
            AddButton(View.EnterIp, "뒤로", new Vector2(150, -210), Palette.PanelLight, () => Show(View.IpMenu));

            AddButton(View.Busy, "취소", new Vector2(0, -210), Palette.Red, () => cancel?.Cancel());

            onlinePanel.gameObject.SetActive(false);
        }

        void AddButton(View v, string text, Vector2 pos, Color color, System.Action onClick)
        {
            if (!viewButtons.TryGetValue(v, out var list)) viewButtons[v] = list = new List<GameObject>();
            list.Add(UIKit.Button(text, onlinePanel, text, pos, new Vector2(280, 60), 26, color, onClick).gameObject);
        }

        void Show(View v)
        {
            view = v;
            foreach (var pair in viewButtons)
                foreach (var go in pair.Value) go.SetActive(pair.Key == v);

            entry.Clear();
            bool entering = v == View.EnterCode || v == View.EnterIp;
            field.gameObject.SetActive(entering);
            copyButton.SetActive(false);
            switch (v)
            {
                case View.Menu:
                    SetStatus("빠른 매칭 · 초대코드 · IP 직접 연결 중 선택하세요", Palette.Muted);
                    break;
                case View.EnterCode:
                    SetStatus("친구에게 받은 초대코드를 입력하세요 (Ctrl+V 붙여넣기)", Palette.Muted);
                    break;
                case View.IpMenu:
                    SetStatus("같은 와이파이·공유기, 또는 라디민·하마치 같은 VPN에서 사용\n방 연 사람의 IP를 친구가 입력합니다", Palette.Muted);
                    break;
                case View.EnterIp:
                    SetStatus("방 연 친구의 IP를 입력하세요 (예: 192.168.0.5)", Palette.Muted);
                    break;
            }
            RefreshEntry();
            RefreshStatus(0);
        }

        void SetStatus(string text, Color color)
        {
            onlineStatus.text = text;
            onlineStatus.color = color;
        }

        void RefreshEntry()
        {
            if (view == View.EnterCode || view == View.EnterIp)
                fieldText.text = entry + "<color=#9AA3C7>|</color>";
        }

        void CopyShare()
        {
            if (pending == null || string.IsNullOrEmpty(pending.ShareText)) return;
            string text = pending.ShareText;
            int sep = text.IndexOf("  /  ", System.StringComparison.Ordinal);
            GUIUtility.systemCopyBuffer = sep > 0 ? text.Substring(0, sep) : text;
            SetStatus("복사했어요! 친구에게 보내주세요", Palette.Green);
        }

        // ---------- text entry (code / IP) ----------

        void OnEnable() => Hook(true);
        void OnDisable() => Hook(false);

        void Hook(bool on)
        {
            var kb = Keyboard.current;
            if (kb == null || on == hooked) return;
            hooked = on;
            if (on) kb.onTextInput += OnChar;
            else kb.onTextInput -= OnChar;
        }

        void OnChar(char c)
        {
            if (view != View.EnterCode && view != View.EnterIp) return;
            if (!Accept(c)) return;
            if (entry.Length >= (view == View.EnterCode ? 10 : 40)) return;
            entry.Append(view == View.EnterCode ? char.ToUpperInvariant(c) : c);
            RefreshEntry();
        }

        bool Accept(char c)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c)) return false;
            if (view == View.EnterCode) return c < 128 && char.IsLetterOrDigit(c);
            return c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == ':' || c == '-');
        }

        void UpdateEntryKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.backspaceKey.wasPressedThisFrame && entry.Length > 0)
            {
                entry.Length--;
                RefreshEntry();
            }
            if ((kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) && kb.vKey.wasPressedThisFrame)
            {
                foreach (char c in GUIUtility.systemCopyBuffer ?? "")
                    if (Accept(c)) entry.Append(view == View.EnterCode ? char.ToUpperInvariant(c) : c);
                RefreshEntry();
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) SubmitEntry();
            if (kb.escapeKey.wasPressedThisFrame) Show(view == View.EnterIp ? View.IpMenu : View.Menu);
        }

        void SubmitEntry()
        {
            string text = entry.ToString().Trim();
            if (text.Length == 0) return;
            StartConnect(view == View.EnterCode ? NetSession.Mode.JoinRoom : NetSession.Mode.JoinDirect, text);
        }

        // ---------- connecting ----------

        void StartConnect(NetSession.Mode mode, string arg)
        {
            if (view == View.Busy || view == View.Matched) return;
            StartCoroutine(ConnectRoutine(mode, arg));
        }

        IEnumerator ConnectRoutine(NetSession.Mode mode, string arg)
        {
            var back = view == View.EnterIp || view == View.IpMenu ? View.IpMenu : View.Menu;
            Show(View.Busy);
            var session = new NetSession();
            pending = session;
            cancel = new CancellationTokenSource();
            var task = session.ConnectAsync(mode, arg, cancel.Token);
            while (!task.IsCompleted)
            {
                SetStatus(session.Status, Palette.Muted);
                bool share = !string.IsNullOrEmpty(session.ShareText);
                field.gameObject.SetActive(share);
                copyButton.SetActive(share);
                if (share)
                    fieldText.text = mode == NetSession.Mode.HostDirect
                        ? $"<size=24>{session.ShareText}</size>"
                        : $"<size=44><b>{session.ShareText}</b></size>";
                yield return null;
            }

            if (!task.Result)
            {
                bool cancelled = cancel.IsCancellationRequested;
                pending = null;
                Show(back);
                SetStatus(cancelled ? "취소했어요" : session.Status, cancelled ? Palette.Muted : Palette.Red);
                yield break;
            }

            // handshake: swap characters (host = P1, client = P2)
            SetStatus("상대 정보 받는 중...", Palette.Muted);
            int hostChar = 0, clientChar = 0;
            if (session.IsHost)
            {
                NetMsg hello = default;
                yield return session.WaitFor(MsgType.Hello, m => hello = m);
                hostChar = selection[0];
                clientChar = hello.A;
                session.Send(new NetMsg { Type = MsgType.Welcome, A = hostChar, B = clientChar });
            }
            else
            {
                bool got = false;
                NetMsg welcome = default;
                float resend = 0f;
                while (!got && !session.Disconnected)
                {
                    if ((resend -= Time.deltaTime) <= 0f)
                    {
                        session.Send(new NetMsg { Type = MsgType.Hello, A = selection[0] });
                        resend = 1f;
                    }
                    got = session.TryTake(MsgType.Welcome, out welcome);
                    yield return null;
                }
                hostChar = welcome.A;
                clientChar = welcome.B;
            }

            if (session.Disconnected)
            {
                var leave = session.LeaveAsync();
                while (!leave.IsCompleted) yield return null;
                pending = null;
                Show(back);
                SetStatus("상대와 연결이 끊겼습니다", Palette.Red);
                yield break;
            }

            Session = session;
            Show(View.Matched);
            int opponentChar = session.IsHost ? clientChar : hostChar;
            var def = Roster.All[Mathf.Clamp(opponentChar, 0, Roster.All.Length - 1)];
            placeholder.gameObject.SetActive(false);
            var opponent = CharacterView.Create(opponentSlot, def, 150);
            opponent.SetFace(CharacterView.Face.Happy);
            StartCoroutine(Tween.Pop(opponent.transform, 0.5f, 0.3f));

            selectedHost = hostChar;
            selectedClient = clientChar;
            SetStatus($"상대 발견! ({def.Name}) — {(session.IsHost ? "내가 P1" : "내가 P2")}", Palette.Green);
            RefreshStatus(0);
        }

        // ---------- mode / characters ----------

        void SetMode(bool isOnline)
        {
            if (view == View.Busy || view == View.Matched) return;
            online = isOnline;
            onlinePanel.gameObject.SetActive(online);
            cpuButtons[0].SetActive(!online);
            cpuButtons[1].SetActive(false);
            if (!online) Cpu[1] = true; // the opponent on this PC is always the CPU
            modeButtons[0].color = online ? Palette.PanelLight : Palette.Hex("#4C8DFF");
            modeButtons[1].color = online ? Palette.Hex("#4C8DFF") : Palette.PanelLight;
            tags[0].text = "나";
            tags[1].text = "CPU";
            hints[0].text = online ? "◀ ▶ 버튼 또는 A/D · ←/→ 로 캐릭터 변경" : "A/D · ←/→ 캐릭터 변경 · Space/Enter 준비";
            hints[1].text = "◀ ▶ 버튼으로 상대 캐릭터 변경";
            ready[0] = !online && Cpu[0];
            ready[1] = !online && Cpu[1];
            if (online) Show(View.Menu);
            for (int p = 0; p < 2; p++) RefreshStatus(p);
        }

        void Change(int p, int delta)
        {
            if (view == View.Busy || view == View.Matched) return;
            if (online && p == 1) return;
            if (!online && ready[p] && !Cpu[p]) return;
            int n = Roster.All.Length;
            selection[p] = (selection[p] + delta + n) % n;
            RebuildPreview(p);
        }

        void ToggleCpu(int p)
        {
            if (online || p == 1) return; // the opponent on this PC is always the CPU
            Cpu[p] = !Cpu[p];
            ready[p] = Cpu[p];
            RefreshStatus(p);
        }

        void RebuildPreview(int p)
        {
            if (previews[p] != null) Destroy(previews[p].gameObject);
            var def = Roster.All[selection[p]];
            previews[p] = CharacterView.Create(slots[p], def, 240);
            StartCoroutine(Tween.Pop(previews[p].transform, 0.7f, 0.25f));
            names[p].text = def.Name;
            names[p].color = def.Body;
            tags[p].color = def.Body;
        }

        void RefreshStatus(int p)
        {
            cpuLabels[p].text = Cpu[p] ? "CPU: ON" : "CPU: OFF";
            if (online)
            {
                statuses[p].text = p == 0 ? (view == View.Matched ? "매칭 완료!" : view == View.Busy ? "연결 중..." : "") : "";
                statuses[p].color = Palette.Green;
            }
            else if (Cpu[p])
            {
                statuses[p].text = "CPU 자동 준비";
                statuses[p].color = Palette.Gold;
            }
            else
            {
                statuses[p].text = ready[p] ? "준비 완료!" : "준비 대기 중...";
                statuses[p].color = ready[p] ? Palette.Green : Palette.Muted;
            }
            previews[p]?.SetFace(ready[p] ? CharacterView.Face.Happy : CharacterView.Face.Normal);
        }

        public IEnumerator Run(PlayerData[] players)
        {
            Cpu[1] = true;
            SetMode(false);
            for (int p = 0; p < 2; p++)
            {
                ready[p] = Cpu[p];
                RebuildPreview(p);
                RefreshStatus(p);
            }

            // test switches: two copies on one PC can connect to each other automatically
            if (DevArgs.Has("-autohost"))
            {
                SetMode(true);
                StartConnect(NetSession.Mode.HostDirect, null);
            }
            else if (DevArgs.Get("-autojoin") is string ip)
            {
                SetMode(true);
                selection[0] = 1;
                RebuildPreview(0);
                StartConnect(NetSession.Mode.JoinDirect, ip);
            }

            while (true)
            {
                if (online)
                {
                    if (view == View.Matched) break;
                    if (view == View.Menu)
                    {
                        if (Controls.Left(0) || Controls.Left(1)) Change(0, -1);
                        if (Controls.Right(0) || Controls.Right(1)) Change(0, 1);
                    }
                    else if (view == View.EnterCode || view == View.EnterIp) UpdateEntryKeys();
                }
                else
                {
                    if (ready[0] && ready[1]) break;
                    if (!Cpu[0])
                    {
                        if (Controls.Left(0) || Controls.Left(1)) Change(0, -1);
                        if (Controls.Right(0) || Controls.Right(1)) Change(0, 1);
                        if (Controls.AnyConfirm)
                        {
                            ready[0] = !ready[0];
                            RefreshStatus(0);
                            if (ready[0]) StartCoroutine(previews[0].Jump(60, 0.3f));
                        }
                    }
                }
                yield return null;
            }

            if (online)
            {
                players[0].Character = Roster.All[Mathf.Clamp(selectedHost, 0, Roster.All.Length - 1)];
                players[1].Character = Roster.All[Mathf.Clamp(selectedClient, 0, Roster.All.Length - 1)];
                players[0].IsCpu = players[1].IsCpu = false;
                players[Session.LocalIndex].IsYou = true;
                players[Session.LocalIndex].IsCpu = DevArgs.Has("-autoplay"); // test switch
                previews[0].SetFace(CharacterView.Face.Happy);
                StartCoroutine(previews[0].Jump(100, 0.4f));
                yield return new WaitForSeconds(1.6f);
            }
            else
            {
                for (int p = 0; p < 2; p++)
                {
                    players[p].Character = Roster.All[selection[p]];
                    players[p].IsCpu = Cpu[p];
                    players[p].IsYou = !Cpu[p];
                    previews[p].SetFace(CharacterView.Face.Happy);
                    StartCoroutine(previews[p].Jump(100, 0.4f));
                }
                yield return new WaitForSeconds(0.9f);
            }
            foreach (var pl in players) pl.Hp = pl.MaxHp;
        }
    }
}
