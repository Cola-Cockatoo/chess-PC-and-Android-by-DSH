using System;
using System.Collections.Generic;
using System.Text;

namespace Chess
{
    /// <summary>
    /// 0x88 棋盘表示，含走法生成、合法性判定、走子/悔棋、Zobrist 哈希与增量评估。
    /// 无任何外部依赖。
    /// </summary>
    public class Board
    {
        public const int White = 1;
        public const int Black = 2;

        public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        // ---- 局面状态 ----
        public int[] Sq = new int[128];       // 棋子编码：type | (color << 3)
        public int Side;                      // 该谁走：White / Black
        public int Castling;                  // CR.* 位掩码
        public int EpSquare = -1;             // 吃过路兵目标格
        public int Halfmove;                  // 50 回合计数器
        public int Fullmove = 1;
        public ulong Key;                     // Zobrist 增量哈希

        // ---- 增量评估数据（白方为正）----
        public int[] MatPst = new int[3];     // 各方的 子力+位置分（不含王）
        public int[] KingSq = new int[3];     // 双方王的位置
        public int[] PieceCount = new int[24];// 按棋子编码统计数量
        public int[] PawnFileCount = new int[24]; // 按 color<<3|file 统计兵数
        public int Phase;                     // 阶段权重（马象1 车2 后4，双方合计）

        private struct Undo
        {
            public Move Move;
            public int Captured;
            public int CapturedSq;
            public int Castling;
            public int EpSquare;
            public int Halfmove;
            public int Fullmove;
            public ulong Key;
        }

        private Undo[] _stack = new Undo[1024];
        private int _stackTop;

        private struct NullUndo
        {
            public int EpSquare;
            public int Halfmove;
            public ulong Key;
        }

        private NullUndo[] _nullStack = new NullUndo[128];
        private int _nullTop;

        public Board()
        {
            Clear();
        }

        // ------------------------------------------------------------------
        // 基础工具
        // ------------------------------------------------------------------

        public static int TypeOf(int piece) { return piece & 7; }
        public static int ColorOf(int piece) { return piece >> 3; }
        public static int Make(int type, int color) { return type | (color << 3); }
        public static int Opp(int color) { return color == White ? Black : White; }

        public void Clear()
        {
            for (int i = 0; i < 128; i++) Sq[i] = 0;
            Side = White;
            Castling = 0;
            EpSquare = -1;
            Halfmove = 0;
            Fullmove = 1;
            Key = 0;
            _stackTop = 0;
            Array.Clear(MatPst, 0, MatPst.Length);
            Array.Clear(KingSq, 0, KingSq.Length);
            Array.Clear(PieceCount, 0, PieceCount.Length);
            Array.Clear(PawnFileCount, 0, PawnFileCount.Length);
            Phase = 0;
        }

        public Board Clone()
        {
            Board b = new Board();
            Array.Copy(Sq, b.Sq, 128);
            b.Side = Side;
            b.Castling = Castling;
            b.EpSquare = EpSquare;
            b.Halfmove = Halfmove;
            b.Fullmove = Fullmove;
            b.Key = Key;
            Array.Copy(MatPst, b.MatPst, 3);
            Array.Copy(KingSq, b.KingSq, 3);
            Array.Copy(PieceCount, b.PieceCount, 24);
            Array.Copy(PawnFileCount, b.PawnFileCount, 24);
            b.Phase = Phase;
            return b;
        }

        public int PieceAt(int sq)
        {
            if (!Squares.OnBoard(sq)) return 0;
            return Sq[sq];
        }

        // ------------------------------------------------------------------
        // 增量数据维护
        // ------------------------------------------------------------------

