using System;
using System.Collections.Generic;

namespace Chess
{
    /// <summary>一手棋的评价等级（从优到劣）。</summary>
    public class Annotation
    {
        public string Symbol;      // !! ! !? ?! ? ??
        public string English;     // Brilliant / Good / ...
        public string Chinese;     // 精彩 / 好棋 / ...
        public int Rank;           // 0 = 最优

        public Annotation(string symbol, string english, string chinese, int rank)
        {
            Symbol = symbol;
            English = english;
            Chinese = chinese;
            Rank = rank;
        }
    }

    /// <summary>等级表：优 → 劣。</summary>
    public static class Annotations
    {
        public static readonly Annotation Brilliant = new Annotation("!!", "Brilliant", "精彩", 0);
        public static readonly Annotation Good = new Annotation("!", "Good", "好棋", 1);
        public static readonly Annotation Interesting = new Annotation("!?", "Interesting", "有趣", 2);
        public static readonly Annotation Dubious = new Annotation("?!", "Dubious", "可疑", 3);
        public static readonly Annotation Mistake = new Annotation("?", "Mistake", "错着", 4);
        public static readonly Annotation Blunder = new Annotation("??", "Blunder", "大漏着", 5);
        public static readonly Annotation Pending = new Annotation("…", "Analyzing", "分析中", 6);

        /// <summary>优 → 劣，用于界面右下角的符号说明。</summary>
        public static readonly Annotation[] All = new Annotation[] { Brilliant, Good, Interesting, Dubious, Mistake, Blunder };
    }

    /// <summary>一手棋的分析结果。</summary>
    public class MoveAnnotation
    {
        public Annotation Grade = Annotations.Pending;
        public int LossCp;          // 相对最佳着法损失的厘兵
        public int ScoreCp;         // 该着法走完后的评估（白方视角，厘兵）
        public int BestScoreCp;     // 最佳着法的评估（白方视角，厘兵）
        public bool Ready;          // 分析是否已完成
    }

    /// <summary>一个局面的分析结果。</summary>
    public class PositionAnalysis
    {
        public int Ply;                     // 局面位于第几手之后（0 = 开局）
        public ulong Key;                   // 局面哈希（校验分析结果是否仍适用于当前局面）
        public int Side;                    // 该谁走
        public int BestScoreCp;             // 走子方视角，厘兵
        public Move BestMove;
        public List<Move> Moves = new List<Move>();
        public List<int> Scores = new List<int>();   // 与 Moves 对应，走子方视角
        public bool Terminal;               // 无子可动（将杀/逼和）
        public int Depth;
        public long Nodes;
        public int ElapsedMs;

        /// <summary>白方视角的评估（厘兵）。</summary>
        public int WhiteCp
        {
            get { return Side == Board.White ? BestScoreCp : -BestScoreCp; }
        }

        /// <summary>白方胜率（0~100）。</summary>
        public double WhiteWinRate
        {
            get { return AnalysisMath.WinRate(WhiteCp); }
        }

        /// <summary>找出某个着法在此局面下的分数（走子方视角，厘兵）。</summary>
        public bool TryGetScore(Move m, out int score)
        {
            for (int i = 0; i < Moves.Count; i++)
            {
                if (Moves[i].From == m.From && Moves[i].To == m.To && Moves[i].Promotion == m.Promotion)
                {
                    score = Scores[i];
                    return true;
                }
            }
            score = 0;
            return false;
        }
    }

    /// <summary>评估值 ↔ 胜率换算。</summary>
    public static class AnalysisMath
    {
        /// <summary>将引擎分（可能含将杀分）折算成普通厘兵，限制在 ±2000 以内。</summary>
        public static int ToCp(int score)
        {
            if (score > Eval.MateThreshold) return 2000 - (Eval.MateScore - score);
            if (score < -Eval.MateThreshold) return -2000 + (Eval.MateScore + score);
            if (score > 2000) return 2000;
            if (score < -2000) return -2000;
            return score;
        }

        /// <summary>厘兵 → 胜率（0~100），采用常用的 logistic 换算。</summary>
        public static double WinRate(int cp)
        {
            int v = ToCp(cp);
            double w = 50.0 + 50.0 * (2.0 / (1.0 + Math.Exp(-0.00368208 * v)) - 1.0);
            if (w < 0.5) w = 0.5;
            if (w > 99.5) w = 99.5;
            return w;
        }

        /// <summary>厘兵 → 便于阅读的文字（如 +1.35 / -0.42 / 将杀）。</summary>
        public static string FormatScore(int cp)
        {
            if (cp > Eval.MateThreshold) return "将杀";
            if (cp < -Eval.MateThreshold) return "-将杀";
            double p = cp / 100.0;
            return (p >= 0 ? "+" : "") + p.ToString("0.00");
        }
    }

