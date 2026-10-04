using BestCrush.Domain.Models;
using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class MarketDataChangeNotifierTest
{
    [Fact]
    public void ForwardsEveryCallIncludingIdenticalNotificationsAndDefaultQuantity()
    {
        MarketDataChangeNotifier notifier = new();
        List<(object? Sender, MarketDataChangedEventArgs Args)> received = [];
        notifier.Changed += (sender, args) => received.Add((sender, args));

        notifier.Notify(MarketObjectType.Rune, 100, "TEST", 10);
        notifier.Notify(MarketObjectType.Rune, 100, "TEST", 10);
        notifier.Notify(MarketObjectType.Equipment, 42, "TEST");

        received.Should().HaveCount(3);
        received.Should().OnlyContain(entry => ReferenceEquals(entry.Sender, notifier));
        received.Select(entry => entry.Args).Should().Equal(
            new MarketDataChangedEventArgs(MarketObjectType.Rune, 100, "TEST", 10),
            new MarketDataChangedEventArgs(MarketObjectType.Rune, 100, "TEST", 10),
            new MarketDataChangedEventArgs(MarketObjectType.Equipment, 42, "TEST", 0));
    }

    [Fact]
    public void CallsSubscribersSynchronouslyInRegistrationOrderAndPropagatesTheirExceptions()
    {
        MarketDataChangeNotifier notifier = new();
        List<string> steps = [];
        InvalidOperationException error = new("test subscriber");
        notifier.Changed += (_, _) => steps.Add("first");
        notifier.Changed += (_, _) => { steps.Add("second"); throw error; };
        notifier.Changed += (_, _) => steps.Add("third");

        Action notify = () => notifier.Notify(MarketObjectType.Resource, 1, "TEST", 1);

        notify.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        steps.Should().Equal("first", "second");
    }
}