        private void AddPiece(int sq, int piece)
        {
            Sq[sq] = piece;
            Key ^= Zobrist.Piece[sq, piece];
            int type = TypeOf(piece);
            int color = ColorOf(piece);
            PieceCount[piece]++;
            if (type == PT.King)
            {
                KingSq[color] = sq;
            }
            else
            {
                MatPst[color] += Eval.PieceValue[type] + Eval.Pst(type, sq, color);
                if (type == PT.Knight) Phase += 1;
                else if (type == PT.Bishop) Phase += 1;
                else if (type == PT.Rook) Phase += 2;
                else if (type == PT.Queen) Phase += 4;
            }
            if (type == PT.Pawn)
            {
                PawnFileCount[(color << 3) | Squares.File(sq)]++;
            }
        }

        private void RemovePiece(int sq)
        {
            int piece = Sq[sq];
            if (piece == 0) return;
            int type = TypeOf(piece);
            int color = ColorOf(piece);
            Key ^= Zobrist.Piece[sq, piece];
            PieceCount[piece]--;
            if (type == PT.King)
            {
                KingSq[color] = 0;
            }
            else
            {
                MatPst[color] -= Eval.PieceValue[type] + Eval.Pst(type, sq, color);
                if (type == PT.Knight) Phase -= 1;
                else if (type == PT.Bishop) Phase -= 1;
                else if (type == PT.Rook) Phase -= 2;
                else if (type == PT.Queen) Phase -= 4;
            }
            if (type == PT.Pawn)
            {
                PawnFileCount[(color << 3) | Squares.File(sq)]--;
            }
            Sq[sq] = 0;
        }

        private void MovePiece(int from, int to)
        {
            int piece = Sq[from];
            RemovePiece(from);
            AddPiece(to, piece);
        }

        /// <summary>重算 Zobrist 键（测试用，校验增量更新是否正确）。</summary>
        public ulong RecomputeKey()
        {
            ulong k = 0;
            for (int i = 0; i < 128; i++)
            {
                if (!Squares.OnBoard(i) || Sq[i] == 0) continue;
                k ^= Zobrist.Piece[i, Sq[i]];
            }
            k ^= Zobrist.Castle[Castling];
            if (EpSquare >= 0) k ^= Zobrist.Ep[EpSquare];
            if (Side == Black) k ^= Zobrist.SideToMove;
            return k;
        }

        /// <summary>重算增量评估数据（测试用）。</summary>
        public void RecomputeIncremental()
        {
            Array.Clear(MatPst, 0, 3);
            Array.Clear(KingSq, 0, 3);
            Array.Clear(PieceCount, 0, PieceCount.Length);
            Array.Clear(PawnFileCount, 0, PawnFileCount.Length);
            Phase = 0;
            for (int i = 0; i < 128; i++)
            {
                if (!Squares.OnBoard(i)) continue;
                int piece = Sq[i];
                if (piece == 0) continue;
                int type = TypeOf(piece);
                int color = ColorOf(piece);
                PieceCount[piece]++;
                if (type == PT.King) KingSq[color] = i;
                else
                {
                    MatPst[color] += Eval.PieceValue[type] + Eval.Pst(type, i, color);
                    if (type == PT.Knight) Phase += 1;
                    else if (type == PT.Bishop) Phase += 1;
                    else if (type == PT.Rook) Phase += 2;
                    else if (type == PT.Queen) Phase += 4;
                }
                if (type == PT.Pawn) PawnFileCount[(color << 3) | Squares.File(i)]++;
            }
        }

        // ------------------------------------------------------------------
        // FEN
        // ------------------------------------------------------------------

        public static Board FromFen(string fen)
        {
            Board b = new Board();
            b.LoadFen(fen);
            return b;
        }

