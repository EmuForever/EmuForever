using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        public long DamageDelivered { get; private set; }
        public int QuestsAccepted { get; private set; }
        public int QuestsCompleted { get; private set; }
        public int ItemsLooted { get; private set; }
        public int Kills { get; private set; }
        readonly ConcurrentQueue<ObjectGuid> _whisperSenders = new();

        // Whisper delivery can run outside this player's map update. Reply from the AI tick, so
        // location, task and counters are read together on the same thread that changes them.
        internal void ReceivedWhisper(ObjectGuid sender) => _whisperSenders.Enqueue(sender);

        internal void ConfirmKill(Unit victim)
        {
            if (victim == null) return;
            ++Kills;
            NoteGameplayProgress();
        }

        static string WhisperStatus(int map, Position location, string goal, string task, int quests, int sessionQuests, int kills)
        {
            task ??= "none";
            if (task.Length > 70) task = task[..67] + "...";
            return $"Location: map {map} ({location.GetPositionX():F1}, {location.GetPositionY():F1}, {location.GetPositionZ():F1}). " +
                $"Goal: {goal}; task: {task}. Quests completed: {quests} ({sessionQuests} this SI session). Kills: {kills} this SI session.";
        }

        void ReplyToWhispers()
        {
            int replies = 0;
            while (replies++ < 8 && _whisperSenders.TryDequeue(out ObjectGuid sender))
            {
                Player recipient = Global.ObjAccessor.FindConnectedPlayer(sender);
                if (recipient == null || recipient.GetSession().IsServerControlled) continue;
                me.Whisper(WhisperStatus(me.GetMapId(), me.GetPosition(), _goal, Activity,
                    me.GetRewardedQuestCount(), QuestsCompleted, Kills), Language.Universal, recipient);
            }
        }
        public override void DamageDealt(Unit victim, ref int damage, DamageEffectType type)
        {
            if (damage > 0) DamageDelivered += damage;
        }
        internal void ConfirmLoot(int item, int count, ObjectGuid source)
        {
            ItemsLooted += count;
            Report("loot_item", $"Looted {count}x item {item}");
        }
        internal void ConfirmMoney(long copper) => Report("loot_money", $"Looted {copper} copper");

    }
}
