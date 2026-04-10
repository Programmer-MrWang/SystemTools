using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Models.ComponentSettings;

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
}