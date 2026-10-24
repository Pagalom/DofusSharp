using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

/// <summary>
/// The Connection TCP socket can close between the first validated owner
/// lookup and the server-selection protobuf decoder. Do not discard an
/// already established exact socket/window owner on a redundant refresh.
/// </summary>
public sealed class ConnectionSelectionOwnerRetentionTest
{
    private static readonly DofusClientWindowKey Window = new(101, 1201);

    [Fact]
    public void AlreadyVerifiedWindowIsUsedWithoutASecondSocketTableScan()
    {
        int lookups = 0;
        DofusClientWindowKey? result =
            DofusNetworkCaptureService.ResolveSelectionWindow(
                Window, () =>
                {
                    lookups++;
                    return null;
                });

        result.Should().Be(Window);
        lookups.Should().Be(0);
    }

    [Fact]
    public void MissingFirstOwnerAttemptsOneExactSocketRefresh()
    {
        int lookups = 0;
        DofusClientWindowKey? result =
            DofusNetworkCaptureService.ResolveSelectionWindow(
                null, () =>
                {
                    lookups++;
                    return Window;
                });

        result.Should().Be(Window);
        lookups.Should().Be(1);
    }

    [Fact]
    public void TwoMissingOwnerChecksNeverInventAWindow()
    {
        int lookups = 0;
        DofusClientWindowKey? result =
            DofusNetworkCaptureService.ResolveSelectionWindow(
                null, () =>
                {
                    lookups++;
                    return null;
                });

        result.Should().BeNull();
        lookups.Should().Be(1);
    }

    [Fact]
    public void CountersSeparateUnownedSelectionAndMissingGameSelection()
    {
        WindowServerAttributionState state = new(diagnosticsEnabled: true);
        state.Reset(10);
        state.RecordSelectionFrame(10, exactOwnerLinked: false);
        state.RecordSelectionFrame(10, exactOwnerLinked: true);

        ServerLinkDiagnostics status = state.ReadDiagnostics();
        status.SelectionFrames.Should().Be(2);
        status.SelectionWithOwner.Should().Be(1);
        status.SelectionWithoutOwner.Should().Be(1);
        status.GameWithSelection.Should().Be(0);
        status.Confirmed.Should().Be(0);

        state.Reset(11);
        state.ReadDiagnostics().SelectionFrames.Should().Be(0);
        state.RecordSelectionFrame(10, exactOwnerLinked: false);
        state.ReadDiagnostics().SelectionFrames.Should().Be(0);
    }

    [Fact]
    public void DiagnosticsDoNotAuthorizeServerLease()
    {
        WindowServerAttributionState state = new(diagnosticsEnabled: true);
        CurrentServerState server = new(state, automaticRoutingEnabled: true);
        state.Reset(1);
        state.RecordSelectionFrame(1, exactOwnerLinked: true);

        server.GetWindowCaptureLease(Window).Should().BeNull();
        server.GetCaptureLease().Should().BeNull();
    }
}
