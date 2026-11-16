using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Chess
{
    public class SearchResult
    {
        public Move Move;
        public int Score;
        public int Depth;
        public long Nodes;
        public int ElapsedMs;
        public bool Resigned;    // 引擎判断已必败（目前不使用）
        public bool Random;      // 是否为"随手一步"
        public List<ScoredMove> Roots;   // 根节点全部合法着法及其分数（走子方视角）
        public bool Complete;            // 是否至少完成了一层迭代加深
    }

    /// <summary>
    /// Negamax + Alpha-Beta + 静态搜索 + 置换表 + 杀手着法 的纯 C# 引擎。
    /// 通过 SkillLevel 控制深度、时间与随机性，从而实现 ELO 分档。
    /// </summary>
    public class Engine
    {
        private const int MaxPly = 64;
        private const int TTBits = 17;
        private const int TTSize = 1 << TTBits;
        private const int TTMask = TTSize - 1;

        private struct TTEntry
        {
            public ulong Key;
            public int Score;
            public int Depth;
            public int Flag;     // 0 = 精确, 1 = 下界, 2 = 上界
            public int From;
            public int To;
            public int Promo;
            public int Flags;
            public int Age;
        }

        private Board _b;
        private SkillLevel _skill;
        private Random _rng;
        private Stopwatch _watch;
        private int _timeLimitMs;
        private bool _stopped;
        private long _nodes;

        // 每个引擎实例独立置换表：AI 走子线程与解析线程互不影响
        private readonly TTEntry[] _tt = new TTEntry[TTSize];

        private readonly List<Move>[] _moves = new List<Move>[MaxPly];
        private readonly List<Move>[] _caps = new List<Move>[MaxPly];
        private readonly int[][] _scores = new int[MaxPly][];
        private readonly Move[,] _killers = new Move[MaxPly, 2];

        private ulong[] _path = new ulong[2048];
        private int _pathCount;

        public Engine()
        {
            for (int i = 0; i < MaxPly; i++)
            {
                _moves[i] = new List<Move>(64);
                _caps[i] = new List<Move>(32);
                _scores[i] = new int[256];
            }
        }

        public void ClearTable()
        {
            Array.Clear(_tt, 0, _tt.Length);
        }

        // ==================================================================
        // 对外入口
        // ==================================================================

        public SearchResult Search(Board position, List<ulong> historyKeys, SkillLevel skill, Random rng)
        {
            _b = position;
            _skill = skill;
            _rng = rng;
            _nodes = 0;
            _stopped = false;
            _timeLimitMs = skill.TimeMs;
            _pathCount = 0;
            for (int i = 0; i < MaxPly; i++) { _killers[i, 0] = Move.Null; _killers[i, 1] = Move.Null; }

            if (historyKeys != null)
            {
                for (int i = 0; i < historyKeys.Count && _pathCount < _path.Length - 2; i++)
                {
                    _path[_pathCount++] = historyKeys[i];
                }
            }

            _watch = Stopwatch.StartNew();

            List<Move> rootMoves = new List<Move>(64);
            _b.GenerateLegalMoves(rootMoves);
            if (rootMoves.Count == 0) return null;

            SearchResult result = new SearchResult();
            result.Nodes = 0;
            result.Depth = 0;
            result.Move = rootMoves[0];
            result.Score = 0;

            int n = rootMoves.Count;
            int[] scores = new int[n];
            int[] iterScores = new int[n];
            Move[] moves = rootMoves.ToArray();

            int completed = 0;
            int bestScore = 0;

            for (int depth = 1; depth <= skill.MaxDepth; depth++)
            {
                bool accurate = skill.NeedAccurateScores || skill.ForceAccurateScores;
                int alpha = -Eval.Infinity;
                int localBest = -Eval.Infinity;
                bool aborted = false;

                for (int i = 0; i < n; i++)
                {
                    Move m = moves[i];
                    _b.TryMake(m);
                    _path[_pathCount++] = _b.Key;
                    int sc;
                    if (accurate)
                    {
                        sc = -AlphaBeta(depth - 1, -Eval.Infinity, Eval.Infinity, 1);
                    }
                    else
                    {
                        sc = -AlphaBeta(depth - 1, -Eval.Infinity, -alpha, 1);
                    }
                    _pathCount--;
                    _b.UnmakeMove();
                    if (_stopped) { aborted = true; break; }
                    iterScores[i] = sc;
                    if (sc > localBest) localBest = sc;
                    if (sc > alpha) alpha = sc;
                }

                if (aborted) break;

                Array.Copy(iterScores, scores, n);
                completed = depth;
                bestScore = localBest;
                SortRoot(moves, scores, n);
                if (bestScore > Eval.MateThreshold - 1000) break;
                if (_watch.ElapsedMilliseconds * 3 > _timeLimitMs) break;
            }

            Move chosen = ChooseMove(moves, scores, n, result);

            // 供"AI 解析"使用的根节点全部着法分数（走子方视角，厘兵）
            result.Roots = new List<ScoredMove>(n);
            result.Complete = completed > 0;
            for (int i = 0; i < n; i++)
            {
                ScoredMove sm = new ScoredMove();
                sm.Move = moves[i];
                sm.Score = completed > 0 ? scores[i] : 0;
                result.Roots.Add(sm);
            }

            result.Move = chosen;
            result.Depth = completed;
            result.Nodes = _nodes;
            result.ElapsedMs = (int)_watch.ElapsedMilliseconds;
            if (!chosen.IsNull)
            {
                for (int i = 0; i < n; i++)
                {
                    if (moves[i].From == chosen.From && moves[i].To == chosen.To && moves[i].Promotion == chosen.Promotion)
                    {
                        result.Score = scores[i];
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>把一步搜索时间做为硬上限：保证界面不会被卡住。</summary>
        public int LastTimeLimit
        {
            get { return _timeLimitMs; }
        }

        private static void SortRoot(Move[] moves, int[] scores, int n)
        {
            for (int i = 1; i < n; i++)
            {
                Move m = moves[i];
                int s = scores[i];
                int j = i - 1;
                while (j >= 0 && scores[j] < s)
                {
                    moves[j + 1] = moves[j];
                    scores[j + 1] = scores[j];
                    j--;
                }
                moves[j + 1] = m;
                scores[j + 1] = s;
            }
        }

        // ==================================================================
        // 选点：按难度引入随机性
        // ==================================================================

        private Move ChooseMove(Move[] moves, int[] scores, int n, SearchResult result)
        {
            if (n <= 0) return Move.Null;
            if (n == 1) return moves[0];

            // 1) 低级难度：直接"随手"走一步
            if (_skill.BlunderPct > 0 && _rng.NextDouble() < _skill.BlunderPct)
            {
                result.Random = true;
                return moves[_rng.Next(n)];
            }

            if (_skill.IsFullStrength) return moves[0];

            // 2) 加噪声后，在最佳分附近的候选里随机选择
            int[] noisy = new int[n];
            int best = int.MinValue;
            int noise = _skill.NoiseCp;
            for (int i = 0; i < n; i++)
            {
                int v = scores[i];
                if (noise > 0) v += _rng.Next(-noise, noise + 1);
                noisy[i] = v;
                if (v > best) best = v;
            }

            int margin = _skill.MarginCp;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (noisy[i] >= best - margin) count++;
            }
            if (count <= 1) return moves[0];

            int pick = _rng.Next(count);
            for (int i = 0; i < n; i++)
            {
                if (noisy[i] >= best - margin)
                {
                    if (pick == 0) return moves[i];
                    pick--;
                }
            }
            return moves[0];
        }

        // ==================================================================
        // 主搜索
        // ==================================================================

        private bool TimeUp()
        {
            if (_stopped) return true;
            if ((_nodes & 511) == 0 && _watch.ElapsedMilliseconds >= _timeLimitMs)
            {
                _stopped = true;
            }
            return _stopped;
        }

        /// <summary>当前局面是否在之前的路径中出现过（_path 的最后一项就是当前局面本身）。</summary>
        private bool IsRepetition()
        {
            ulong key = _b.Key;
            for (int i = _pathCount - 2; i >= 0; i--)
            {
                if (_path[i] == key) return true;
            }
            return false;
        }

        private int AlphaBeta(int depth, int alpha, int beta, int ply)
        {
            if (_stopped) return 0;

            if (ply > 0)
            {
                if (_b.Halfmove >= 100 || IsRepetition()) return 0;
            }

            bool inCheck = _b.InCheck();

            if (inCheck) depth++;   // 被将军时延伸一层

            if (depth <= 0) return Quiescence(alpha, beta, ply);
            if (ply >= MaxPly - 2) return Eval.Evaluate(_b);

            _nodes++;
            if (TimeUp()) return 0;

            int alphaOrig = alpha;
            ulong key = _b.Key;
            int slot = (int)(key & (ulong)TTMask);
            TTEntry e = _tt[slot];
            Move ttMove = Move.Null;
            if (e.Key == key)
            {
                ttMove = new Move(e.From, e.To, e.Promo, e.Flags);
                if (e.Depth >= depth)
                {
                    int sc = e.Score;
                    if (sc > Eval.MateThreshold) sc -= ply;
                    else if (sc < -Eval.MateThreshold) sc += ply;
                    if (e.Flag == 0) return sc;
                    if (e.Flag == 1 && sc >= beta) return sc;
                    if (e.Flag == 2 && sc <= alpha) return sc;
                }
            }

            int staticEval = Eval.Evaluate(_b);

            // 空着裁剪（仅在非将军、有子力、深度足够时使用）
            if (!inCheck && ply > 0 && depth >= 3 && beta < Eval.MateThreshold
                && (_b.PieceCount[Board.Make(PT.Knight, _b.Side)] + _b.PieceCount[Board.Make(PT.Bishop, _b.Side)]
                    + _b.PieceCount[Board.Make(PT.Rook, _b.Side)] + _b.PieceCount[Board.Make(PT.Queen, _b.Side)]) > 0)
            {
                _b.MakeNull();
                _path[_pathCount++] = _b.Key;
                int sc = -AlphaBeta(depth - 3, -beta, -beta + 1, ply + 1);
                _pathCount--;
                _b.UnmakeNull();
                if (_stopped) return 0;
                if (sc >= beta)
                {
                    return sc > Eval.MateThreshold ? beta : sc;
                }
            }

            List<Move> moves = _moves[ply];
            moves.Clear();
            _b.GenerateMoves(moves, false);
            int[] scores = _scores[ply];
            ScoreMoves(moves, scores, ply, ttMove);

            int best = -Eval.Infinity;
            Move bestMove = Move.Null;
            int legal = 0;
            int moveCount = moves.Count;

            for (int i = 0; i < moveCount; i++)
            {
                // 选择排序：每轮挑出当前分数最高的着法
                int bi = i;
                for (int j = i + 1; j < moveCount; j++)
                {
                    if (scores[j] > scores[bi]) bi = j;
                }
                if (bi != i)
                {
                    Move tm = moves[i]; moves[i] = moves[bi]; moves[bi] = tm;
                    int ts = scores[i]; scores[i] = scores[bi]; scores[bi] = ts;
                }

                Move m = moves[i];
                bool quiet = m.Promotion == 0 && (m.Flags & MF.EnPassant) == 0 && _b.Sq[m.To] == 0;
                if (!_b.TryMake(m)) continue;
                legal++;
                _path[_pathCount++] = _b.Key;

                int sc;
                bool reduce = depth >= 3 && legal > 3 && quiet && !inCheck;
                if (reduce)
                {
                    sc = -AlphaBeta(depth - 2, -alpha - 1, -alpha, ply + 1);
                    if (!_stopped && sc > alpha)
                    {
                        sc = -AlphaBeta(depth - 1, -beta, -alpha, ply + 1);
                    }
                }
                else
                {
                    sc = -AlphaBeta(depth - 1, -beta, -alpha, ply + 1);
                }

                _pathCount--;
                _b.UnmakeMove();
                if (_stopped) return 0;

                if (sc > best)
                {
                    best = sc;
                    bestMove = m;
                }
                if (sc > alpha) alpha = sc;
                if (alpha >= beta)
                {
                    if (quiet)
                    {
                        Move k0 = _killers[ply, 0];
                        if (!(k0.From == m.From && k0.To == m.To))
                        {
                            _killers[ply, 1] = k0;
                            _killers[ply, 0] = m;
                        }
                    }
                    break;
                }
            }

            if (legal == 0)
            {
                return inCheck ? -Eval.MateScore + ply : 0;
            }

            int flag = best <= alphaOrig ? 2 : (best >= beta ? 1 : 0);
            TTEntry ne = new TTEntry();
            ne.Key = key;
            ne.Score = best;
            if (best > Eval.MateThreshold) ne.Score = best + ply;
            else if (best < -Eval.MateThreshold) ne.Score = best - ply;
            ne.Depth = depth;
            ne.Flag = flag;
            ne.From = bestMove.From;
            ne.To = bestMove.To;
            ne.Promo = bestMove.Promotion;
            ne.Flags = bestMove.Flags;
            ne.Age = 0;
            TTEntry old = _tt[slot];
            if (old.Key != key || old.Depth <= depth || flag == 0)
            {
                _tt[slot] = ne;
            }
            return best;
        }

        private void ScoreMoves(List<Move> moves, int[] scores, int ply, Move ttMove)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                Move m = moves[i];
                int s = 0;
                if (m.From == ttMove.From && m.To == ttMove.To && m.Promotion == ttMove.Promotion && !ttMove.IsNull)
                {
                    s = 1000000;
                }
                else
                {
                    int victim = _b.Sq[m.To];
                    if ((m.Flags & MF.EnPassant) != 0) victim = Board.Make(PT.Pawn, Board.Opp(_b.Side));
                    if (victim != 0)
                    {
                        s = 100000 + Eval.PieceValue[Board.TypeOf(victim)] * 16 - Eval.PieceValue[Board.TypeOf(_b.Sq[m.From])];
                    }
                    else if (m.Promotion != 0)
                    {
                        s = 90000 + Eval.PieceValue[m.Promotion];
                    }
                    else
                    {
                        Move k0 = _killers[ply, 0];
                        Move k1 = _killers[ply, 1];
                        if (k0.From == m.From && k0.To == m.To) s = 80000;
                        else if (k1.From == m.From && k1.To == m.To) s = 79000;
                    }
                }
                scores[i] = s;
            }
        }

        private int Quiescence(int alpha, int beta, int ply)
        {
            if (_stopped) return 0;
            _nodes++;
            if (TimeUp()) return 0;

            bool inCheck = _b.InCheck();
            int best;
            if (!inCheck)
            {
                int stand = Eval.Evaluate(_b);
                if (stand >= beta) return stand;
                if (stand > alpha) alpha = stand;
                best = stand;
            }
            else
            {
                best = -Eval.Infinity;
            }
            if (ply >= MaxPly - 2) return inCheck ? Eval.Evaluate(_b) : best;

            List<Move> caps = _caps[ply];
            caps.Clear();
            _b.GenerateMoves(caps, !inCheck);   // 被将军时搜索所有应将着法
            int[] scores = _scores[ply];
            for (int i = 0; i < caps.Count; i++)
            {
                Move m = caps[i];
                int victim = _b.Sq[m.To];
                if ((m.Flags & MF.EnPassant) != 0) victim = Board.Make(PT.Pawn, Board.Opp(_b.Side));
                int s = victim != 0
                    ? Eval.PieceValue[Board.TypeOf(victim)] * 16 - Eval.PieceValue[Board.TypeOf(_b.Sq[m.From])]
                    : 90000 + Eval.PieceValue[m.Promotion];
                scores[i] = s;
            }

            int legal = 0;
            for (int i = 0; i < caps.Count; i++)
            {
                int bi = i;
                for (int j = i + 1; j < caps.Count; j++)
                {
                    if (scores[j] > scores[bi]) bi = j;
                }
                if (bi != i)
                {
                    Move tm = caps[i]; caps[i] = caps[bi]; caps[bi] = tm;
                    int ts = scores[i]; scores[i] = scores[bi]; scores[bi] = ts;
                }

                Move m = caps[i];
                if (!_b.TryMake(m)) continue;
                legal++;
                _path[_pathCount++] = _b.Key;
                int sc = -Quiescence(-beta, -alpha, ply + 1);
                _pathCount--;
                _b.UnmakeMove();
                if (_stopped) return 0;
                if (sc > best) best = sc;
                if (sc > alpha) alpha = sc;
                if (alpha >= beta) return best;
            }
            if (inCheck && legal == 0) return -Eval.MateScore + ply;
            return best;
        }
    }
}
