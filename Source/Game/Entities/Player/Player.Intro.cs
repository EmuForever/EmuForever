using Framework.Constants;
using Game.DataStorage;
using System;

namespace Game.Entities
{
    internal enum PlayerIntroKind { None, Movie, Scene, Cinematic }

    public partial class Player
    {
        internal static (PlayerIntroKind Kind, int Id) SelectIntro(PlayerInfo info, PlayerCreateMode mode, int classCinematic, int raceCinematic)
        {
            if (info == null)
                return (PlayerIntroKind.None, 0);
            if (mode == PlayerCreateMode.NPE)
                return info.introSceneIdNPE.GetValueOrDefault() > 0 ? (PlayerIntroKind.Scene, info.introSceneIdNPE.Value) : (PlayerIntroKind.None, 0);
            if (mode != PlayerCreateMode.Normal)
                return (PlayerIntroKind.None, 0);
            if (info.introMovieId.GetValueOrDefault() > 0)
                return (PlayerIntroKind.Movie, info.introMovieId.Value);
            if (info.introSceneId.GetValueOrDefault() > 0)
                return (PlayerIntroKind.Scene, info.introSceneId.Value);
            if (classCinematic > 0)
                return (PlayerIntroKind.Cinematic, classCinematic);
            return raceCinematic > 0 ? (PlayerIntroKind.Cinematic, raceCinematic) : (PlayerIntroKind.None, 0);
        }

        public bool TryPlayIntro()
        {
            var info = Global.ObjectMgr.GetPlayerInfo(GetRace(), GetClass());
            int classCinematic = CliDB.ChrClassesStorage.TryGetValue(GetClass(), out var classEntry) ? classEntry.CinematicSequenceID : 0;
            int raceCinematic = CliDB.ChrRacesStorage.TryGetValue((int)GetRace(), out var raceEntry) ? raceEntry.CinematicSequenceID : 0;
            var intro = SelectIntro(info, GetCreateMode(), classCinematic, raceCinematic);
            switch (intro.Kind)
            {
                case PlayerIntroKind.Movie:
                    if (!CliDB.MovieStorage.ContainsKey(intro.Id)) return false;
                    SendMovieStart(intro.Id);
                    return true;
                case PlayerIntroKind.Scene:
                    return GetSceneMgr().PlayScene(intro.Id) != 0;
                case PlayerIntroKind.Cinematic:
                    if (!CliDB.CinematicSequencesStorage.ContainsKey(intro.Id)) return false;
                    SendCinematicStart(intro.Id);
                    return true;
                default:
                    return false;
            }
        }
    }
}
