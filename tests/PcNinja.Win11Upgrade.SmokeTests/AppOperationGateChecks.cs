using System.ComponentModel;
using System.Security.Principal;
using PcNinja.Win11Upgrade.Core;

internal static class AppOperationGateChecks
{
    internal static async Task RunAsync()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new Exception("The fixture user SID is unavailable.");
        string Name() => @"Local\PcNinja.AppOperationGate.Tests." + Guid.NewGuid().ToString("N");
        var updateProbes = 0;
        var gate = new AppOperationGate.TestEndpoint(Name(), sid, () => updateProbes++);

        using (var outer = gate.EnterWindows())
        {
            await Task.Yield(); // Disposal/nesting must not depend on the acquisition thread.
            using (var inner = gate.EnterWindows())
            {
                ExpectBlocked(() => gate.EnterUpdate());
                ExpectBlocked(inner.TransferToHelper);
                ExpectBlocked(outer.TransferToHelper);
                ExpectBlocked(outer.AdoptHandoff);
                await Independent(() => ExpectBlocked(() => gate.EnterWindows()));
            }
            if (updateProbes != 0) throw new Exception("Windows entry unexpectedly probed real/update-only OS state.");
            await Independent(() => ExpectBlocked(() => gate.EnterUpdate()));
        }
        using (var update = gate.EnterUpdate())
        {
            if (updateProbes != 1) throw new Exception("Update entry did not run the injected preflight once.");
            ExpectBlocked(() => gate.EnterUpdate());
            ExpectBlocked(() => gate.EnterWindows());
            await Task.Yield();
            await Independent(() => ExpectBlocked(() => gate.EnterWindows()));
        }

        // An inherited async child owns its own reference. Disposing the root
        // cannot release the native gate while that child's nested scope is live.
        var root = gate.EnterWindows();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = Task.Run(async () =>
        {
            using var nested = gate.EnterWindows();
            ready.SetResult();
            await finish.Task;
        });
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        root.Dispose();
        root.Dispose(); // Exactly-once cleanup.
        try { await Independent(() => ExpectBlocked(() => gate.EnterUpdate())); }
        finally { finish.TrySetResult(); }
        await child.WaitAsync(TimeSpan.FromSeconds(5));
        using (gate.EnterWindows()) { }

        var failingProbe = new AppOperationGate.TestEndpoint(Name(), sid,
            () => throw new InvalidOperationException("injected pending Windows restart"));
        ExpectBlocked(() => failingProbe.EnterUpdate());
        using (failingProbe.EnterWindows()) { } // Failed preflight must release/close the acquired object.

        var deniedName = Name();
        var denied = new AppOperationGate.TestEndpoint(deniedName, sid, administratorProbe:
            () => throw new UnauthorizedAccessException("injected non-elevated token"));
        try
        {
            using var unexpected = denied.EnterWindows();
            throw new Exception("Non-administrator entry was accepted.");
        }
        catch (UnauthorizedAccessException) { }
        using (var first = new Semaphore(1, 1, deniedName, out var created))
            if (!created) throw new Exception("Denied entry created a native operation object before its permission check.");

        // An existing same-name object with the ordinary token's default DACL is
        // not silently trusted/resecured, even when the current user can open it.
        var weakName = Name();
        using (var weak = new Semaphore(1, 1, weakName))
        {
            var strict = new AppOperationGate.TestEndpoint(weakName, sid);
            try
            {
                using var unexpected = strict.EnterWindows();
                throw new Exception("An unexpected owner/group/DACL was trusted.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or Win32Exception) { }
            if (!weak.WaitOne(0)) throw new Exception("Rejecting weak gate security changed its semaphore count.");
            weak.Release();
        }

        // Simulated READY/GO only: helper has opened its handle BEFORE transfer;
        // parent disarms release BEFORE GO; helper adopts only AFTER GO.
        var transfer = new AppOperationGate.TestEndpoint(Name(), sid);
        var parent = transfer.EnterUpdate();
        var helper = await Independent(transfer.OpenHandoff);
        try
        {
            if (!helper.IsHandoff || helper.IsAdopted || parent.IsTransferred)
                throw new Exception("A prepared handoff already owned release responsibility.");
            parent.TransferToHelper();
            if (!parent.IsTransferred) throw new Exception("Transfer did not disarm the parent release.");
            ExpectBlocked(parent.TransferToHelper);
            parent.Dispose();
            await Independent(() => ExpectBlocked(() => transfer.EnterWindows()));
            helper.AdoptHandoff();
            if (!helper.IsAdopted) throw new Exception("GO adoption did not assign helper release responsibility.");
            ExpectBlocked(helper.AdoptHandoff);
            ExpectBlocked(helper.TransferToHelper);
            await Independent(() => ExpectBlocked(() => transfer.EnterUpdate()));
        }
        finally { parent.Dispose(); helper.Dispose(); }
        using (transfer.EnterWindows()) { }

        // GO failure: neither side releases an unadopted transferred lease.
        // Count 0 remains blocked until BOTH handles close, then the object dies.
        var failedGo = new AppOperationGate.TestEndpoint(Name(), sid);
        var failedParent = failedGo.EnterUpdate();
        var unadopted = await Independent(failedGo.OpenHandoff);
        try
        {
            failedParent.TransferToHelper();
            failedParent.Dispose();
            await Independent(() => ExpectBlocked(() => failedGo.EnterWindows()));
        }
        finally { failedParent.Dispose(); unadopted.Dispose(); }
        using (failedGo.EnterWindows()) { }

        // Cancellation before GO/transfer leaves normal release with the parent;
        // closing the helper's unadopted handle must not unlock Windows work.
        var beforeTransfer = new AppOperationGate.TestEndpoint(Name(), sid);
        using (var normalParent = beforeTransfer.EnterUpdate())
        {
            var waitingHelper = await Independent(beforeTransfer.OpenHandoff);
            waitingHelper.Dispose();
            await Independent(() => ExpectBlocked(() => beforeTransfer.EnterWindows()));
        }
        using (beforeTransfer.EnterWindows()) { }

        var absent = new AppOperationGate.TestEndpoint(Name(), sid);
        try
        {
            using var unexpected = absent.OpenHandoff();
            throw new Exception("A helper created an absent operation gate instead of opening an existing hold.");
        }
        catch (Win32Exception) { }
        Console.WriteLine("PASS: Secure unique-Local semaphore ownership/DACL, async Windows nesting/reference lifetime, cross-flow contention, update preflight and READY/GO release transfer/failure. No production Global gate, Setup/process/registry query, app replacement or restart.");
    }

    private static void ExpectBlocked(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Conflicting or invalid operation lease transition was accepted.");
    }

    private static Task Independent(Action action)
    {
        using (ExecutionContext.SuppressFlow()) return Task.Run(action);
    }

    private static Task<T> Independent<T>(Func<T> action)
    {
        using (ExecutionContext.SuppressFlow()) return Task.Run(action);
    }
}
