using System;
using System.Collections.Generic;

namespace Chess
{
    /// <summary>定式学习里"当前可以走的一步"（一个候选节点 + 解析出来的合法着法）。</summary>
    public sealed class StudyStep
    {
        public int NodeIndex;
        public Move Move;
        public string San = "";
        public string Comment = "";
        public string VariationName = "";
        /// <summary>这条变化的一句话说明（有命名变化时才有）。</summary>
        public string Note = "";
        /// <summary>这一步是不是学员走的（是的话界面上要给箭头）。</summary>
        public bool IsStudentMove;
        /// <summary>走完这一步后还有没有后续（false 表示这条变化到此结束）。</summary>
        public bool HasNext;
        /// <summary>这一步分岔出的变化条数（> 1 表示学员要在这里选一个）。</summary>
        public int BranchCount;
    }

    /// <summary>定式（开局）教学的推进器：维护当前局面、下一步候选着法、点评与分支。</summary>
    public sealed class StudySession
    {
        private readonly Opening _op;
        private readonly ChessGame _game = new ChessGame();
        private readonly List<int> _path = new List<int>();
        private int[] _next;
        private int _lastNode = -1;

        public StudySession(Opening opening)
        {
            if (opening == null) throw new ArgumentNullException("opening");
            _op = opening;
            Restart();
        }

        public Opening Opening { get { return _op; } }

        public ChessGame Game { get { return _game; } }

        /// <summary>从开局算起走过的节点下标（用于显示着法列表）。</summary>
        public IList<int> Path { get { return _path.AsReadOnly(); } }

        /// <summary>最后走的那一步的节点下标（-1 表示还没走）。</summary>
        public int LastNode { get { return _lastNode; } }

        /// <summary>这条变化走完了。</summary>
        public bool Finished { get { return _next == null || _next.Length == 0; } }

        /// <summary>学员执哪一方。</summary>
        public int StudentSide { get { return _op.Side == "w" ? Board.White : Board.Black; } }

        public bool IsStudentSide(int side) { return side == StudentSide; }

        /// <summary>现在轮到学员走（界面上要出现箭头）。</summary>
        public bool IsStudentTurn { get { return IsStudentSide(_game.Position.Side); } }

        public void Restart()
        {
            _game.NewGame();
            _path.Clear();
            _next = _op.Root;
            _lastNode = -1;
        }

        /// <summary>当前局面下所有可走的候选着法（学员回合可能有多个 → 多个箭头）。</summary>
        public List<StudyStep> Candidates()
        {
            List<StudyStep> list = new List<StudyStep>();
            if (_next == null) return list;
            List<Move> legal = _game.LegalMoves();
            for (int i = 0; i < _next.Length; i++)
            {
                StudyStep step = Resolve(_next[i], legal);
                if (step != null) list.Add(step);
            }
            return list;
        }

        /// <summary>按"从哪到哪"找候选着法（棋盘点击用）。</summary>
        public StudyStep FindCandidate(int from, int to)
        {
            List<StudyStep> list = Candidates();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Move.From == from && list[i].Move.To == to) return list[i];
            }
            return null;
        }

        /// <summary>走候选着法里的某一步（按节点下标）。</summary>
        public StudyStep Play(int nodeIndex)
        {
            List<StudyStep> list = Candidates();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].NodeIndex == nodeIndex) return Play(list[i]);
            }
            return null;
        }

        /// <summary>走候选着法里的某一步（按 from/to）。</summary>
        public StudyStep PlayMove(int from, int to)
        {
            StudyStep step = FindCandidate(from, to);
            return step == null ? null : Play(step);
        }

        private StudyStep Play(StudyStep step)
        {
            if (!_game.Play(step.Move)) return null;
            _lastNode = step.NodeIndex;
            _path.Add(step.NodeIndex);
            _next = _op.Nodes[step.NodeIndex].Children;
            return step;
        }

        /// <summary>程序（对手）走一步：多个候选时走第一个（主线）。返回值可能为 null（已结束）。</summary>
        public StudyStep PlayOpponent()
        {
            List<StudyStep> list = Candidates();
            if (list.Count == 0) return null;
            return Play(list[0]);
        }

        /// <summary>取某个节点的信息（点评 / 变化名 / 是否还有后续）。</summary>
        public StudyStep Node(int nodeIndex)
        {
            return Resolve(nodeIndex, null);
        }

        private StudyStep Resolve(int nodeIndex, List<Move> legal)
        {
            if (nodeIndex < 0 || nodeIndex >= _op.Nodes.Length) return null;
            OpeningMove node = _op.Nodes[nodeIndex];
            Move move = Openings.MoveFromSan(_game.Position, legal, node.San);
            if (move.IsNull) return null;
            StudyStep step = new StudyStep();
            step.NodeIndex = nodeIndex;
            step.Move = move;
            step.San = node.San;
            step.Comment = node.Comment;
            step.VariationName = node.VariationName;
            step.Note = node.Note;
            step.IsStudentMove = IsStudentTurn;
            step.HasNext = node.Children != null && node.Children.Length > 0;
            step.BranchCount = _next != null ? _next.Length : 0;
            return step;
        }
    }

    /// <summary>定式学习的入口：数据 + 工具方法。</summary>
    public static class Openings
    {
        public static Opening[] All()
        {
            return OpeningsData.All();
        }

        public static int Count { get { return OpeningsData.All().Length; } }

        public static Opening At(int index)
        {
            Opening[] all = OpeningsData.All();
            if (index < 0 || index >= all.Length) return null;
            return all[index];
        }

        public static Opening ById(string id)
        {
            Opening[] all = OpeningsData.All();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Id == id) return all[i];
            }
            return null;
        }

        /// <summary>把记谱（SAN）解析成该局面下的合法着法；找不到返回 Move.Null。
        /// legal 为 null 时自己生成。</summary>
        public static Move MoveFromSan(Board position, List<Move> legal, string san)
        {
            if (position == null || string.IsNullOrEmpty(san)) return Move.Null;
            if (legal == null)
            {
                legal = new List<Move>();
                position.GenerateLegalMoves(legal);
            }
            for (int i = 0; i < legal.Count; i++)
            {
                if (position.San(legal[i]) == san) return legal[i];
            }
            // 容错：接受"0-0"这种写法差异
            string alt = san.Replace('0', 'O');
            for (int i = 0; i < legal.Count; i++)
            {
                if (position.San(legal[i]).Replace('0', 'O') == alt) return legal[i];
            }
            return Move.Null;
        }
    }
}
