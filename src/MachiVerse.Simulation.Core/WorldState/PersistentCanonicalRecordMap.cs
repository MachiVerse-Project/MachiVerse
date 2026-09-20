using System.Collections;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

/// <summary>
/// Immutable AVL-backed canonical map keyed by RecordId. Updates copy only the O(log N) search path,
/// while in-order enumeration preserves bytewise OpaqueId128 canonical order.
///
/// This is an implementation structure only: it does not change record identity, ordering, payload,
/// or any canonical digest input.
/// </summary>
internal sealed class PersistentCanonicalRecordMapV1<T> : IReadOnlyList<T>
    where T : class
{
    private readonly Node? _root;
    private readonly Func<T, OpaqueId128> _keySelector;
    private readonly string _duplicateCode;
    private readonly string _zeroCode;

    private PersistentCanonicalRecordMapV1(
        Node? root,
        Func<T, OpaqueId128> keySelector,
        string duplicateCode,
        string zeroCode)
    {
        _root = root;
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _duplicateCode = duplicateCode ?? throw new ArgumentNullException(nameof(duplicateCode));
        _zeroCode = zeroCode ?? throw new ArgumentNullException(nameof(zeroCode));
    }

    public int Count => CountOf(_root);

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var node = _root!;
            var remaining = index;
            while (true)
            {
                var leftCount = CountOf(node.Left);
                if (remaining < leftCount)
                {
                    node = node.Left!;
                    continue;
                }

                if (remaining == leftCount)
                    return node.Value;

                remaining -= leftCount + 1;
                node = node.Right!;
            }
        }
    }

    public static PersistentCanonicalRecordMapV1<T> FromUnordered(
        IEnumerable<T> values,
        Func<T, OpaqueId128> keySelector,
        string duplicateCode,
        string zeroCode)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(keySelector);

        var sorted = new SortedDictionary<OpaqueId128, T>();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            var key = keySelector(value);
            if (key.IsZero)
                throw new InvalidDataException(zeroCode);
            if (!sorted.TryAdd(key, value))
                throw new InvalidDataException(duplicateCode);
        }

        var canonical = sorted.Values.ToArray();
        return FromCanonical(canonical, keySelector, duplicateCode, zeroCode);
    }

    public static PersistentCanonicalRecordMapV1<T> FromCanonical(
        IReadOnlyList<T> values,
        Func<T, OpaqueId128> keySelector,
        string duplicateCode,
        string zeroCode)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(keySelector);

        OpaqueId128? previous = null;
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] ?? throw new InvalidDataException("persistent-canonical-map.value-null");
            var key = keySelector(value);
            if (key.IsZero)
                throw new InvalidDataException(zeroCode);
            if (previous is { } prior && prior.CompareTo(key) >= 0)
                throw new InvalidDataException(duplicateCode);
            previous = key;
        }

        return new PersistentCanonicalRecordMapV1<T>(
            BuildBalanced(values, keySelector, 0, values.Count),
            keySelector,
            duplicateCode,
            zeroCode);
    }

    public bool TryGet(OpaqueId128 key, out T? value)
    {
        var node = _root;
        while (node is not null)
        {
            var comparison = key.CompareTo(node.Key);
            if (comparison == 0)
            {
                value = node.Value;
                return true;
            }

            node = comparison < 0 ? node.Left : node.Right;
        }

        value = null;
        return false;
    }

    public PersistentCanonicalRecordMapV1<T> AddRange(
        IEnumerable<T> additions,
        string collisionCode)
        => Apply(additions, allowCreate: true, allowReplace: false, collisionCode, missingCode: collisionCode);

    public PersistentCanonicalRecordMapV1<T> ReplaceRange(
        IEnumerable<T> replacements,
        string missingCode)
        => Apply(replacements, allowCreate: false, allowReplace: true, collisionCode: missingCode, missingCode);

    public PersistentCanonicalRecordMapV1<T> UpsertRange(
        IEnumerable<T> values)
        => Apply(values, allowCreate: true, allowReplace: true, _duplicateCode, _duplicateCode);

    public IEnumerator<T> GetEnumerator()
    {
        if (_root is null)
            yield break;

        var stack = new Stack<Node>();
        var node = _root;
        while (stack.Count != 0 || node is not null)
        {
            while (node is not null)
            {
                stack.Push(node);
                node = node.Left;
            }

            node = stack.Pop();
            yield return node.Value;
            node = node.Right;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private PersistentCanonicalRecordMapV1<T> Apply(
        IEnumerable<T> values,
        bool allowCreate,
        bool allowReplace,
        string collisionCode,
        string missingCode)
    {
        ArgumentNullException.ThrowIfNull(values);
        var root = _root;
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            var key = _keySelector(value);
            if (key.IsZero)
                throw new InvalidDataException(_zeroCode);
            root = Set(
                root,
                key,
                value,
                allowCreate,
                allowReplace,
                collisionCode,
                missingCode);
        }

        return ReferenceEquals(root, _root)
            ? this
            : new PersistentCanonicalRecordMapV1<T>(root, _keySelector, _duplicateCode, _zeroCode);
    }

    private static Node? BuildBalanced(
        IReadOnlyList<T> values,
        Func<T, OpaqueId128> keySelector,
        int start,
        int length)
    {
        if (length == 0)
            return null;

        var leftLength = length >> 1;
        var middle = start + leftLength;
        var value = values[middle];
        return new Node(
            keySelector(value),
            value,
            BuildBalanced(values, keySelector, start, leftLength),
            BuildBalanced(values, keySelector, middle + 1, length - leftLength - 1));
    }

    private static Node Set(
        Node? node,
        OpaqueId128 key,
        T value,
        bool allowCreate,
        bool allowReplace,
        string collisionCode,
        string missingCode)
    {
        if (node is null)
        {
            if (!allowCreate)
                throw new InvalidDataException(missingCode);
            return new Node(key, value, null, null);
        }

        var comparison = key.CompareTo(node.Key);
        if (comparison == 0)
        {
            if (!allowReplace)
                throw new InvalidDataException(collisionCode);
            if (ReferenceEquals(node.Value, value))
                return node;
            return new Node(key, value, node.Left, node.Right);
        }

        if (comparison < 0)
        {
            var left = Set(node.Left, key, value, allowCreate, allowReplace, collisionCode, missingCode);
            if (ReferenceEquals(left, node.Left))
                return node;
            return Balance(new Node(node.Key, node.Value, left, node.Right));
        }

        var right = Set(node.Right, key, value, allowCreate, allowReplace, collisionCode, missingCode);
        if (ReferenceEquals(right, node.Right))
            return node;
        return Balance(new Node(node.Key, node.Value, node.Left, right));
    }

    private static Node Balance(Node node)
    {
        var balance = HeightOf(node.Left) - HeightOf(node.Right);
        if (balance > 1)
        {
            if (HeightOf(node.Left!.Left) < HeightOf(node.Left.Right))
                return RotateRight(new Node(node.Key, node.Value, RotateLeft(node.Left), node.Right));
            return RotateRight(node);
        }

        if (balance < -1)
        {
            if (HeightOf(node.Right!.Right) < HeightOf(node.Right.Left))
                return RotateLeft(new Node(node.Key, node.Value, node.Left, RotateRight(node.Right)));
            return RotateLeft(node);
        }

        return node;
    }

    private static Node RotateLeft(Node node)
    {
        var pivot = node.Right ?? throw new InvalidOperationException("persistent-canonical-map.rotate-left");
        var moved = new Node(node.Key, node.Value, node.Left, pivot.Left);
        return new Node(pivot.Key, pivot.Value, moved, pivot.Right);
    }

    private static Node RotateRight(Node node)
    {
        var pivot = node.Left ?? throw new InvalidOperationException("persistent-canonical-map.rotate-right");
        var moved = new Node(node.Key, node.Value, pivot.Right, node.Right);
        return new Node(pivot.Key, pivot.Value, pivot.Left, moved);
    }

    private static int HeightOf(Node? node) => node?.Height ?? 0;
    private static int CountOf(Node? node) => node?.Count ?? 0;

    private sealed class Node
    {
        public Node(OpaqueId128 key, T value, Node? left, Node? right)
        {
            Key = key;
            Value = value;
            Left = left;
            Right = right;
            Height = checked(1 + Math.Max(HeightOf(left), HeightOf(right)));
            Count = checked(1 + CountOf(left) + CountOf(right));
        }

        public OpaqueId128 Key { get; }
        public T Value { get; }
        public Node? Left { get; }
        public Node? Right { get; }
        public int Height { get; }
        public int Count { get; }
    }
}
