using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Checkers;

/// <summary>Доска рисуется кодом в Render(). Клики приходят из MainWindow через Click().</summary>
public sealed class BoardView : Control
{
    // --- состояние партии ---
    Board _board = Board.Initial();
    Side _turn = Side.White;
    List<Move> _legal = new();
    readonly List<Pos> _path = new();   // выбранная игроком часть хода
    Move? _last;
    bool _gameOver, _thinking;
    int _quietPlies, _gameId;
    string _status = "";

    // --- анимация ---
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    readonly Stopwatch _sw = new();
    Move? _anim;
    int _seg;
    Cell _animCell;
    readonly HashSet<Pos> _gone = new();
    const double SegMs = 260;

    // --- раскладка ---
    double _cs, _ox, _oy;

    public bool WhiteIsBot { get; set; }
    public bool BlackIsBot { get; set; }
    public int BotDepth { get; set; } = 4;
    public bool Flipped { get; private set; }
    public event Action<string>? StatusChanged;

    public BoardView()
    {
        _timer.Tick += (_, _) => Tick();
    }

    bool IsBot(Side s) => s == Side.White ? WhiteIsBot : BlackIsBot;

    // ================= управление партией =================

    public void NewGame()
    {
        _gameId++;                       // отменяет результат думающего бота от старой партии
        _timer.Stop();
        _anim = null;
        _gone.Clear();
        _thinking = false;
        _board = Board.Initial();
        _turn = Side.White;
        _path.Clear();
        _last = null;
        _gameOver = false;
        _quietPlies = 0;
        Flipped = WhiteIsBot && !BlackIsBot; // если играем чёрными — поворачиваем доску
        BeginTurn();
    }

    void Report(string text)
    {
        _status = text;
        StatusChanged?.Invoke(text);
    }

    void BeginTurn()
    {
        _legal = Rules.GetMoves(_board, _turn);
        _path.Clear();

        if (_legal.Count == 0)
        {
            _gameOver = true;
            Report(_turn == Side.White ? "Победили чёрные!" : "Победили белые!");
            InvalidateVisual();
            return;
        }
        if (_quietPlies >= 30) // 15 ходов подряд только дамками без взятий
        {
            _gameOver = true;
            Report("Ничья");
            InvalidateVisual();
            return;
        }

        string who = _turn == Side.White ? "белые" : "чёрные";
        if (IsBot(_turn)) Report($"Ходят {who}: бот думает…");
        else Report($"Ходят {who}" + (_legal[0].IsCapture ? " — обязательное взятие!" : ""));

        InvalidateVisual();
        if (IsBot(_turn)) _ = RunBotAsync();
    }

    async Task RunBotAsync()
    {
        int id = _gameId;
        var snapshot = _board.Clone();
        var side = _turn;
        int depth = BotDepth;

        _thinking = true;
        var work = Task.Run(() => Bot.Choose(snapshot, side, depth)); // считаем в фоне — окно не зависает
        await Task.Delay(300);                                        // небольшая пауза, чтобы ход не был мгновенным
        var move = await work;

        if (id != _gameId) return;
        _thinking = false;
        if (move != null) StartMove(move);
    }

    // ================= анимация =================

    void StartMove(Move m)
    {
        _path.Clear();
        _anim = m;
        _seg = 0;
        _animCell = _board[m.From];
        _gone.Clear();
        _sw.Restart();
        _timer.Start();
        InvalidateVisual();
    }

    void Tick()
    {
        if (_anim == null) { _timer.Stop(); return; }

        if (_sw.Elapsed.TotalMilliseconds >= SegMs)
        {
            if (_anim.IsCapture) _gone.Add(_anim.Captured[_seg]); // побитая фигура исчезает по прилёту
            _seg++;
            _sw.Restart();

            var reached = _anim.Path[_seg];
            if (_animCell == Cell.White && reached.R == 0) _animCell = Cell.WhiteKing;
            if (_animCell == Cell.Black && reached.R == 7) _animCell = Cell.BlackKing;

            if (_seg >= _anim.Path.Count - 1) { FinishMove(); return; }
        }
        InvalidateVisual();
    }