        public void LoadFen(string fen)
        {
            Clear();
            string[] parts = fen.Trim().Split(' ');
            string rows = parts[0];
            int rank = 7, file = 0;
            foreach (char c in rows)
            {
                if (c == '/')
                {
                    rank--; file = 0;
                    continue;
                }
                if (c >= '1' && c <= '8')
                {
                    file += c - '0';
                    continue;
                }
                int type = PT.None, color = White;
                switch (char.ToLowerInvariant(c))
                {
                    case 'p': type = PT.Pawn; break;
                    case 'n': type = PT.Knight; break;
                    case 'b': type = PT.Bishop; break;
                    case 'r': type = PT.Rook; break;
                    case 'q': type = PT.Queen; break;
                    case 'k': type = PT.King; break;
                }
                color = char.IsUpper(c) ? White : Black;
                if (type != PT.None && rank >= 0 && rank < 8 && file >= 0 && file < 8)
                {
                    AddPiece(Squares.Index(file, rank), Make(type, color));
                }
                file++;
            }
            Side = (parts.Length > 1 && parts[1] == "b") ? Black : White;
            Castling = 0;
            if (parts.Length > 2)
            {
                if (parts[2].IndexOf('K') >= 0) Castling |= CR.WhiteKing;
                if (parts[2].IndexOf('Q') >= 0) Castling |= CR.WhiteQueen;
                if (parts[2].IndexOf('k') >= 0) Castling |= CR.BlackKing;
                if (parts[2].IndexOf('q') >= 0) Castling |= CR.BlackQueen;
            }
            EpSquare = -1;
            if (parts.Length > 3 && parts[3] != "-")
            {
                int ep = Squares.Parse(parts[3]);
                if (ep >= 0) EpSquare = ep;
            }
            Halfmove = 0;
            if (parts.Length > 4) int.TryParse(parts[4], out Halfmove);
            Fullmove = 1;
            if (parts.Length > 5) int.TryParse(parts[5], out Fullmove);

            Key = RecomputeKey();
        }

        public string ToFen()
        {
            StringBuilder sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;
                for (int file = 0; file < 8; file++)
                {
                    int p = Sq[Squares.Index(file, rank)];
                    if (p == 0) { empty++; continue; }
                    if (empty > 0) { sb.Append(empty); empty = 0; }
                    char c = Squares.PieceLetter(TypeOf(p));
                    sb.Append(ColorOf(p) == White ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                }
                if (empty > 0) sb.Append(empty);
                if (rank > 0) sb.Append('/');
            }
            sb.Append(Side == White ? " w " : " b ");
            StringBuilder cs = new StringBuilder();
            if ((Castling & CR.WhiteKing) != 0) cs.Append('K');
            if ((Castling & CR.WhiteQueen) != 0) cs.Append('Q');
            if ((Castling & CR.BlackKing) != 0) cs.Append('k');
            if ((Castling & CR.BlackQueen) != 0) cs.Append('q');
            sb.Append(cs.Length == 0 ? "-" : cs.ToString());
            sb.Append(' ');
            sb.Append(EpSquare >= 0 ? Squares.Name(EpSquare) : "-");
            sb.Append(' ').Append(Halfmove).Append(' ').Append(Fullmove);
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 攻击判定
        // ------------------------------------------------------------------

        private static readonly int[] KnightOffsets = new int[] { 33, 31, 18, 14, -33, -31, -18, -14 };
        private static readonly int[] BishopOffsets = new int[] { 17, 15, -17, -15 };
        private static readonly int[] RookOffsets = new int[] { 16, -16, 1, -1 };
        private static readonly int[] KingOffsets = new int[] { 17, 16, 15, 1, -1, -15, -16, -17 };

        /// <summary>sq 是否被 byColor 方攻击。</summary>
        public bool IsSquareAttacked(int sq, int byColor)
        {
            // 兵
            if (byColor == White)
            {
                int a = sq - 15, b = sq - 17;
                if (Squares.OnBoard(a) && Sq[a] == Make(PT.Pawn, White)) return true;
                if (Squares.OnBoard(b) && Sq[b] == Make(PT.Pawn, White)) return true;
            }
            else
            {
                int a = sq + 15, b = sq + 17;
                if (Squares.OnBoard(a) && Sq[a] == Make(PT.Pawn, Black)) return true;
                if (Squares.OnBoard(b) && Sq[b] == Make(PT.Pawn, Black)) return true;
            }
            // 马
            for (int i = 0; i < 8; i++)
            {
                int t = sq + KnightOffsets[i];
                if (Squares.OnBoard(t) && Sq[t] == Make(PT.Knight, byColor)) return true;
            }
            // 王
            for (int i = 0; i < 8; i++)
            {
                int t = sq + KingOffsets[i];
                if (Squares.OnBoard(t) && Sq[t] == Make(PT.King, byColor)) return true;
            }
            // 象 / 后（斜线）
            for (int i = 0; i < 4; i++)
            {
                int dir = BishopOffsets[i];
                int t = sq + dir;
                while (Squares.OnBoard(t))
                {
                    int p = Sq[t];
                    if (p != 0)
                    {
                        if (ColorOf(p) == byColor && (TypeOf(p) == PT.Bishop || TypeOf(p) == PT.Queen)) return true;
                        break;
                    }
                    t += dir;
                }
            }
            // 车 / 后（直线）
            for (int i = 0; i < 4; i++)
            {
                int dir = RookOffsets[i];
                int t = sq + dir;
                while (Squares.OnBoard(t))
                {
                    int p = Sq[t];
                    if (p != 0)
                    {
                        if (ColorOf(p) == byColor && (TypeOf(p) == PT.Rook || TypeOf(p) == PT.Queen)) return true;
                        break;
                    }
                    t += dir;
                }
            }
            return false;
        }

        /// <summary>某一方"控制"（攻击范围）的全部格子，做成 64 位掩码。
        /// 位序：rank * 8 + file（第 0 位 = a1），与界面无关，方便两端共用同一套判定。
        ///
        /// 语义：几何攻击范围 —— 兵的斜前方、马/王的一步、象车后的射线（被第一个子挡住即停，
        /// 那个被挡住的格子本身算控制）。与"能真的吃过去"略有差别：被牵制的子依然算控制它的格子
        /// （这是通行的控制格画法，本程序按此实现）。</summary>
        public ulong ControlledSquares(int byColor)
        {
            ulong mask = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int f = 0; f < 8; f++)
                {
                    if (IsSquareAttacked(Squares.Index(f, r), byColor)) mask |= 1UL << (r * 8 + f);
                }
            }
            return mask;
        }

