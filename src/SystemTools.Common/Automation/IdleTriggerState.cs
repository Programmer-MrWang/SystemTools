namespace SystemTools.Common.Automation;

public enum IdleTransition { None, Trigger, Revert }

public sealed class IdleTriggerState
{
    private bool _triggered;
    public IdleTransition Sample(TimeSpan idle, TimeSpan threshold)
    {
        if (idle < TimeSpan.Zero || threshold < TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(idle));
        if (idle >= threshold)
        {
            if (_triggered) return IdleTransition.None;
            _triggered = true;
            return IdleTransition.Trigger;
        }
        if (!_triggered) return IdleTransition.None;
        _triggered = false;
        return IdleTransition.Revert;
    }
}
