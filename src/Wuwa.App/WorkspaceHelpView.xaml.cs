using System.Windows;
using System.Windows.Controls;

namespace Wuwa.App;

public partial class WorkspaceHelpView : UserControl
{
    private Action? _back;

    public WorkspaceHelpView()
    {
        InitializeComponent();
    }

    public void Configure(Action back)
    {
        _back = back ?? throw new ArgumentNullException(nameof(back));
    }

    private void AuthorBilibili_OnClick(object sender, RoutedEventArgs e) =>
        App.OpenAuthorBilibili(Window.GetWindow(this));

    private void Back_OnClick(object sender, RoutedEventArgs e) => _back?.Invoke();
}
