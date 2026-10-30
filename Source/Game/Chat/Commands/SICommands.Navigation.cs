using Framework.Constants;
using Framework.IO;
using Game.AI;
using Game.AI.SIMovement;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Game.Chat.Commands;

partial class SICommands
{
    sealed record TravelMarker(ObjectGuid Guid, int Map, int Instance, int Node);
    static readonly ConditionalWeakTable<Player, List<TravelMarker>> TravelMarkers = new();
    sealed record TravelMarkerSettings(float Radius, int Limit);
    sealed class TravelMarkerStream
    {
        public TimeSpan Remaining;
        public int PhaseContext;
        public bool HasPhaseContext;
    }
    // Character identity keeps show mode enabled across relogs in this server
    // process. NPCs themselves are streamed only by the owning Player map update.
    static readonly ConcurrentDictionary<ObjectGuid,TravelMarkerSettings> TravelMarkerViews = new();
    static readonly ConditionalWeakTable<Player,TravelMarkerStream> TravelMarkerStreams = new();
    const int EnabledTravelMarkerEntry = 621; // Chicken, no scripts/services.
    const int BannedTravelMarkerEntry = 2442; // Cow: visibly different from an enabled point.

    // Real subcommands make info/ban/unban discoverable through normal command help.
    [CommandGroup("nav")]
    class NavigationCommands
    {
        [Command("", Permission)]
        [Command("help", Permission)]
        static bool Help(CommandHandler h, StringArguments a) => Navigation(h, "help", a);
        [Command("show", Permission)]
        static bool Show(CommandHandler h, StringArguments a) => Navigation(h, "show", a);
        [Command("hide", Permission)]
        static bool Hide(CommandHandler h, StringArguments a) => Navigation(h, "hide", a);
        [Command("info", Permission)]
        static bool Info(CommandHandler h, StringArguments a) => Navigation(h, "info", a);
        [Command("ban", Permission)]
        [Command("blacklist", Permission)]
        [Command("remove", Permission)]
        static bool Ban(CommandHandler h, StringArguments a) => Navigation(h, "ban", a);
        [Command("unban", Permission)]
        [Command("restore", Permission)]
        static bool Unban(CommandHandler h, StringArguments a) => Navigation(h, "unban", a);
        [Command("move", Permission)]
        static bool Move(CommandHandler h, StringArguments a) => Navigation(h, "move", a);
        [Command("list", Permission)]
        static bool List(CommandHandler h, StringArguments a) => Navigation(h, "list", a);
        [Command("blacklisted", Permission)]
        static bool Blacklisted(CommandHandler h, StringArguments a) => Navigation(h, "blacklisted", a);
        [Command("export", Permission)]
        static bool Export(CommandHandler h, StringArguments a) => Navigation(h, "export", a);
    }

    static void HideTravelMarkers(Player player)
    {
        if (!TravelMarkers.TryGetValue(player, out var markers)) return;
        lock(markers)
        {
            foreach (var marker in markers)
                if (marker.Map == player.GetMapId() && marker.Instance == player.GetInstanceId())
                    ObjectAccessor.GetCreature(player, marker.Guid)?.ToTempSummon()?.UnSummon();
            markers.Clear();
        }
    }

    internal static void ClearTravelMarkers(Player player)
    {
        HideTravelMarkers(player);
        if(TravelMarkerStreams.TryGetValue(player,out var stream))
        {stream.Remaining=TimeSpan.Zero;stream.HasPhaseContext=false;}
    }

    static int TravelMarkerPhaseContext(Player player)
    {
        var phase=player.GetPhaseShift();
        int hash=HashCode.Combine(player.GetMapId(),player.GetInstanceId(),phase.Flags,phase.PersonalGuid);
        foreach(var entry in phase.Phases)hash^=HashCode.Combine(entry.Key,entry.Value.Flags);
        foreach(var entry in phase.VisibleMapIds)hash^=entry.Key.GetHashCode();
        return hash;
    }

