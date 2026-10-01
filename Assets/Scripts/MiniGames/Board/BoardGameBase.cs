using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Shared base for turn-based board games (connect four, othello). Handles black/white assignment, turns, the per-move
    /// clock (timeout = automatic move), mouse picking, hover preview, CPU turns and move sync.
    /// A move is a single action (A = cell index) emitted by the player whose turn it is; both PCs apply it
    /// deterministically, so only moves travel over the network.
    /// </summary>
    public abstract class BoardGameBase : MiniGameBase
    {
        protected const int Empty = 0, Black = 1, White = 2;
        const int ActMove = 1;

        protected int Size;  // columns
        protected int Rows;
        protected float CellPx;
        protected abstract int BoardSize { get; }
        protected virtual int BoardRows => BoardSize;
        protected abstract float CellSize { get; }
        protected abstract float TurnTime { get; }

        protected int[] Cells;
        protected Image[] Stones;
        protected RectTransform Board, StoneLayer;
        protected int BlackPlayer;
        protected int Turn = Black;
        protected int MoveCount;
        protected Text Prompt;

        readonly RectTransform[] panels = new RectTransform[2];
        readonly Text[] statusTexts = new Text[2];
        protected readonly Text[] CountTexts = new Text[2];
        readonly RectTransform[] clockFills = new RectTransform[2];
        Image hover, lastMark;
        float clock, cpuDelay;
        bool sentThisTurn;

        protected int PlayerOf(int color) => color == Black ? BlackPlayer : 1 - BlackPlayer;
        protected int ColorOf(int p) => p == BlackPlayer ? Black : White;
        protected static int Other(int color) => color == Black ? White : Black;
        protected int Idx(int x, int y) => y * Size + x;
        protected bool In(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Rows;

        /// <summary>Screen-space center of a cell/intersection inside the board rect.</summary>
        protected Vector2 CellCenter(int x, int y) => new Vector2((x - (Size - 1) / 2f) * CellPx, ((Rows - 1) / 2f - y) * CellPx);

        protected abstract void DrawBoard();
        protected abstract bool IsLegal(int idx, int color);
        /// <summary>Apply a (legal) move: update Cells/visuals and advance <see cref="Turn"/>. Return the winner (player index) or -1.</summary>
        protected abstract int ApplyMove(int idx, int color);
        /// <summary>Move chosen by the CPU, or automatically when the clock runs out.</summary>
        protected abstract int ChooseAuto(int color);
        protected virtual void OnTurnStart() { }

        // piece colors / names (default: black & white stones)
        protected virtual Color StoneColor(int color) => color == Black ? StoneBlack : StoneWhite;
        protected virtual string ColorName(int color) => color == Black ? "흑" : "백";

        /// <summary>Move for a click on cell (x, y), or -1. Default: the empty cell itself.</summary>
        protected virtual int PickMove(int x, int y) => Cells[Idx(x, y)] == Empty ? Idx(x, y) : -1;
        /// <summary>Cell where the piece of <paramref name="move"/> ends up (hover preview / last-move mark).</summary>
        protected virtual int MoveCell(int move) => move;

        protected override void Build()
        {
            Background();
            Size = BoardSize;
            Rows = BoardRows;
            CellPx = CellSize;
            Cells = new int[Size * Rows];
            Stones = new Image[Size * Rows];
            BlackPlayer = Seed % 2; // random but identical on both PCs

            Prompt = UIKit.Label("Prompt", Root, "", 40, Color.white);
            Prompt.rectTransform.Place(new Vector2(0, 385), new Vector2(1200, 56));
            Prompt.supportRichText = true;

            Board = UIKit.Img("Board", Root, BoardColor, UIKit.Rounded).rectTransform;
            Board.Place(new Vector2(0, -45), new Vector2((Size - 1) * CellPx + CellPx * 1.1f, (Rows - 1) * CellPx + CellPx * 1.1f));
            DrawBoard();
            StoneLayer = UIKit.Rect("Stones", Board).Stretch();

            hover = UIKit.Img("Hover", StoneLayer, Color.clear, UIKit.Circle);
            hover.rectTransform.sizeDelta = Vector2.one * CellPx * 0.86f;
            lastMark = UIKit.Img("Last", StoneLayer, Palette.Red, UIKit.Circle);
            lastMark.rectTransform.sizeDelta = Vector2.one * CellPx * 0.22f;
            lastMark.gameObject.SetActive(false);

            for (int p = 0; p < 2; p++)
            {
                var panel = PlayerPanel(p, 400, 540, -45);
                panel.anchoredPosition = new Vector2(p == 0 ? -735 : 735, -45);
                panels[p] = panel;
                bool black = ColorOf(p) == Black;
                var stone = UIKit.Img("Stone", panel, StoneColor(ColorOf(p)), UIKit.Circle);
                stone.rectTransform.Place(new Vector2(0, 150), new Vector2(110, 110));
                UIKit.Label("Color", panel, $"{ColorName(ColorOf(p))} ({(black ? "선공" : "후공")})", 34, Color.white).rectTransform.Place(new Vector2(0, 65), new Vector2(360, 50));
                CountTexts[p] = UIKit.Label("Count", panel, "", 40, Palette.Gold);
                CountTexts[p].rectTransform.Place(new Vector2(0, 5), new Vector2(360, 56));
                statusTexts[p] = UIKit.Label("Status", panel, "", 36, Palette.Muted);
                statusTexts[p].rectTransform.Place(new Vector2(0, -80), new Vector2(360, 50));
                var clockBg = UIKit.Img("ClockBg", panel, new Color(0, 0, 0, 0.4f), UIKit.Rounded).rectTransform;
                clockBg.Place(new Vector2(0, -150), new Vector2(320, 18));
                clockFills[p] = UIKit.Img("Clock", clockBg, Players[p].Color, UIKit.Rounded).rectTransform;
                UIKit.Label("Hint", panel, IsLocal(p) && !Players[p].IsCpu ? "마우스로 두기" : "", 24, Palette.Muted, style: FontStyle.Normal)
                    .rectTransform.Place(new Vector2(0, -210), new Vector2(360, 36));
            }
        }

        protected virtual Color BoardColor => Palette.Hex("#D9A35B");
        protected static readonly Color StoneBlack = Palette.Hex("#1B1C22");
        protected static readonly Color StoneWhite = Palette.Hex("#F3F4F8");

        protected Image PlaceStone(int idx, int color)
        {
            int x = idx % Size, y = idx / Size;
            var s = UIKit.Img("Stone", StoneLayer, StoneColor(color), UIKit.Circle);
            s.rectTransform.Place(CellCenter(x, y), Vector2.one * CellPx * 0.9f);
            UIKit.Img("Shine", s.transform, Color.white.WithAlpha(color == Black ? 0.18f : 0.6f), UIKit.Circle)
                .rectTransform.Place(new Vector2(-CellPx * 0.14f, CellPx * 0.14f), Vector2.one * CellPx * 0.25f);
            Stones[idx] = s;
            lastMark.transform.SetAsLastSibling();
            return s;
        }

        protected override void OnBegin() => BeginTurn();

        void BeginTurn()
        {
            clock = TurnTime;
            cpuDelay = Random.Range(0.45f, 0.9f);
            sentThisTurn = false;
            int p = PlayerOf(Turn);
            for (int i = 0; i < 2; i++)
            {
                bool mine = i == p;
                statusTexts[i].text = mine ? "차례!" : "대기";
                statusTexts[i].color = mine ? Palette.Green : Palette.Muted;
                if (mine) StartCoroutine(Tween.Pop(statusTexts[i].transform, 1.3f, 0.2f));
            }
            string who = Online ? (IsLocal(p) ? "내 차례" : "상대 차례") : $"{Players[p].Tag} 차례";
            Prompt.text = $"{who} — {ColorName(Turn)}";
            Prompt.color = Players[p].Color;
            OnTurnStart();
        }

        void SetClock(int p, float ratio)
        {
            for (int i = 0; i < 2; i++)
            {
                float r = i == p ? Mathf.Clamp01(ratio) : 1f;
                clockFills[i].anchorMin = Vector2.zero;
                clockFills[i].anchorMax = new Vector2(r, 1);
                clockFills[i].offsetMin = clockFills[i].offsetMax = Vector2.zero;
                clockFills[i].GetComponent<Image>().color = i == p && ratio < 0.3f ? Palette.Red : Players[i].Color;
            }
        }

        protected virtual void Update()
        {
            hover.color = Color.clear;
            if (!Running || WinnerIndex >= 0) return;

            int p = PlayerOf(Turn);
            clock -= Time.deltaTime;
            SetClock(p, clock / TurnTime);
            if (!IsLocal(p) || sentThisTurn) return;

            if (clock <= 0f)
            {
                SendMove(p, ChooseAuto(Turn));
                return;
            }

            if (IsCpuHere(p))
            {
                cpuDelay -= Time.deltaTime;
                if (cpuDelay <= 0f) SendMove(p, ChooseAuto(Turn));
                return;
            }

            // human: hover + click on the nearest cell
            var local = MouseIn(Board);
            int x = Mathf.RoundToInt(local.x / CellPx + (Size - 1) / 2f);
            int y = Mathf.RoundToInt((Rows - 1) / 2f - local.y / CellPx);
            if (!In(x, y) || (CellCenter(x, y) - local).magnitude > CellPx * 0.5f) return;
            int idx = PickMove(x, y);
            if (idx < 0) return;

            bool legal = IsLegal(idx, Turn);
            int cell = MoveCell(idx);
            hover.rectTransform.anchoredPosition = CellCenter(cell % Size, cell / Size);
            hover.color = legal ? StoneColor(Turn).WithAlpha(0.45f) : Palette.Red.WithAlpha(0.35f);
            if (Controls.Click(0))
            {
                if (legal) SendMove(p, idx);
                else OnIllegalClick(idx);
            }
        }

        protected virtual void OnIllegalClick(int idx) { }

        void SendMove(int p, int idx)
        {
            if (idx < 0) return;
            sentThisTurn = true;
            Emit(ActMove, p, idx);
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Kind != ActMove || WinnerIndex >= 0) return;
            if (act.Player != PlayerOf(Turn) || act.A < 0 || act.A >= Cells.Length || !IsLegal(act.A, Turn)) return;

            int color = Turn;
            int cell = MoveCell(act.A); // before applying (e.g. the landing row of a dropped disc)
            int winner = ApplyMove(act.A, color);
            MoveCount++;
            int x = cell % Size, y = cell / Size;
            lastMark.gameObject.SetActive(true);
            lastMark.rectTransform.anchoredPosition = CellCenter(x, y);
            lastMark.transform.SetAsLastSibling();

            if (winner >= 0)
            {
                DeclareWinner(winner);
                return;
            }
            BeginTurn();
        }

        protected IEnumerator PopupOnBoard(string text, Color color)
        {
            var label = UIKit.Label("Popup", Root, text, 72, color);
            label.rectTransform.Place(new Vector2(0, -45), new Vector2(900, 120));
            UIKit.Shadow(label, 6f, 0.6f);
            yield return Tween.Pop(label.transform, 1.8f, 0.25f);
            yield return new WaitForSeconds(0.6f);
            yield return Tween.FadeGraphic(label, 0f, 0.3f);
            Destroy(label.gameObject);
        }

        protected override void OnFinish()
        {
            hover.color = Color.clear;
            for (int p = 0; p < 2; p++)
            {
                statusTexts[p].text = "";
                SetClock(-1, 1f);
                Stamp(panels[p], p == WinnerIndex, 0);
            }
        }
    }
}
