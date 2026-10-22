using Microsoft.Azure.Kinect.BodyTracking;

// Keep hardware startup policy independently testable. Native libraries and the
// model are validated before this is called; only a GPU tracker failure retries.
internal static class TrackerStartup
{
    public static T Create<T>(WorkerConfig config, Func<TrackerProcessingMode,T> create,
        CancellationToken token, Action<string> log, out TrackerProcessingMode activeMode)
    {
        token.ThrowIfCancellationRequested();
        activeMode=config.ProcessingMode;
        log("Initializing body tracker: "+activeMode);
        try { return create(activeMode); }
        catch(Exception gpuError) when(config.AllowCpuFallback && activeMode!=TrackerProcessingMode.Cpu &&
            gpuError is not OperationCanceledException && !token.IsCancellationRequested)
        {
            log("GPU tracker initialization failed: "+gpuError+". Retrying CPU.");
            token.ThrowIfCancellationRequested();
            activeMode=TrackerProcessingMode.Cpu;
            log("Initializing body tracker: Cpu (fallback; tracking may be slower)");
            try { return create(activeMode); }
            catch(Exception cpuError) when(cpuError is not OperationCanceledException && !token.IsCancellationRequested)
            {
                throw new AggregateException("Both GPU and CPU body tracker initialization failed.",gpuError,cpuError);
            }
        }
    }
}