    internal static void UpdateTravelMarkers(Player player,TimeSpan diff)
    {
        if(!player.IsInWorld || player.GetSession().IsServerControlled)return;
        if(!TravelMarkerViews.TryGetValue(player.GetGUID(),out var settings) || !player.IsGameMaster() || player.IsBeingTeleported())
        {
            if(TravelMarkers.TryGetValue(player,out var old)&&old.Count>0)ClearTravelMarkers(player);
            return;
        }
        var stream=TravelMarkerStreams.GetOrCreateValue(player);
        stream.Remaining-=diff;
        if(stream.Remaining>TimeSpan.Zero)return;
        stream.Remaining=TimeSpan.FromSeconds(1);
        try
        {
            int phase=TravelMarkerPhaseContext(player);
            if(stream.HasPhaseContext && stream.PhaseContext!=phase)HideTravelMarkers(player);
            stream.PhaseContext=phase;stream.HasPhaseContext=true;
            var graph=SITravelGraphs.Get(Global.WorldMgr.GetDataPath(),player.GetMapId());
            if(graph==null)return; // Keep show mode on while the background graph loads.
            if(Global.ObjectMgr.GetCreatureTemplate(EnabledTravelMarkerEntry)==null || Global.ObjectMgr.GetCreatureTemplate(BannedTravelMarkerEntry)==null)
                throw new InvalidOperationException("SI navigation marker templates 621/2442 are missing.");
            SyncTravelMarkers(player,graph,settings);
        }
        catch(Exception error)
        {
            stream.Remaining=TimeSpan.FromSeconds(10);
            Log.outError(LogFilter.SI,$"SI navigation marker stream for {player.GetGUID()}: {error.Message}");
        }
    }

    static void SyncTravelMarkers(Player player,SITravelGraph graph,TravelMarkerSettings settings,
        Func<int,Vector3,TempSummon> create=null)
    {
        var here=new Vector3(player.GetPositionX(),player.GetPositionY(),player.GetPositionZ());
        var desired=graph.NearbyNodes(here,settings.Radius,settings.Limit).ToHashSet();
        var markers=TravelMarkers.GetOrCreateValue(player);
        lock(markers)SyncTravelMarkersLocked(player,graph,desired,markers,create);
    }

    static void SyncTravelMarkersLocked(Player player,SITravelGraph graph,HashSet<int> desired,List<TravelMarker> markers,
        Func<int,Vector3,TempSummon> create)
    {
        for(int i=markers.Count-1;i>=0;--i)
        {
            var marker=markers[i];
            var creature=ObjectAccessor.GetCreature(player,marker.Guid);
            if(marker.Map!=player.GetMapId() || marker.Instance!=player.GetInstanceId() ||
                !desired.Contains(marker.Node) || creature?.IsInWorld!=true || !creature.IsAlive())
            {
                if(marker.Map==player.GetMapId() && marker.Instance==player.GetInstanceId())creature?.ToTempSummon()?.UnSummon();
                markers.RemoveAt(i);
            }
        }
        var existing=markers.Select(m=>m.Node).ToHashSet();
        foreach(int node in desired)
        {
            if(existing.Contains(node))
            {
                RefreshTravelMarkers(player,graph,node,moved:true);
                continue;
            }
            var position=graph.Position(node);
            int entry=graph.IsBlacklisted(node)?BannedTravelMarkerEntry:EnabledTravelMarkerEntry;
            var summon=create!=null?create(node,position):player.GetMap().SummonCreature(entry,new Position(position),
                privateObjectOwner:player.GetGUID(),initializeBeforeAdd:marker=>
                {
                    // Static road points must not become passengers of a GM's transport.
                    PhasingHandler.InheritPhaseShift(marker,player);
                    ConfigureTravelMarker(marker);
                });
            if(summon==null)continue;
            // AddToWorld initializes native creature AI; retain the passive debug AI.
            if(create==null)ConfigureTravelMarker(summon);
            markers.Add(new(summon.GetGUID(),player.GetMapId(),player.GetInstanceId(),node));
        }
    }

