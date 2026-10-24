using Framework.Constants;
using Framework.Database;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Game.AI
{
    // Only IDs cross this boundary. No worker reads a live Player, spellbook, map or trainer cache.
    // Two readers bound DB concurrency; TryWrite defers excess requests on their owning AI instead
    // of blocking a map tick or dropping an already accepted availability check.
    internal static class SITrainerQueries
    {
        sealed record Request(Class Class, int Level, TaskCompletionSource<Dictionary<int, HashSet<int>>> Completion);
        static readonly Channel<Request> Requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(128)
        { SingleWriter = false, SingleReader = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });

        static SITrainerQueries()
        {
            for (int i = 0; i < 2; ++i) _ = Task.Run(Work);
        }

        public static Task<Dictionary<int, HashSet<int>>> TryQuery(Class playerClass, int level)
        {
            var completion = new TaskCompletionSource<Dictionary<int, HashSet<int>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            return Requests.Writer.TryWrite(new(playerClass, level, completion)) ? completion.Task : null;
        }

        static async Task Work()
        {
            await foreach (var request in Requests.Reader.ReadAllAsync())
            {
                try
                {
                    var query = new PreparedStatement("SELECT DISTINCT ts.TrainerId, ts.SpellId FROM trainer_spell ts " +
                        "JOIN creature_trainer ct ON ct.TrainerId=ts.TrainerId JOIN creature_template c ON c.entry=ct.CreatureId " +
                        "WHERE c.trainer_class=@0 AND ts.ReqLevel<=@1 LIMIT 16385");
                    query.SetInt32(0, (int)request.Class); query.SetInt32(1, request.Level);
                    using var rows = DB.World.Query(query);
                    if (rows.GetFieldCount() != 2) throw new InvalidOperationException("Trainer availability query failed.");
                    var offers = new Dictionary<int, HashSet<int>>();
                    int count = 0;
                    if (!rows.IsEmpty()) do
                    {
                        if (++count > 16384) throw new InvalidOperationException("Trainer availability exceeded its safety limit.");
                        int trainer = rows.Read<int>(0);
                        if (!offers.TryGetValue(trainer, out var spells)) offers.Add(trainer, spells = new());
                        spells.Add(rows.Read<int>(1));
                    } while (rows.NextRow());
                    request.Completion.TrySetResult(offers);
                }
                catch (Exception error) { request.Completion.TrySetException(error); }
            }
        }
    }
}
