using System;

namespace Chess
{
    /// <summary>一档难度（ELO 等级分）对应的引擎行为参数。</summary>
    public class SkillLevel
    {
        public int Elo;
        public string Tier;      // 中文档位名
        public int MaxDepth;     // 最大搜索深度
        public int TimeMs;       // 每步思考时间上限（毫秒）
        public double BlunderPct;// 犯错概率：直接随机走一步
        public int MarginCp;     // 候选走法分数窗口（越大越"看不清"）
        public int NoiseCp;      // 根节点分数扰动
        public bool ForceAccurateScores; // 强制根节点取精确分（AI 解析用）
        public string Desc;      // 难度描述

        public bool NeedAccurateScores
        {
            get { return MarginCp > 0 || NoiseCp > 0 || BlunderPct > 0; }
        }

        public bool IsFullStrength
        {
            get { return BlunderPct <= 0 && MarginCp <= 0 && NoiseCp <= 0; }
        }

        public override string ToString()
        {
            return "ELO " + Elo;
        }
    }

    /// <summary>ELO 100 ~ 2000，每 100 分一档，共 20 档。</summary>
    public static class SkillTable
    {
        public const int MinElo = 100;
        public const int MaxElo = 2000;
        public const int Step = 100;

        public static readonly SkillLevel[] Levels = Build();

        private static SkillLevel[] Build()
        {
            int[] depth = { 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 7, 8, 10 };
            int[] time = { 120, 150, 200, 250, 300, 350, 400, 450, 500, 550, 600, 700, 800, 900, 1000, 1150, 1300, 1450, 1600, 1800 };
            double[] blunder = { 0.85, 0.66, 0.50, 0.38, 0.30, 0.24, 0.19, 0.15, 0.12, 0.09, 0.07, 0.055, 0.045, 0.035, 0.028, 0.020, 0.014, 0.009, 0.004, 0.0 };
            int[] margin = { 400, 330, 270, 220, 185, 155, 132, 115, 100, 86, 72, 60, 50, 42, 35, 28, 20, 12, 6, 0 };
            int[] noise = { 120, 100, 85, 72, 60, 50, 42, 36, 30, 25, 20, 16, 13, 10, 8, 6, 4, 2, 1, 0 };

            SkillLevel[] list = new SkillLevel[20];
            for (int i = 0; i < 20; i++)
            {
                SkillLevel s = new SkillLevel();
                s.Elo = MinElo + i * Step;
                s.MaxDepth = depth[i];
                s.TimeMs = time[i];
                s.BlunderPct = blunder[i];
                s.MarginCp = margin[i];
                s.NoiseCp = noise[i];
                s.Tier = TierName(s.Elo);
                s.Desc = Describe(s.Elo);
                list[i] = s;
            }
            return list;
        }

        public static string TierName(int elo)
        {
            if (elo <= 300) return "入门";
            if (elo <= 600) return "初级";
            if (elo <= 900) return "中级";
            if (elo <= 1200) return "进阶";
            if (elo <= 1500) return "高手";
            if (elo <= 1700) return "专家";
            if (elo <= 1900) return "大师";
            return "特级大师";
        }

        public static string Describe(int elo)
        {
            if (elo <= 300) return "随手乱走，经常送子";
            if (elo <= 600) return "只看一步，会漏战术";
            if (elo <= 900) return "懂基本吃子与兑换";
            if (elo <= 1200) return "有简单战术意识";
            if (elo <= 1500) return "能算两三步的战术";
            if (elo <= 1700) return "攻防均衡，失误很少";
            if (elo <= 1900) return "算度较深，善于抓漏";
            return "全力搜索，几乎不失误";
        }

        public static SkillLevel ForElo(int elo)
        {
            if (elo < MinElo) elo = MinElo;
            if (elo > MaxElo) elo = MaxElo;
            int idx = (elo - MinElo) / Step;
            if (idx < 0) idx = 0;
            if (idx >= Levels.Length) idx = Levels.Length - 1;
            return Levels[idx];
        }
    }
}
