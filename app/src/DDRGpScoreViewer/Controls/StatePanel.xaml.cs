using System.Windows;
using System.Windows.Controls;

namespace DDRGpScoreViewer.Controls;

public partial class StatePanel : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(StatePanel), new PropertyMetadata(string.Empty));

    public StatePanel() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