        /// <summary>控制格掩码里的位（rank * 8 + file）。</summary>
        public static ulong ControlBit(int file, int rank)
        {
            return 1UL << (rank * 8 + file);
        }

        public bool InCheck(int color)
        {
            int k = KingSq[color];
            if (k == 0 && Sq[0] != Make(PT.King, color)) return false;
            return IsSquareAttacked(k, Opp(color));
        }

        public bool InCheck()
        {
            return InCheck(Side);
        }

        // ------------------------------------------------------------------
        // 走法生成
        // ------------------------------------------------------------------

        private void AddPawnMove(List<Move> list, int from, int to, int promoRank, int flags)
        {
            if (Squares.Rank(to) == promoRank)
            {
                list.Add(new Move(from, to, PT.Queen, flags));
                list.Add(new Move(from, to, PT.Rook, flags));
                list.Add(new Move(from, to, PT.Bishop, flags));
                list.Add(new Move(from, to, PT.Knight, flags));
            }
            else
            {
                list.Add(new Move(from, to, 0, flags));
            }
        }

        /// <summary>生成伪合法走法（可能让己方王被吃；王车易位已含"不经过被攻击格"的检查）。</summary>
        public void GenerateMoves(List<Move> list, bool capturesOnly)
        {
            int us = Side;
            int them = Opp(us);
            for (int sq = 0; sq < 128; sq++)
            {
                if ((sq & 0x88) != 0) continue;
                int piece = Sq[sq];
                if (piece == 0 || ColorOf(piece) != us) continue;
                int type = TypeOf(piece);
                switch (type)
                {
                    case PT.Pawn:
                        {
                            int dir = us == White ? 16 : -16;
                            int startRank = us == White ? 1 : 6;
                            int promoRank = us == White ? 7 : 0;
                            int to = sq + dir;
                            if (!capturesOnly && Squares.OnBoard(to) && Sq[to] == 0)
                            {
                                AddPawnMove(list, sq, to, promoRank, MF.None);
                                if (Squares.Rank(sq) == startRank)
                                {
                                    int to2 = to + dir;
                                    if (Squares.OnBoard(to2) && Sq[to2] == 0)
                                        list.Add(new Move(sq, to2, 0, MF.DoublePush));
                                }
                            }
                            for (int d = -1; d <= 1; d += 2)
                            {
                                int t = sq + dir + d;
                                if (!Squares.OnBoard(t)) continue;
                                int target = Sq[t];
                                if (target != 0)
                                {
                                    if (ColorOf(target) == them) AddPawnMove(list, sq, t, promoRank, MF.None);
                                }
                                else if (t == EpSquare)
                                {
                                    list.Add(new Move(sq, t, 0, MF.EnPassant));
                                }
                            }
                            break;
                        }
                    case PT.Knight:
                        for (int i = 0; i < 8; i++)
                        {
                            int t = sq + KnightOffsets[i];
                            if (!Squares.OnBoard(t)) continue;
                            int target = Sq[t];
                            if (target != 0 && ColorOf(target) == us) continue;
                            if (capturesOnly && target == 0) continue;
                            list.Add(new Move(sq, t, 0, MF.None));
                        }
                        break;
                    case PT.Bishop:
                    case PT.Rook:
                    case PT.Queen:
                        {
                            int[] offs = type == PT.Bishop ? BishopOffsets : (type == PT.Rook ? RookOffsets : KingOffsets);
                            int n = type == PT.Queen ? 8 : 4;
                            for (int i = 0; i < n; i++)
                            {
                                int dir = offs[i];
                                int t = sq + dir;
                                while (Squares.OnBoard(t))
                                {
                                    int target = Sq[t];
                                    if (target == 0)
                                    {
                                        if (!capturesOnly) list.Add(new Move(sq, t, 0, MF.None));
                                    }
                                    else
                                    {
                                        if (ColorOf(target) == them) list.Add(new Move(sq, t, 0, MF.None));
                                        break;
                                    }
                                    t += dir;
                                }
                            }
                            break;
                        }
                    case PT.King:
                        for (int i = 0; i < 8; i++)
                        {
                            int t = sq + KingOffsets[i];
                            if (!Squares.OnBoard(t)) continue;
                            int target = Sq[t];
                            if (target != 0 && ColorOf(target) == us) continue;
                            if (capturesOnly && target == 0) continue;
                            list.Add(new Move(sq, t, 0, MF.None));
                        }
                        if (!capturesOnly) GenerateCastles(list, us);
                        break;
                }
            }
        }

