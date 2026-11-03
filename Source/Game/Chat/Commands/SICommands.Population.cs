using Framework.Constants;
using Framework.Database;
using Framework.IO;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Game.Chat.Commands;

partial class SICommands
{
    internal static Team CreationTeam(string value) => value.ToLowerInvariant() switch
    {
        "horde" => Team.Horde,
        "alliance" => Team.Alliance,
        _ => throw new ArgumentException("Choose horde or alliance.")
    };

    internal static Race CreationRace(Team team,string value)
    {
        string token=value.Replace("_","").Replace("-","").Replace(" ","");
        if(token.Equals("skyborn",StringComparison.OrdinalIgnoreCase)||token.Equals("skyborne",StringComparison.OrdinalIgnoreCase))
            return team==Team.Alliance?Race.SkyborneAlliance:Race.SkyborneHorde;
        if(!Enum.TryParse(token,true,out Race race)||!RandomCreationRaces(team).Contains(race))
            throw new ArgumentException($"Choose a {team} race: {string.Join(", ",RandomCreationRaces(team).Select(CreationRaceName))}.");
        return race;
    }

    internal static Class CreationClass(string value)
    {
        if(!Enum.TryParse(value,true,out Class cls)||!Enum.IsDefined(cls)||cls==Class.None)
            throw new ArgumentException("Unknown character class: "+value);
        return cls;
    }

    internal static (Race Race,Class Class) NamedCreationOptions(IReadOnlyList<string> words)
    {
        if(words.Count==4)return (CreationRace(CreationTeam(words[1]),words[2]),CreationClass(words[3]));
        // Keep the previous explicit race/class form working as well.
        if(words.Count==3&&Enum.TryParse(words[1],true,out Race legacy)&&Enum.IsDefined(legacy)&&legacy!=Race.None)
            return (legacy,CreationClass(words[2]));
        throw new ArgumentException("Use .si create \"first surname\" <horde|alliance> <race> <class>.");
    }

    internal static (Team Team,int Count,Race? Race,Class? Class) RandomCreationRequest(IReadOnlyList<string> words)
    {
        if(words.Count==4)
        {
            Team team=CreationTeam(words[1]);
            return (team,1,CreationRace(team,words[2]),CreationClass(words[3]));
        }
        var legacy=RandomCreationOptions(words);
        return (legacy.Team,legacy.Count,null,null);
    }

    internal static string CreationRaceName(Race race) => race is Race.SkyborneAlliance or Race.SkyborneHorde?"Skyborn":race.ToString();
    internal static string CreationMessage(string name,Team team,Race race,Class cls,ObjectGuid guid) =>
        $"[siPlayer created] Name: \"{name}\", Faction: \"{team}\". Race: \"{CreationRaceName(race)}\", Class: \"{cls}\", GUID: {guid.GetCounter()}";

    static void ValidateCreationCombination(Race race,Class cls)
    {
        if(!ClassicClasses(race).Contains(cls)||Global.ObjectMgr.GetPlayerInfo(race,cls)==null)
            throw new ArgumentException($"{CreationRaceName(race)} ({Player.TeamForRace(race)}) does not support {cls}. Supported classes: "+
                string.Join(", ",ClassicClasses(race).Where(c=>Global.ObjectMgr.GetPlayerInfo(race,c)!=null)));
    }

    static Race[] AvailableCreationRaces(Team team) => RandomCreationRaces(team)
        .Where(r=>ClassicClasses(r).Any(c=>Global.ObjectMgr.GetPlayerInfo(r,c)!=null)).ToArray();

    static void CreateRandom(CommandHandler h,Team team,Race? chosenRace,Class? chosenClass,
        Action<bool,ObjectGuid> completed,long generation)
    {
        Race[] races=chosenRace.HasValue?[chosenRace.Value]:AvailableCreationRaces(team);
        if(races.Length==0)throw new InvalidOperationException("No supported race/class combinations for "+team);
        RandomNumberGenerator.Shuffle(races.AsSpan());
        foreach(Race race in races)
        {
            var classes=ClassicClasses(race).Where(c=>Global.ObjectMgr.GetPlayerInfo(race,c)!=null).ToArray();
            Class cls=chosenClass??classes[RandomNumberGenerator.GetInt32(classes.Length)];
            ValidateCreationCombination(race,cls);
            Gender gender=RandomNumberGenerator.GetInt32(2)==0?Gender.Male:Gender.Female;
            foreach(Gender sex in new[]{gender,gender==Gender.Male?Gender.Female:Gender.Male})
            {
                if(AvailableRandomName(race,sex) is not {} name)continue;
                CreateNamed(h,(name.First+" "+name.Surname).Trim(),race,cls,sex,completed,generation);
                return;
            }
        }
        throw new InvalidOperationException("Could not generate an unused valid name for the requested faction/race.");
    }

    internal static (int Alliance,int Horde) PopulationTargets(int count) => ((count+1)/2,count/2);
    internal static int PopulationNeeded(int target,int added,int pending,int creating) => Math.Max(0,target-added-pending-creating);
    internal static int PopulateArgument(string text)
    {
        if(!int.TryParse(text.Trim(),out int count)||count is <1 or >10000)
            throw new ArgumentException("Use .si populate <1-10000>. The count is the number of additional SI players to spawn.");
        return count;
    }

    static bool _populating;

    [Command("populate",Permission,true)]
    static bool Populate(CommandHandler h,StringArguments arguments)
    {
        if(!Enabled(h))return false;
        return Run(h,()=>QueuePopulation(h,PopulateArgument(arguments.GetString())));
    }