    static void ConfigureTravelMarker(Creature marker)
    {
        if(marker is TempSummon summon)summon.SetTempSummonType(TempSummonType.ManualDespawn);
        if (marker.GetAI() is not NullCreatureAI) marker.SetAI(new NullCreatureAI(marker));
        marker.SetReactState(ReactStates.Passive);
        marker.SetObjectScale(marker.GetEntry()==BannedTravelMarkerEntry?4f:6.5f);
        marker.IsPrivateGMMarker=true;
        marker.m_serverSideVisibility.SetValue(ServerSideVisibilityType.GM,AccountTypes.Moderator);
        marker.SetCanMelee(false);
        marker.SetImmuneToAll(true);
        marker.SetUnitFlag(UnitFlags.NonAttackable);
        marker.RemoveUnitFlag(UnitFlags.Uninteractible); // Selectable for info/ban/unban.
        marker.ReplaceAllNpcFlags(NPCFlags1.None);
        marker.ReplaceAllNpcFlags2(NPCFlags2.None);
        marker.SetFaction(35);
        marker.SetDefaultMovementType(MovementGeneratorType.Idle);
        marker.GetMotionMaster().Clear();
        marker.GetMotionMaster().MoveIdle();
        marker.SetControlled(true, UnitState.Root);
    }

    static TravelMarker SelectedTravelMarker(Player player)
    {
        if (!player.IsGameMaster() || !TravelMarkers.TryGetValue(player, out var markers)) return null;
        TravelMarker selected;
        lock(markers)selected = markers.FirstOrDefault(m => m.Guid == player.GetTarget() &&
            m.Map == player.GetMapId() && m.Instance == player.GetInstanceId());
        // A real chicken/cow, expired marker or somebody else's marker must never ban a nearby point.
        return selected != null && ObjectAccessor.GetCreature(player, selected.Guid) is { IsInWorld: true } creature &&
            creature.GetPrivateObjectOwner() == player.GetGUID() ? selected : null;
    }

    static void RefreshTravelMarkers(Player player, SITravelGraph graph, int node, bool moved=false)
    {
        int entry = graph.IsBlacklisted(node) ? BannedTravelMarkerEntry : EnabledTravelMarkerEntry;
        if(!TravelMarkers.TryGetValue(player,out var markers))return;
        // Each operator's map update refreshes its own view. Never enumerate a
        // different map thread's marker list while it is streaming in/out.
        lock(markers)
            foreach (var marker in markers)
                if (marker.Map == graph.Map && marker.Instance == player.GetInstanceId() && marker.Node == node &&
                    ObjectAccessor.GetCreature(player, marker.Guid) is { IsInWorld: true } creature)
                {
                    // Update in place so the operator's selection remains on this point.
                    if (creature.GetEntry()!=entry && creature.UpdateEntry(entry)) ConfigureTravelMarker(creature);
                    var point=graph.Position(node);
                    if(moved && creature.GetExactDist(point.X,point.Y,point.Z)>.01f)
                        creature.NearTeleportTo(point.X,point.Y,point.Z,creature.GetOrientation());
                }
    }

