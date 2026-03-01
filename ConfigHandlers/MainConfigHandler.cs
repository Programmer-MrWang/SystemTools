using ClassIsland.Shared.Helpers;
using System;
using System.ComponentModel;
using System.IO;
using SystemTools.Shared;

namespace SystemTools.ConfigHandlers;

public class MainConfigHandler
{
    readonly string _configPath;
    public MainConfigData Data { get; set; }

    public MainConfigHandler(string pluginConfigFolder)
    {
        _configPath = Path.Combine(pluginConfigFolder, "Main.json");

        Data = ConfigureFileHelper.LoadConfig<MainConfigData>(_configPath);

        SubscribeToChanges();

        GlobalConstants.MainConfig = this;
    }
    

    void SubscribeToChanges()
    {
        Data.PropertyChanged += (sender, args) => { Save(); };
    }

    public void Save()
    {
        ConfigureFileHelper.SaveConfig(_configPath, Data);
    }
}