using System;
using System.Collections.Generic;

namespace SystemTools.Common.Automation;

public sealed class ActionRestoreStore<T>
{
    private sealed class Frame(Guid group, T original)
    {
        public Guid Group { get; } = group;
        public T Original { get; set; } = original;
        public HashSet<object> Owners { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, List<Frame>> _targets = new();
    private readonly Dictionary<(Guid, object), string> _owners = new();

    public void Capture(Guid group, object item, string target, T current)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue((group, item), out var previousTarget) && previousTarget != target)
                throw new InvalidOperationException("请先恢复此行动，再更改其目标。");
            if (!_targets.TryGetValue(target, out var frames))
                _targets[target] = frames = [];
            var frame = frames.Find(frame => frame.Group == group);
            if (frame == null)
            {
                frame = new Frame(group, current);
                frames.Add(frame);
            }
            else if (!ReferenceEquals(frames[^1], frame))
            {
                // 同组的后续行动再次取得目标时，保留其余组的恢复链。
                var index = frames.IndexOf(frame);
                frames.RemoveAt(index);
                frames[index].Original = frame.Original;
                frame.Original = current;
                frames.Add(frame);
            }
            frame.Owners.Add(item);
            _owners[(group, item)] = target;
        }
    }

    public bool TryRestore(Guid group, object item, out T original)
    {
        lock (_gate)
        {
            original = default!;
            if (!_owners.Remove((group, item), out var target)) return false;
            var frames = _targets[target];
            var index = frames.FindIndex(frame => frame.Group == group);
            var frame = frames[index];
            frame.Owners.Remove(item);
            if (frame.Owners.Count != 0) return false;
            frames.RemoveAt(index);
            if (index < frames.Count)
            {
                // 后触发的组仍持有目标时，只更新它将来恢复的基线。
                frames[index].Original = frame.Original;
                return false;
            }
            if (frames.Count == 0) _targets.Remove(target);
            original = frame.Original;
            return true;
        }
    }
}
