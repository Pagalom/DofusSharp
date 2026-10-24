using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class DofusClientWindowSelectionTest
{
    private static DofusWindowInfo Window(long hwnd, int pid, string title) =>
        new(new IntPtr(hwnd), title, "Dofus", 0, 0, 800, 600)
        {
            ProcessId = pid
        };

    private static DofusClientWindowKey Key(long hwnd, int pid) => new(hwnd, pid);

    [Theory]
    [InlineData("Dofus")]
    [InlineData("Dofus 3.7.12.12 - Release")]
    [InlineData("")]
    public void UnrecognizedTitleDoesNotCreateCharacterName(string title)
    {
        DofusWindowTitleParser.Parse(title).Name.Should().BeNull();
    }

    [Fact]
    public void TitleSuppliesDisplayNameClassAndBuildButNotAccountIdentity()
    {
        var parsed = DofusWindowTitleParser.Parse(
            "Alice - Ecaflip - 3.7.12.12 - Release");
        parsed.Name.Should().Be("Alice");
        parsed.ClassName.Should().Be("Ecaflip");
        parsed.Build.Should().Be("3.7.12.12");

        CurrentDofusClientsState state = new();
        state.Update([Window(101, 1001, "Alice - Ecaflip - 3.7.12.12 - Release")]);
        state.SelectedClient.Should().NotBeNull();
        state.SelectedClient!.HasCharacterTitle.Should().BeTrue();

        // Window-title discovery cannot create a verified profile.
        new CurrentCharacterState().ActiveProfile.Should().BeNull();
    }

    [Fact]
    public void TwoDistinctWindowsFollowExplicitForegroundEvents()
    {
        CurrentDofusClientsState state = new();
        DofusWindowInfo first = Window(101, 1001, "Alice - Ecaflip - 3.7.12.12 - Release");
        DofusWindowInfo second = Window(102, 1002, "Bob - Cra - 3.7.12.12 - Release");
        state.Update([first, second]);
        state.SelectedClient.Should().BeNull(
            "the window enumeration order cannot decide which client is active");

        state.Update([first, second], Key(101, 1001));
        state.SelectedClient!.DisplayName.Should().Be("Alice");
        state.Update([second, first], Key(102, 1002));
        state.SelectedClient!.DisplayName.Should().Be("Bob");
        state.Clients.Select(x => x.DisplayName).Should().Equal("Alice", "Bob");
    }

    [Fact]
    public void ManualClickStaysSelectedWhileBestCrushIsForeground()
    {
        CurrentDofusClientsState state = new();
        DofusWindowInfo first = Window(101, 1001, "Alice - Ecaflip - 3.7.12.12 - Release");
        DofusWindowInfo second = Window(102, 1002, "Bob - Cra - 3.7.12.12 - Release");
        state.Update([first, second], Key(101, 1001));

        state.SelectWindow(Key(102, 1002)).Should().BeTrue();
        state.Update([second, first]); // BestCrush / another application has focus.
        state.SelectedClient!.DisplayName.Should().Be("Bob");
        state.Update([first, second], Key(101, 1001)); // Dofus focus returns.
        state.SelectedClient!.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public void ExplicitClickOnAlreadySelectedWindowStillRestoresManualBrowse()
    {
        CurrentDofusClientsState state = new();
        DofusClientWindowKey key = Key(101, 1001);
        state.Update([Window(101, 1001,
            "Alice - Ecaflip - 3.7.12.12 - Release")]);
        int requests = 0;
        state.TargetRequested += requested =>
        {
            requested.Should().Be(key);
            requests++;
        };

        state.SelectWindow(key).Should().BeTrue();
        state.SelectWindow(key).Should().BeTrue();
        requests.Should().Be(2,
            "two explicit card clicks must each revoke browsing even when targeting the same window");
    }

    [Fact]
    public void TargetingAlreadySelectedCardRestoresItsOwnServerAfterSqlBrowse()
    {
        CurrentDofusClientsState clients = new();
        DofusClientWindowKey tyleziaWindow = Key(101, 1001);
        clients.Update([Window(101, 1001,
            "Pagalom - Eniripsa - 3.7.12.12 - Release")], tyleziaWindow);

        ServerViewFilterState view = new();
        view.FollowWindow(tyleziaWindow, "Tylezia");
        clients.TargetRequested += target => view.FollowWindow(
            target, target == tyleziaWindow ? "Tylezia" : null);

        view.BrowseServer("Kourial");
        clients.SelectedClient!.Key.Should().Be(tyleziaWindow);
        view.IsBrowsingManually.Should().BeTrue();
        view.ServerName.Should().Be("Kourial");

        // The same previously selected card must remain clickable: its
        // TargetRequested event returns to the verified window's server.
        clients.SelectWindow(tyleziaWindow).Should().BeTrue();
        clients.SelectedClient!.Key.Should().Be(tyleziaWindow);
        view.ServerName.Should().Be("Tylezia");
        view.IsBrowsingManually.Should().BeFalse();
        view.TargetWindow.Should().Be(tyleziaWindow);
    }

    [Fact]
    public void ReturningToSameDofusWindowRaisesRealActivationOnlyOnce()
    {
        CurrentDofusClientsState state = new();
        DofusClientWindowKey key = Key(101, 1001);
        DofusWindowInfo first = Window(101, 1001,
            "Alice - Ecaflip - 3.7.12.12 - Release");
        List<DofusClientWindowKey> activations = [];
        state.GameActivated += window => activations.Add(window);

        state.Update([first], key);
        state.Update([first], key);
        activations.Should().Equal(key);

        state.Update([first]); // User switches to BestCrush to browse SQL.
        state.Update([first], key); // User returns to same Dofus.
        activations.Should().Equal(key, key);
    }

    [Fact]
    public void FastReturnToGameAfterBrowsingRestoresServerEvenIfPollingMissedAppFocus()
    {
        CurrentDofusClientsState state = new();
        DofusClientWindowKey key = Key(101, 1001);
        DofusWindowInfo window = Window(101, 1001,
            "Alice - Ecaflip - 3.7.12.12 - Release");
        int activations = 0;
        state.GameActivated += _ => activations++;

        state.Update([window], key);
        activations.Should().Be(1);

        // No background/null-foreground poll occurs between browsing
        // an SQL server in BestCrush and the user returning to Dofus.
        state.ExpectGameActivationAfterBrowsing();
        state.Update([window], key);
        activations.Should().Be(2);
        state.Update([window], key);
        activations.Should().Be(2, "only one restore is needed per browsing click");
    }

    [Fact]
    public void GameActivationOverridesPreviouslyTargetedOtherWindow()
    {
        CurrentDofusClientsState state = new();
        DofusWindowInfo first = Window(101, 1001,
            "Alice - Ecaflip - 3.7.12.12 - Release");
        DofusWindowInfo second = Window(102, 1002,
            "Alice - Iop - 3.7.12.12 - Release");
        state.Update([first, second], Key(101, 1001));
        state.SelectWindow(Key(102, 1002)).Should().BeTrue();
        state.SelectedClient!.Key.Should().Be(Key(102, 1002));

        state.Update([first, second]);
        state.Update([first, second], Key(101, 1001));
        state.SelectedClient!.Key.Should().Be(Key(101, 1001));
    }

    [Fact]
    public void UnknownClientCannotBeSelectedOrBecomeKnownFromTitleAlone()
    {
        CurrentDofusClientsState state = new();
        state.Update([Window(101, 1001, "Dofus")]);
        state.SelectedClient!.HasCharacterTitle.Should().BeFalse();
        state.SelectedClient!.DisplayName.Should().Be("Client Dofus");
        state.SelectWindow(Key(555, 9999)).Should().BeFalse();
        state.Clients.Should().ContainSingle();
    }

    [Fact]
    public void ClosingOneClientRevokesSelectionAndNeverReusesStaleWindow()
    {
        CurrentDofusClientsState state = new();
        DofusWindowInfo first = Window(101, 1001, "Alice - Ecaflip - 3.7.12.12 - Release");
        DofusWindowInfo second = Window(102, 1002, "Bob - Cra - 3.7.12.12 - Release");
        state.Update([first, second], Key(101, 1001));
        state.Update([second]);
        state.SelectedClient!.Key.Should().Be(Key(102, 1002));
        state.SelectWindow(Key(101, 1001)).Should().BeFalse();
        state.Update([]);
        state.SelectedClient.Should().BeNull();
        state.Clients.Should().BeEmpty();
    }

    [Fact]
    public void ReusedHwndWithDifferentPidIsNewClient()
    {
        CurrentDofusClientsState state = new();
        state.Update([Window(101, 1001, "Alice - Ecaflip - 3.7.12.12 - Release")]);
        state.Update([Window(101, 1002, "Bob - Cra - 3.7.12.12 - Release")]);
        state.SelectedClient!.Key.Should().Be(Key(101, 1002));
        state.SelectWindow(Key(101, 1001)).Should().BeFalse();
    }

    [Fact]
    public void PointerTargetUsesActualTopLevelWindowNotForegroundOrListOrder()
    {
        DofusWindowInfo a = Window(101, 1001, "A");
        DofusWindowInfo b = Window(102, 1002, "B");
        DofusWindowService.ResolvePointerTarget([a, b], new IntPtr(102),
            100, 100)!.ProcessId.Should().Be(1002);
        DofusWindowService.ResolvePointerTarget([b, a], new IntPtr(101),
            100, 100)!.ProcessId.Should().Be(1001);
    }

    [Fact]
    public void PointerWithoutOsHitProofNeverChoosesFallbackWindow()
    {
        DofusWindowInfo a = Window(101, 1001, "A");
        DofusWindowService.ResolvePointerTarget([a], IntPtr.Zero,
            10, 10).Should().BeNull();
        DofusWindowService.ResolvePointerTarget([a], new IntPtr(888),
            10, 10).Should().BeNull();
    }

    [Fact]
    public void PointerOutsideWindowCannotTargetItsEquipment()
    {
        DofusWindowInfo a = Window(101, 1001, "A");
        DofusWindowService.ResolvePointerTarget([a], new IntPtr(101),
            -1, 1).Should().BeNull();
        DofusWindowService.ResolvePointerTarget([a], new IntPtr(101),
            799, 599).Should().NotBeNull();
        DofusWindowService.ResolvePointerTarget([a], new IntPtr(101),
            800, 600).Should().BeNull();
    }

    [Fact]
    public void InvalidPidOrDuplicateHandleFailClosedForPointerTarget()
    {
        DofusWindowInfo a = Window(101, 1001, "A");
        DofusWindowInfo invalidPid = Window(101, 0, "No owner");
        DofusWindowService.ResolvePointerTarget([invalidPid],
            new IntPtr(101), 10, 10).Should().BeNull();
        DofusWindowService.ResolvePointerTarget([a, a],
            new IntPtr(101), 10, 10).Should().BeNull();
    }

    [Fact]
    public void NewBestCrushProcessStartsWithNoClientOrSelection()
    {
        CurrentDofusClientsState state = new();
        state.Update([Window(101, 1001, "Alice - Ecaflip - 3.7.12.12 - Release")]);
        CurrentDofusClientsState restarted = new();
        restarted.Clients.Should().BeEmpty();
        restarted.SelectedClient.Should().BeNull();
    }
}
