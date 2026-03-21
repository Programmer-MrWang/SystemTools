using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Models.ComponentSettings;

public partial class NextClassDisplaySettings : ObservableObject
{
    [ObservableProperty]
    private string _prefixText = "下节课是 ";

    [ObservableProperty]
    private bool _showTeacherName = true;
}