    void FinishMove()
    {
        var m = _anim!;
        _timer.Stop();

        var moved = _board[m.From];
        _quietPlies = (m.IsCapture || !moved.IsKing()) ? 0 : _quietPlies + 1;

        _board.Apply(m);
        _last = m;
        _anim = null;
        _gone.Clear();
        _turn = Rules.Opp(_turn);
        BeginTurn();
    }

    // ================= ввод =================

    bool Starts(Move m)
    {
        for (int i = 0; i < _path.Count; i++)
            if (m.Path[i] != _path[i]) return false;
        return true;
    }

    IEnumerable<Move> Candidates() => _legal.Where(m => m.Path.Count > _path.Count && Starts(m));

    bool HumanCanAct => !_gameOver && _anim == null && !_thinking && !IsBot(_turn);

    public void Click(Point pt)
    {
        if (!HumanCanAct) return;

        var p = HitTest(pt);
        if (p == null) return;
        var pos = p.Value;

        if (_path.Count > 0)
        {
            bool isNext = Candidates().Any(m => m.Path[_path.Count] == pos);
            if (isNext)
            {
                _path.Add(pos);
                var done = _legal.FirstOrDefault(m => m.Path.Count == _path.Count && Starts(m));
                if (done != null) StartMove(done);
                else InvalidateVisual();
                return;
            }
        }

        // иначе — (пере)выбор фигуры
        _path.Clear();
        if (_legal.Any(m => m.From == pos)) _path.Add(pos);
        InvalidateVisual();
    }

    Pos? HitTest(Point pt)
    {
        Recalc();
        int sc = (int)Math.Floor((pt.X - _ox) / _cs);
        int sr = (int)Math.Floor((pt.Y - _oy) / _cs);
        if (sc < 0 || sc > 7 || sr < 0 || sr > 7) return null;
        return new Pos(Flipped ? 7 - sr : sr, Flipped ? 7 - sc : sc);
    }

    // ================= рисование =================

    void Recalc()
    {
        double side = Math.Min(Bounds.Width, Bounds.Height);
        _cs = side / 8.7;
        _ox = (Bounds.Width - 8 * _cs) / 2;
        _oy = (Bounds.Height - 8 * _cs) / 2;
    }

    Point TopLeft(int r, int c)
    {
        int sr = Flipped ? 7 - r : r, sc = Flipped ? 7 - c : c;
        return new Point(_ox + sc * _cs, _oy + sr * _cs);
    }

    Point Center(Pos p)
    {
        var t = TopLeft(p.R, p.C);
        return new Point(t.X + _cs / 2, t.Y + _cs / 2);
    }

