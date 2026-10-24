using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Game.AI.SIMovement;

// Slack is the horizontal distance a destination may move to reach walkable mesh
// (corpses and NPCs on props the mesh does not model).
public readonly record struct SIPathPosition(int MapId, Vector3 Point, bool HeightKnown=true, float Slack=0)
{
    public bool Valid => MapId>=0 && float.IsFinite(Point.X)&&float.IsFinite(Point.Y)&&float.IsFinite(Point.Z);
}
public sealed record SIGroundPath(ImmutableArray<Vector3> Points,string Error=null)
{
    public const string CapacityError="SI navigation queue is full";
    public bool CapacityLimited=>Error==CapacityError;
    public bool Success=>Error==null&&Points.Length>=2;
    // The route reaches the (possibly slackened) destination rather than a horizon prefix.
    public bool Complete{get;init;}
    // The destination is not connected; the route ends at the closest reachable point.
    public bool Partial{get;init;}
    public static SIGroundPath Failed(string reason)=>new([],reason);
}

// Port of ForeverWorld/SI/Movement/SINavigationQueue. Only the mesh provider is
// replaced: this server already has managed Detour and needs no native bridge.
public delegate SIGroundPath SIPathBuild(SIPathPosition from,SIPathPosition to,Func<bool> cancelled,ImmutableArray<SITravelThreat> threats);
public sealed class SINavigationQueue : IAsyncDisposable
{
    sealed record Request(SIPathPosition From,SIPathPosition To,CancellationToken Cancel,
        TaskCompletionSource<SIGroundPath> Completion,ulong Seed,int Sequence,ImmutableArray<SITravelThreat> Threats);
    // Two bounded lanes retain continent/map locality in each private tile cache.
    // Idle workers may steal; one busy map can still use both workers. Wait mode
    // preserves admitted work, while TryWrite rejects admission without blocking a tick.
    readonly Channel<Request>[] _requests=Enumerable.Range(0,2).Select(_=>Channel.CreateBounded<Request>(new BoundedChannelOptions(64)
        {FullMode=BoundedChannelFullMode.Wait,AllowSynchronousContinuations=false,SingleReader=false,SingleWriter=false})).ToArray();
    // Binary wakeups cannot accumulate orphaned notifications after stealing.
    readonly SemaphoreSlim[] _wake=[new(0,1),new(0,1)];
    readonly int[] _busy=new int[2],_signalled=new int[2];
    void Signal(int worker)
    {
        if(Interlocked.Exchange(ref _signalled[worker],1)!=0)return;
        try{_wake[worker].Release();}
        catch(ObjectDisposedException){} // Concurrent shutdown drains/cancels admitted work.
    }
    readonly CancellationTokenSource _stop=new();
    readonly Task[] _workers;
    readonly SIWorldNavigation[] _navigation=new SIWorldNavigation[2];
    long _builds,_failures;
    public string Statistics
    {
        get
        {
            long loads=0,evictions=0,bytes=0;int tiles=0;
            // Approximate worker counters for diagnostics; never enumerate their meshes.
            foreach(var navigation in _navigation)if(navigation!=null)
            {var cache=navigation.CacheStatistics;loads+=cache.Loads;evictions+=cache.Evictions;tiles+=cache.Tiles;bytes+=cache.FileBytes;}
            return $"SI movement: paths={Interlocked.Read(ref _builds)} failures={Interlocked.Read(ref _failures)}; tile loads={loads} evictions={evictions} resident={tiles} source MiB={bytes/1048576}; {SITravelGraphs.Statistics}";
        }
    }
    public SINavigationQueue(string data):this(()=>new SIWorldNavigation(data).Find) {}
    public SINavigationQueue(Func<SIPathBuild> factory)
    {
        _workers=new Task[2];
        for(int i=0;i<2;++i)
        {
            int worker=i;
            _workers[i]=Task.Run(()=>{var build=factory();_navigation[worker]=build.Target as SIWorldNavigation;return Run(build,worker);});
        }
    }
    public Task<SIGroundPath> RequestPath(SIPathPosition from,SIPathPosition to,CancellationToken cancel,ulong seed=0,int sequence=0,ImmutableArray<SITravelThreat> threats=default)
    {
        var completion=new TaskCompletionSource<SIGroundPath>(TaskCreationOptions.RunContinuationsAsynchronously);
        // As in the reference, reject immediately rather than blocking a world tick.
        var request=new Request(from,to,cancel,completion,seed,sequence,threats.IsDefault?[]:threats);
        int lane=from.MapId&1;
        if(!_requests[lane].Writer.TryWrite(request))
        {
            lane=1-lane;
            if(!_requests[lane].Writer.TryWrite(request))
            {completion.SetResult(SIGroundPath.Failed(SIGroundPath.CapacityError));return completion.Task;}
        }
        Signal(lane);
        if(Volatile.Read(ref _busy[lane])!=0||_requests[lane].Reader.Count>1)Signal(1-lane);
        return completion.Task;
    }
    async Task Run(SIPathBuild find,int worker)
    {
        try
        {
            while(true)
            {
                await _wake[worker].WaitAsync(_stop.Token);
                Volatile.Write(ref _signalled[worker],0);
                Volatile.Write(ref _busy[worker],1);
                try
                {
                while(_requests[worker].Reader.TryRead(out var request)||
                    Volatile.Read(ref _busy[1-worker])!=0&&_requests[1-worker].Reader.TryRead(out request))
                {
                if(_requests[worker].Reader.Count>0)Signal(1-worker);
                if(request.Cancel.IsCancellationRequested){request.Completion.TrySetCanceled(request.Cancel);continue;}
                try
                {
                    bool Cancelled()=>request.Cancel.IsCancellationRequested||_stop.IsCancellationRequested;
                    Interlocked.Increment(ref _builds);
                    var path=find(request.From,request.To,Cancelled,request.Threats);
                    // Route quality is selected by the clean-ground filter. Do not
                    // replace it with a random shorter/longer anchor detour.
                    if(!path.Success)Interlocked.Increment(ref _failures);
                    if(Cancelled())request.Completion.TrySetCanceled();else request.Completion.TrySetResult(path);
                }
                catch(OperationCanceledException){request.Completion.TrySetCanceled();}
                catch(Exception error){Interlocked.Increment(ref _failures);request.Completion.TrySetResult(SIGroundPath.Failed(error.Message));}
                }
                }
                finally{Volatile.Write(ref _busy[worker],0);}
            }
        }
        catch(OperationCanceledException)when(_stop.IsCancellationRequested){}
    }
    static float Length(ImmutableArray<Vector3> points){float sum=0;for(int i=1;i<points.Length;++i)sum+=Vector3.Distance(points[i-1],points[i]);return sum;}
    public async ValueTask DisposeAsync()
    {
        foreach(var lane in _requests)lane.Writer.TryComplete();
        _stop.Cancel();await Task.WhenAll(_workers);
        foreach(var lane in _requests)while(lane.Reader.TryRead(out var request))request.Completion.TrySetCanceled();
        foreach(var wake in _wake)wake.Dispose();_stop.Dispose();
    }
}
public static class SIMovementRuntime
{
    static readonly object Gate=new();static SINavigationQueue _queue;
    // Called by every moving bot on every map tick; only creation takes the lock.
    public static SINavigationQueue Get(string data){var queue=Volatile.Read(ref _queue);if(queue!=null)return queue;lock(Gate)return _queue??=new(data);}
    public static string Statistics{get{lock(Gate)return _queue?.Statistics??"SI movement not initialized";}}
    public static void Shutdown(){SINavigationQueue queue;lock(Gate){queue=_queue;_queue=null;}if(queue!=null)queue.DisposeAsync().AsTask().GetAwaiter().GetResult();}
}
