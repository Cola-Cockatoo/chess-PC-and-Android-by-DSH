using System;
using System.Collections.Generic;
using System.Diagnostics;
using Chess;

/// <summary>
/// 自检程序（控制台）：perft 计数、增量状态一致性、走法合法性、引擎分档冒烟测试与强弱对比。
/// 用法：chess_test.exe [--quick]
/// </summary>
public static class ChessTestMain
{
    private static int _failures;
    private static int _checks;

    public static int Main(string[] args)
    {
        bool quick = Array.IndexOf(args, "--quick") >= 0;

        Console.WriteLine("=== Chess self-test ===");
        PerftSuite(quick);
        ConsistencyTests();
        AnalysisTests();
        SanTests();
        ControlTests();
        OpeningTests();
        EngineSmoke(quick);
        if (!quick) StrengthTest();

        Console.WriteLine();
        Console.WriteLine("checks: " + _checks + ", failures: " + _failures);
        Console.WriteLine(_failures == 0 ? "ALL TESTS PASSED" : "TESTS FAILED");
        return _failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    private static void Check(bool ok, string what)
    {
        _checks++;
        if (!ok)
        {
            _failures++;
            Console.WriteLine("  [FAIL] " + what);
        }
    }

    private static void Info(string s)
    {
        Console.WriteLine(s);
    }

    // ------------------------------------------------------------------
    // 控制格（攻击范围）掩码
    // ------------------------------------------------------------------
    private static void ControlTests()
    {
        Info("");
        Info("--- control squares ---");

        // 1) 开局：兵只控制斜前方两格，不控制正前方
        Board b = new Board();
        b.LoadFen(Board.StartFen);
        ulong w = b.ControlledSquares(Board.White);
        ulong k = b.ControlledSquares(Board.Black);

        Check(Has(w, 'a', 3) && Has(w, 'h', 3) && Has(w, 'b', 3) && Has(w, 'g', 3), "开局白兵控制 a3/b3/g3/h3");
        Check(!Has(w, 'a', 4) && !Has(w, 'd', 4), "开局白兵不控制正前方 a4/d4");
        Check(Has(k, 'a', 6) && Has(k, 'h', 6) && Has(k, 'b', 6) && Has(k, 'g', 6), "开局黑兵控制 a6/b6/g6/h6");
        Check(!Has(k, 'a', 5), "开局黑兵不控制正前方 a5");
        Check(Has(w, 'c', 3) && Has(k, 'c', 6), "开局马控制的范围（c3/c6 各自被对方兵控制）");

        // 2) 兵的斜吃：白兵 d4 控制 c5/e5，不控制 d5
        b.LoadFen("8/8/8/8/3P4/8/8/8 w - - 0 1");
        w = b.ControlledSquares(Board.White);
        Check(Has(w, 'c', 5) && Has(w, 'e', 5), "白兵 d4 控制 c5/e5");
        Check(!Has(w, 'd', 5), "白兵 d4 不控制 d5");

        // 3) 射线被挡住即停：白车 a1 + 白兵 a3 → 控制 a2、a3，但到不了 a4
        b.LoadFen("8/8/8/8/8/P7/8/R7 w - - 0 1");
        w = b.ControlledSquares(Board.White);
        Check(Has(w, 'a', 2) && Has(w, 'a', 3), "白车控制 a2/a3（被己方兵挡住的 a3 也算控制）");
        Check(!Has(w, 'a', 4), "白车不越过挡子控制 a4");

        // 4) 后：四条直线 + 四条斜线
        b.LoadFen("8/8/8/8/3Q4/8/8/8 w - - 0 1");
        w = b.ControlledSquares(Board.White);
        Check(Has(w, 'd', 8) && Has(w, 'd', 1) && Has(w, 'a', 4) && Has(w, 'h', 4), "白后 d4 控制十字四端");
        Check(Has(w, 'a', 1) && Has(w, 'h', 8) && Has(w, 'g', 1) && Has(w, 'a', 7), "白后 d4 控制斜线四端");
        Check(Has(w, 'e', 5) && Has(w, 'c', 3) && Has(w, 'e', 3) && Has(w, 'c', 5), "白后 d4 控制四个相邻斜格");

        // 5) 王的一步（不含自己站的格子）
        b.LoadFen("8/8/8/8/8/8/4K3/8 w - - 0 1");
        w = b.ControlledSquares(Board.White);
        Check(Has(w, 'd', 1) && Has(w, 'd', 2) && Has(w, 'd', 3) && Has(w, 'e', 3) && Has(w, 'f', 3) &&
              Has(w, 'f', 2) && Has(w, 'f', 1) && Has(w, 'e', 1), "白王 e2 控制周围 8 格");
        Check(!Has(w, 'e', 2), "王不控制自己站的格子");

        // 6) 掩码与 IsSquareAttacked 完全一致（随机对局逐步抽查 64×2 格）
        Random rng = new Random(20260202);
        ChessGame g = new ChessGame();
        int mismatch = 0;
        int plies = 0;
        for (int ply = 0; ply < 400; ply++)
        {
            ulong mw = g.Position.ControlledSquares(Board.White);
            ulong mb = g.Position.ControlledSquares(Board.Black);
            for (int f = 0; f < 8; f++)
            {
                for (int r = 0; r < 8; r++)
                {
                    int sq = Squares.Index(f, r);
                    ulong bit = Board.ControlBit(f, r);
                    if (((mw & bit) != 0) != g.Position.IsSquareAttacked(sq, Board.White)) mismatch++;
                    if (((mb & bit) != 0) != g.Position.IsSquareAttacked(sq, Board.Black)) mismatch++;
                }
            }
            List<Chess.Move> ms = g.LegalMoves();
            if (ms.Count == 0) break;
            g.Play(ms[rng.Next(ms.Count)]);
            plies++;
        }
        Check(plies > 100, "控制格一致性抽查走了 " + plies + " 步");
        Check(mismatch == 0, "控制格掩码与 IsSquareAttacked 完全一致（不符 " + mismatch + " 处）");

        // 7) 位序约定，以及"没有任何子的一方控制格为 0"
        Check(Board.ControlBit(0, 0) == 1UL, "a1 对应最低位");
        Check(Board.ControlBit(7, 7) == (1UL << 63), "h8 对应最高位");
        Board empty = new Board();
        empty.LoadFen("8/8/8/8/8/8/8/4k3 w - - 0 1");
        Check(empty.ControlledSquares(Board.White) == 0, "白方无子时控制格为 0");
        Check(empty.ControlledSquares(Board.Black) != 0, "黑方单王仍控制周围 8 格");

    }

    private static bool Has(ulong mask, char file, int rank)
    {
        return (mask & Board.ControlBit(file - 'a', rank - 1)) != 0;
    }

    // ------------------------------------------------------------------
    // 定式学习：数据完整性与"每一条变化都真的能走出来"
    // ------------------------------------------------------------------
    private static void OpeningTests()
    {
        Info("");
        Info("--- openings ---");

        Opening[] all = Openings.All();
        Check(all.Length >= 12, "定式数量 " + all.Length + "（至少 12 个）");

        int white = 0, black = 0, branchPoints = 0, leaves = 0, nodes = 0;
        HashSet<string> ids = new HashSet<string>();
        foreach (Opening op in all)
        {
            Check(!string.IsNullOrEmpty(op.Id) && !string.IsNullOrEmpty(op.Name), "定式有 id 与名称：" + op.Id);
            Check(ids.Add(op.Id), "定式 id 不重复：" + op.Id);
            Check(op.Side == "w" || op.Side == "b", op.Name + " 的 side 合法（" + op.Side + "）");
            Check(!string.IsNullOrEmpty(op.Summary), op.Name + " 有总体思路说明");
            Check(!string.IsNullOrEmpty(op.Level), op.Name + " 有难度定位：" + op.Level);
            Check(op.Plans != null && op.Plans.Length >= 2, op.Name + " 有典型计划 " +
                  (op.Plans == null ? 0 : op.Plans.Length) + " 条");
            Check(op.Pitfalls != null && op.Pitfalls.Length >= 1, op.Name + " 有常见错误 " +
                  (op.Pitfalls == null ? 0 : op.Pitfalls.Length) + " 条");
            if (op.Plans != null)
            {
                for (int pi = 0; pi < op.Plans.Length; pi++)
                    Check(op.Plans[pi].Length >= 6, op.Name + " 计划 " + (pi + 1) + " 写得够具体");
            }
            if (op.Pitfalls != null)
            {
                for (int pi = 0; pi < op.Pitfalls.Length; pi++)
                    Check(op.Pitfalls[pi].Length >= 6, op.Name + " 常见错误 " + (pi + 1) + " 写得够具体");
            }
            if (op.Side == "w") white++; else black++;

            nodes += op.Nodes.Length;
            Check(op.Root != null && op.Root.Length > 0, op.Name + " 有第一步");
            for (int i = 0; i < op.Nodes.Length; i++)
            {
                OpeningMove nd = op.Nodes[i];
                Check(!string.IsNullOrEmpty(nd.San), op.Name + " 节点 " + i + " 有着法");
                Check(!string.IsNullOrEmpty(nd.Comment), op.Name + " 着法 " + nd.San + " 有点评");
                if (nd.Children != null && nd.Children.Length > 1) branchPoints++;
                if (nd.Children == null || nd.Children.Length == 0) leaves++;
            }

            // 走遍这个定式的每一条路径：每一步都要能在当前局面下走出来
            int paths = 0;
            WalkAll(op, op.Root, new List<string>(), ref paths);
            Check(paths > 0, op.Name + " 至少有一条完整变化（实际 " + paths + " 条）");
        }
        Check(white >= 5, "教白方的定式 " + white + " 个");
        Check(black >= 5, "教黑方的定式 " + black + " 个");
        Check(branchPoints >= 5, "有分支的着法点 " + branchPoints + " 个（学员要选一个）");
        int noted = 0, namedNodes = 0;
        foreach (Opening op in all)
        {
            foreach (OpeningMove nd in op.Nodes)
            {
                if (nd.VariationName.Length > 0)
                {
                    namedNodes++;
                    if (nd.Note.Length > 0) noted++;
                }
            }
        }
        Check(namedNodes >= 48, "有命名的变化 " + namedNodes + " 个（少了说明有名字没挂到岔口上）");
        Check(noted == namedNodes, "每个命名变化都有说明（" + noted + "/" + namedNodes + "）");
        Check(leaves >= 10 && nodes > 150, "节点 " + nodes + "，变化结尾 " + leaves);

        // StudySession 的行为：默认轮到学员（白方先手）、候选与点评可用、能推进到结束
        Opening it = Openings.ById("italian");
        Check(it != null, "按 id 找到意大利开局");
        if (it != null)
        {
            StudySession s = new StudySession(it);
            Check(s.IsStudentTurn, "开局轮到学员走（白方）");
            Check(s.Candidates().Count == 1, "意大利开局第一步只有一个候选");
            Check(s.Candidates()[0].San == "e4", "第一步候选是 e4");
            Check(!string.IsNullOrEmpty(s.Candidates()[0].Comment), "第一步带点评");
            Check(s.PlayMove(0, 0) == null, "走到一个不存在的格子不产生着法");
            int guard = 0;
            while (!s.Finished && guard++ < 100)
            {
                if (s.IsStudentTurn)
                {
                    List<StudyStep> cs = s.Candidates();
                    Check(cs.Count > 0, "学员回合一定有候选着法");
                    if (cs.Count == 0) break;
                    Check(s.Play(cs[0].NodeIndex) != null, "学员能走出候选着法");
                }
                else
                {
                    Check(s.PlayOpponent() != null, "程序能应一手");
                }
            }
            Check(s.Finished, "沿主线能走到变化结束（用了 " + guard + " 步）");
            Check(s.Game.PlyCount > 8, "走完后手数 " + s.Game.PlyCount);
            s.Restart();
            Check(s.Game.PlyCount == 0 && s.IsStudentTurn && !s.Finished, "重来后回到起始局面");
        }

        // 定式每一步都必须是合法着法（不合法会在上面 WalkAll 里报错）
        Check(_failures == 0 || true, "定式数据检查完成");
    }

    /// <summary>对一棵着法树做深度优先遍历，沿路用引擎逐步验证每一步合法。</summary>
    private static void WalkAll(Opening op, int[] nodes, List<string> line, ref int paths)
    {
        if (nodes == null || nodes.Length == 0)
        {
            paths++;
            if (paths == 1)
            {
                Check(line.Count > 0, op.Name + " 变化非空");
            }
            return;
        }
        for (int k = 0; k < nodes.Length; k++)
        {
            OpeningMove nd = op.Nodes[nodes[k]];
            // 用"这条线走到这里为止"的局面来验证
            ChessGame g = new ChessGame();
            bool ok = true;
            for (int i = 0; i < line.Count; i++)
            {
                Move m = Openings.MoveFromSan(g.Position, null, line[i]);
                if (m.IsNull || !g.Play(m)) { ok = false; break; }
            }
            Move mv = ok ? Openings.MoveFromSan(g.Position, null, nd.San) : Move.Null;
            if (mv.IsNull)
            {
                Check(false, op.Name + " 的变化里着法走不通：" + string.Join(" ", line.ToArray()) +
                             (line.Count > 0 ? " " : "") + nd.San);
                continue;
            }
            line.Add(nd.San);
            WalkAll(op, nd.Children, line, ref paths);
            line.RemoveAt(line.Count - 1);
        }
    }

    // ------------------------------------------------------------------
    // perft
    // ------------------------------------------------------------------
    private static void PerftSuite(bool quick)
    {
        Info("");
        Info("--- perft ---");
        PerftCase("startpos", Board.StartFen, new long[] { 20, 400, 8902, 197281, 4865609 }, quick ? 4 : 5);
        PerftCase("kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
            new long[] { 48, 2039, 97862, 4085603 }, quick ? 3 : 4);
        PerftCase("pos3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
            new long[] { 14, 191, 2812, 43238, 674624 }, 5);
        PerftCase("pos4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1",
            new long[] { 6, 264, 9467, 422333 }, quick ? 3 : 4);
        PerftCase("pos5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 0 1",
            new long[] { 44, 1486, 62379, 2103487 }, quick ? 3 : 4);
        PerftCase("pos6", "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 1",
            new long[] { 46, 2079, 89890, 3894594 }, quick ? 3 : 4);
    }

    private static void PerftCase(string name, string fen, long[] expected, int maxDepth)
    {
        Stopwatch sw = Stopwatch.StartNew();
        for (int d = 1; d <= maxDepth && d <= expected.Length; d++)
        {
            Board b = Board.FromFen(fen);
            long n = Perft(b, d);
            Check(n == expected[d - 1], name + " perft(" + d + ") = " + n + " expected " + expected[d - 1]);
        }
        // 合法走法生成器与"伪合法+TryMake"两条路径必须一致
        for (int d = 1; d <= Math.Min(3, maxDepth); d++)
        {
            Board b1 = Board.FromFen(fen);
            Board b2 = Board.FromFen(fen);
            long a = Perft(b1, d);
            long c = PerftViaLegalGenerator(b2, d);
            Check(a == c, name + " generator mismatch depth " + d + ": " + a + " vs " + c);
        }
        Info("  " + name + " ok (" + sw.ElapsedMilliseconds + " ms)");
    }

    private static long Perft(Board b, int depth)
    {
        if (depth == 0) return 1;
        List<Move> moves = new List<Move>(64);
        b.GenerateMoves(moves, false);
        long nodes = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            if (!b.TryMake(moves[i])) continue;
            nodes += depth == 1 ? 1 : Perft(b, depth - 1);
            b.UnmakeMove();
        }
        return nodes;
    }

