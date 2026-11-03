using Companion.Api.Endpoints;
using Companion.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Tests;

/// <summary>The command channel to the bridge: delivery, expiry, results, and "is a bridge there".</summary>
public class BridgeCommandQueueTests
{
    private readonly ManualClock _clock = new(Fakes.Start);
    private readonly BridgeCommandQueue _queue;

    public BridgeCommandQueueTests() => _queue = new BridgeCommandQueue(NullLogger<BridgeCommandQueue>.Instance, _clock);

    private LaunchCommand Enqueue(int appId = 220, string title = "Half-Life 2")
    {
        var queued = _queue.Enqueue(appId, title);
        Assert.NotNull(queued.Command);
        return queued.Command;
    }

    [Fact]
    public void No_bridge_is_connected_until_it_polls()
    {
        Assert.False(_queue.IsBridgeConnected);
        Assert.Null(_queue.LastBridgeContactAt);

        _queue.ClaimOpen();

        Assert.True(_queue.IsBridgeConnected);
        Assert.Equal(Fakes.Start, _queue.LastBridgeContactAt);
    }

    [Fact]
    public void A_bridge_counts_as_gone_when_it_stops_polling()
    {
        _queue.RecordBridgeContact();
        _clock.Advance(BridgeCommandQueue.ConnectedWindow - TimeSpan.FromSeconds(1));
        Assert.True(_queue.IsBridgeConnected);

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(_queue.IsBridgeConnected);
    }

    [Fact]
    public void A_new_command_is_pending_and_becomes_delivered_when_the_bridge_asks()
    {
        var command = Enqueue();
        Assert.Equal(LaunchState.Pending, command.State);
        Assert.Equal(220, command.AppId);

        var claimed = _queue.ClaimOpen();

        var delivered = Assert.Single(claimed);
        Assert.Equal(command.Id, delivered.Id);
        Assert.Equal(LaunchState.Delivered, delivered.State);
        Assert.Equal(LaunchState.Delivered, _queue.Find(command.Id)!.State);
    }

    [Fact]
    public void A_delivered_command_is_offered_again_until_the_bridge_reports_a_result()
    {
        // The HTTP answer may get lost: the bridge must be able to get the command again (it runs each id once itself)
        var command = Enqueue();
        Assert.Single(_queue.ClaimOpen());
        _clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(command.Id, Assert.Single(_queue.ClaimOpen()).Id);

        Assert.True(_queue.ApplyResult(command.Id, success: true, reason: null, detail: null));
        Assert.Empty(_queue.ClaimOpen());
        Assert.Equal(LaunchState.Done, _queue.Find(command.Id)!.State);
    }

