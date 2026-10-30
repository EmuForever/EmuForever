// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Game.Networking.Packets;

namespace Game.Entities
{
    public partial class Player
    {
        // The Classic client ignores a pet bar (SMSG_PET_SPELLS_MESSAGE) for a unit it does not yet know as this player's pet or
        // charm. The pet's create and the player's Summon/Charm field go out with the map's object updates at the end of the
        // tick, after packets sent directly, so the packet log showed the bar arriving first, and it never appeared. The bar waits
        // for the next world tick instead.
        PetSpells _pendingPetBar;
        ServerTime _pendingPetBarQueuedAt;

        void SendPetBar(PetSpells packet)
        {
            _pendingPetBar = packet;
            _pendingPetBarQueuedAt = LoopTime.ServerTime;
        }

        void SendPendingPetBar()
        {
            if (_pendingPetBar == null || LoopTime.ServerTime <= _pendingPetBarQueuedAt)
                return;

            PetSpells packet = _pendingPetBar;
            _pendingPetBar = null;
            SendPacket(packet);
        }

        // An empty PetSpells removes the bar; a bar still waiting for that pet must not follow it.
        void SendEmptyPetBar()
        {
            _pendingPetBar = null;
            SendPacket(new PetSpells());
        }
    }
}
