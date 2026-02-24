using System;
using System.Windows.Forms;

namespace SystemTools.Services;

public interface IHotkeyService
{
    /// <summary>
    /// 热键按下事件（返回触发该事件的按键信息）
    /// </summary>
    event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    /// <summary>
    /// 注册监听特定热键
    /// </summary>
    /// <param name="modifierKeys">修饰键</param>
    /// <param name="virtualKey">虚拟键码</param>
    /// <returns>热键ID（用于注销）</returns>
    int RegisterHotkey(int modifierKeys, uint virtualKey);

    /// <summary>
    /// 注销特定热键
    /// </summary>
    /// <param name="hotkeyId">热键ID</param>
    void UnregisterHotkey(int hotkeyId);

    /// <summary>
    /// 检查热键是否已被注册
    /// </summary>
    bool IsHotkeyRegistered(int modifierKeys, uint virtualKey);

    /// <summary>
    /// 临时挂起所有热键（用于录制新热键）
    /// </summary>
    void SuspendAllHotkeys();

    /// <summary>
    /// 恢复所有热键注册
    /// </summary>
    void ResumeAllHotkeys();

    /// <summary>
    /// 获取热键显示文本
    /// </summary>
    string GetHotkeyDisplay(int modifierKeys, uint virtualKey);
}

public class HotkeyEventArgs : EventArgs
{
    public int ModifierKeys { get; set; }
    public uint VirtualKey { get; set; }
    public int HotkeyId { get; set; }
}