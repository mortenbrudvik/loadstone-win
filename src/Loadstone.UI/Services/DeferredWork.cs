using System.Windows.Threading;

namespace Loadstone.UI.Services;

/// <summary>
/// Queues asynchronous work on a dispatcher and gives a fault the treatment a synchronous throw on
/// the UI thread gets: it reaches <see cref="Dispatcher.UnhandledException"/>, where App logs it as
/// fatal, restores the snap flags and lets the process end. The discarded-task idiom this replaces
/// carried the fault to <see cref="TaskScheduler.UnobservedTaskException"/>, which fires from the
/// finalizer - so a first-run Settings failure was logged only if the GC happened to run before
/// exit, and usually not at all.
/// </summary>
/// <remarks>
/// The <c>new Action(...)</c> is the whole fix and is load-bearing. Every <c>Dispatcher.BeginInvoke</c>
/// overload takes a <see cref="Delegate"/>, so an async lambda handed over bare converts to its
/// natural type, <c>Func&lt;Task&gt;</c>, and the dispatcher parks the returned task in the operation's
/// result and never looks at it: the discarded-task idiom again, which is what this shipped as until
/// the review of 2026-10-26 ran it and saw no handler fire. Wrapped as an <see cref="Action"/> the
/// lambda is an async void method, and an async void method posts its exception to the
/// synchronization context it started on, which inside a dispatcher operation is the dispatcher.
/// <c>DeferredWorkTests</c> pins both faulting paths, so a refactor to <c>InvokeAsync</c> or back to
/// the bare lambda fails there first.
/// </remarks>
internal static class DeferredWork
{
    public static void Defer(this Dispatcher dispatcher, Func<Task> work) =>
        dispatcher.BeginInvoke(new Action(async () => await work()));
}
