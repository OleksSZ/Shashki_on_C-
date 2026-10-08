namespace Checkers;

public enum Side { White, Black }

public enum Cell : byte { Empty, White, WhiteKing, Black, BlackKing }

public static class CellExt
{
    public static Side SideOf(this Cell c) => (c == Cell.White || c == Cell.WhiteKing) ? Side.White : Side.Black;
    public static bool IsKing(this Cell c) => c == Cell.WhiteKing || c == Cell.BlackKing;
    public static bool Belongs(this Cell c, Side s) => c != Cell.Empty && c.SideOf() == s;
}

public readonly record struct Pos(int R, int C);

/// <summary>Ход: путь фигуры (включая начальную клетку) и список побитых фигур.</summary>
public sealed class Move
{
    public List<Pos> Path { get; } = new();
    public List<Pos> Captured { get; } = new();

    public Pos From => Path[0];
    public Pos To => Path[^1];
    public bool IsCapture => Captured.Count > 0;

    public Move Clone()
    {
        var m = new Move();
        m.Path.AddRange(Path);
        m.Captured.AddRange(Captured);
        return m;
    }
}

public sealed class Board
{
    // Ряд 0 — сверху (чёрные), ряд 7 — снизу (белые). Тёмные клетки: (r + c) % 2 == 1.
    public Cell[,] G { get; } = new Cell[8, 8];

    public Cell this[Pos p] => G[p.R, p.C];

    public static Board Initial()
    {
        var b = new Board();
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                if ((r + c) % 2 == 0) continue;
                if (r < 3) b.G[r, c] = Cell.Black;
                else if (r > 4) b.G[r, c] = Cell.White;
            }
        return b;
    }

    public Board Clone()
    {
        var nb = new Board();
        Array.Copy(G, nb.G, G.Length);
        return nb;
    }

    public void Apply(Move m)
    {
        var piece = G[m.From.R, m.From.C];
        G[m.From.R, m.From.C] = Cell.Empty;
        foreach (var p in m.Captured) G[p.R, p.C] = Cell.Empty;

        // Если шашка хоть раз побывала на последней горизонтали — она дамка
        if (piece == Cell.White && m.Path.Any(p => p.R == 0)) piece = Cell.WhiteKing;
        if (piece == Cell.Black && m.Path.Any(p => p.R == 7)) piece = Cell.BlackKing;

        G[m.To.R, m.To.C] = piece;
    }
}

/// <summary>
/// Правила русских шашек: обязательное взятие, взятие назад, "летающие" дамки,
/// превращение в дамку по ходу боя, нельзя бить одну фигуру дважды.
/// </summary>
public static class Rules
{
    static readonly (int dr, int dc)[] Dirs = { (-1, -1), (-1, 1), (1, -1), (1, 1) };

    public static Side Opp(Side s) => s == Side.White ? Side.Black : Side.White;

    static bool In(int r, int c) => (uint)r < 8 && (uint)c < 8;

    public static List<Move> GetMoves(Board b, Side side)
    {
        var captures = new List<Move>();

        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                var cell = b.G[r, c];
                if (!cell.Belongs(side)) continue;

                var start = new Pos(r, c);
                b.G[r, c] = Cell.Empty; // чтобы фигура не мешала сама себе (дамка может вернуться через своё поле)
                var m = new Move();
                m.Path.Add(start);
                Capture(b, cell.IsKing(), side, start, m, captures);
                b.G[r, c] = cell;
            }

        if (captures.Count > 0) return captures; // взятие обязательно

        var result = new List<Move>();
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                var cell = b.G[r, c];
                if (!cell.Belongs(side)) continue;

                if (cell.IsKing())
                {
                    foreach (var (dr, dc) in Dirs)
                    {
                        int nr = r + dr, nc = c + dc;
                        while (In(nr, nc) && b.G[nr, nc] == Cell.Empty)
                        {
                            result.Add(Simple(r, c, nr, nc));
                            nr += dr; nc += dc;
                        }
                    }
                }
                else
                {
                    int dr = side == Side.White ? -1 : 1;
                    foreach (int dc in new[] { -1, 1 })
                    {
                        int nr = r + dr, nc = c + dc;
                        if (In(nr, nc) && b.G[nr, nc] == Cell.Empty)
                            result.Add(Simple(r, c, nr, nc));
                    }
                }
            }
        return result;
    }

    static Move Simple(int r1, int c1, int r2, int c2)
    {
        var m = new Move();
        m.Path.Add(new Pos(r1, c1));
        m.Path.Add(new Pos(r2, c2));
        return m;
    }

    // Рекурсивный поиск серий взятий. Возвращает true, если нашлось хотя бы одно взятие.
    static bool Capture(Board b, bool king, Side side, Pos cur, Move m, List<Move> result)
    {
        bool found = false;

        foreach (var (dr, dc) in Dirs)
        {
            int r = cur.R + dr, c = cur.C + dc;
            if (king)
                while (In(r, c) && b.G[r, c] == Cell.Empty) { r += dr; c += dc; }
            if (!In(r, c)) continue;

            var victim = b.G[r, c];
            if (victim == Cell.Empty || victim.SideOf() == side) continue;

            var vp = new Pos(r, c);
            if (m.Captured.Contains(vp)) continue; // побитые фигуры снимаются после хода и мешают проходу

            var terminal = new List<Move>();
            var extended = new List<Move>();

            int lr = r + dr, lc = c + dc;
            while (In(lr, lc) && b.G[lr, lc] == Cell.Empty)
            {
                found = true;
                var land = new Pos(lr, lc);
                var nm = m.Clone();
                nm.Path.Add(land);
                nm.Captured.Add(vp);

                bool nowKing = king || (side == Side.White ? lr == 0 : lr == 7);
                var sub = new List<Move>();
                if (Capture(b, nowKing, side, land, nm, sub)) extended.AddRange(sub);
                else terminal.Add(nm);

                if (!king) break;
                lr += dr; lc += dc;
            }

            // Если с какого-то поля приземления можно бить дальше — обязаны выбрать такое поле
            result.AddRange(extended.Count > 0 ? extended : terminal);
        }

        if (!found && m.Captured.Count > 0) result.Add(m);
        return found;
    }
}