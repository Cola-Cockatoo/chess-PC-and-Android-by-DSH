using System;
using System.Collections.Generic;

namespace Chess
{
    public enum GameState
    {
        Playing = 0,
        Checkmate,
        Stalemate,
        DrawFiftyMove,
        DrawRepetition,
        DrawInsufficientMaterial
    }

    /// <summary>一局棋的完整状态（局面 + 历史 + 结果判定）。</summary>
    public class ChessGame
    {
        public Board Position;
        public List<Move> MoveHistory = new List<Move>();
        public List<string> SanHistory = new List<string>();
        public List<ulong> Keys = new List<ulong>();
        public GameState State = GameState.Playing;
        public int Winner;              // 0 无 / Board.White / Board.Black

        public ChessGame()
        {
            NewGame(Board.StartFen);
        }

        public void NewGame()
        {
            NewGame(Board.StartFen);
        }

        public void NewGame(string fen)
        {
            Position = Board.FromFen(fen);
            MoveHistory.Clear();
            SanHistory.Clear();
            Keys.Clear();
            Keys.Add(Position.Key);
            State = GameState.Playing;
            Winner = 0;
            UpdateState();
        }

        public bool IsOver
        {
            get { return State != GameState.Playing; }
        }

        public int PlyCount
        {
            get { return MoveHistory.Count; }
        }

        public List<Move> LegalMoves()
        {
            List<Move> list = new List<Move>(64);
            Position.GenerateLegalMoves(list);
            return list;
        }

        public List<Move> LegalMovesFrom(int sq)
        {
            List<Move> result = new List<Move>(16);
            List<Move> all = LegalMoves();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].From == sq) result.Add(all[i]);
            }
            return result;
        }

        public bool IsLegal(Move m)
        {
            List<Move> all = LegalMoves();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].From == m.From && all[i].To == m.To && all[i].Promotion == m.Promotion)
                    return true;
            }
            return false;
        }

        /// <summary>走一步棋（不校验合法性，调用方应确保合法）。</summary>
        public bool Play(Move m)
        {
            if (IsOver) return false;
            string san = Position.San(m);          // 必须在走子前计算
            if (!Position.TryMake(m)) return false;
            MoveHistory.Add(m);
            SanHistory.Add(san);
            Keys.Add(Position.Key);
            UpdateState();
            return true;
        }

        /// <summary>走一步棋并记录指定的 SAN 谱。</summary>
        public bool Play(Move m, string san)
        {
            if (IsOver) return false;
            if (!Position.TryMake(m)) return false;
            MoveHistory.Add(m);
            SanHistory.Add(san);
            Keys.Add(Position.Key);
            UpdateState();
            return true;
        }

        public void UndoLast()
        {
            if (MoveHistory.Count == 0) return;
            Position.UnmakeMove();
            MoveHistory.RemoveAt(MoveHistory.Count - 1);
            if (SanHistory.Count > 0) SanHistory.RemoveAt(SanHistory.Count - 1);
            if (Keys.Count > 1) Keys.RemoveAt(Keys.Count - 1);
            State = GameState.Playing;
            Winner = 0;
            UpdateState();
        }

        public void UndoPlies(int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (MoveHistory.Count == 0) break;
                UndoLast();
            }
        }

        public void UpdateState()
        {
            State = GameState.Playing;
            Winner = 0;

            List<Move> legal = LegalMoves();
            if (legal.Count == 0)
            {
                if (Position.InCheck(Position.Side))
                {
                    State = GameState.Checkmate;
                    Winner = Board.Opp(Position.Side);
                }
                else
                {
                    State = GameState.Stalemate;
                }
                return;
            }

            if (Position.Halfmove >= 100)
            {
                State = GameState.DrawFiftyMove;
                return;
            }
            if (Board.RepetitionCount(Keys, Position.Key) >= 3)
            {
                State = GameState.DrawRepetition;
                return;
            }
            if (Position.IsInsufficientMaterial())
            {
                State = GameState.DrawInsufficientMaterial;
            }
        }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case GameState.Checkmate:
                        return (Winner == Board.White ? "白方" : "黑方") + "将杀获胜";
                    case GameState.Stalemate:
                        return "逼和 · 和棋";
                    case GameState.DrawFiftyMove:
                        return "50 回合规则 · 和棋";
                    case GameState.DrawRepetition:
                        return "三次重复局面 · 和棋";
                    case GameState.DrawInsufficientMaterial:
                        return "子力不足 · 和棋";
                }
                if (Position.InCheck(Position.Side)) return (Position.Side == Board.White ? "白方" : "黑方") + "被将军";
                return "";
            }
        }

        /// <summary>只描述结束原因，用于界面上的副标题。</summary>
        public string StateReason
        {
            get
            {
                switch (State)
                {
                    case GameState.Checkmate: return "将杀";
                    case GameState.Stalemate: return "逼和";
                    case GameState.DrawFiftyMove: return "50 回合规则";
                    case GameState.DrawRepetition: return "三次重复局面";
                    case GameState.DrawInsufficientMaterial: return "子力不足";
                }
                return "";
            }
        }
    }
}
