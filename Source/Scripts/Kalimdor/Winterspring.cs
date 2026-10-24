/*
 * This file is part of the TrinityCore Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License as published by the
 * Free Software Foundation; either version 2 of the License, or (at your
 * option) any later version.
 *
 * This program is distributed in the hope that it will be useful, but WITHOUT
 * ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
 * FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
 * more details.
 *
 * You should have received a copy of the GNU General Public License along
 * with this program. If not, see <http://www.gnu.org/licenses/>.
 */
// C# port of TrinityCore wotlk_classic.
using Framework.Constants;
using Game;
using Game.AI;
using Game.Entities;
using Game.Scripting;
using System;
using static Scripts.Kalimdor.Winterspring.WinterspringData;

namespace Scripts.Kalimdor.Winterspring
{
    [Script]
    class npc_ranshalla : EscortAI
    {
        int _nextDialogue = -1;
        TimeSpan _actionTimer;
        void StartNextDialogueText(int textEntry)
        {
            int index = Array.FindIndex(IntroDialogue, row => row.Text == textEntry);
            if (index < 0) return;
            _nextDialogue = index;
            DoNextDialogueStep();
        }
        void DialogueUpdate(TimeSpan diff)
        {
            if (_actionTimer <= TimeSpan.Zero) return;
            if (_actionTimer <= diff) DoNextDialogueStep();
            else _actionTimer -= diff;
        }
        void DoNextDialogueStep()
        {
            if (_nextDialogue < 0 || _nextDialogue >= IntroDialogue.Length || IntroDialogue[_nextDialogue].Text == 0)
            {
                _actionTimer = TimeSpan.Zero;
                return;
            }
            var row = IntroDialogue[_nextDialogue];
            _actionTimer = TimeSpan.FromMilliseconds(row.Delay);
            if (row.Speaker != 0 && row.Text >= 0) GetSpeakerByEntry(row.Speaker)?.GetAI().Talk(row.Text);
            JustDidDialogueStep(row.Text);
            ++_nextDialogue;
        }


        public npc_ranshalla(Creature creature) : base(creature)
        {
            Initialize();
        }

        void Initialize()
        {
            _delayTimer = TimeSpan.FromMilliseconds(0);
        }

        TimeSpan _delayTimer;

        ObjectGuid _firstPriestessGUID;
        ObjectGuid _secondPriestessGUID;
        ObjectGuid _guardEluneGUID;
        ObjectGuid _voiceEluneGUID;
        ObjectGuid _altarGUID;

        public override void Reset()
        {
            Initialize();
        }

        // Called when the player activates the torch / altar
        public void DoContinueEscort(bool isAltarWaypoint = false)
        {
            me.InterruptNonMeleeSpells(false);

            if (isAltarWaypoint)
                Talk(SAY_RANSHALLA_ALTAR_1);
            else
                Talk(SAY_AFTER_TORCH);

            _delayTimer = TimeSpan.FromMilliseconds(2000);
        }

        // Called when Ranshalla starts to channel on a torch / altar
        void DoChannelTorchSpell(bool isAltarWaypoint = false)
        {
            // Check if we are using the fire or the altar and remove the no_interact flag
            if (isAltarWaypoint)
            {
                if (GetClosestGameObjectWithEntry(me, GO_ELUNE_ALTAR, 10.0f) is GameObject go1)
                {
                    go1.RemoveFlag(GameObjectFlags.NotSelectable);
                    me.SetFacingToObject(go1);
                    _altarGUID = go1.GetGUID();
                }
            }
            else if (GetClosestGameObjectWithEntry(me, GO_ELUNE_FIRE, 10.0f) is GameObject go2)
                go2.RemoveFlag(GameObjectFlags.NotSelectable);

            // Yell and set escort to pause
            Talk(SAY_REACH_TORCH);
            Talk(EMOTE_CHANT_SPELL);
            SetEscortPaused(true);
            DoCast(me, SPELL_LIGHT_TORCH);
        }

