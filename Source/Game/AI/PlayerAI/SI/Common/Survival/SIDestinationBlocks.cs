using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace Game.AI;
// Gameplay cooldowns, not a path planner. Keep failure memory bounded after replacing navigation.
public sealed class SIDestinationBlocks
{
    public const long Duration=300000;
    readonly List<(int Map,Vector3 Point,long Until)> _items=new();
    public bool Contains(int map,Vector3 point,long now)=>_items.Any(x=>x.Map==map&&x.Until>now&&Math.Abs(x.Point.Z-point.Z)<4&&Vector2.Distance(new(x.Point.X,x.Point.Y),new(point.X,point.Y))<6);
    public void Block(int map,Vector3 point,long now){Prune(now);_items.RemoveAll(x=>x.Map==map&&Vector3.Distance(x.Point,point)<1);if(_items.Count>=256)_items.RemoveAt(0);_items.Add((map,point,now+Duration));}
    public void Prune(long now)=>_items.RemoveAll(x=>x.Until<=now);
    public void Unblock(int map,Vector3 point)=>_items.RemoveAll(x=>x.Map==map&&Vector3.Distance(x.Point,point)<6);
}
