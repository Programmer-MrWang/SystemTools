using System.Collections.Generic;
using Avalonia.Controls;
using SystemTools.Models;
using SystemTools.Services;

namespace SystemTools.Controls;

public partial class AiAttachmentDropConfirmation : UserControl
{
    public AiAttachmentDropConfirmation()
        : this(new AiAttachmentLoadResult([], []))
    {
    }

    public AiAttachmentDropConfirmation(AiAttachmentLoadResult result)
    {
        Accepted = result.Accepted;
        Rejected = result.Rejected;
        Summary = Accepted.Count == 1
            ? "将把以下附件添加到当前消息。"
            : $"将把以下 {Accepted.Count} 个附件添加到当前消息。";
        InitializeComponent();
        DataContext = this;
        RejectedItemsWarning.IsVisible = Rejected.Count > 0;
    }

    public IReadOnlyList<AiAttachment> Accepted { get; }

    public IReadOnlyList<string> Rejected { get; }

    public string Summary { get; }
}