    static void QueuePopulation(CommandHandler h,int additional)
    {
        if(_populating)throw new InvalidOperationException("An SI population request is already running. Use .si despawn all to cancel it.");
        var targets=PopulationTargets(additional);
        var accounts=BotAccounts();
        var candidates=new List<ObjectGuid>();
        if(accounts.Count>0)
        {
            using var rows=DB.Characters.Query("SELECT guid FROM characters WHERE deleteInfos_Name IS NULL AND account IN ("+
                string.Join(',',accounts.Keys.Select(id=>unchecked((uint)id)))+")");
            if(!rows.IsEmpty())do
            {
                var guid=ObjectGuid.Create(HighGuid.Player,rows.Read<long>(0));
                var entry=Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                if(entry is { IsDeleted:false }&&!Editing.Contains(guid)&&Global.WorldMgr.FindSession(entry.AccountId)==null)
                    candidates.Add(guid);
            }while(rows.NextRow());
        }
        RandomNumberGenerator.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(candidates));
        var seen=new HashSet<int>();
        var offline=new Dictionary<Team,Queue<ObjectGuid>> { [Team.Alliance]=new(),[Team.Horde]=new() };
        foreach(var guid in candidates)
        {
            var entry=Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
            Team team=Player.TeamForRace(entry.RaceId);
            if(offline.ContainsKey(team)&&seen.Add(entry.AccountId))offline[team].Enqueue(guid);
        }
        var creating=new Dictionary<Team,int> { [Team.Alliance]=0,[Team.Horde]=0 };
        var pending=new Dictionary<ObjectGuid,(Team Team,long Deadline)>();
        // Only logins requested by this batch count toward its additions. Existing
        // players and other spawn commands must not consume this request's quota.
        int created=0,spawned=0,failures=0,lastProgress=0,alliance=0,horde=0;
        long generation=SpawnQueue.Generation,lastAdvance=Environment.TickCount64;
        _populating=true;
        Work.Enqueue(()=>
        {
            bool Finish(string reason)
            {
                _populating=false;h.SendSysMessage(reason);return true;
            }
            try
            {
                if(!SpawnQueue.IsCurrent(generation))return Finish("SI populate cancelled; existing online characters were kept.");
                long now=Environment.TickCount64;
                foreach(var request in pending.ToArray())
                {
                    var entry=Global.CharacterCacheStorage.GetCharacterCacheByGuid(request.Key);
                    var session=entry==null?null:Global.WorldMgr.FindSession(entry.AccountId);
                    if(session?.GetPlayer()?.IsInWorld==true&&!session.PlayerDisconnected())
                    {
                        pending.Remove(request.Key);++spawned;lastAdvance=now;
                        if(request.Value.Team==Team.Alliance)++alliance;else ++horde;
                    }
                    else if(session==null||session.PlayerDisconnected()||now>=request.Value.Deadline)
                    {pending.Remove(request.Key);++failures;}
                }
                if(alliance>=targets.Alliance&&horde>=targets.Horde&&creating.Values.Sum()==0&&pending.Count==0)
                    return Finish($"SI populate complete: added {spawned} SI players (Alliance {alliance}, Horde {horde}); {created} characters created, {failures} failures. Existing online SI players were kept.");
                if(failures>=3||now-lastAdvance>300000)
                    return Finish($"SI populate stopped after failed or stalled logins: added Alliance {alliance}/{targets.Alliance}, Horde {horde}/{targets.Horde}; failures {failures}. Check server logs before retrying.");
                if(spawned-lastProgress>=50)
                {lastProgress=spawned;h.SendSysMessage($"SI populate: added Alliance {alliance}/{targets.Alliance}, Horde {horde}/{targets.Horde}; created {created}, failures {failures}.");}
                // Bound database creation and native character-login work separately.
                if(pending.Count>=8||creating.Values.Sum()>=2)return false;
                int needAlliance=PopulationNeeded(targets.Alliance,alliance,pending.Values.Count(p=>p.Team==Team.Alliance),creating[Team.Alliance]);
                int needHorde=PopulationNeeded(targets.Horde,horde,pending.Values.Count(p=>p.Team==Team.Horde),creating[Team.Horde]);
                if(needAlliance==0&&needHorde==0)return false;
                Team team=needAlliance>=needHorde?Team.Alliance:Team.Horde;
                if(offline[team].TryDequeue(out var guid))
                {
                    var entry=Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                    if(entry==null||entry.IsDeleted||Editing.Contains(guid)||Global.WorldMgr.FindSession(entry.AccountId)!=null)return false;
                    try{SpawnCharacter(h,guid,announce:false);pending[guid]=(team,now+90000);}
                    catch(Exception error){++failures;h.SendSysMessage("SI populate login failed: "+error.GetBaseException().Message);}
                    return false;
                }
                ++creating[team];
                try
                {
                    CreateRandom(h,team,null,null,(success,newGuid)=>
                    {
                        --creating[team];
                        if(success){++created;pending[newGuid]=(team,Environment.TickCount64+90000);lastAdvance=Environment.TickCount64;}
                        else ++failures;
                    },generation);
                }
                catch(Exception error){--creating[team];++failures;h.SendSysMessage("SI populate creation failed: "+error.GetBaseException().Message);}
                return false;
            }
            catch(Exception error){return Finish("SI populate failed: "+error.GetBaseException().Message);}
        });
        h.SendSysMessage($"SI populate: adding {additional} SI players, Alliance {targets.Alliance}, Horde {targets.Horde}. Reusing offline SI characters first, then creating and spawning faction shortages. Work is paced; existing online SI players stay online and do not count toward this batch.");
    }
}
