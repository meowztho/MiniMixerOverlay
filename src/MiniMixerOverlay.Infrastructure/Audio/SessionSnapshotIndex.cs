namespace MiniMixerOverlay.Infrastructure.Audio;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

// A published map is immutable. Polling replaces it atomically; commands keep their lookup valid.
internal sealed class SessionSnapshotIndex<T> where T : class
{
    private Dictionary<string, T> _current = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string sessionId, out T? value) =>
        Volatile.Read(ref _current).TryGetValue(sessionId, out value);

    public IReadOnlyList<string> Replace(IReadOnlyDictionary<string, T> sessions) =>
        Replace(sessions, out _);

    public IReadOnlyList<string> Replace(IReadOnlyDictionary<string, T> sessions, out IReadOnlyList<string> added) =>
        Replace(sessions, out added, out _);

    // Retired is the OLD map's orphaned resources. Caller may dispose these
    // only while no concurrent consumer can still be inside a command.
    public IReadOnlyList<string> Replace(
        IReadOnlyDictionary<string, T> sessions,
        out IReadOnlyList<string> added,
        out IReadOnlyList<T> retired)
    {
        var next = new Dictionary<string, T>(sessions, StringComparer.OrdinalIgnoreCase);
        var previous = Interlocked.Exchange(ref _current, next);
        added = next.Keys.Where(id => !previous.ContainsKey(id)).ToArray();
        retired = previous
            .Where(item => !next.TryGetValue(item.Key, out var replacement) ||
                           !ReferenceEquals(item.Value, replacement))
            .Select(item => item.Value).Distinct<T>(ReferenceEqualityComparer.Instance).ToArray();
        return previous.Keys.Where(id => !next.ContainsKey(id)).ToArray();
    }
}