    [Fact]
    public void Asking_twice_for_the_same_game_returns_the_open_command()
    {
        var first = _queue.Enqueue(220, "Half-Life 2");
        var second = _queue.Enqueue(220, "Half-Life 2");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Command!.Id, second.Command!.Id);
        Assert.Single(_queue.ClaimOpen());
    }

    [Fact]
    public void A_finished_game_can_be_started_again()
    {
        var first = Enqueue();
        _queue.ClaimOpen();
        _queue.ApplyResult(first.Id, success: true, null, null);

        var again = _queue.Enqueue(220, "Half-Life 2");

        Assert.True(again.Created);
        Assert.NotEqual(first.Id, again.Command!.Id);
    }

    [Fact]
    public void A_command_nobody_picked_up_expires_and_is_never_delivered_late()
    {
        var command = Enqueue();

        _clock.Advance(BridgeCommandQueue.CommandLifetime + TimeSpan.FromSeconds(1));

        Assert.Empty(_queue.ClaimOpen());
        var expired = _queue.Find(command.Id)!;
        Assert.Equal(LaunchState.Expired, expired.State);
        Assert.Null(expired.DeliveredAt);
    }

    [Fact]
    public void A_command_the_bridge_took_but_never_confirmed_expires_too()
    {
        var command = Enqueue();
        _queue.ClaimOpen();

        _clock.Advance(BridgeCommandQueue.ConfirmationTimeout + TimeSpan.FromSeconds(1));

        Assert.Empty(_queue.ClaimOpen());
        var expired = _queue.Find(command.Id)!;
        Assert.Equal(LaunchState.Expired, expired.State);
        Assert.NotNull(expired.DeliveredAt);
    }

    [Fact]
    public void A_late_report_for_a_delivered_command_is_still_taken()
    {
        // The PC did start the game, only the confirmation was slow: the truth wins over "expired"
        var command = Enqueue();
        _queue.ClaimOpen();
        _clock.Advance(BridgeCommandQueue.ConfirmationTimeout + TimeSpan.FromSeconds(1));
        Assert.Equal(LaunchState.Expired, _queue.Find(command.Id)!.State);

        Assert.True(_queue.ApplyResult(command.Id, success: true, null, null));
        Assert.Equal(LaunchState.Done, _queue.Find(command.Id)!.State);
    }

    [Fact]
    public void A_report_for_a_command_that_was_never_delivered_is_ignored()
    {
        var command = Enqueue();

        Assert.False(_queue.ApplyResult(command.Id, success: true, null, null));
        Assert.Equal(LaunchState.Pending, _queue.Find(command.Id)!.State);
    }

    [Fact]
    public void A_report_for_an_unknown_id_is_ignored()
    {
        Assert.False(_queue.ApplyResult(Guid.NewGuid(), success: true, null, null));
    }

    [Fact]
    public void A_second_report_does_not_change_a_finished_command()
    {
        var command = Enqueue();
        _queue.ClaimOpen();
        Assert.True(_queue.ApplyResult(command.Id, success: false, LaunchFailureReason.SteamNotFound, "no handler"));

        Assert.False(_queue.ApplyResult(command.Id, success: true, null, null));
        Assert.Equal(LaunchState.Failed, _queue.Find(command.Id)!.State);
    }

    [Theory]
    [InlineData("steam-not-found", "steam-not-found")]
    [InlineData("start-failed", "start-failed")]
    [InlineData("something-new", "start-failed")]
    [InlineData(null, "start-failed")]
    public void A_failure_keeps_only_reasons_the_server_knows(string? reported, string expected)
    {
        var command = Enqueue();
        _queue.ClaimOpen();

        _queue.ApplyResult(command.Id, success: false, reported, "detail");

        var failed = _queue.Find(command.Id)!;
        Assert.Equal(LaunchState.Failed, failed.State);
        Assert.Equal(expected, failed.FailureReason);
        Assert.Equal("detail", failed.FailureDetail);
    }

    [Fact]
    public void Text_from_outside_is_cleaned_and_bounded()
    {
        var command = _queue.Enqueue(220, "Half\r\nLife\t" + new string('x', 500)).Command!;
        _queue.ClaimOpen();
        _queue.ApplyResult(command.Id, success: false, null, "bad\u0007thing" + new string('y', 500));

        var failed = _queue.Find(command.Id)!;
        Assert.DoesNotContain(failed.GameName.ToCharArray(), c => char.IsControl(c));
        Assert.True(failed.GameName.Length <= 120);
        Assert.StartsWith("HalfLife", failed.GameName);
        Assert.DoesNotContain(failed.FailureDetail!.ToCharArray(), c => char.IsControl(c));
        Assert.True(failed.FailureDetail!.Length <= 200);
    }

    [Fact]
    public void The_number_of_open_commands_is_bounded()
    {
        for (var appId = 1; appId <= BridgeCommandQueue.MaxOpen; appId++)
        {
            Assert.NotNull(_queue.Enqueue(appId, "Game " + appId).Command);
        }

        var overflow = _queue.Enqueue(999, "One too many");
        Assert.Null(overflow.Command);

        // A game that is already waiting is still answered with its command
        Assert.False(_queue.Enqueue(1, "Game 1").Created);
        Assert.NotNull(_queue.Enqueue(1, "Game 1").Command);
    }

    [Fact]
    public void Finished_commands_are_forgotten_after_a_while()
    {
        var command = Enqueue();
        _queue.ClaimOpen();
        _queue.ApplyResult(command.Id, success: true, null, null);
        Assert.NotNull(_queue.Find(command.Id));

        _clock.Advance(BridgeCommandQueue.KeepFinished + TimeSpan.FromSeconds(1));

        Assert.Null(_queue.Find(command.Id));
    }

    [Fact]
    public void The_remaining_time_counts_down_from_the_limit_of_the_current_stage()
    {
        var command = Enqueue();
        Assert.Equal(30, _queue.RemainingSeconds(command));

        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(20, _queue.RemainingSeconds(command));

        var delivered = _queue.ClaimOpen().Single();
        Assert.Equal(30, _queue.RemainingSeconds(delivered));

        _queue.ApplyResult(delivered.Id, success: true, null, null);
        Assert.Equal(0, _queue.RemainingSeconds(_queue.Find(delivered.Id)!));
    }
}

public class SteamRunUrlTests
{
    [Theory]
    [InlineData(220, "steam://run/220")]
    [InlineData(1245620, "steam://run/1245620")]
    [InlineData(int.MaxValue, "steam://run/2147483647")]
    public void The_address_is_built_from_the_number_alone(int appId, string expected) =>
        Assert.Equal(expected, LaunchEndpoints.SteamRunUrl(appId));
}