        void DoSummonPriestess()
        {
            // Summon 2 Elune priestess and make each of them move to a different spot
            if (me.SummonCreature(NPC_PRIESTESS_ELUNE, WingThicketLocations[0], TempSummonType.CorpseDespawn, TimeSpan.FromSeconds(0)) is Creature priestess1)
            {
                priestess1.GetMotionMaster().MovePoint(0, WingThicketLocations[3]);
                _firstPriestessGUID = priestess1.GetGUID();
            }
            if (me.SummonCreature(NPC_PRIESTESS_ELUNE, WingThicketLocations[1], TempSummonType.CorpseDespawn, TimeSpan.FromSeconds(0)) is Creature priestess2)
            {
                // Left priestess2 should have a distinct move point because she is the one who starts the dialogue at point reach
                priestess2.GetMotionMaster().MovePoint(1, WingThicketLocations[4]);
                _secondPriestessGUID = priestess2.GetGUID();
            }
        }

        public void SummonedMovementInform(Creature summoned, MovementGeneratorType type, int pointId)
        {
            if (type != MovementGeneratorType.Point || summoned.GetEntry() != NPC_PRIESTESS_ELUNE || pointId != 1)
                return;

            // Start the dialogue when the priestess reach the altar (they should both reach the point in the same time)
            StartNextDialogueText(SAY_PRIESTESS_ALTAR_3);
        }

        public override void WaypointReached(int pointId, int pathId)
        {
            switch (pointId)
            {
                case 3:
                    Talk(SAY_ENTER_OWL_THICKET);
                    break;
                case 10: // Cavern 1
                case 15: // Cavern 2
                case 20: // Cavern 3
                case 25: // Cavern 4
                case 36: // Cavern 5
                    DoChannelTorchSpell();
                    break;
                case 39:
                    StartNextDialogueText(SAY_REACH_ALTAR_1);
                    SetEscortPaused(true);
                    break;
                case 41:
                {
                    // Search for all nearest lights and respawn them
                    foreach (GameObject light in me.GetGameObjectListWithEntryInGrid(GO_ELUNE_LIGHT, 20))
                    {
                        if (light.IsSpawned()) continue;
                        light.SetRespawnTime(TimeSpan.FromSeconds(115));
                        light.Refresh();
                    }

                    if (ObjectAccessor.GetGameObject(me,_altarGUID) is GameObject altar1)
                        me.SetFacingToObject(altar1);
                    break;
                }
                case 42:
                    // Summon the 2 priestess
                    SetEscortPaused(true);
                    DoSummonPriestess();
                    Talk(SAY_RANSHALLA_ALTAR_2);
                    _events.ScheduleEvent(EVENT_RESUME, TimeSpan.FromSeconds(2));
                    break;
                case 44:
                    // Stop the escort and turn towards the altar
                    SetEscortPaused(true);
                    if (ObjectAccessor.GetGameObject(me, _altarGUID) is GameObject altar2)
                        me.SetFacingToObject(altar2);
                    break;
            }
        }

