using System;

namespace Chess
{
    /// <summary>子力价值、位置表与静态评估。</summary>
    public static class Eval
    {
        public const int MateScore = 30000;
        public const int MateThreshold = 29000;
        public const int Infinity = 100000;

        /// <summary>子力价值（厘兵）。</summary>
        public static readonly int[] PieceValue = new int[] { 0, 100, 320, 335, 500, 950, 0 };

        // 位置表均以白方视角书写，行序为 第8横线 → 第1横线（a8..h1）。
        private static readonly int[] PawnPst = new int[]
        {
              0,  0,  0,  0,  0,  0,  0,  0,
             50, 50, 50, 50, 50, 50, 50, 50,
             10, 10, 20, 30, 30, 20, 10, 10,
              5,  5, 10, 25, 25, 10,  5,  5,
              0,  0,  0, 20, 20,  0,  0,  0,
              5, -5,-10,  0,  0,-10, -5,  5,
              5, 10, 10,-20,-20, 10, 10,  5,
              0,  0,  0,  0,  0,  0,  0,  0
        };

        private static readonly int[] KnightPst = new int[]
        {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50
        };

        private static readonly int[] BishopPst = new int[]
        {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20
        };

        private static readonly int[] RookPst = new int[]
        {
              0,  0,  0,  0,  0,  0,  0,  0,
              5, 10, 10, 10, 10, 10, 10,  5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
              0,  0,  0,  5,  5,  0,  0,  0
        };

        private static readonly int[] QueenPst = new int[]
        {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
             -5,  0,  5,  5,  5,  5,  0, -5,
              0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20
        };

        private static readonly int[] KingMidPst = new int[]
        {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
             20, 20,  0,  0,  0,  0, 20, 20,
             20, 30, 10,  0,  0, 10, 30, 20
        };

        private static readonly int[] KingEndPst = new int[]
        {
            -50,-40,-30,-20,-20,-30,-40,-50,
            -30,-20,-10,  0,  0,-10,-20,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-30,  0,  0,  0,  0,-30,-30,
            -50,-30,-30,-30,-30,-30,-30,-50
        };

        public static int Pst(int type, int sq, int color)
        {
            if (type == PT.King) return 0;         // 王的位置分按阶段单独计算
            int rank = Squares.Rank(sq);
            int file = Squares.File(sq);
            int idx = color == Board.White ? (7 - rank) * 8 + file : rank * 8 + file;
            switch (type)
            {
                case PT.Pawn: return PawnPst[idx];
                case PT.Knight: return KnightPst[idx];
                case PT.Bishop: return BishopPst[idx];
                case PT.Rook: return RookPst[idx];
                case PT.Queen: return QueenPst[idx];
            }
            return 0;
        }

        private static int KingPst(int sq, int color, int phase)
        {
            int rank = Squares.Rank(sq);
            int file = Squares.File(sq);
            int idx = color == Board.White ? (7 - rank) * 8 + file : rank * 8 + file;
            int mid = KingMidPst[idx];
            int end = KingEndPst[idx];
            if (phase > 24) phase = 24;
            if (phase < 0) phase = 0;
            return (mid * phase + end * (24 - phase)) / 24;
        }

        /// <summary>静态评估，返回"当前走子方视角"的分数。</summary>
        public static int Evaluate(Board b)
        {
            int white = b.MatPst[Board.White];
            int black = b.MatPst[Board.Black];

            white += KingPst(b.KingSq[Board.White], Board.White, b.Phase);
            black += KingPst(b.KingSq[Board.Black], Board.Black, b.Phase);

            // 双象奖励
            if (CountBishops(b, Board.White) >= 2) white += 32;
            if (CountBishops(b, Board.Black) >= 2) black += 32;

            // 叠兵惩罚
            for (int f = 0; f < 8; f++)
            {
                int wp = b.PawnFileCount[(Board.White << 3) | f];
                int bp = b.PawnFileCount[(Board.Black << 3) | f];
                if (wp > 1) white -= 12 * (wp - 1);
                if (bp > 1) black -= 12 * (bp - 1);
            }

            int score = white - black;
            if (b.Side == Board.Black) score = -score;
            return score + 8; // 先手小奖励
        }

        private static int CountBishops(Board b, int color)
        {
            return b.PieceCount[Board.Make(PT.Bishop, color)];
        }

        /// <summary>简化评估（叶节点静态搜索用，不含走子方视角翻转的额外项）。</summary>
        public static int EvaluateRelative(Board b)
        {
            return Evaluate(b);
        }
    }
}
