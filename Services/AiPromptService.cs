using System;
using System.IO;
using SystemTools.Shared;

namespace SystemTools.Services;

public sealed class AiPromptService
{
    private readonly string _promptPath;

    public AiPromptService()
    {
        _promptPath = Path.Combine(GlobalConstants.Information.PluginFolder, "agents.md");
    }

    public string LoadSystemPrompt()
    {
        if (!File.Exists(_promptPath))
        {
            throw new FileNotFoundException("未找到 AI 系统提示词文件 agents.md。", _promptPath);
        }

        var prompt = File.ReadAllText(_promptPath).Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidDataException("AI 系统提示词文件 agents.md 不能为空。");
        }

        return prompt;
    }
}