        void JustDidDialogueStep(int entry)
        {
            switch (entry)
            {
                case NPC_RANSHALLA:
                    // Start the altar channeling
                    DoChannelTorchSpell(true);
                    break;
                case SAY_RANSHALLA_ALTAR_6:
                    SetEscortPaused(false);
                    break;
                case SAY_PRIESTESS_ALTAR_8:
                    // make the gem respawn
                    if (GetClosestGameObjectWithEntry(me, GO_ELUNE_GEM, 10.0f, false) is GameObject gem1)
                    {
                        if (gem1.IsSpawned())
                            break;

                        gem1.SetRespawnTime(TimeSpan.FromSeconds(90));
                        gem1.Refresh();
                    }
                    break;
                case SAY_PRIESTESS_ALTAR_9:
                    // move near the escort npc
                    if (ObjectAccessor.GetCreature(me, _firstPriestessGUID) is Creature priestess3)
                        priestess3.GetMotionMaster().MovePoint(0, WingThicketLocations[6]);
                    break;
                case SAY_PRIESTESS_ALTAR_13:
                    // summon the Guardian of Elune
                    if (me.SummonCreature(NPC_GUARDIAN_ELUNE, WingThicketLocations[2], TempSummonType.CorpseDespawn, TimeSpan.FromSeconds(0)) is Creature guard1)
                    {
                        guard1.GetMotionMaster().MovePoint(0, WingThicketLocations[5]);
                        _guardEluneGUID = guard1.GetGUID();
                    }
                    // summon the Voice of Elune
                    if (ObjectAccessor.GetGameObject(me, _altarGUID) is GameObject altar3)
                    {
                        if (me.SummonCreature(NPC_VOICE_ELUNE, altar3.GetPosition(), TempSummonType.TimedDespawn, TimeSpan.FromSeconds(30)) is Creature voice1)
                            _voiceEluneGUID = voice1.GetGUID();
                    }
                    break;
                case SAY_VOICE_ALTAR_15:
                    // move near the escort npc and continue dialogue
                    if (ObjectAccessor.GetCreature(me, _secondPriestessGUID) is Creature priestess4)
                    {
                        priestess4.GetAI().Talk(SAY_PRIESTESS_ALTAR_14);
                        priestess4.GetMotionMaster().MovePoint(0, WingThicketLocations[7]);
                    }
                    break;
                case SAY_PRIESTESS_ALTAR_19:
                    // make the voice of elune leave
                    if (ObjectAccessor.GetCreature(me, _guardEluneGUID) is Creature guard2)
                    {
                        guard2.GetMotionMaster().MovePoint(0, WingThicketLocations[2]);
                        guard2.DespawnOrUnsummon(TimeSpan.FromSeconds(4));
                    }
                    break;
                case SAY_PRIESTESS_ALTAR_20:
                    // make the first priestess leave
                    if (ObjectAccessor.GetCreature(me, _firstPriestessGUID) is Creature priestess5)
                    {
                        priestess5.GetMotionMaster().MovePoint(0, WingThicketLocations[0]);
                        priestess5.DespawnOrUnsummon(TimeSpan.FromSeconds(4));
                    }
                    break;
                case SAY_PRIESTESS_ALTAR_21:
                    // make the second priestess leave
                    if (ObjectAccessor.GetCreature(me, _secondPriestessGUID) is Creature priestess6)
                    {
                        priestess6.GetMotionMaster().MovePoint(0, WingThicketLocations[1]);
                        priestess6.DespawnOrUnsummon(TimeSpan.FromSeconds(4));
                    }
                    break;
                case DATA_EVENT_END:
                    // Turn towards the player
                    if (GetPlayerForEscort() is Player player1)
                    {
                        me.SetFacingToObject(player1);
                        Talk(SAY_RANSHALLA_END_1, player1);
                    }
                    break;
                case SAY_RANSHALLA_END_2:
                    // Turn towards the altar and kneel - quest complete
                    if (ObjectAccessor.GetGameObject(me, _altarGUID) is GameObject altar4)
                    {
                        me.SetFacingToObject(altar4);
                        altar4.ResetDoorOrButton();
                    }
                    me.SetStandState(UnitStandStateType.Kneel);
                    if (GetPlayerForEscort() is Player player2)
                    {
                        player2.GroupEventHappens(QUEST_GUARDIANS_ALTAR, me);
                        Talk(SAY_RANSHALLA_END_2, player2);
                    }
                    me.DespawnOrUnsummon(TimeSpan.FromSeconds(4));
                    break;
            }
        }

        Creature GetSpeakerByEntry(int entry)
        {
            switch (entry)
            {
                case NPC_RANSHALLA:
                    return me;
                case NPC_VOICE_ELUNE:
                    return ObjectAccessor.GetCreature(me, _voiceEluneGUID);
                case NPC_PRIESTESS_DATA_1:
                    return ObjectAccessor.GetCreature(me, _firstPriestessGUID);
                case NPC_PRIESTESS_DATA_2:
                    return ObjectAccessor.GetCreature(me, _secondPriestessGUID);
                default:
                    return null;
            }

        }

        public override void UpdateEscortAI(TimeSpan diff)
        {
            DialogueUpdate(diff);

            if (_delayTimer > TimeSpan.Zero)
            {
                if (_delayTimer <= diff)
                {
                    SetEscortPaused(false);
                    _delayTimer = TimeSpan.FromMilliseconds(0);
                }
                else
                    _delayTimer -= diff;
            }
            _events.Update(diff);
            if (_events.ExecuteEvent() == EVENT_RESUME)
                StartNextDialogueText(SAY_PRIESTESS_ALTAR_3);

            base.UpdateEscortAI(diff);
        }

        public override void OnQuestAccept(Player player, Quest quest)
        {
            if (quest.Id == QUEST_GUARDIANS_ALTAR)
            {
                Talk(SAY_QUEST_START);
                me.SetFaction(10);

                LoadPath(PATH_ESCORT_RANSHALLA);
                Start(false, player.GetGUID(), quest);
            }
        }

    
        
    
    }
}

