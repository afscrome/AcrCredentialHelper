namespace AlexCrome.Acr.CredentialHelper.Tests;

public class GlobalLockTests
{
    [Test]
    public async Task GlobalLockIsExclusiveUntilReleased()
    {
        var globalLock = new GlobalLock($"acr-test-{Guid.NewGuid():N}");
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = globalLock.RunAsync(async (waited, token) =>
        {
            entered.SetResult(waited);
            await release.Task.WaitAsync(token);
            return 42;
        });
        Task<bool> second;
        try
        {
            await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(10))).IsFalse();
            second = new GlobalLock(globalLock.Name)
                .RunAsync((waited, _) => Task.FromResult(waited));
            await Task.Delay(500);
            await Assert.That(second.IsCompleted).IsFalse();
        }
        finally
        {
            release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await Assert.That(await first).IsEqualTo(42);
        await Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(10))).IsTrue();
    }

    [Test]
    public async Task CancellingGlobalLockWaitDoesNotTakeOwnership()
    {
        var globalLock = new GlobalLock($"acr-test-{Guid.NewGuid():N}");
        await globalLock.RunAsync(async (_, _) =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var invoked = false;
            var pending = globalLock.RunAsync((_, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            }, cts.Token);
            await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10)))
                .Throws<OperationCanceledException>();
            await Assert.That(pending.IsCanceled).IsTrue();
            await Assert.That(invoked).IsFalse();
            return 0;
        });

        var waited = await globalLock.RunAsync((waited, _) => Task.FromResult(waited))
            .WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(waited).IsFalse();
    }

    [Test]
    public async Task PreCancelledGlobalLockDoesNotTakeOwnership()
    {
        var globalLock = new GlobalLock($"acr-test-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var invoked = false;
        await Assert.That(async () => await globalLock.RunAsync((_, _) =>
        {
            invoked = true;
            return Task.FromResult(0);
        }, cts.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(invoked).IsFalse();

        var waited = await globalLock.RunAsync((waited, _) => Task.FromResult(waited))
            .WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(waited).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GlobalLockIsReleasedWhenCallbackFails(bool asynchronously)
    {
        var globalLock = new GlobalLock($"acr-test-{Guid.NewGuid():N}");
        var failure = new InvalidOperationException("Callback failure");
        await Assert.That(async () => await globalLock.RunAsync<int>((_, _) =>
        {
            if (!asynchronously)
            {
                throw failure;
            }

            return FailAsync();
        })).Throws<InvalidOperationException>();

        var waited = await globalLock.RunAsync((waited, _) => Task.FromResult(waited))
            .WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(waited).IsFalse();

        async Task<int> FailAsync()
        {
            await Task.Yield();
            throw failure;
        }
    }

    [Test]
    public async Task GlobalLockIsReleasedWhenCallbackIsCancelled()
    {
        var globalLock = new GlobalLock($"acr-test-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource();
        var pending = globalLock.RunAsync(async (_, token) =>
        {
            await Assert.That(token).IsEqualTo(cts.Token);
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }, cts.Token);
        await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10)))
            .Throws<OperationCanceledException>();
        await Assert.That(pending.IsCanceled).IsTrue();

        var waited = await globalLock.RunAsync((waited, _) => Task.FromResult(waited))
            .WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(waited).IsFalse();
    }

    [Test]
    public async Task AbandonedGlobalLockIsAcquired()
    {
        var name = $"acr-test-{Guid.NewGuid():N}";
        // A thread that exits while owning a mutex abandons it, as a crashed process would.
        var crashed = new Thread(() => new Mutex(initiallyOwned: true, name));
        crashed.Start();
        crashed.Join();

        var waited = await new GlobalLock(name)
            .RunAsync((waited, _) => Task.FromResult(waited))
            .WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(waited).IsFalse();
    }
}
