using System;

namespace Game.AI;

// Movement waits for native cast completion AND the resulting mounted aura.
// A rejected/interrupted cast eventually releases travel instead of retrying forever.
public sealed class SITravelMount
{
    public bool Pending { get; private set; }
    long _until;
    bool _wasMounted;
    long _retryAt;
    long? _outdoorsSince;

    public void ObserveMounted(long now,bool mounted,bool swimming=false)
    {
        if(_wasMounted&&!mounted)_retryAt=Math.Max(_retryAt,now+30000);
        _wasMounted=mounted;
        if(swimming)_outdoorsSince=null;
    }

    public bool CanAttempt(long now,bool outdoors,bool swimming)
    {
        if(!outdoors||swimming){_outdoorsSince=null;return false;}
        _outdoorsSince??=now;
        return now>=_retryAt&&now-_outdoorsSince.Value>=2000;
    }

    public void Cancel(long now)
    {Pending=false;_outdoorsSince=null;_retryAt=Math.Max(_retryAt,now+3000);}

    public void Begin(long now, int castMilliseconds)
    {
        Pending = true;
        _until = now + Math.Clamp(castMilliseconds + 2000L, 3000, 15000);
    }

    public bool Waiting(long now, bool mounted, bool mountedAura, bool castingMount, bool canWait)
    {
        if (!Pending) return false;
        if (!canWait || mounted && mountedAura) { Pending = false; return false; }
        if (castingMount) _until = Math.Max(_until, now + 2000);
        if (now < _until) return true;
        Pending = false;
        return false;
    }
}
