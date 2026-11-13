using MiniParty.Group1Net;

namespace Group1Server.Bot;

/// <summary>봇이 각 1조 게임에서 만드는 입력. Unity 게임의 CPU 플레이와 비슷한 속도·실력.</summary>
public abstract class BotGame
{
    protected readonly BotClient Bot;
    protected readonly Random Rng;
    protected double T; // 게임 시작 후 시간

    protected BotGame(BotClient bot, Random rng)
    {
        Bot = bot;
        Rng = rng;
    }

    public static BotGame Create(int id, BotClient bot, Random rng) => id switch
    {
        G1Game.SpaceTug => new Tug(bot, rng),
        G1Game.Cps => new Cps(bot, rng),
        G1Game.F1Reaction => new F1(bot, rng),
        G1Game.Aim => new Aim(bot, rng),
        G1Game.Stack => new Stack(bot, rng),
        G1Game.Timing => new Timing(bot, rng),
        _ => null,
    };

    public void Tick(double dt)
    {
        T += dt;
        OnTick();
    }

    protected abstract void OnTick();
    public virtual void OnSystem(int kind, int a, float f) { }

    protected double R(double min, double max) => min + Rng.NextDouble() * (max - min);

    // 스페이스바 줄다리기: 초당 6.5~9회 연타
    sealed class Tug : BotGame
    {
        double next;
        public Tug(BotClient b, Random r) : base(b, r) { }

        protected override void OnTick()
        {
            if (T < next) return;
            next = T + 1 / R(6.5, 9);
            Bot.Emit(G1Game.TugPull);
        }
    }

    // CPS: 5초 동안 클릭, 끝나면 Final
    sealed class Cps : BotGame
    {
        double next;
        int clicks;
        float last;
        bool final;
        public Cps(BotClient b, Random r) : base(b, r) { }

        protected override void OnTick()
        {
            if (final) return;
            if (T >= 5.0)
            {
                final = true;
                Bot.Emit(G1Game.CpsFinal, clicks, last);
                return;
            }
            if (T < next) return;
            next = T + 1 / R(6.5, 9.5);
            clicks++;
            last = (float)T;
            Bot.Emit(G1Game.CpsClick);
        }
    }

    // F1: 서버가 히트를 시작하면 불 5개 + 대기 뒤 0.18~0.32초에 반응, 가끔 부정출발
    sealed class F1 : BotGame
    {
        int heat;
        double heatStart, goAt, react, jumpAt;
        bool reported = true;
        public F1(BotClient b, Random r) : base(b, r) { }

        public override void OnSystem(int kind, int a, float f)
        {
            if (kind == G1Game.F1HeatStart)
            {
                heat = a;
                heatStart = T;
                goAt = 5.3 + f;
                react = R(0.18, 0.32);
                jumpAt = Rng.NextDouble() < 0.07 ? R(1, 5) : -1;
                reported = false;
            }
            else if (kind == G1Game.F1HeatResult) reported = true;
        }

        protected override void OnTick()
        {
            if (reported) return;
            double heatT = T - heatStart;
            if (jumpAt > 0 && heatT >= jumpAt) Report(G1Game.F1CodeJump, 0);
            else if (heatT >= goAt + react) Report(G1Game.F1CodeReact, (float)react);
        }

        void Report(int code, float seconds)
        {
            reported = true;
            Bot.Emit(G1Game.F1Report, heat * 10 + code, seconds);
        }
    }

    // 에임: 표적 24개 중 약 80% 를 0.3~0.6초 만에 맞히고, 25초 뒤 Final
    sealed class Aim : BotGame
    {
        readonly double[] shotAt = new double[24];
        readonly float[] reactOf = new float[24];
        int hits;
        float sum;
        bool final;

        public Aim(BotClient b, Random r) : base(b, r)
        {
            for (int i = 0; i < 24; i++)
            {
                reactOf[i] = (float)R(0.3, 0.6);
                shotAt[i] = Rng.NextDouble() < 0.8 ? 1.0 + i * 0.75 + reactOf[i] : -1;
            }
        }

        protected override void OnTick()
        {
            if (final) return;
            for (int i = 0; i < 24; i++)
            {
                if (shotAt[i] < 0 || T < shotAt[i]) continue;
                shotAt[i] = -1;
                hits++;
                sum += reactOf[i];
                Bot.Emit(G1Game.AimHit, i, reactOf[i]);
            }
            if (T >= 25)
            {
                final = true;
                Bot.Emit(G1Game.AimFinal, hits, sum);
            }
        }
    }

    // 블럭 쌓기: 약 1초마다 한 층, 조금씩 잘려 좁아지고 가끔 완전히 빗나감
    sealed class Stack : BotGame
    {
        float left = -150, width = 300;
        int level;
        double next = 1;
        bool done;
        public Stack(BotClient b, Random r) : base(b, r) { }

        protected override void OnTick()
        {
            if (done || T < next) return;
            next = T + R(0.7, 1.3);
            if (Rng.NextDouble() < 0.12 || width < 20)
            {
                done = true;
                Bot.Emit(G1Game.StackFail, level);
                return;
            }
            float w = MathF.Max(10, width - (float)R(0, 14));
            int nl = (int)Math.Round(left + R(0, width - w));
            nl = Math.Clamp(nl, (int)MathF.Ceiling(left), (int)MathF.Floor(left + width - w));
            level++;
            left = nl;
            width = w;
            Bot.Emit(G1Game.StackPlace, level * 100000 + nl + 50000, w);
        }
    }

    // 타이밍 서바이벌: 0.5~1.1초마다 판정, 15% 확률로 실패 (목숨 3개)
    sealed class Timing : BotGame
    {
        int level, lives = 3;
        double next = 0.8;
        bool done;
        public Timing(BotClient b, Random r) : base(b, r) { }

        protected override void OnTick()
        {
            if (done || T < next) return;
            next = T + R(0.5, 1.1);
            if (Rng.NextDouble() < 0.15)
            {
                if (lives > 1) Bot.Emit(G1Game.TimingMiss, --lives);
                else
                {
                    done = true;
                    Bot.Emit(G1Game.TimingOut, level);
                }
                return;
            }
            Bot.Emit(G1Game.TimingHit, ++level, (float)R(0, 25));
        }
    }
}
