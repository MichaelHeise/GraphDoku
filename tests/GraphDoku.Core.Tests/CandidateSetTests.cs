using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Tests;

public class CandidateSetTests
{
    [Fact]
    public void From_domain_contains_all_values_in_ascending_order()
    {
        var set = CandidateSet.FromDomain(new ValueDomain(-2, 3));

        set.Should().Equal(-2, -1, 0, 1, 2, 3);
        set.Count.Should().Be(6);
        set.MinValue.Should().Be(-2);
        set.MaxValue.Should().Be(3);
        set.SingleValue.Should().BeNull();
        set.Contains(4).Should().BeFalse();
        set.Contains(-3).Should().BeFalse();
        set.Contains(int.MinValue).Should().BeFalse();
        set.Contains(int.MaxValue).Should().BeFalse();
    }

    [Fact]
    public void From_value_outside_domain_is_empty()
    {
        var domain = new ValueDomain(1, 9);

        CandidateSet.FromValue(domain, 5).Should().Equal(5);
        CandidateSet.FromValue(domain, 5).SingleValue.Should().Be(5);
        CandidateSet.FromValue(domain, 12).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Without_and_only_return_new_sets_and_keep_the_original()
    {
        var set = CandidateSet.FromDomain(new ValueDomain(1, 5));

        var smaller = set.Without([2, 4, 7]);
        smaller.Should().Equal(1, 3, 5);
        set.Should().HaveCount(5);
        set.Without([7]).Should().BeSameAs(set, "ohne Änderung bleibt die Instanz erhalten");
        set.Without(3).Should().Equal(1, 2, 4, 5);
        smaller.Only(3).Should().Equal(3);
        smaller.Invoking(s => s.Only(2)).Should().Throw<InvalidOperationException>();
        smaller.Without([1, 3, 5]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Large_domains_span_several_words()
    {
        var domain = new ValueDomain(-100, ValueDomain.MaxSize - 101);
        var set = CandidateSet.FromDomain(domain).Without(Enumerable.Range(-100, 4000));

        set.Count.Should().Be(ValueDomain.MaxSize - 4000);
        set.MinValue.Should().Be(3900);
        set.MaxValue.Should().Be(ValueDomain.MaxSize - 101);
        set.Should().BeInAscendingOrder();
        set.NextAbove(3950).Should().Be(3951);
        set.NextBelow(3900).Should().BeNull();
        set.Invoking(_ => CandidateSet.FromDomain(new ValueDomain(0, ValueDomain.MaxSize))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Next_above_and_below_skip_gaps()
    {
        var set = CandidateSet.FromDomain(new ValueDomain(1, 9)).Without([3, 4, 5, 6]);

        set.NextAbove(2).Should().Be(7);
        set.NextAbove(9).Should().BeNull();
        set.NextBelow(7).Should().Be(2);
        set.NextBelow(1).Should().BeNull();
    }

    [Fact]
    public void Set_comparisons_and_value_equality()
    {
        var set = CandidateSet.FromDomain(new ValueDomain(1, 4)).Without(2);

        set.SetEquals([4, 3, 1]).Should().BeTrue();
        set.IsSubsetOf([1, 2, 3, 4]).Should().BeTrue();
        set.IsProperSubsetOf([1, 3, 4]).Should().BeFalse();
        set.IsSupersetOf([1, 4]).Should().BeTrue();
        set.IsProperSupersetOf([1, 3, 4]).Should().BeFalse();
        set.Overlaps([2, 5]).Should().BeFalse();
        set.Overlaps([3]).Should().BeTrue();

        var same = CandidateSet.FromDomain(new ValueDomain(0, 10)).Without([0, 2, 5, 6, 7, 8, 9, 10]);
        same.Should().Equal(set);
        same.Equals(set).Should().BeTrue("Gleichheit hängt nur von den Werten ab");
        same.GetHashCode().Should().Be(set.GetHashCode());
        set.ToString().Should().Be("{1, 3, 4}");
    }
}
