using System.Collections;
using System.Globalization;
using System.Numerics;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Candidates;

/// <summary>
/// Unveränderliche Kandidatenmenge eines Knotens als Bitmenge über dessen Wertebereich. Änderungen liefern eine neue
/// Menge; dadurch kann <see cref="CandidateState"/> Mengen zwischen Kopien teilen und Schnappschüsse billig anlegen.
/// Aufzählung immer aufsteigend.
/// </summary>
public sealed class CandidateSet : IReadOnlySet<int>, IEquatable<CandidateSet>
{
    private readonly int _offset;
    private readonly int _length;
    private readonly ulong[] _bits;

    private CandidateSet(int offset, int length, ulong[] bits)
    {
        _offset = offset;
        _length = length;
        _bits = bits;
        var count = 0;
        foreach (var word in bits)
        {
            count += BitOperations.PopCount(word);
        }

        Count = count;
        MinValue = count == 0 ? 0 : FindFirst();
        MaxValue = count == 0 ? 0 : FindLast();
    }

    /// <summary>Anzahl der Kandidaten.</summary>
    public int Count { get; }

    public bool IsEmpty => Count == 0;

    /// <summary>Kleinster Kandidat; bei leerer Menge 0 (vorher <see cref="IsEmpty"/> prüfen).</summary>
    public int MinValue { get; }

    /// <summary>Größter Kandidat; bei leerer Menge 0 (vorher <see cref="IsEmpty"/> prüfen).</summary>
    public int MaxValue { get; }

    /// <summary>Der einzige Kandidat, wenn genau einer übrig ist, sonst <c>null</c>.</summary>
    public int? SingleValue => Count == 1 ? MinValue : null;

    /// <summary>Alle Werte des Bereichs als Kandidaten.</summary>
    public static CandidateSet FromDomain(ValueDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var length = CheckedLength(domain);
        var bits = new ulong[WordCount(length)];
        for (var i = 0; i < length; i++)
        {
            bits[i >> 6] |= 1UL << (i & 63);
        }

        return new CandidateSet(domain.Min, length, bits);
    }

    /// <summary>Nur <paramref name="value"/> als Kandidat (leer, wenn der Wert außerhalb des Bereichs liegt).</summary>
    public static CandidateSet FromValue(ValueDomain domain, int value)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var length = CheckedLength(domain);
        var bits = new ulong[WordCount(length)];
        if (domain.Contains(value))
        {
            var index = value - domain.Min;
            bits[index >> 6] |= 1UL << (index & 63);
        }

        return new CandidateSet(domain.Min, length, bits);
    }

    public bool Contains(int value)
    {
        var index = (long)value - _offset;
        return index >= 0 && index < _length && (_bits[index >> 6] & (1UL << (int)(index & 63))) != 0;
    }

    /// <summary>Menge ohne die angegebenen Werte (dieselbe Instanz, wenn sich nichts ändert).</summary>
    public CandidateSet Without(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ulong[]? bits = null;
        foreach (var value in values)
        {
            if (!Contains(value))
            {
                continue;
            }

            bits ??= (ulong[])_bits.Clone();
            var index = value - _offset;
            bits[index >> 6] &= ~(1UL << (index & 63));
        }

        return bits is null ? this : new CandidateSet(_offset, _length, bits);
    }

    public CandidateSet Without(int value) => Contains(value) ? Without([value]) : this;

    /// <summary>Menge, die nur noch <paramref name="value"/> enthält; der Wert muss Kandidat sein.</summary>
    public CandidateSet Only(int value)
    {
        if (!Contains(value))
        {
            throw new InvalidOperationException($"{value.ToString(CultureInfo.InvariantCulture)} ist kein Kandidat.");
        }

        if (Count == 1)
        {
            return this;
        }

        var bits = new ulong[_bits.Length];
        var index = value - _offset;
        bits[index >> 6] = 1UL << (index & 63);
        return new CandidateSet(_offset, _length, bits);
    }

    /// <summary>Kleinster Kandidat, der größer als <paramref name="value"/> ist, sonst <c>null</c>.</summary>
    public int? NextAbove(long value)
    {
        foreach (var candidate in this)
        {
            if (candidate > value)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Größter Kandidat, der kleiner als <paramref name="value"/> ist, sonst <c>null</c>.</summary>
    public int? NextBelow(long value)
    {
        int? result = null;
        foreach (var candidate in this)
        {
            if (candidate >= value)
            {
                break;
            }

            result = candidate;
        }

        return result;
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool SetEquals(IEnumerable<int> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var set = other as IReadOnlySet<int> ?? other.ToHashSet();
        return set.Count == Count && set.All(Contains);
    }

    public bool IsSubsetOf(IEnumerable<int> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var set = other.ToHashSet();
        return this.All(set.Contains);
    }

    public bool IsSupersetOf(IEnumerable<int> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.All(Contains);
    }

    public bool IsProperSubsetOf(IEnumerable<int> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var set = other.ToHashSet();
        return set.Count > Count && this.All(set.Contains);
    }

    public bool IsProperSupersetOf(IEnumerable<int> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var set = other.ToHashSet();
        return set.Count < Count && set.All(Contains);
    }

    public bool Overlaps(IEnumerable<int> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.Any(Contains);
    }

    public bool Equals(CandidateSet? other) => other is not null && (ReferenceEquals(this, other) || SetEquals(other));

    public override bool Equals(object? obj) => obj is CandidateSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in this)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => "{" + string.Join(", ", this.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "}";

    private static int CheckedLength(ValueDomain domain)
    {
        if (domain.Size > ValueDomain.MaxSize)
        {
            throw new ArgumentException(
                $"Der Wertebereich {domain} ist zu groß (höchstens {ValueDomain.MaxSize} Werte).",
                nameof(domain));
        }

        return (int)domain.Size;
    }

    private static int WordCount(int length) => Math.Max(1, (length + 63) >> 6);

    private int FindFirst()
    {
        for (var w = 0; w < _bits.Length; w++)
        {
            if (_bits[w] != 0)
            {
                return _offset + (w << 6) + BitOperations.TrailingZeroCount(_bits[w]);
            }
        }

        throw new InvalidOperationException("Leere Menge.");
    }

    private int FindLast()
    {
        for (var w = _bits.Length - 1; w >= 0; w--)
        {
            if (_bits[w] != 0)
            {
                return _offset + (w << 6) + 63 - BitOperations.LeadingZeroCount(_bits[w]);
            }
        }

        throw new InvalidOperationException("Leere Menge.");
    }

    /// <summary>Aufsteigende Aufzählung ohne Zwischenspeicher.</summary>
    public struct Enumerator : IEnumerator<int>
    {
        private readonly CandidateSet _set;
        private int _word;
        private ulong _remaining;

        internal Enumerator(CandidateSet set)
        {
            _set = set;
            _word = -1;
            _remaining = 0;
            Current = 0;
        }

        public int Current { get; private set; }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (_remaining == 0)
            {
                _word++;
                if (_word >= _set._bits.Length)
                {
                    return false;
                }

                _remaining = _set._bits[_word];
            }

            var bit = BitOperations.TrailingZeroCount(_remaining);
            _remaining &= _remaining - 1;
            Current = _set._offset + (_word << 6) + bit;
            return true;
        }

        public void Reset()
        {
            _word = -1;
            _remaining = 0;
        }

        public readonly void Dispose()
        {
        }
    }
}