    /// <summary>局面分析器：用固定强度的引擎给"每一手棋"打分。</summary>
    public static class Analyzer
    {
        /// <summary>分析档位：全力搜索、无随机性，且根节点取精确分。</summary>
        public static SkillLevel Profile(int maxDepth, int timeMs)
        {
            SkillLevel s = new SkillLevel();
            s.Elo = 0;
            s.Tier = "分析";
            s.Desc = "AI 解析";
            s.MaxDepth = maxDepth;
            s.TimeMs = timeMs;
            s.BlunderPct = 0;
            s.MarginCp = 0;
            s.NoiseCp = 0;
            s.ForceAccurateScores = true;
            return s;
        }

        /// <summary>分析一个局面；无合法着法时返回终局结果。</summary>
        public static PositionAnalysis Analyze(Board position, List<ulong> keys, SkillLevel profile, Random rng)
        {
            PositionAnalysis pa = new PositionAnalysis();
            pa.Side = position.Side;
            pa.Key = position.Key;

            List<Move> legal = new List<Move>(64);
            position.GenerateLegalMoves(legal);
            if (legal.Count == 0)
            {
                pa.Terminal = true;
                pa.BestScoreCp = position.InCheck(position.Side) ? -Eval.MateScore : 0;
                return pa;
            }

            Engine engine = new Engine();
            SearchResult r = engine.Search(position, keys, profile, rng);
            if (r == null || r.Roots == null)
            {
                pa.Terminal = true;
                pa.BestScoreCp = -Eval.MateScore;
                return pa;
            }

            pa.Depth = r.Depth;
            pa.Nodes = r.Nodes;
            pa.ElapsedMs = r.ElapsedMs;
            pa.BestMove = r.Move;
            pa.BestScoreCp = r.Roots.Count > 0 ? r.Roots[0].Score : r.Score;
            for (int i = 0; i < r.Roots.Count; i++)
            {
                pa.Moves.Add(r.Roots[i].Move);
                pa.Scores.Add(r.Roots[i].Score);
            }
            return pa;
        }

        /// <summary>根据"局面分析"给实际走出的一手棋定级。</summary>
        public static MoveAnnotation Judge(PositionAnalysis pa, Move played, int moverSide)
        {
            MoveAnnotation ma = new MoveAnnotation();
            if (pa == null)
            {
                ma.Ready = false;
                return ma;
            }

            ma.Ready = true;
            int playedScore;
            if (pa.Terminal)
            {
                // 终局局面：没有着法可走
                ma.LossCp = 0;
                ma.BestScoreCp = pa.WhiteCp;
                ma.ScoreCp = pa.WhiteCp;
                ma.Grade = Annotations.Good;
                return ma;
            }
            if (!pa.TryGetScore(played, out playedScore))
            {
                ma.Ready = false;
                return ma;
            }

            int best = pa.BestScoreCp;
            int loss = AnalysisMath.ToCp(best) - AnalysisMath.ToCp(playedScore);
            if (loss < 0) loss = 0;
            ma.LossCp = loss;

            // 白方视角的评估
            int whiteBest = moverSide == Board.White ? best : -best;
            int whitePlayed = moverSide == Board.White ? playedScore : -playedScore;
            ma.BestScoreCp = whiteBest;
            ma.ScoreCp = whitePlayed;

            bool playedBest = playedScore >= best;
            bool onlyMove = IsOnlyMove(pa);
            ma.Grade = Classify(loss, playedBest, onlyMove);
            return ma;
        }

        /// <summary>最佳着法是否"遥遥领先"（第二名差距 ≥ 1.5 兵或直接杀棋）。</summary>
        private static bool IsOnlyMove(PositionAnalysis pa)
        {
            if (pa.Scores.Count < 2) return true;
            int best = pa.Scores[0];
            int second = pa.Scores[1];
            if (best > Eval.MateThreshold) return true;
            if (AnalysisMath.ToCp(best) - AnalysisMath.ToCp(second) >= 150) return true;
            // 除最佳着法外其余都会明显变差，也算"唯一好棋"
            for (int i = 1; i < pa.Scores.Count; i++)
            {
                if (AnalysisMath.ToCp(best) - AnalysisMath.ToCp(pa.Scores[i]) < 150) return false;
            }
            return true;
        }

        /// <summary>按损失分定级。</summary>
        public static Annotation Classify(int lossCp, bool playedBest, bool onlyMove)
        {
            if (playedBest && onlyMove) return Annotations.Brilliant;
            if (lossCp <= 15) return Annotations.Good;
            if (lossCp <= 50) return Annotations.Interesting;
            if (lossCp <= 120) return Annotations.Dubious;
            if (lossCp <= 300) return Annotations.Mistake;
            return Annotations.Blunder;
        }
    }
}
