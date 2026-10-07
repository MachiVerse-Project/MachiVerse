using System.Collections;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

// Two representations share one immutable index, including its count and canonical order.
// Reverse projection preserves the ordinary persistent update contract without rebuilding the index.
internal sealed class ProjectedCanonicalRecordMapV1<TSource, T> : ICanonicalRecordMapV1<T>
    where TSource : class
    where T : class
{
    private readonly ICanonicalRecordMapV1<TSource> _source;
    private readonly Func<TSource, T> _project;
    private readonly Func<T, TSource> _reverse;

    public ProjectedCanonicalRecordMapV1(
        ICanonicalRecordMapV1<TSource> source,
        Func<TSource, T> project,
        Func<T, TSource> reverse)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _reverse = reverse ?? throw new ArgumentNullException(nameof(reverse));
    }

    public int Count => _source.Count;
    public T this[int index] => _project(_source[index]);

    public bool TryGet(OpaqueId128 key, out T? value)
    {
        if (_source.TryGet(key, out var source))
        {
            value = _project(source!);
            return true;
        }
        value = null;
        return false;
    }

    public ICanonicalRecordMapV1<T> AddRange(IEnumerable<T> additions, string collisionCode)
        => new ProjectedCanonicalRecordMapV1<TSource, T>(
            _source.AddRange(additions.Select(_reverse), collisionCode), _project, _reverse);

    public ICanonicalRecordMapV1<T> ReplaceRange(IEnumerable<T> replacements, string missingCode)
        => new ProjectedCanonicalRecordMapV1<TSource, T>(
            _source.ReplaceRange(replacements.Select(_reverse), missingCode), _project, _reverse);

    public IEnumerator<T> GetEnumerator()
    {
        foreach (var source in _source)
            yield return _project(source);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
