namespace AlexCrome.Acr.CredentialHelper;

/// <summary>
/// Named cross-process lock for running asynchronous operations exclusively.
/// </summary>
/// <remarks>
/// A named <see cref="Mutex"/> is thread-affine: it must be released by the thread that acquired it.
/// Async continuations resume on arbitrary thread-pool threads (ConfigureAwait cannot pin them in a
/// console app, which has no synchronization context), so the mutex is owned by a dedicated thread
/// that acquires it, waits for the callback to complete, then releases it.
/// </remarks>
public sealed class GlobalLock
{
    internal GlobalLock(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    internal string Name { get; }

    /// <summary>Runs an asynchronous operation exclusively, releasing the lock even if it fails or is cancelled.</summary>
    /// <param name="action">The operation, receiving whether acquisition waited and the cancellation token.</param>
    public async Task<T> RunAsync<T>(Func<bool, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var handle = await AcquireAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await action(handle.Waited, cancellationToken);
    }

    private Task<Handle> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var acquired = new TaskCompletionSource<Handle>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Thread(() => Own(Name, acquired, cancellationToken))
        {
            IsBackground = true,
            Name = "Global lock",
        };
        owner.Start();
        return acquired.Task;
    }

    private static void Own(string name, TaskCompletionSource<Handle> acquired, CancellationToken cancellationToken)
    {
        try
        {
            using var mutex = new Mutex(initiallyOwned: false, name);
            var waited = !TryWait(mutex, 0, cancellationToken);
            // Unix cannot combine a named mutex and a cancellation handle in WaitAny.
            while (waited && !TryWait(mutex, 100, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    acquired.TrySetCanceled(cancellationToken);
                    return;
                }
            }

            var released = new ManualResetEventSlim();
            var freed = new ManualResetEventSlim();
            acquired.TrySetResult(new Handle(released, freed, waited));
            released.Wait();
            try
            {
                mutex.ReleaseMutex();
            }
            finally
            {
                freed.Set();
            }
        }
        catch (Exception ex)
        {
            acquired.TrySetException(ex);
        }
    }

    /// <returns>True if the mutex was acquired; false on timeout or cancellation.</returns>
    private static bool TryWait(Mutex mutex, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            return mutex.WaitOne(millisecondsTimeout);
        }
        catch (AbandonedMutexException)
        {
            // A previous holder exited without releasing; ownership has passed to this thread.
            return true;
        }
    }

    private sealed class Handle : IDisposable
    {
        private ManualResetEventSlim? _released;
        private readonly ManualResetEventSlim _freed;

        internal Handle(ManualResetEventSlim released, ManualResetEventSlim freed, bool waited)
        {
            _released = released;
            _freed = freed;
            Waited = waited;
        }

        /// <summary>True if the mutex was not immediately available.</summary>
        public bool Waited { get; }

        /// <summary>Releases the lock, blocking until the owning thread has released the mutex.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, null) is { } released)
            {
                released.Set();
                _freed.Wait();
            }
        }
    }
}