    static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));

    public override void Render(DrawingContext g)
    {
        base.Render(g);
        g.FillRectangle(Brush(255, 38, 38, 42), new Rect(Bounds.Size));
        Recalc();
        if (_cs < 10) return;

        var light = Brush(255, 240, 217, 181);
        var dark = Brush(255, 166, 120, 84);

        // клетки
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                var t = TopLeft(r, c);
                g.FillRectangle((r + c) % 2 == 1 ? dark : light, new Rect(t.X, t.Y, _cs, _cs));
            }

        // подсветка последнего хода
        if (_last != null)
        {
            var hb = Brush(90, 255, 230, 80);
            foreach (var p in _last.Path)
            {
                var t = TopLeft(p.R, p.C);
                g.FillRectangle(hb, new Rect(t.X, t.Y, _cs, _cs));
            }
        }

        bool humanTurn = HumanCanAct;

        // выбранная фигура / пройденная часть хода
        if (humanTurn && _path.Count > 0)
        {
            var sb = Brush(120, 80, 200, 90);
            foreach (var p in _path)
            {
                var t = TopLeft(p.R, p.C);
                g.FillRectangle(sb, new Rect(t.X, t.Y, _cs, _cs));
            }
        }

        DrawLabels(g);

        // кольца у фигур, которыми можно ходить
        if (humanTurn && _path.Count == 0)
        {
            var pen = new Pen(Brush(200, 90, 220, 110), Math.Max(2, _cs * 0.04));
            double rr = _cs * 0.43;
            foreach (var from in _legal.Select(m => m.From).Distinct())
                g.DrawEllipse(null, pen, Center(from), rr, rr);
        }

        // фигуры
        double radius = _cs * 0.37;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                var cell = _board.G[r, c];
                if (cell == Cell.Empty) continue;
                var pos = new Pos(r, c);
                if (_anim != null && pos == _anim.From) continue; // эта фигура сейчас летит
                if (_gone.Contains(pos)) continue;
                var ctr = Center(pos);
                DrawPiece(g, cell, ctr.X, ctr.Y, radius, 0);
            }

        // летящая фигура
        if (_anim != null && _seg < _anim.Path.Count - 1)
        {
            var a = Center(_anim.Path[_seg]);
            var b = Center(_anim.Path[_seg + 1]);
            double t = Math.Min(1.0, _sw.Elapsed.TotalMilliseconds / SegMs);
            double k = t * t * (3 - 2 * t); // плавное ускорение/замедление
            double x = a.X + (b.X - a.X) * k;
            double y = a.Y + (b.Y - a.Y) * k;
            double lift = Math.Sin(Math.PI * t) * _cs * 0.14;
            DrawPiece(g, _animCell, x, y, radius, lift);
        }

        // точки возможных ходов
        if (humanTurn && _path.Count > 0)
        {
            var db = Brush(190, 60, 190, 80);
            double rr = _cs * 0.14;
            foreach (var p in Candidates().Select(m => m.Path[_path.Count]).Distinct())
                g.DrawEllipse(db, null, Center(p), rr, rr);
        }

        // конец игры
        if (_gameOver)
        {
            g.FillRectangle(Brush(150, 0, 0, 0), new Rect(_ox, _oy + _cs * 3, _cs * 8, _cs * 2));
            DrawCentered(g, _status, _cs * 0.42, Brushes.White, _ox + _cs * 4, _oy + _cs * 4, FontWeight.Bold);
        }
    }

    void DrawLabels(DrawingContext g)
    {
        var br = Brush(255, 170, 170, 175);
        double size = Math.Max(10, _cs * 0.2);
        for (int i = 0; i < 8; i++)
        {
            int col = Flipped ? 7 - i : i;
            int row = Flipped ? 7 - i : i;
            DrawCentered(g, ((char)('a' + col)).ToString(), size, br,
                _ox + (i + 0.5) * _cs, _oy + 8 * _cs + _cs * 0.17);
            DrawCentered(g, (8 - row).ToString(), size, br,
                _ox - _cs * 0.17, _oy + (i + 0.5) * _cs);
        }
    }

    static void DrawCentered(DrawingContext g, string text, double size, IBrush brush,
                             double cx, double cy, FontWeight weight = FontWeight.Normal)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
        g.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }

    static void DrawPiece(DrawingContext g, Cell cell, double cx, double cy, double r, double lift)
    {
        bool white = cell.SideOf() == Side.White;
        var body = white ? Brush(255, 238, 232, 218) : Brush(255, 48, 48, 54);
        var edge = white ? Color.FromRgb(165, 150, 125) : Color.FromRgb(10, 10, 12);
        var shine = white ? Brush(170, 255, 255, 255) : Brush(110, 190, 190, 205);

        // тень на доске
        g.DrawEllipse(Brush((byte)(lift > 0 ? 55 : 80), 0, 0, 0), null,
            new Point(cx + 3, cy + r * 0.2 + 3), r, r);

        double y = cy - lift;
        var edgePen = new Pen(new SolidColorBrush(edge), Math.Max(1.5, r * 0.07));
        g.DrawEllipse(body, edgePen, new Point(cx, y), r, r);

        // внутреннее кольцо и блик
        var innerPen = new Pen(new SolidColorBrush(Color.FromArgb(120, edge.R, edge.G, edge.B)), Math.Max(1, r * 0.05));
        g.DrawEllipse(null, innerPen, new Point(cx, y), r * 0.68, r * 0.68);
        g.DrawEllipse(shine, null, new Point(cx - r * 0.3, y - r * 0.32), r * 0.3, r * 0.2);

        if (cell.IsKing())
        {
            var gold = white ? Brush(255, 200, 140, 0) : Brush(255, 255, 205, 60);
            DrawCentered(g, "♛", r * 1.15, gold, cx, y);
        }
    }
}