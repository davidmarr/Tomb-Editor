#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyManager;

/// <summary>
/// View model for a single flyby camera entry in the camera list.
/// </summary>
public partial class FlybyCameraItemViewModel : ObservableObject
{
    public FlybyCameraInstance Camera { get; }

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private string _timecode = "00:00.00";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDuplicateIndex;

    [ObservableProperty]
    private string _roomName = string.Empty;

    public FlybyCameraItemViewModel(FlybyCameraInstance camera)
    {
        Camera = camera;
        _number = camera.Number;
        _roomName = camera.Room?.ToString() ?? "NULL";
    }
}
