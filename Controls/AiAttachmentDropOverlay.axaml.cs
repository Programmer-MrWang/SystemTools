using Avalonia.Controls;

namespace SystemTools.Controls;

public partial class AiAttachmentDropOverlay : UserControl
{
    public AiAttachmentDropOverlay()
    {
        InitializeComponent();
    }

    public bool ShowForFiles(int fileCount, int availableSlots, bool canModifyAttachments)
    {
        if (fileCount <= 0)
        {
            Hide();
            return false;
        }

        IsVisible = true;
        var isValid = canModifyAttachments && availableSlots > 0;
        ValidIcon.IsVisible = isValid;
        InvalidIcon.IsVisible = !isValid;

        if (!canModifyAttachments)
        {
            HintText.Text = "当前无法添加附件";
            SubHintText.Text = "请等待当前附件处理或 AI 回复完成";
            return false;
        }

        if (availableSlots <= 0)
        {
            HintText.Text = "附件数量已达上限";
            SubHintText.Text = $"每条消息最多添加 {Services.AiAttachmentService.MaximumAttachmentCount} 个附件";
            return false;
        }

        HintText.Text = fileCount == 1
            ? "松开以检查 1 个附件"
            : $"松开以检查 {fileCount} 个附件";
        SubHintText.Text = fileCount > availableSlots
            ? $"最多还可添加 {availableSlots} 个，其余文件会在确认时列出"
            : "松开后可预览并确认上传";
        return true;
    }

    public void Hide()
    {
        IsVisible = false;
        ValidIcon.IsVisible = false;
        InvalidIcon.IsVisible = false;
    }
}
