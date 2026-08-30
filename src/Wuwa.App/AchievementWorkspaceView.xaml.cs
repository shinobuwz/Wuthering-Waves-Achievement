using System.Windows.Controls;

namespace Wuwa.App;

/// <summary>
/// Passive achievement feature surface. MainWindow currently coordinates the existing
/// AchievementWorkspace/OCR/tracker commands while this view owns their visual namescope.
/// </summary>
public partial class AchievementWorkspaceView : UserControl
{
    public AchievementWorkspaceView()
    {
        InitializeComponent();
    }
}
