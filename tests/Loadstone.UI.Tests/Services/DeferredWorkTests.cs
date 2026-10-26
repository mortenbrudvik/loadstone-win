using System.Windows.Threading;
using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class DeferredWorkTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The reason the helper exists. App's crash policy lives in DispatcherUnhandledException: log as
    /// fatal, give Windows its edge snapping back, let the process end. Deferred startup work has to
    /// fault into that handler and not into a task nobody holds. Dispatcher.BeginInvoke takes a
    /// Delegate, and an async lambda handed to it straight becomes a Func&lt;Task&gt; whose task is
    /// dropped on the floor: the fault then surfaces, if at all, from the finalizer as an
    /// unobserved-task event, logged as an error with the process carrying on.
    /// </summary>
    [WpfFact]
    public void Defer_WhenTheWorkFaultsAfterAnAwait_TheFaultReachesTheDispatcher()
    {
        var reached = RunToTheDispatcher(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("deferred work faulted");
        });

        reached.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("deferred work faulted");
    }

    /// <summary>A throw before the first await takes another path inside the async machinery and must land in the same place.</summary>
    [WpfFact]
    public void Defer_WhenTheWorkThrowsBeforeItsFirstAwait_TheFaultReachesTheDispatcher()
    {
        var reached = RunToTheDispatcher(() => throw new InvalidOperationException("threw synchronously"));

        reached.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("threw synchronously");
    }

    [WpfFact]
    public void Defer_RunsTheWork_OnceTheDispatcherPumps()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ran = false;

        dispatcher.Defer(async () =>
        {
            await Task.Yield();
            ran = true;
        });

        ran.ShouldBeFalse("the work is queued, not run inline");
        PumpUntil(() => ran);
        ran.ShouldBeTrue();
    }

    /// <summary>Queues <paramref name="work"/> and pumps until its fault reaches the dispatcher's handler, or the pump gives up; null when it never arrived.</summary>
    private static Exception? RunToTheDispatcher(Func<Task> work)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        Exception? reached = null;
        DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
        {
            reached = e.Exception;
            e.Handled = true;
        };

        dispatcher.UnhandledException += handler;
        try
        {
            dispatcher.Defer(work);
            PumpUntil(() => reached is not null);
        }
        finally
        {
            dispatcher.UnhandledException -= handler;
        }

        return reached;
    }

    /// <summary>
    /// Runs the dispatcher until <paramref name="condition"/> holds or <see cref="PumpTimeout"/> passes.
    /// A timer rather than a re-posted Background callback, so a condition that never comes true does
    /// not spin a core for the whole timeout.
    /// </summary>
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var deadline = Environment.TickCount64 + (long)PumpTimeout.TotalMilliseconds;
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Background,
            (_, _) =>
            {
                if (condition() || Environment.TickCount64 >= deadline)
                {
                    frame.Continue = false;
                }
            },
            Dispatcher.CurrentDispatcher);
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }
    }
}