        private void GenerateCastles(List<Move> list, int us)
        {
            if (us == White)
            {
                if ((Castling & CR.WhiteKing) != 0
                    && Sq[Squares.E1] == Make(PT.King, White)
                    && Sq[Squares.H1] == Make(PT.Rook, White)
                    && Sq[5] == 0 && Sq[6] == 0
                    && !IsSquareAttacked(Squares.E1, Black) && !IsSquareAttacked(5, Black))
                    list.Add(new Move(Squares.E1, 6, 0, MF.Castle));
                if ((Castling & CR.WhiteQueen) != 0
                    && Sq[Squares.E1] == Make(PT.King, White)
                    && Sq[Squares.A1] == Make(PT.Rook, White)
                    && Sq[1] == 0 && Sq[2] == 0 && Sq[3] == 0
                    && !IsSquareAttacked(Squares.E1, Black) && !IsSquareAttacked(3, Black))
                    list.Add(new Move(Squares.E1, 2, 0, MF.Castle));
            }
            else
            {
                if ((Castling & CR.BlackKing) != 0
                    && Sq[Squares.E8] == Make(PT.King, Black)
                    && Sq[Squares.H8] == Make(PT.Rook, Black)
                    && Sq[117] == 0 && Sq[118] == 0
                    && !IsSquareAttacked(Squares.E8, White) && !IsSquareAttacked(117, White))
                    list.Add(new Move(Squares.E8, 118, 0, MF.Castle));
                if ((Castling & CR.BlackQueen) != 0
                    && Sq[Squares.E8] == Make(PT.King, Black)
                    && Sq[Squares.A8] == Make(PT.Rook, Black)
                    && Sq[113] == 0 && Sq[114] == 0 && Sq[115] == 0
                    && !IsSquareAttacked(Squares.E8, White) && !IsSquareAttacked(115, White))
                    list.Add(new Move(Squares.E8, 114, 0, MF.Castle));
            }
        }

