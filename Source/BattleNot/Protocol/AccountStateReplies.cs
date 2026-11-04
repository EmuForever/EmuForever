using Bgs.Protocol;
using Bgs.Protocol.Account.V1;
using Google.Protobuf;

namespace BattleNot;

// Account-v1 clients request the names/status separately from the logon handles.
// Both replies must use the v1 schema; an empty successful reply hides account choices.
static class AccountStateReplies
{
    const ulong GameEntityHigh = 0x200000200576F57;
    const uint Region = 2;

    public static (uint Status, byte[] Body) BuildV1(BnetAccount owner, uint method, byte[] payload)
    {
        try {
            if (method == 30) return AccountState(owner, GetAccountStateRequest.Parser.ParseFrom(payload));
            if (method == 31) return GameState(owner, GetGameAccountStateRequest.Parser.ParseFrom(payload));
            return (Rpc.ErrorRpcInvalidMethod, []);
        }
        catch (InvalidProtocolBufferException) { return (Rpc.ErrorRpcMalformedRequest, []); }
    }

    static (uint, byte[]) AccountState(BnetAccount owner, GetAccountStateRequest request)
    {
        if (request.EntityId != null && request.EntityId.Low != owner.Id) return (Rpc.ErrorDenied, []);
        var options = request.Options;
        bool all = options == null || options.AllFields;
        var state = new Bgs.Protocol.Account.V1.AccountState();
        var tags = new AccountFieldTags();
        if (all || options!.FieldPrivacyInfo) {
            state.PrivacyInfo = new PrivacyInfo { IsUsingRid = false, IsVisibleForViewFriends = false, IsHiddenFromFriendFinder = true };
            tags.PrivacyInfoTag = 0xD7CA834D;
        }
        if (all || options!.FieldAccountLevelInfo)
            state.AccountLevelInfo = new AccountLevelInfo { Country = "USA", PreferredRegion = Region };
        if (all || options!.FieldGameAccounts) {
            var games = new GameAccountList { Region = Region };
            foreach (var game in owner.Games)
                games.Handle.Add(new GameAccountHandle { Id = game.Id, Program = Rpc.Wow, Region = Region });
            state.GameAccounts.Add(games);
            tags.GameAccountTags.Add(new RegionTag { Region = Region, Tag = 0x5C46D483 });
        }
        var response = new GetAccountStateResponse { State = state, Tags = tags };
        return (Rpc.ErrorOk, response.ToByteArray());
    }

    static (uint, byte[]) GameState(BnetAccount owner, GetGameAccountStateRequest request)
    {
#pragma warning disable CS0612
        if (request.AccountId != null && request.AccountId.Low != owner.Id) return (Rpc.ErrorDenied, []);
#pragma warning restore CS0612
        var id = request.GameAccountId;
        if (id == null || !id.HasLow || id.Low > uint.MaxValue) return (Rpc.ErrorRpcMalformedRequest, []);
        if (id.HasHigh && id.High != GameEntityHigh) return (Rpc.ErrorDenied, []);
        var game = owner.Games.FirstOrDefault(candidate => candidate.Id == (uint)id.Low);
        if (game == null) return (Rpc.ErrorDenied, []);
        var options = request.Options;
        bool all = options == null || options.AllFields;
        var state = new GameAccountState();
        var tags = new GameAccountFieldTags();
        if (all || options!.FieldGameLevelInfo) {
            state.GameLevelInfo = new GameLevelInfo { Name = game.AccountName, Program = Rpc.Wow };
            tags.GameLevelInfoTag = 0x5C46D483;
        }
        if (all || options!.FieldGameStatus) {
            state.GameStatus = new GameStatus { Program = Rpc.Wow, IsSuspended = game.Banned,
                IsBanned = game.PermanentlyBanned, SuspensionExpires = (ulong)game.UnbanDate * 1000000 };
            tags.GameStatusTag = 0x98B75F99;
        }
        if (all || options!.FieldGameTimeInfo)
            state.GameTimeInfo = new GameTimeInfo { IsUnlimitedPlayTime = true };
        return (Rpc.ErrorOk, new GetGameAccountStateResponse { State = state, Tags = tags }.ToByteArray());
    }
}
