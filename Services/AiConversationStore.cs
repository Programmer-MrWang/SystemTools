using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using SystemTools.Models;
using SystemTools.Shared;

namespace SystemTools.Services;

public sealed class AiConversationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public AiConversationStore()
    {
        var configFolder = GlobalConstants.PluginConfigFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClassIsland",
            "Plugins",
            "SystemTools");
        _filePath = Path.Combine(configFolder, "AiConversations.json");
        Load();
    }

    public ObservableCollection<AiConversation> Conversations { get; } = [];

    public Guid? ActiveConversationId { get; private set; }

    public string? LastLoadError { get; private set; }

    public AiConversation CreateConversation()
    {
        var now = DateTimeOffset.Now;
        var conversation = new AiConversation
        {
            CreatedAt = now,
            UpdatedAt = now
        };

        Conversations.Insert(0, conversation);
        ActiveConversationId = conversation.Id;
        Save();
        return conversation;
    }

    public void SetActiveConversation(AiConversation? conversation)
    {
        ActiveConversationId = conversation?.Id;
        Save();
    }

    public void Touch(AiConversation conversation)
    {
        conversation.UpdatedAt = DateTimeOffset.Now;
        var index = Conversations.IndexOf(conversation);
        if (index > 0)
        {
            Conversations.Move(index, 0);
        }
    }

    public bool DeleteConversation(AiConversation conversation)
    {
        var removed = Conversations.Remove(conversation);
        if (!removed)
        {
            return false;
        }

        foreach (var attachment in conversation.Messages.SelectMany(message => message.Attachments))
        {
            attachment.Dispose();
        }

        if (ActiveConversationId == conversation.Id)
        {
            ActiveConversationId = Conversations.FirstOrDefault()?.Id;
        }

        Save();
        return true;
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var document = new ConversationStoreDocument
        {
            ActiveConversationId = ActiveConversationId,
            Conversations = Conversations.ToList()
        };
        var json = JsonSerializer.Serialize(document, SerializerOptions);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<ConversationStoreDocument>(json, SerializerOptions);
            if (document?.Conversations is null)
            {
                return;
            }

            foreach (var conversation in document.Conversations
                         .Where(x => x is not null)
                         .OrderByDescending(x => x.UpdatedAt))
            {
                conversation.Messages ??= [];
                foreach (var message in conversation.Messages)
                {
                    message.Attachments ??= [];
                    message.IsStreaming = false;
                    message.InitializeRuntimeState();
                }

                Conversations.Add(conversation);
            }

            ActiveConversationId = document.ActiveConversationId;
        }
        catch (Exception ex)
        {
            var backupPath = _filePath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            try
            {
                File.Copy(_filePath, backupPath, overwrite: false);
                LastLoadError = $"{ex.Message}；原文件已备份到 {Path.GetFileName(backupPath)}";
            }
            catch
            {
                LastLoadError = ex.Message;
            }
        }
    }

    private sealed class ConversationStoreDocument
    {
        public Guid? ActiveConversationId { get; init; }

        public List<AiConversation> Conversations { get; init; } = [];
    }
}