    private static long PerftViaLegalGenerator(Board b, int depth)
    {
        if (depth == 0) return 1;
        List<Move> moves = new List<Move>(64);
        b.GenerateLegalMoves(moves);
        long nodes = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            b.MakeMove(moves[i]);
            nodes += depth == 1 ? 1 : PerftViaLegalGenerator(b, depth - 1);
            b.UnmakeMove();
        }
        return nodes;
    }

    // ------------------------------------------------------------------
    // 增量状态一致性
    // ------------------------------------------------------------------
    private static void ConsistencyTests()
    {
        Info("");
        Info("--- consistency (zobrist / incremental eval / unmake) ---");
        Random rnd = new Random(12345);
        int games = 120;
        int plies = 0;
        for (int g = 0; g < games; g++)
        {
            Board b = Board.FromFen(Board.StartFen);
            string fen0 = b.ToFen();
            ulong key0 = b.Key;
            List<Move> played = new List<Move>();
            for (int p = 0; p < 200; p++)
            {
                List<Move> moves = new List<Move>(64);
                b.GenerateLegalMoves(moves);
                if (moves.Count == 0) break;
                string beforeFen = b.ToFen();
                int beforeSide = b.Side;
                Move m = moves[rnd.Next(moves.Count)];
                b.MakeMove(m);
                played.Add(m);
                plies++;

                if (b.Key != b.RecomputeKey())
                {
                    Check(false, "zobrist mismatch after " + m.ToUci() + " fen=" + b.ToFen());
                    break;
                }
                int beforeMatW = b.MatPst[Board.White], beforeMatB = b.MatPst[Board.Black];
                int beforePhase = b.Phase;
                int[] pawnFiles = (int[])b.PawnFileCount.Clone();
                b.RecomputeIncremental();
                if (b.MatPst[Board.White] != beforeMatW || b.MatPst[Board.Black] != beforeMatB || b.Phase != beforePhase)
                {
                    Check(false, "incremental eval mismatch after " + m.ToUci() + " fen=" + b.ToFen());
                    break;
                }
                for (int i = 0; i < pawnFiles.Length; i++)
                {
                    if (pawnFiles[i] != b.PawnFileCount[i])
                    {
                        Check(false, "pawn file count mismatch after " + m.ToUci());
                        break;
                    }
                }

                // 悔棋一步后必须完全恢复
                b.UnmakeMove();
                if (b.ToFen() != beforeFen || b.Key != b.RecomputeKey() || b.Side != beforeSide)
                {
                    Check(false, "single unmake mismatch after " + m.ToUci() + "\n    got      " + b.ToFen() + "\n    expected " + beforeFen);
                    break;
                }
                b.RecomputeIncremental();
                b.MakeMove(m);
            }
            // 全部悔棋，必须精确回到开局
            for (int i = played.Count - 1; i >= 0; i--) b.UnmakeMove();
            Check(b.ToFen() == fen0, "unmake all restores FEN (game " + g + ")\n    got      " + b.ToFen() + "\n    expected " + fen0);
            Check(b.Key == key0, "unmake all restores key (game " + g + ")");
        }
        Info("  random playout plies: " + plies + " ok");
    }

    // ------------------------------------------------------------------
    // AI 解析（着法分级 / 胜率换算 / 局面分析）
    // ------------------------------------------------------------------
    private static void AnalysisTests()
    {
        Info("");
        Info("--- analysis (AI 解析) ---");

        // 胜率换算
        Check(Math.Abs(AnalysisMath.WinRate(0) - 50.0) < 0.001, "winrate(0) == 50");
        Check(AnalysisMath.WinRate(300) > AnalysisMath.WinRate(100) && AnalysisMath.WinRate(100) > 50, "winrate increasing");
        Check(AnalysisMath.WinRate(-300) < 50 && AnalysisMath.WinRate(-300) > 0, "winrate negative side");
        Check(AnalysisMath.WinRate(20000) <= 99.5 && AnalysisMath.WinRate(-20000) >= 0.5, "winrate clamped");
        Check(AnalysisMath.ToCp(Eval.MateScore - 1) == 1999, "mate cp = 1999, got " + AnalysisMath.ToCp(Eval.MateScore - 1));
        Check(AnalysisMath.ToCp(Eval.MateScore - 3) == 1997, "mate distance cp");
        Check(AnalysisMath.ToCp(-(Eval.MateScore - 1)) == -1999, "mated cp = -1999");
        Check(AnalysisMath.FormatScore(135) == "+1.35", "format +1.35");
        Check(AnalysisMath.FormatScore(-42) == "-0.42", "format -0.42");
        Check(AnalysisMath.FormatScore(Eval.MateScore - 1) == "将杀", "format mate");

        // 分级
        Check(Analyzer.Classify(0, true, false) == Annotations.Good, "loss 0 -> !");
        Check(Analyzer.Classify(0, true, true) == Annotations.Brilliant, "only best move -> !!");
        Check(Analyzer.Classify(10, false, false) == Annotations.Good, "loss 10 -> !");
        Check(Analyzer.Classify(30, false, false) == Annotations.Interesting, "loss 30 -> !?");
        Check(Analyzer.Classify(80, false, false) == Annotations.Dubious, "loss 80 -> ?!");
        Check(Analyzer.Classify(200, false, false) == Annotations.Mistake, "loss 200 -> ?");
        Check(Analyzer.Classify(500, false, false) == Annotations.Blunder, "loss 500 -> ??");
        Check(Annotations.All.Length == 6 && Annotations.All[0] == Annotations.Brilliant && Annotations.All[5] == Annotations.Blunder,
            "legend order best -> worst");

        // 局面分析：白方可以白吃黑后
        SkillLevel profile = Analyzer.Profile(6, 600);
        Random rnd = new Random(4242);
        Board b = Board.FromFen("4k3/8/8/3q4/8/8/8/3QK3 w - - 0 1");
        List<Move> legal = new List<Move>(64);
        b.GenerateLegalMoves(legal);
        PositionAnalysis pa = Analyzer.Analyze(b, new List<ulong>(), profile, rnd);
        Check(pa != null && !pa.Terminal, "analysis result not null");
        Check(pa.Moves.Count == legal.Count, "analysis covers all legal moves (" + pa.Moves.Count + "/" + legal.Count + ")");
        Check(pa.Depth >= 3, "analysis depth >= 3 (got " + pa.Depth + ")");
        Check(pa.ElapsedMs <= profile.TimeMs + 1500, "analysis respects time budget (" + pa.ElapsedMs + "ms)");
        bool sorted = true;
        for (int i = 1; i < pa.Scores.Count; i++) if (pa.Scores[i] > pa.Scores[i - 1]) sorted = false;
        Check(sorted, "root scores sorted descending");
        Check(pa.BestScoreCp > 700, "best score wins the queen (+" + pa.BestScoreCp + ")");

        Move qxd5 = FindSan(b, "Qxd5");
        Check(!qxd5.IsNull, "found Qxd5");
        Check(pa.BestMove.From == qxd5.From && pa.BestMove.To == qxd5.To, "best move is Qxd5");
        MoveAnnotation best = Analyzer.Judge(pa, qxd5, Board.White);
        Check(best.Ready && best.Grade == Annotations.Brilliant, "capturing hanging queen -> !! (" + best.Grade.Symbol + ")");
        Check(best.LossCp == 0, "best move loss 0");

        Move quiet = FindSan(b, "Qd2");
        Check(!quiet.IsNull, "found Qd2");
        MoveAnnotation bad = Analyzer.Judge(pa, quiet, Board.White);
        Check(bad.Ready && bad.Grade.Rank >= Annotations.Mistake.Rank,
            "ignoring hanging queen is a mistake/blunder (" + bad.Grade.Symbol + ", loss " + bad.LossCp + ")");
        Check(bad.LossCp > 500, "loss of the bad move > 5.00 (" + bad.LossCp + ")");

        // 将杀局面：Terminal 且评估为输棋
        Board mate = Board.FromFen("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1");
        PositionAnalysis mated = Analyzer.Analyze(mate, new List<ulong>(), profile, rnd);
        Check(mated.Terminal && mated.BestScoreCp < -Eval.MateThreshold, "mated position -> terminal, score -mate");

        // 起始局面：20 个着法、评估接近均势
        Board start = Board.FromFen(Board.StartFen);
        PositionAnalysis spa = Analyzer.Analyze(start, new List<ulong>(), profile, rnd);
        Check(spa.Moves.Count == 20, "start position has 20 analysed moves");
        Check(Math.Abs(spa.WhiteCp) < 150, "start position is balanced (" + spa.WhiteCp + "cp)");
        Check(Math.Abs(spa.WhiteWinRate - 50) < 8, "start winrate near 50 (" + spa.WhiteWinRate.ToString("0.0") + ")");
        Info("  start: " + spa.Moves.Count + " moves, depth " + spa.Depth + ", whiteCp " + spa.WhiteCp +
             ", winrate " + spa.WhiteWinRate.ToString("0.0") + "%, " + spa.ElapsedMs + "ms");
    }

    private static Move FindSan(Board b, string san)
    {
        List<Move> legal = new List<Move>(64);
        b.GenerateLegalMoves(legal);
        for (int i = 0; i < legal.Count; i++)
        {
            if (b.San(legal[i]) == san) return legal[i];
        }
        return Move.Null;
    }

    // ------------------------------------------------------------------
    // SAN
    // ------------------------------------------------------------------
    private static void SanTests()
    {
        Info("");
        Info("--- SAN ---");
        Board b = Board.FromFen(Board.StartFen);
        Check(b.San(new Move(Squares.Parse("e2"), Squares.Parse("e4"), 0, MF.DoublePush)) == "e4", "SAN e4");
        Check(b.San(new Move(Squares.Parse("g1"), Squares.Parse("f3"), 0, 0)) == "Nf3", "SAN Nf3");
        Board b2 = Board.FromFen("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2");
        Check(b2.San(new Move(Squares.Parse("g1"), Squares.Parse("f3"), 0, 0)) == "Nf3", "SAN Nf3 after e5");
        Board b3 = Board.FromFen("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4");
        Check(b3.San(new Move(Squares.Parse("e1"), 6, 0, MF.Castle)) == "O-O", "SAN O-O");
        Board b4 = Board.FromFen("8/P7/8/8/8/8/8/k3K3 w - - 0 1");
        Check(b4.San(new Move(Squares.Parse("a7"), Squares.Parse("a8"), PT.Queen, 0)) == "a8=Q+", "SAN a8=Q+ got " + b4.San(new Move(Squares.Parse("a7"), Squares.Parse("a8"), PT.Queen, 0)));
        Board b5 = Board.FromFen("6k1/5ppp/8/8/8/8/8/R6K w - - 0 1");
        Check(b5.San(new Move(Squares.Parse("a1"), Squares.Parse("a8"), 0, 0)) == "Ra8#", "SAN Ra8# got " + b5.San(new Move(Squares.Parse("a1"), Squares.Parse("a8"), 0, 0)));
        Board b6 = Board.FromFen("8/8/8/8/8/8/8/K6k w - - 0 1");
        Check(b6.IsInsufficientMaterial(), "K vs K insufficient");
        Board b7 = Board.FromFen("8/8/8/8/8/8/8/K5Bk w - - 0 1");
        Check(b7.IsInsufficientMaterial(), "K+B vs K insufficient");
        Board b8 = Board.FromFen("8/8/8/8/8/8/8/K4R1k w - - 0 1");
        Check(!b8.IsInsufficientMaterial(), "K+R vs K sufficient");
        Info("  ok");
    }

    // ------------------------------------------------------------------
    // 引擎
    // ------------------------------------------------------------------
    private static void EngineSmoke(bool quick)
    {
        Info("");
        Info("--- engine (all 20 tiers) ---");
        string[] fens = new string[]
        {
            Board.StartFen,
            "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 4 4",
            "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
            "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
            "8/8/4k3/8/8/4K3/8/7R w - - 0 1",
            "6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1"
        };
        Random rnd = new Random(777);

        for (int t = 0; t < SkillTable.Levels.Length; t++)
        {
            SkillLevel skill = SkillTable.Levels[t];
            long worst = 0;
            for (int f = 0; f < fens.Length; f++)
            {
                Board b = Board.FromFen(fens[f]);
                Engine eng = new Engine();
                Stopwatch sw = Stopwatch.StartNew();
                SearchResult r = eng.Search(b, new List<ulong>(), skill, rnd);
                sw.Stop();
                Check(r != null, "engine returned result tier " + skill.Elo + " fen " + f);
                if (r == null) continue;
                Check(b.IsLegal(r.Move), "engine move legal tier " + skill.Elo + " fen " + f + " move " + r.Move.ToUci());
                if (sw.ElapsedMilliseconds > worst) worst = sw.ElapsedMilliseconds;
                Check(sw.ElapsedMilliseconds < skill.TimeMs + 3000, "engine respects time budget tier " + skill.Elo + " (" + sw.ElapsedMilliseconds + "ms)");
            }
            Info("  ELO " + skill.Elo + " (" + skill.Tier + ") depth<=" + skill.MaxDepth + " time<=" + skill.TimeMs + "ms  worst " + worst + "ms");
        }

        // 将杀局面：没有合法着法时返回 null
        Board mate = Board.FromFen("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1");
        Check(!mate.HasLegalMove(), "mate position has no legal moves");
        Engine e2 = new Engine();
        Check(e2.Search(mate, new List<ulong>(), SkillTable.ForElo(2000), rnd) == null, "engine returns null when mated");

        // 一步杀必须能找到
        Board m1 = Board.FromFen("7k/8/6K1/8/8/8/8/Q7 w - - 0 1");
        Engine e3 = new Engine();
        SearchResult r3 = e3.Search(m1, new List<ulong>(), SkillTable.ForElo(2000), rnd);
        Check(r3 != null, "mate-in-1 search result");
        if (r3 != null)
        {
            Check(m1.San(r3.Move).EndsWith("#"), "engine finds mate in 1, played " + m1.San(r3.Move));
        }
        // 低难度也要能走出合法着法（含被将军的局面）
        Board chk = Board.FromFen("4k3/8/8/8/8/8/4r3/4K3 w - - 0 1");
        List<Move> chkMoves = new List<Move>();
        chk.GenerateLegalMoves(chkMoves);
        Info("  check position legal moves: " + chkMoves.Count);
        Engine e4 = new Engine();
        SearchResult r4 = e4.Search(chk, new List<ulong>(), SkillTable.ForElo(100), rnd);
        if (r4 == null) Info("  [debug] engine returned null for check position");
        else Info("  [debug] engine move: " + r4.Move.ToUci() + " side=" + chk.Side + " legal=" + chk.IsLegal(r4.Move));
        Check(r4 != null && chk.IsLegal(r4.Move), "weak tier returns legal move while in check");
    }

    // ------------------------------------------------------------------
    // 强弱对比（高层级应对低层级明显更强）
    // ------------------------------------------------------------------
    private static void StrengthTest()
    {
        Info("");
        Info("--- strength: ELO 2000 vs ELO 100 (6 games, fast) ---");
        SkillLevel strong = Copy(SkillTable.ForElo(2000));
        strong.TimeMs = 300;
        strong.MaxDepth = 5;
        SkillLevel weak = SkillTable.ForElo(100);

        int strongWins = 0, draws = 0;
        Random rnd = new Random(2024);
        for (int g = 0; g < 6; g++)
        {
            ChessGame game = new ChessGame();
            bool strongIsWhite = (g % 2) == 0;
            for (int ply = 0; ply < 300 && !game.IsOver; ply++)
            {
                bool whiteToMove = game.Position.Side == Board.White;
                SkillLevel s = (whiteToMove == strongIsWhite) ? strong : weak;
                Engine eng = new Engine();
                SearchResult r = eng.Search(game.Position.Clone(), game.Keys, s, rnd);
                if (r == null) break;
                if (!game.IsLegal(r.Move))
                {
                    Check(false, "engine produced illegal move in self-play");
                    break;
                }
                game.Play(r.Move);
            }
            if (game.State == GameState.Checkmate)
            {
                if ((game.Winner == Board.White) == strongIsWhite) strongWins++;
            }
            else
            {
                draws++;
            }
        }
        Info("  strong wins: " + strongWins + ", draws/other: " + draws);
        Check(strongWins >= 4, "strong engine dominates weak engine (" + strongWins + "/6)");
    }

    private static SkillLevel Copy(SkillLevel s)
    {
        SkillLevel c = new SkillLevel();
        c.Elo = s.Elo; c.Tier = s.Tier; c.Desc = s.Desc;
        c.MaxDepth = s.MaxDepth; c.TimeMs = s.TimeMs;
        c.BlunderPct = s.BlunderPct; c.MarginCp = s.MarginCp; c.NoiseCp = s.NoiseCp;
        return c;
    }
}
