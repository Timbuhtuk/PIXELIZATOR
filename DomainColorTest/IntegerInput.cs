using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DomainColorTest;

/// <summary>An integer editor with bounded values and keyboard/spinner support.</summary>
public sealed class IntegerInput : UserControl
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(IntegerInput), new PropertyMetadata(1, BoundsChanged));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(IntegerInput), new PropertyMetadata(4096, BoundsChanged));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(int), typeof(IntegerInput), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Changed, Coerce));
    private readonly TextBox _editor;
    private bool _updating;
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public event EventHandler? ValueChanged;

    public IntegerInput()
    {
        Focusable = false;
        MinHeight = 30;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        _editor = new TextBox { TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 2, 5, 2) };
        _editor.Text = Value.ToString(CultureInfo.InvariantCulture);
        _editor.TextChanged += (_, _) =>
        {
            if (_updating) return;
            if (int.TryParse(_editor.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= Minimum && value <= Maximum)
            {
                _updating = true;
                SetCurrentValue(ValueProperty, value);
                _updating = false;
            }
        };
        _editor.LostKeyboardFocus += (_, _) => Commit();
        _editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down)
            {
                Commit();
                SetCurrentValue(ValueProperty, Math.Clamp(Value + (e.Key == Key.Up ? 1 : -1), Minimum, Maximum));
                e.Handled = true;
            }
            else if (e.Key == Key.Enter) Commit();
        };
        grid.Children.Add(_editor);
        var buttons = new Grid();
        buttons.RowDefinitions.Add(new RowDefinition());
        buttons.RowDefinitions.Add(new RowDefinition());
        foreach (int direction in new[] { 1, -1 })
        {
            var button = new System.Windows.Controls.Primitives.RepeatButton { Content = direction > 0 ? "▴" : "▾", Padding = new Thickness(0), Focusable = false, FontSize = 10 };
            button.Click += (_, _) => { Commit(); SetCurrentValue(ValueProperty, Math.Clamp(Value + direction, Minimum, Maximum)); };
            Grid.SetRow(button, direction > 0 ? 0 : 1);
            buttons.Children.Add(button);
        }
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        Content = grid;
    }

    public void Commit()
    {
        if (int.TryParse(_editor.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
        UpdateText();
    }
    private void UpdateText()
    {
        if (_updating) return;
        _updating = true;
        _editor.Text = Value.ToString(CultureInfo.InvariantCulture);
        _updating = false;
    }
    private static object Coerce(DependencyObject d, object value)
    {
        var input = (IntegerInput)d;
        return Math.Clamp((int)value, input.Minimum, Math.Max(input.Minimum, input.Maximum));
    }
    private static void BoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => d.CoerceValue(ValueProperty);
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var input = (IntegerInput)d;
        input.UpdateText();
        input.ValueChanged?.Invoke(input, EventArgs.Empty);
    }
}
