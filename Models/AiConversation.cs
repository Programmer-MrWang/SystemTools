using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SystemTools.Models;

public sealed class AiConversation : INotifyPropertyChanged
{
    private string _title = "新对话";
    private DateTimeOffset _updatedAt = DateTimeOffset.Now;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? "新对话" : value.Trim();
            if (string.Equals(value, _title, StringComparison.Ordinal)) return;
            _title = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (value == _updatedAt) return;
            _updatedAt = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<AiConversationMessage> Messages { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AiConversationMessage : INotifyPropertyChanged
{
    private string _role = "user";
    private string _content = string.Empty;
    private bool _isStreaming;
    private bool _isEditing;
    private string _draftContent = string.Empty;
    private string _activityText = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Role
    {
        get => _role;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? "user" : value.Trim().ToLowerInvariant();
            if (string.Equals(value, _role, StringComparison.Ordinal)) return;
            _role = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUser));
            OnPropertyChanged(nameof(IsAssistant));
        }
    }

    public string Content
    {
        get => _content;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _content, StringComparison.Ordinal)) return;
            _content = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (value == _isStreaming) return;
            _isStreaming = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanShowAssistantActions));
        }
    }

    [JsonIgnore]
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (value == _isEditing) return;
            _isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotEditing));
        }
    }

    [JsonIgnore]
    public string DraftContent
    {
        get => _draftContent;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _draftContent, StringComparison.Ordinal)) return;
            _draftContent = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string ActivityText
    {
        get => _activityText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _activityText, StringComparison.Ordinal)) return;
            _activityText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActivityText));
        }
    }

    [JsonIgnore]
    public bool IsUser => string.Equals(Role, "user", StringComparison.Ordinal);

    [JsonIgnore]
    public bool IsAssistant => !IsUser;

    [JsonIgnore]
    public bool IsNotEditing => !IsEditing;

    [JsonIgnore]
    public bool HasActivityText => !string.IsNullOrWhiteSpace(ActivityText);

    [JsonIgnore]
    public bool CanShowAssistantActions => IsAssistant && !IsStreaming;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