        /// <summary>生成完全合法走法。</summary>
        public void GenerateLegalMoves(List<Move> list)
        {
            List<Move> pseudo = new List<Move>(64);
            GenerateMoves(pseudo, false);
            for (int i = 0; i < pseudo.Count; i++)
            {
                if (TryMake(pseudo[i]))
                {
                    list.Add(pseudo[i]);
                    UnmakeMove();
                }
            }
        }

        public bool HasLegalMove()
        {
            List<Move> pseudo = new List<Move>(64);
            GenerateMoves(pseudo, false);
            for (int i = 0; i < pseudo.Count; i++)
            {
                if (TryMake(pseudo[i]))
                {
                    UnmakeMove();
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 走子 / 悔棋
        // ------------------------------------------------------------------

        public void MakeMove(Move m)
        {
            Undo u = new Undo();
            u.Move = m;
            u.Captured = 0;
            u.CapturedSq = -1;
            u.Castling = Castling;
            u.EpSquare = EpSquare;
            u.Halfmove = Halfmove;
            u.Fullmove = Fullmove;
            u.Key = Key;

            int piece = Sq[m.From];
            int type = TypeOf(piece);
            int color = ColorOf(piece);
            int captured = 0;
            int capturedSq = -1;

            if ((m.Flags & MF.EnPassant) != 0)
            {
                capturedSq = m.To + (color == White ? -16 : 16);
                captured = Sq[capturedSq];
                RemovePiece(capturedSq);
            }
            else if (Sq[m.To] != 0)
            {
                capturedSq = m.To;
                captured = Sq[m.To];
                RemovePiece(m.To);
            }

            MovePiece(m.From, m.To);
            if (m.Promotion != 0)
            {
                RemovePiece(m.To);
                AddPiece(m.To, Make(m.Promotion, color));
            }

            if ((m.Flags & MF.Castle) != 0)
            {
                int rf, rt;
                if (m.To == 6) { rf = 7; rt = 5; }
                else if (m.To == 2) { rf = 0; rt = 3; }
                else if (m.To == 118) { rf = 119; rt = 117; }
                else { rf = 112; rt = 115; }
                MovePiece(rf, rt);
            }

            u.Captured = captured;
            u.CapturedSq = capturedSq;

            int newCastling = Castling;
            if (type == PT.King)
            {
                if (color == White) newCastling &= ~(CR.WhiteKing | CR.WhiteQueen);
                else newCastling &= ~(CR.BlackKing | CR.BlackQueen);
            }
            if (m.From == Squares.A1 || m.To == Squares.A1) newCastling &= ~CR.WhiteQueen;
            if (m.From == Squares.H1 || m.To == Squares.H1) newCastling &= ~CR.WhiteKing;
            if (m.From == Squares.A8 || m.To == Squares.A8) newCastling &= ~CR.BlackQueen;
            if (m.From == Squares.H8 || m.To == Squares.H8) newCastling &= ~CR.BlackKing;
            if (newCastling != Castling)
            {
                Key ^= Zobrist.Castle[Castling];
                Key ^= Zobrist.Castle[newCastling];
                Castling = newCastling;
            }

            if (EpSquare >= 0)
            {
                Key ^= Zobrist.Ep[EpSquare];
                EpSquare = -1;
            }
            if ((m.Flags & MF.DoublePush) != 0)
            {
                EpSquare = (m.From + m.To) / 2;
                Key ^= Zobrist.Ep[EpSquare];
            }

            if (type == PT.Pawn || captured != 0) Halfmove = 0;
            else Halfmove++;
            if (color == Black) Fullmove++;

            Side = Opp(color);
            Key ^= Zobrist.SideToMove;

            if (_stackTop >= _stack.Length)
            {
                Array.Resize(ref _stack, _stack.Length * 2);
            }
            _stack[_stackTop++] = u;
        }

        public void UnmakeMove()
        {
            if (_stackTop == 0) return;
            Undo u = _stack[--_stackTop];
            Move m = u.Move;

            // 撤销王车易位中的车
            if ((m.Flags & MF.Castle) != 0)
            {
                int rf, rt;
                if (m.To == 6) { rf = 7; rt = 5; }
                else if (m.To == 2) { rf = 0; rt = 3; }
                else if (m.To == 118) { rf = 119; rt = 117; }
                else { rf = 112; rt = 115; }
                MovePiece(rt, rf);
            }

            int piece = Sq[m.To];
            int color = ColorOf(piece);
            if (m.Promotion != 0)
            {
                RemovePiece(m.To);
                AddPiece(m.To, Make(PT.Pawn, color));
                piece = Sq[m.To];
            }
            MovePiece(m.To, m.From);

            if (u.CapturedSq >= 0 && u.Captured != 0)
            {
                AddPiece(u.CapturedSq, u.Captured);
            }

            Castling = u.Castling;
            EpSquare = u.EpSquare;
            Halfmove = u.Halfmove;
            Fullmove = u.Fullmove;
            Key = u.Key;
            Side = color;   // 悔棋后轮到刚才走子的那一方
        }

        /// <summary>走一步并检查是否让自己的王处于被攻击状态；非法则自动回滚。</summary>
        public bool TryMake(Move m)
        {
            int mover = ColorOf(Sq[m.From]);
            MakeMove(m);
            if (IsSquareAttacked(KingSq[mover], Opp(mover)))
            {
                UnmakeMove();
                return false;
            }
            return true;
        }

        public bool IsLegal(Move m)
        {
            if (TryMake(m))
            {
                UnmakeMove();
                return true;
            }
            return false;
        }

        /// <summary>空着（只用于搜索中的空着裁剪）。</summary>
        public void MakeNull()
        {
            NullUndo u = new NullUndo();
            u.EpSquare = EpSquare;
            u.Halfmove = Halfmove;
            u.Key = Key;
            if (_nullTop >= _nullStack.Length)
            {
                Array.Resize(ref _nullStack, _nullStack.Length * 2);
            }
            _nullStack[_nullTop++] = u;

            if (EpSquare >= 0)
            {
                Key ^= Zobrist.Ep[EpSquare];
                EpSquare = -1;
            }
            Halfmove++;
            Side = Opp(Side);
            Key ^= Zobrist.SideToMove;
        }

        public void UnmakeNull()
        {
            if (_nullTop == 0) return;
            NullUndo u = _nullStack[--_nullTop];
            Side = Opp(Side);
            EpSquare = u.EpSquare;
            Halfmove = u.Halfmove;
            Key = u.Key;
        }

        // ------------------------------------------------------------------
        // 记谱（SAN）
        // ------------------------------------------------------------------

        public string San(Move m)
        {
            StringBuilder sb = new StringBuilder();
            bool capture = Sq[m.To] != 0 || (m.Flags & MF.EnPassant) != 0;
            if ((m.Flags & MF.Castle) != 0)
            {
                sb.Append(m.To == 6 || m.To == 118 ? "O-O" : "O-O-O");
            }
            else
            {
                int type = TypeOf(Sq[m.From]);
                if (type == PT.Pawn)
                {
                    if (capture) sb.Append((char)('a' + Squares.File(m.From))).Append('x');
                    sb.Append(Squares.Name(m.To));
                    if (m.Promotion != 0) sb.Append('=').Append(Squares.PieceLetter(m.Promotion));
                }
                else
                {
                    sb.Append(Squares.PieceLetter(type));
                    List<Move> all = new List<Move>(64);
                    GenerateLegalMoves(all);
                    // 标准 SAN 的消歧规则（先看"同类型、能到同一格"的其他子）：
                    //   · 没有别的子能到    → 不消歧
                    //   · 没有别的子与它同列 → 只写列（Nbd2）
                    //   · 有同列但没有同行  → 只写行（R1a3）
                    //   · 行列都撞          → 写完整格子名（Qa1a3）
                    bool sameFile = false, sameRank = false, other = false;
                    for (int i = 0; i < all.Count; i++)
                    {
                        Move o = all[i];
                        if (o.To != m.To || o.From == m.From) continue;
                        if (TypeOf(Sq[o.From]) != type) continue;
                        other = true;
                        if (Squares.File(o.From) == Squares.File(m.From)) sameFile = true;
                        if (Squares.Rank(o.From) == Squares.Rank(m.From)) sameRank = true;
                    }
                    if (other)
                    {
                        if (!sameFile) sb.Append((char)('a' + Squares.File(m.From)));
                        else if (!sameRank) sb.Append((char)('1' + Squares.Rank(m.From)));
                        else sb.Append(Squares.Name(m.From));
                    }
                    if (capture) sb.Append('x');
                    sb.Append(Squares.Name(m.To));
                }
            }

            if (TryMake(m))
            {
                bool oppCheck = InCheck(Side);
                bool oppHasMove = HasLegalMove();
                if (oppCheck && !oppHasMove) sb.Append('#');
                else if (oppCheck) sb.Append('+');
                UnmakeMove();
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 和棋判定
        // ------------------------------------------------------------------

        /// <summary>子力不足判和：K vs K、K+轻子 vs K、K+B vs K+B 同色格。</summary>
        public bool IsInsufficientMaterial()
        {
            int knights = 0, bishops = 0, others = 0;
            int bishopColor = -1;
            bool sameColorBishops = true;
            for (int i = 0; i < 128; i++)
            {
                if (!Squares.OnBoard(i)) continue;
                int p = Sq[i];
                if (p == 0) continue;
                int t = TypeOf(p);
                if (t == PT.King) continue;
                if (t == PT.Knight) { knights++; continue; }
                if (t == PT.Bishop)
                {
                    bishops++;
                    int c = Squares.SameColorIndex(i);
                    if (bishopColor < 0) bishopColor = c;
                    else if (bishopColor != c) sameColorBishops = false;
                    continue;
                }
                others++;
            }
            if (others > 0) return false;
            if (bishops == 0 && knights <= 1) return true;                 // K vs K / K+马 vs K
            if (knights == 0 && bishops == 1) return true;                 // K+象 vs K
            if (knights == 0 && bishops >= 2 && sameColorBishops) return true; // 全部象在同色格
            return false;
        }

        /// <summary>统计重复局面次数（含当前局面）。</summary>
        public static int RepetitionCount(List<ulong> keys, ulong key)
        {
            int n = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] == key) n++;
            }
            return n;
        }
    }

    /// <summary>固定种子的 Zobrist 随机表。</summary>
    public static class Zobrist
    {
        public static readonly ulong[,] Piece = new ulong[128, 24];
        public static readonly ulong[] Castle = new ulong[16];
        public static readonly ulong[] Ep = new ulong[128];
        public static readonly ulong SideToMove;

        static Zobrist()
        {
            Random rnd = new Random(20240517);
            byte[] buf = new byte[8];
            for (int i = 0; i < 128; i++)
            {
                for (int p = 1; p < 24; p++)
                {
                    rnd.NextBytes(buf);
                    Piece[i, p] = BitConverter.ToUInt64(buf, 0);
                }
            }
            for (int i = 0; i < 16; i++)
            {
                if (i == 0) { Castle[i] = 0; continue; }
                rnd.NextBytes(buf);
                Castle[i] = BitConverter.ToUInt64(buf, 0);
            }
            for (int i = 0; i < 128; i++)
            {
                rnd.NextBytes(buf);
                Ep[i] = BitConverter.ToUInt64(buf, 0);
            }
            rnd.NextBytes(buf);
            SideToMove = BitConverter.ToUInt64(buf, 0);
        }
    }
}
