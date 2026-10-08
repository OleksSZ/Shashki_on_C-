using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Checkers;

/// <summary>Главное окно. Весь интерфейс создаётся кодом, XAML не нужен.</summary>
public sealed class MainWindow : Window
{
    static readonly int[] Depths = { 0, 2, 4, 6 };

    readonly BoardView _view = new();
    readonly ComboBox _mode = new()
    {
        ItemsSource = new[] { "Вы (белые) vs Бот", "Вы (чёрные) vs Бот", "Два игрока", "Бот vs Бот" },
        SelectedIndex = 0,
        Width = 190
    };
    readonly ComboBox _level = new()
    {
        ItemsSource = new[] { "Лёгкий", "Средний", "Сложный", "Эксперт" },
        SelectedIndex = 1,
        Width = 130
    };
    readonly Button _newGame = new() { Content = "Новая игра" };
    readonly TextBlock _status = new()
    {
        Foreground = Brushes.White,
        FontWeight = FontWeight.Bold,
        FontSize = 15,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 0, 0)
    };

    public MainWindow()
    {
        Title = "Шашки";
        Width = 760;
        Height = 840;
        MinWidth = 520;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(38, 38, 42));

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8),
        };
        bar.Children.Add(_mode);
        bar.Children.Add(_level);
        bar.Children.Add(_newGame);
        bar.Children.Add(_status);

        // Border с фоном гарантированно получает события мыши — пробрасываем клики в доску
        var host = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(38, 38, 42)),
            Child = _view
        };
        host.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
                _view.Click(e.GetPosition(_view));
        };

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(host); // последний элемент занимает всё оставшееся место
        Content = dock;

        _view.StatusChanged += s => _status.Text = s;
        _newGame.Click += (_, _) => StartGame();
        _mode.SelectionChanged += (_, _) => StartGame();
        _level.SelectionChanged += (_, _) => _view.BotDepth = Depths[Math.Max(0, _level.SelectedIndex)];

        Opened += (_, _) => StartGame();
    }

    void StartGame()
    {
        int m = _mode.SelectedIndex;
        _view.WhiteIsBot = m == 1 || m == 3;
        _view.BlackIsBot = m == 0 || m == 3;
        _view.BotDepth = Depths[Math.Max(0, _level.SelectedIndex)];
        _view.NewGame();
    }
}