    static bool Navigation(CommandHandler h, string action, StringArguments arguments) => Run(h, () =>
    {
        string[] words = (arguments.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (action == "help")
        {
            h.SendSysMessage(".si nav show [radius=250, max=1000] [limit=100, max=200]: enable nearby NPC marker streaming as you travel. Chickens = enabled; cows = BLACKLISTED. GM mode (.gm on) is required to see them. No time limit; .si nav hide turns streaming off.");
            h.SendSysMessage("Select a marker: .si nav info shows its point; .si nav ban blacklists it; .si nav unban restores it; .si nav move moves it to your position. .si nav hide removes your markers.");
            h.SendSysMessage(".si nav info with no marker shows graph/storage information. Optional [ID|nearest] works with info/ban/unban/move; IDs belong to your current map. blacklist/remove and restore remain aliases.");
            h.SendSysMessage(".si nav list [radius] [limit] and .si nav blacklisted print point details; .si nav export writes every point to JSON. Bans and moves are permanent navigation edits, saved separately from generated caches; active SI routes replan.");
            return;
        }
        Player player = h.GetSession()?.GetPlayer() ?? throw new ArgumentException("Use this command in game.");
        if (action == "hide")
        {
            if (words.Length != 0) throw new ArgumentException("Use .si nav hide without arguments.");
            TravelMarkerViews.TryRemove(player.GetGUID(),out _);
            ClearTravelMarkers(player); h.SendSysMessage("SI travel marker streaming off; markers hidden."); return;
        }
        if(action=="show")
        {
            float radius=250;int limit=100;
            if(words.Length>2 || words.Length>0&&!float.TryParse(words[0],NumberStyles.Float,CultureInfo.InvariantCulture,out radius) ||
                words.Length>1&&!int.TryParse(words[1],out limit))throw new ArgumentException("Use .si nav show [radius] [limit], with numeric arguments.");
            if(!float.IsFinite(radius)||radius is <1 or >1000||limit is <1 or >200)
                throw new ArgumentException("Use a radius of 1-1000 yards and a marker limit of 1-200.");
            TravelMarkerViews[player.GetGUID()]=new(radius,limit);
            TravelMarkerStreams.GetOrCreateValue(player).Remaining=TimeSpan.Zero;
            h.SendSysMessage($"SI navigation marker streaming ON: nearby points load as you travel, across maps, within {radius:F0} yards (up to {limit} nearby NPCs). Chickens = enabled (6.5x); cows = BLACKLISTED (4x). No timer; .si nav hide turns it off. .gm on is required; .gm off hides markers and .gm on resumes them. Graph loading is automatic.");
            return;
        }
        var graph = SITravelGraphs.Get(Global.WorldMgr.GetDataPath(), player.GetMapId());
        if (action == "info" && words.Length == 0)
        {
            h.SendSysMessage(graph == null ? $"Map {player.GetMapId()}: road graph loading/building (small instance maps have no road graph)." :
                $"Map {graph.Map}: {graph.NodeCount} road points, {graph.EdgeCount} links, {graph.BlacklistedNodes.Count()} blacklisted points; revision {graph.Revision}.");
            h.SendSysMessage("Points are sampled from extracted mmaps about 133 yards apart, retaining separate floors, and linked by verified navmesh paths. Chickens = enabled; cows = BLACKLISTED.");
            h.SendSysMessage($"Graph: {SITravelGraphs.GraphFile(Global.WorldMgr.GetDataPath(), player.GetMapId())}. Blacklist: {SITravelBlacklist.FileName(Global.WorldMgr.GetDataPath(), player.GetMapId())}");
            h.SendSysMessage($"Moved points: {SITravelPointMoves.FileName(Global.WorldMgr.GetDataPath(), player.GetMapId())}. Bans and moves survive server restarts and graph regeneration; keep these edit files with your server backups.");
            h.SendSysMessage(TravelMarkerViews.ContainsKey(player.GetGUID())?"Marker streaming: ON (visible only to you with .gm on; .si nav hide turns it off).":"Marker streaming: OFF (.si nav show enables nearby markers as you travel).");
            if (graph == null || SelectedTravelMarker(player) == null) return;
        }
        if (graph == null)
        {
            h.SendSysMessage("This map's road graph is not ready. Loading/building has been requested; retry shortly. Small instance maps have no road graph.");
            return;
        }
        Vector3 here = new(player.GetPositionX(), player.GetPositionY(), player.GetPositionZ());
        if (action == "export")
        {
            if (words.Length != 0) throw new ArgumentException("Use .si nav export without arguments.");
            string file = Path.Combine(Global.WorldMgr.GetDataPath(), "navigation", $"si-travel-{graph.Map:D4}.points.json");
            var points = Enumerable.Range(0, graph.NodeCount).Select(id =>
            {
                var point = graph.Position(id);
                var source=graph.SourcePosition(id);
                return new { Id = id, point.X, point.Y, point.Z, Links = graph.Degree(id), Blacklisted = graph.IsBlacklisted(id), Moved=graph.IsMoved(id), SourceX=source.X, SourceY=source.Y, SourceZ=source.Z };
            });
            File.WriteAllText(file, JsonSerializer.Serialize(new { Map = graph.Map, Points = points }, new JsonSerializerOptions { WriteIndented = true }));
            h.SendSysMessage($"Exported all {graph.NodeCount} road points on map {graph.Map} to {file}");
            return;
        }
        void Describe(int id)
        {
            var p = graph.Position(id);
            h.SendSysMessage(FormattableString.Invariant($"SI road point {id}: map {graph.Map}, {p.X:F2} {p.Y:F2} {p.Z:F2}, {Vector3.Distance(here, p):F1} yd, {graph.Degree(id)} links, {(graph.IsBlacklisted(id) ? "BLACKLISTED" : "enabled")}."));
            if(graph.IsMoved(id))
            {
                var original=graph.SourcePosition(id);
                h.SendSysMessage(FormattableString.Invariant($"Permanently moved from {original.X:F2} {original.Y:F2} {original.Z:F2}; routing uses the current coordinates shown above."));
            }
        }
        if (action == "list")
        {
            float radius = 250; int limit = 100;
            if (words.Length > 2 || words.Length > 0 && !float.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                words.Length > 1 && !int.TryParse(words[1], out limit))
                throw new ArgumentException("Use .si nav show|list [radius] [limit], with numeric arguments.");
            if (!float.IsFinite(radius) || radius is < 1 or > 1000 || limit is < 1 or > 200)
                throw new ArgumentException("Use a radius of 1-1000 yards and a marker limit of 1-200.");
            var all = graph.NearbyNodes(here, radius, int.MaxValue).ToArray();
            var nearby = all.Take(limit).ToArray();
            foreach (int id in nearby) Describe(id);
            h.SendSysMessage($"Map {graph.Map}: {all.Length} points within {radius:F0} yards; {nearby.Length} listed.");
            return;
        }
        if (action == "blacklisted")
        {
            if (words.Length != 0) throw new ArgumentException("Use .si nav blacklisted without arguments.");
            var blocked = graph.BlacklistedNodes.ToArray();
            foreach (int id in blocked.Take(100)) Describe(id);
            h.SendSysMessage($"{blocked.Length} blacklisted road points on map {graph.Map}; up to 100 listed. File: {SITravelBlacklist.FileName(Global.WorldMgr.GetDataPath(), graph.Map)}");
            return;
        }
        int node;
        if (words.Length > 1) throw new ArgumentException("Specify one point ID or nearest; IDs always belong to your current map.");
        if (words.Length == 0)
            node = SelectedTravelMarker(player)?.Node ?? throw new ArgumentException("Select one of your .si nav show NPC markers, then use .si nav " + action + ". No point was changed.");
        else if (words[0].Equals("nearest", StringComparison.OrdinalIgnoreCase))
        {
            var nearest = graph.NearbyNodes(here, 200, 1).ToArray();
            if (nearest.Length == 0) throw new ArgumentException("No road point within 200 yards; use .si nav show or specify its ID.");
            node = nearest[0];
        }
        else if (!int.TryParse(words[0], out node) || (uint)node >= graph.NodeCount)
            throw new ArgumentException($"Point ID must be 0-{graph.NodeCount - 1} on map {graph.Map}.");
        if(action=="move")
        {
            if(player.IsInFlight()||player.GetTransport()!=null)throw new ArgumentException("Stand on ordinary reachable ground before moving a road point.");
            var old=graph;
            graph=SITravelGraphs.Move(Global.WorldMgr.GetDataPath(),graph,node,here);
            RefreshTravelMarkers(player,graph,node,moved:true);
            h.SendSysMessage(graph==old?"The road point is already at your position.":"Road point moved to your position and saved permanently. Nearby walking links were recalculated; active SI routes will replan. Its blacklist state is unchanged.");
        }
        else if (action != "info")
        {
            bool blocked = action == "ban";
            bool changed = graph.SetBlacklisted(node, blocked);
            RefreshTravelMarkers(player, graph, node);
            h.SendSysMessage(changed ? $"Road point {node} {(blocked ? "blacklisted" : "restored")}. Saved; active SI routes will replan." : "That point already has the requested state.");
        }
        Describe(node);
    });
}
