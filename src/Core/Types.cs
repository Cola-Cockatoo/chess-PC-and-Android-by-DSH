using System;

namespace Chess
{
    /// <summary>对局模式：人机 / 人人（本地）。</summary>
    public enum GameMode
    {
        HumanVsAi,
        HumanVsHuman
    }

    /// <summary>棋子类型。</summary>
    public static class PT
    {
        public const int None = 0;
        public const int Pawn = 1;
        public const int Knight = 2;
        public const int Bishop = 3;
        public const int Rook = 4;
        public const int Queen = 5;
        public const int King = 6;
    }

    /// <summary>走法标志位。</summary>
    public static class MF
    {
        public const int None = 0;
        public const int EnPassant = 1;
        public const int Castle = 2;
        public const int DoublePush = 4;
    }

    /// <summary>王车易位权利位。</summary>
    public static class CR
    {
        public const int WhiteKing = 1;
        public const int WhiteQueen = 2;
        public const int BlackKing = 4;
        public const int BlackQueen = 8;
    }

    /// <summary>一个走法。</summary>
    public struct Move
    {
        public int From;
        public int To;
        public int Promotion;   // PT.* 或 0
        public int Flags;       // MF.*

        public Move(int from, int to, int promotion, int flags)
        {
            From = from;
            To = to;
            Promotion = promotion;
            Flags = flags;
        }

        /// <summary>空着法（占位用）。</summary>
        public static readonly Move Null = new Move(0, 0, 0, 0);

        public bool IsNull
        {
            get { return From == 0 && To == 0 && Promotion == 0 && Flags == 0; }
        }

        public bool IsCaptureFlag
        {
            get { return (Flags & MF.EnPassant) != 0; }
        }

        public string ToUci()
        {
            return Squares.Name(From) + Squares.Name(To) + (Promotion == 0 ? "" : Squares.PromotionLetter(Promotion));
        }

        public override string ToString()
        {
            return ToUci();
        }
    }

    /// <summary>带分数的着法（分析用）。</summary>
    public struct ScoredMove
    {
        public Move Move;
        public int Score;    // 走子方视角，厘兵
    }

    /// <summary>棋盘坐标辅助（0x88 表示法：index = rank * 16 + file，rank 0 = 第 1 横线）。</summary>
    public static class Squares
    {
        public const int A1 = 0;
        public const int E1 = 4;
        public const int H1 = 7;
        public const int A8 = 112;
        public const int E8 = 116;
        public const int H8 = 119;

        public static bool OnBoard(int sq)
        {
            return (sq & 0x88) == 0;
        }

        public static int Rank(int sq)
        {
            return sq >> 4;
        }

        public static int File(int sq)
        {
            return sq & 7;
        }

        public static int Index(int file, int rank)
        {
            return rank * 16 + file;
        }

        public static string Name(int sq)
        {
            if (!OnBoard(sq)) return "--";
            return "" + (char)('a' + File(sq)) + (char)('1' + Rank(sq));
        }

        public static int Parse(string name)
        {
            if (name == null || name.Length < 2) return -1;
            int f = name[0] - 'a';
            int r = name[1] - '1';
            if (f < 0 || f > 7 || r < 0 || r > 7) return -1;
            return Index(f, r);
        }

        public static char PieceLetter(int type)
        {
            switch (type)
            {
                case PT.Pawn: return 'P';
                case PT.Knight: return 'N';
                case PT.Bishop: return 'B';
                case PT.Rook: return 'R';
                case PT.Queen: return 'Q';
                case PT.King: return 'K';
            }
            return '?';
        }

        public static string PromotionLetter(int type)
        {
            return PieceLetter(type).ToString().ToLowerInvariant();
        }

        /// <summary>该格的"同色格"编号（0..31），用于判断象的同色异格。</summary>
        public static int SameColorIndex(int sq)
        {
            return (Rank(sq) + File(sq)) % 2;
        }
    }
}
