using System;
using System.Collections.Generic;

namespace Chess
{
    /// <summary>复盘里的一手棋：走了什么、好不好、更好的走法是什么。</summary>
    public sealed class ReviewStep
    {
        public int Ply;                  // 0 起：第几手（黑白合计）
        public string San = "";
        public Move Move;
        public bool WhiteMoved;
        public int LossCp;               // 相对最佳着法的损失（厘兵）
        public Annotation Grade;         // 等级（精彩/好棋/…/大漏着）
        public Move BestMove;
        public string BestSan = "";
        public int EvalCp;               // 走完这手后白方视角（厘兵）
        public int EvalBeforeCp;         // 走之前白方视角
        public bool IsKey;
        public bool IsQuiz;
        /// <summary>关键步的类型，决定讲解的写法。</summary>
        public string Kind = "";         // "blunder" / "mistake" / "brilliant" / "swing" / "quiet"
    }

    /// <summary>一盘棋的复盘数据（逐手评价 + 关键步挑选 + 关键步间跳转）。</summary>
    public sealed class Review
    {
        public readonly List<ReviewStep> Steps = new List<ReviewStep>();
        public readonly List<Move> Moves = new List<Move>();
        public GameMode Mode;
        public int HumanSide = Board.White;

        public int Count { get { return Steps.Count; } }

