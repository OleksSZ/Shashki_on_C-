namespace Checkers;

/// <summary>
/// Бот: negamax + альфа-бета отсечение. depth = 0 — случайный ход (лёгкий уровень).
/// </summary>
public static class Bot
{
    const int Inf = 1_000_000;

    public static Move? Choose(Board board, Side side, int depth)
    {
        var moves = Rules.GetMoves(board, side);
        if (moves.Count == 0) return null;
        if (moves.Count == 1) return moves[0];
        if (depth <= 0) return moves[Random.Shared.Next(moves.Count)];

        int best = -Inf;
        var bestMoves = new List<Move>();
        foreach (var m in moves)
        {
            var nb = board.Clone();
            nb.Apply(m);
            int score = -Negamax(nb, Rules.Opp(side), depth - 1, -Inf, Inf);
            if (score > best) { best = score; bestMoves.Clear(); bestMoves.Add(m); }
            else if (score == best) bestMoves.Add(m);
        }
        // среди равных ходов выбираем случайный, чтобы партии не повторялись
        return bestMoves[Random.Shared.Next(bestMoves.Count)];
    }

    static int Negamax(Board b, Side side, int depth, int alpha, int beta)
    {
        var moves = Rules.GetMoves(b, side);
        if (moves.Count == 0) return -50_000 - depth; // нет ходов — проигрыш (чем раньше, тем хуже)

        // на нулевой глубине доигрываем взятия, чтобы не оценивать позицию посреди размена
        if (depth <= 0 && !moves[0].IsCapture) return Eval(b, side);

        int best = -Inf;
        foreach (var m in moves)
        {
            var nb = b.Clone();
            nb.Apply(m);
            int score = -Negamax(nb, Rules.Opp(side), depth - 1, -beta, -alpha);
            if (score > best) best = score;
            if (best > alpha) alpha = best;
            if (alpha >= beta) break;
        }
        return best;
    }

    static int Eval(Board b, Side side)
    {
        int total = 0;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                var p = b.G[r, c];
                if (p == Cell.Empty) continue;

                bool white = p.SideOf() == Side.White;
                int v;
                if (p.IsKing()) v = 320;
                else
                {
                    v = 100;
                    int advance = white ? 7 - r : r;
                    v += advance * 6;                          // продвижение к дамкам
                    if (white ? r == 7 : r == 0) v += 8;       // задний ряд — защита
                }
                if (c >= 2 && c <= 5 && r >= 2 && r <= 5) v += 4; // центр

                total += p.SideOf() == side ? v : -v;
            }
        return total;
    }
}