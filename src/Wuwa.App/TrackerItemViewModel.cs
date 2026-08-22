using Wuwa.Core;

namespace Wuwa.App;

public sealed record TrackerItemViewModel(AchievementRow Row)
{
    public string Name => Row.Name;
    public string Description => Row.Description;
    public string Context => $"{Row.Version} · {Row.FirstCategory} / {Row.SecondCategory}";
}