        public int KeyCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Steps.Count; i++) if (Steps[i].IsKey) n++;
                return n;
            }
        }

        public bool IsStudentMove(ReviewStep s)
        {
            if (Mode == GameMode.HumanVsHuman) return true;
            return s.WhiteMoved == (HumanSide == Board.White);
        }

        /// <summary>回放到第 ply 手之后的局面（ply=0 即开局）。</summary>
        public ChessGame GameAt(int ply)
        {
            ChessGame g = new ChessGame();
            for (int i = 0; i < ply && i < Moves.Count; i++) g.Play(Moves[i]);
            return g;
        }

        /// <summary>关键步列表（按顺序）。</summary>
        public List<int> KeyPlies()
        {
            List<int> list = new List<int>();
            for (int i = 0; i < Steps.Count; i++) if (Steps[i].IsKey) list.Add(i);
            return list;
        }

        public int PreviousKey(int beforePly)
        {
            List<int> keys = KeyPlies();
            int found = -1;
            for (int i = 0; i < keys.Count; i++) if (keys[i] < beforePly) found = keys[i];
            return found;
        }

        public int NextKey(int afterPly)
        {
            List<int> keys = KeyPlies();
            for (int i = 0; i < keys.Count; i++) if (keys[i] > afterPly) return keys[i];
            return -1;
        }

        public static Review Build(ChessGame game, GameMode mode, int humanSide, SkillLevel profile,
                                   Random rng, Action<int, int> progress)
        {
            Review r = new Review();
            r.Mode = mode;
            r.HumanSide = humanSide;
            if (game == null || game.MoveHistory.Count == 0) return r;

            SkillLevel p = profile ?? Analyzer.Profile(4, 45);
            Random rnd = rng ?? new Random(20260101);
            ChessGame replay = new ChessGame();
            int total = game.MoveHistory.Count;
            int evalBefore = 0;

            for (int i = 0; i < total; i++)
            {
                Move mv = game.MoveHistory[i];
                bool whiteToMove = replay.Position.Side == Board.White;
                PositionAnalysis pa = Analyzer.Analyze(replay.Position, replay.Keys, p, rnd);
                MoveAnnotation ma = Analyzer.Judge(pa, mv, replay.Position.Side);

                ReviewStep st = new ReviewStep();
                st.Ply = i;
                st.Move = mv;
                st.San = i < game.SanHistory.Count ? game.SanHistory[i] : replay.Position.San(mv);
                st.WhiteMoved = whiteToMove;
                st.LossCp = ma.LossCp;
                st.Grade = ma.Grade ?? Annotations.Good;
                st.BestMove = pa.BestMove;
                st.BestSan = pa.BestMove.IsNull ? "" : replay.Position.San(pa.BestMove);
                st.EvalCp = ma.ScoreCp;
                st.EvalBeforeCp = evalBefore;
                evalBefore = ma.ScoreCp;
                r.Steps.Add(st);
                r.Moves.Add(mv);

                replay.Play(mv);
                if (progress != null) progress(i + 1, total);
            }

            SelectKeySteps(r);
            return r;
        }

        /// <summary>
        /// 关键步的挑法（"不太严格、也不太奇异"）：
        ///   · 跳过开局前 6 手（定式阶段讲了也没营养），跳过"无选择"的局面
        ///   · 坏棋：亏 ≥ 0.6 兵即可入选（不要求一定是大漏着）
        ///   · 好棋：走了引擎首选、而且当时的选择并不唯一（说明确实找到了一手好棋）
        ///   · 转折：这一手之后胜负天平明显翻了面
        ///   · 关键步之间至少隔 4 手，最多 4 个，保证节奏不挤也不散
        /// </summary>
        private static void SelectKeySteps(Review r)
        {
            for (int i = 0; i < r.Steps.Count; i++)
            {
                ReviewStep s = r.Steps[i];
                if (!r.IsStudentMove(s)) continue;
                if (s.Ply < 6) continue;                                  // 开局定式
                bool hopeless = s.BestSan.Length == 0;                    // 这一步没有"更好"可言
                if (hopeless) continue;

                bool bestPlayed = s.Move.From == s.BestMove.From && s.Move.To == s.BestMove.To;
                bool swing = Math.Abs(s.EvalCp) >= 150 && Math.Abs(s.EvalBeforeCp) < 120 &&
                             Math.Sign(s.EvalCp) != Math.Sign(s.EvalBeforeCp) && s.EvalBeforeCp != 0;

                if (s.LossCp >= 60)
                {
                    s.Kind = s.LossCp >= 300 ? "blunder" : "mistake";
                    s.IsKey = true;
                }
                else if (bestPlayed && s.LossCp <= 15 && s.Grade == Annotations.Brilliant)
                {
                    s.Kind = "brilliant";
                    s.IsKey = true;
                }
                else if (swing)
                {
                    s.Kind = "swing";
                    s.IsKey = true;
                }
                else if (bestPlayed && s.LossCp <= 15 && s.Ply >= 10 && s.Ply <= 40 &&
                         (s.Ply % 7 == 0))
                {
                    s.Kind = "quiet";                                     // 偶尔夸一句稳的好棋
                    s.IsKey = true;
                }
            }

            // 按"分量"排序，挑最多 4 个，且彼此至少隔 4 手
            List<ReviewStep> keys = new List<ReviewStep>();
            for (int i = 0; i < r.Steps.Count; i++) if (r.Steps[i].IsKey) keys.Add(r.Steps[i]);
            keys.Sort(delegate(ReviewStep a, ReviewStep b) { return Weight(b).CompareTo(Weight(a)); });

            List<ReviewStep> chosen = new List<ReviewStep>();
            for (int i = 0; i < keys.Count && chosen.Count < 4; i++)
            {
                bool tooClose = false;
                for (int k = 0; k < chosen.Count; k++)
                {
                    if (Math.Abs(chosen[k].Ply - keys[i].Ply) < 4) { tooClose = true; break; }
                }
                if (tooClose) continue;
                chosen.Add(keys[i]);
            }

            // 一个都没有时，讲"最亏的一手"，保证复盘里有东西可讲
            if (chosen.Count == 0)
            {
                ReviewStep worst = null;
                for (int i = 0; i < r.Steps.Count; i++)
                {
                    ReviewStep s = r.Steps[i];
                    if (!r.IsStudentMove(s) || s.BestSan.Length == 0) continue;
                    if (worst == null || s.LossCp > worst.LossCp) worst = s;
                }
                if (worst != null)
                {
                    worst.Kind = worst.LossCp >= 300 ? "blunder" : "mistake";
                    chosen.Add(worst);
                }
            }

            for (int i = 0; i < r.Steps.Count; i++) r.Steps[i].IsKey = false;
            for (int i = 0; i < chosen.Count; i++)
            {
                chosen[i].IsKey = true;
                chosen[i].IsQuiz = chosen[i].Kind == "blunder" || chosen[i].LossCp >= 200;
            }
        }

        private static int Weight(ReviewStep s)
        {
            int w = s.LossCp;
            if (s.Kind == "brilliant") w += 800;
            if (s.Kind == "swing") w += 600;
            if (s.Kind == "blunder") w += 400;
            if (s.Kind == "quiet") w -= 200;
            return w;
        }
    }

    public enum LessonMode
    {
        Auto,      // 自动走棋
        KeyStop,   // 停在关键步，等「继续」
        Quiz,      // 臭棋：退回本步之前等用户自己下
        Correct,   // 刚答对：棋子已经走上去，停下来让用户看清，等「继续」
        Done       // 讲完了
    }

    /// <summary>复盘教学状态机。</summary>
    public sealed class ReviewLesson
    {
        public readonly Review Review;
        public int Ply;
        public LessonMode Mode = LessonMode.Auto;
        public int QuizTries;
        public string Feedback = "";
        public readonly List<string> Log = new List<string>();
        /// <summary>与 Log 一一对应：这条是在"第几手"说的（回退时据此逐行还原）。</summary>
        public readonly List<int> LogPly = new List<int>();

        /// <summary>答题时用户实际走的那一手（界面要把它画在棋盘上）。</summary>
        public Move QuizApplied = Move.Null;
        public string QuizAppliedSan = "";

        private readonly Random _rng = new Random(20260101);
        private int _evalTick;

        public ReviewLesson(Review review)
        {
            Review = review ?? new Review();
            Reset();
        }

        public void Reset()
        {
            Ply = 0;
            Mode = Review.Count > 0 ? LessonMode.Auto : LessonMode.Done;
            QuizTries = 0;
            Feedback = "";
            QuizApplied = Move.Null;
            QuizAppliedSan = "";
            _evalTick = 0;
            Log.Clear();
            Say(ReviewSpeech.Intro(Review));
        }

        public bool Waiting
        {
            get
            {
                return Mode == LessonMode.KeyStop || Mode == LessonMode.Quiz ||
                       Mode == LessonMode.Correct || Mode == LessonMode.Done;
            }
        }

        public ReviewStep CurrentStep
        {
            get
            {
                int i = Mode == LessonMode.KeyStop ? Ply - 1 : Ply;
                return i >= 0 && i < Review.Count ? Review.Steps[i] : null;
            }
        }

        public void Say(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            Log.Add(line);
            LogPly.Add(Ply);
        }

        public bool Advance()
        {
            if (Mode != LessonMode.Auto) return false;
            if (Ply >= Review.Count) { Finish(); return false; }

            ReviewStep s = Review.Steps[Ply];
            if (s.IsKey && s.IsQuiz)
            {
                Mode = LessonMode.Quiz;
                QuizTries = 0;
                Feedback = "";
                QuizApplied = Move.Null;
                QuizAppliedSan = "";
                Say(ReviewSpeech.QuizPrompt(Review, s));
                return false;
            }

            Ply++;
            Say(ReviewSpeech.Plain(Review, s));
            _evalTick++;
            if (_evalTick % 4 == 0) Say(ReviewSpeech.EvalLine(Review, s));

            if (s.IsKey)
            {
                Mode = LessonMode.KeyStop;
                Feedback = "";
                Say(ReviewSpeech.DeepComment(Review, s));
                return false;
            }
            return true;
        }

        /// <summary>关键步 / 答对之后点「继续」。</summary>
        public void Continue()
        {
            if (Mode == LessonMode.KeyStop)
            {
                Mode = LessonMode.Auto;
                Feedback = "";
                return;
            }
            if (Mode == LessonMode.Correct)
            {
                // 答对后：切回实战的下一手继续讲
                QuizApplied = Move.Null;
                QuizAppliedSan = "";
                Mode = LessonMode.Auto;
                Feedback = "";
                Say(ReviewSpeech.BackToGame(Review, Review.Steps[Ply - 1]));
                _evalTick = 0;
                return;
            }
        }

        /// <summary>答题：提交用户走的一步。</summary>
        public QuizVerdict Submit(Move move, out string message)
        {
            message = "";
            if (Mode != LessonMode.Quiz) return QuizVerdict.Ignored;
            ReviewStep s = Review.Steps[Ply];
            ChessGame pre = Review.GameAt(Ply);
            PositionAnalysis pa = Analyzer.Analyze(pre.Position, pre.Keys, Analyzer.Profile(4, 45), _rng);
            MoveAnnotation ma = Analyzer.Judge(pa, move, pre.Position.Side);
            QuizTries++;
            string played = pre.Position.San(move);

            bool exact = !s.BestMove.IsNull && move.From == s.BestMove.From && move.To == s.BestMove.To &&
                         move.Promotion == s.BestMove.Promotion;
            bool correct = exact || ma.LossCp <= 50;

            if (correct)
            {
                // 真的把这步棋走上去，然后停下让用户看清
                QuizApplied = move;
                QuizAppliedSan = played;
                Ply++;
                Mode = LessonMode.Correct;
                Feedback = "对了！";
                message = ReviewSpeech.QuizRight(Review, s, played);
                Say(message);
                return QuizVerdict.Correct;
            }

            message = ReviewSpeech.QuizWrong(Review, s, played, ma.LossCp, QuizTries);
            Feedback = "再想想";
            Say(message);
            if (QuizTries >= 2) { Say(ReviewSpeech.Hint(s)); Feedback = "提示已给出"; }
            return QuizVerdict.Wrong;
        }

        /// <summary>回到上一步：把对话框逐行还原到那一刻（不追加新句子）。</summary>
        public bool StepBack()
        {
            if (Ply <= 0) return false;
            QuizApplied = Move.Null;
            QuizAppliedSan = "";
            QuizTries = 0;
            Feedback = "";
            Ply--;
            Mode = LessonMode.Auto;      // 停在这里，等用户点「下一步 / 继续」
            _evalTick = Ply;
            // 逐行还原：只保留"到达第 Ply 手"为止说过的话
            int keep = 0;
            while (keep < LogPly.Count && LogPly[keep] <= Ply) keep++;
            if (keep < Log.Count) Log.RemoveRange(keep, Log.Count - keep);
            if (keep < LogPly.Count) LogPly.RemoveRange(keep, LogPly.Count - keep);
            return true;
        }

        public bool CanStepBack { get { return Ply > 0; } }

        /// <summary>跳到上/下一个关键步；返回是否真的跳了。</summary>
        public bool JumpToKey(int direction)
        {
            if (Review.KeyCount == 0) return false;
            int here = Mode == LessonMode.KeyStop ? Ply - 1 : Ply;
            int target = direction < 0 ? Review.PreviousKey(here) : Review.NextKey(here);
            if (target < 0) return false;

            ReviewStep s = Review.Steps[target];
            QuizApplied = Move.Null;
            QuizAppliedSan = "";
            QuizTries = 0;
            Feedback = "";
            if (direction > 0)
            {
                Ply = s.IsQuiz ? target : target + 1;
                Mode = s.IsQuiz ? LessonMode.Quiz : LessonMode.KeyStop;
                Say(ReviewSpeech.JumpForward(Review, s));
            }
            else
            {
                Ply = s.IsQuiz ? target : target + 1;
                Mode = s.IsQuiz ? LessonMode.Quiz : LessonMode.KeyStop;
                Say(ReviewSpeech.JumpBack(Review, s));
            }
            if (Mode == LessonMode.KeyStop) Say(ReviewSpeech.DeepComment(Review, s));
            else Say(ReviewSpeech.QuizPrompt(Review, s));
            return true;
        }

        public bool CanJump(int direction)
        {
            int here = Mode == LessonMode.KeyStop ? Ply - 1 : Ply;
            return direction < 0 ? Review.PreviousKey(here) >= 0 : Review.NextKey(here) >= 0;
        }

        /// <summary>当前棋盘上应该显示到第几手（答题时停在"走这一步之前"）。</summary>
        public int DisplayPly
        {
            get
            {
                if (Mode == LessonMode.Quiz) return Ply;
                return Math.Min(Ply, Review.Count);
            }
        }

        private void Finish()
        {
            Mode = LessonMode.Done;
            Feedback = "";
            Say(ReviewSpeech.Outro(Review));
        }

        public string ProgressText
        {
            get
            {
                int keySeen = 0, keyTotal = 0;
                for (int i = 0; i < Review.Steps.Count; i++)
                {
                    if (!Review.Steps[i].IsKey) continue;
                    keyTotal++;
                    if (i < Ply) keySeen++;
                }
                return "第 " + Math.Min(Ply, Review.Count) + " / " + Review.Count +
                       " 手 · 关键步 " + Math.Min(keySeen, keyTotal) + " / " + keyTotal;
            }
        }
    }

    public enum QuizVerdict { Correct, Wrong, Ignored }

    /// <summary>复盘解说：短句、具体、不重复套路；宁可不说话，也不为了说话而说话。</summary>
    public static class ReviewSpeech
    {
        private static string Side(bool white) { return white ? "白" : "黑"; }

        private static string PieceName(int piece)
        {
            switch (Board.TypeOf(piece))
            {
                case PT.Pawn: return "兵";
                case PT.Knight: return "马";
                case PT.Bishop: return "象";
                case PT.Rook: return "车";
                case PT.Queen: return "后";
                case PT.King: return "王";
            }
            return "子";
        }

        public static string Intro(Review r)
        {
            if (r.Count == 0) return "没有棋谱，复盘不了。";
            string who = r.Mode == GameMode.HumanVsHuman ? "你们" : "你";
            return r.Count + " 手，一起过一遍。关键的几处我会停下来，" + who + "走坏的地方会退回让你重下。";
        }

        /// <summary>自动播放：只给"这手干了什么"这么一句，没有就不说。</summary>
        public static string Plain(Review r, ReviewStep s)
        {
            ChessGame before = r.GameAt(s.Ply);
            Board b = before.Position;
            int captured = b.PieceAt(s.Move.To);
            bool isCapture = captured != 0 || (s.Move.Flags & MF.EnPassant) != 0;
            bool castle = (s.Move.Flags & MF.Castle) != 0;
            bool promo = s.Move.Promotion != 0;
            int type = Board.TypeOf(b.PieceAt(s.Move.From));
            ChessGame after = r.GameAt(s.Ply + 1);
            bool check = after.Position.InCheck(after.Position.Side);

            string note;
            if (castle) note = "易位，王先安顿好";
            else if (promo) note = "升变！小兵成后";
            else if (isCapture && check) note = "吃子还带将军";
            else if (isCapture) note = "吃" + PieceName(captured);
            else if (check) note = "将军";
            else if (type == PT.King) note = "王挪一格";
            else if (type == PT.Pawn)
            {
                int f = Squares.File(s.Move.To), rk = Squares.Rank(s.Move.To);
                note = ((f == 3 || f == 4) && (rk == 3 || rk == 4)) ? "抢中心" : "推兵";
            }
            else if (type == PT.Queen && s.Ply < 12) note = "后早出，当心被追";
            else if (type == PT.Knight || type == PT.Bishop) note = "出子";
            else note = "重子上场";

            // 太普通的推兵/出子就别啰嗦了
            if ((note == "推兵" || note == "出子") && s.Ply % 2 == 1) note = "";
            return note.Length == 0 ? s.San : s.San + "——" + note;
        }

        public static string EvalLine(Review r, ReviewStep s)
        {
            int cp = s.EvalCp;
            double rate = AnalysisMath.WinRate(cp);
            if (cp > Eval.MateThreshold) return "白方胜势。";
            if (cp < -Eval.MateThreshold) return "黑方胜势。";
            if (rate >= 70) return "白方舒服（" + Math.Round(rate) + "%）。";
            if (rate >= 58) return "白方稍好（" + Math.Round(rate) + "%）。";
            if (rate > 42) return "均势（" + Math.Round(rate) + "%）。";
            if (rate > 30) return "黑方稍好（" + Math.Round(rate) + "%）。";
            return "黑方占上风（" + Math.Round(rate) + "%）。";
        }

        /// <summary>关键步：一两句说到点上。</summary>
        public static string DeepComment(Review r, ReviewStep s)
        {
            double loss = s.LossCp / 100.0;
            if (s.Kind == "brilliant")
                return "这步 " + s.San + " 是这盘里最准的一手——别的走法都会松劲。拿住了。";
            if (s.Kind == "swing")
                return "局面就是从这儿翻的。" + Side(s.WhiteMoved) + "方这一手之后，主动换边了。";
            if (s.Kind == "quiet")
                return s.San + " 不起眼，但很干净：没送、没乱动。";
            if (s.Kind == "blunder" || s.LossCp >= 200)
                return s.San + " 亏了 " + loss.ToString("0.0") + " 个兵。" +
                       (s.BestSan.Length > 0 ? "换 " + s.BestSan + " 就还稳。" : "");
            return s.San + " 差了点，约 " + loss.ToString("0.0") + " 个兵；" +
                   (s.BestSan.Length > 0 ? s.BestSan + " 更稳。" : "本可以更稳。");
        }

        public static string QuizPrompt(Review r, ReviewStep s)
        {
            return "换你来：这个局面该走哪步？";
        }

        public static string QuizRight(Review r, ReviewStep s, string played)
        {
            return "对，就是 " + played + "。比实战的 " + s.San + " 稳得多。";
        }

        public static string QuizWrong(Review r, ReviewStep s, string played, int lossCp, int tries)
        {
            string head = tries <= 1 ? played + " 不行" : "还是不行";
            if (lossCp > 0) return head + "——要亏 " + (lossCp / 100.0).ToString("0.0") + " 个兵。再试。";
            return head + "——这样会让出主动。再试。";
        }

        public static string Hint(ReviewStep s)
        {
            if (string.IsNullOrEmpty(s.BestSan)) return "提示：先动没发挥作用的子。";
            return "提示：走 " + s.BestSan + "。";
        }

        public static string BackToGame(Review r, ReviewStep s)
        {
            return "实战走的是 " + s.San + "，接着看。";
        }

        public static string JumpForward(Review r, ReviewStep s)
        {
            return "→ 第 " + (s.Ply + 1) + " 手 " + s.San + "。";
        }

        public static string JumpBack(Review r, ReviewStep s)
        {
            return "← 第 " + (s.Ply + 1) + " 手 " + s.San + "。";
        }

        public static string Outro(Review r)
        {
            int bl = 0, mi = 0, br = 0;
            for (int i = 0; i < r.Steps.Count; i++)
            {
                ReviewStep s = r.Steps[i];
                if (!r.IsStudentMove(s)) continue;
                if (s.Kind == "brilliant") br++;
                else if (s.Kind == "blunder") bl++;
                else if (s.Kind == "mistake") mi++;
            }
            string tail;
            if (br > 0 && bl == 0 && mi == 0) tail = br + " 步很亮，几乎没送东西。";
            else if (bl > 0) tail = "踩了 " + bl + " 个坑，记住就值。" + (br > 0 ? "顺便说，" + br + " 步好棋也不错。" : "");
            else if (mi > 0) tail = "几处小亏，补上就上一档。";
            else tail = "下得很干净。";
            return "复盘结束。" + tail;
        }
    }
}
