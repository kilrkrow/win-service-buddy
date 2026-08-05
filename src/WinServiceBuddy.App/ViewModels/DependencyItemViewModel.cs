using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinServiceBuddy.App.ViewModels;

/// <summary>One SCM dependency row in the side panel.</summary>
public partial class DependencyItemViewModel : ViewModelBase
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Running | Stopped | Disabled | Missing | …</summary>
    public required string StatusKind { get; init; }

    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? ServiceName : DisplayName;

    /// <summary>Short glyph for status (no image assets required).</summary>
    public string StatusIcon => StatusKind switch
    {
        "Running" => "●",
        "Stopped" => "●",
        "Disabled" => "⊘",
        "Missing" => "!",
        _ => "○"
    };

    public IBrush StatusBrush => StatusKind switch
    {
        "Running" => BrushRunning,
        "Stopped" => BrushStopped,
        "Disabled" => BrushDisabled,
        "Missing" => BrushMissing,
        _ => BrushUnknown
    };

    public string StatusLabel => StatusKind;

    public string ToolTip => $"{ServiceName} — {StatusKind}";

    private static readonly IBrush BrushRunning = SolidColorBrush.Parse("#3DDC97");
    private static readonly IBrush BrushStopped = SolidColorBrush.Parse("#FF6B6B");
    private static readonly IBrush BrushDisabled = SolidColorBrush.Parse("#8B9AAB");
    private static readonly IBrush BrushMissing = SolidColorBrush.Parse("#F0C929");
    private static readonly IBrush BrushUnknown = SolidColorBrush.Parse("#5C6B7A");
}
