namespace AlexCrome.Acr.CredentialHelper.Auth;

/// <summary>
/// Cross-process lock that ensures only one helper process shows a sign-in prompt at a time.
/// </summary>
/// <remarks>
/// A named <see cref="Mutex"/> is thread-affine: it must be released by the thread that acquired it.
/// Async continuations resume on arbitrary thread-pool threads (ConfigureAwait cannot pin them in a
/// console app, which has no synchronization context), so the mutex is owned by a dedicated thread
/// that acquires it, waits for the handle to be disposed, then releases it.
/// </remarks>
public sealed class InteractiveAuthenticationLock
{
    public static InteractiveAuthenticationLock Default { get; internal set; } =
        new("Docker.Credential.Acr.InteractiveAuthentication");

    /// <summary>A lock that never blocks, for isolating tests from each other.</summary>
    internal static InteractiveAuthenticationLock None { get; } = new(null);

    internal InteractiveAuthenticationLock(string? name)
    {
        Name = name;
    }

    internal string? Name { get; }

    public Task<Handle> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Name is null)
        {
            return Task.FromResult(new Handle(null, null, waited: false));
        }

        var acquired = new TaskCompletionSource<Handle>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Thread(() => Own(Name, acquired, cancellationToken))
        {
            IsBackground = true,
            Name = "Interactive authentication lock",
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
            if (waited && !TryWait(mutex, Timeout.Infinite, cancellationToken))
            {
                acquired.TrySetCanceled(cancellationToken);
                return;
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
        try
        {
            return WaitHandle.WaitAny([mutex, cancellationToken.WaitHandle], millisecondsTimeout) == 0;
        }
        catch (AbandonedMutexException ex) when (ex.MutexIndex == 0)
        {
            // A previous holder exited without releasing; ownership has passed to this thread.
            return true;
        }
    }

    public sealed class Handle : IDisposable
    {
        private ManualResetEventSlim? _released;
        private readonly ManualResetEventSlim? _freed;

        internal Handle(ManualResetEventSlim? released, ManualResetEventSlim? freed, bool waited)
        {
            _released = released;
            _freed = freed;
            Waited = waited;
        }

        /// <summary>True if another process held the lock, so it may have just completed sign-in.</summary>
        public bool Waited { get; }

        /// <summary>Releases the lock, blocking until the owning thread has released the mutex.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, null) is { } released)
            {
                released.Set();
                _freed!.Wait();
            }
        }
    }
}
