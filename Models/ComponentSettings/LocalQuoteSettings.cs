using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Models.ComponentSettings;

public enum LocalQuoteProgressBarPosition
{
    [Description("置于底部")]
    Bottom = 0,
    [Description("置于顶部")]
    Top = 1
}

public partial class LocalQuoteSettings : ObservableObject
{
    [ObservableProperty]
    private string _quotesFilePath = string.Empty;

    [ObservableProperty]
    private bool _enableAnimation = true;

    [ObservableProperty]
    private int _carouselIntervalSeconds = 6;

    [ObservableProperty] 
    private bool _isPersistenceEnabled = true;

    [ObservableProperty]
    private int _lastIndex = -1;

    [ObservableProperty]
    private DateTime _lastSwitchTime = DateTime.MinValue;

    [ObservableProperty]
    private bool _showProgressBar = true;

    [ObservableProperty]
    private LocalQuoteProgressBarPosition _progressBarPosition = LocalQuoteProgressBarPosition.Bottom;
}
