using Framework.Constants;
using Game.Networking;
using Game.Networking.Packets;

namespace Game
{
    public partial class WorldSession
    {
        // The glue sends these empty queries even when the realm provides no
        // Blizzard store or paid character services.
        [WorldPacketHandler(ClientOpcodes.BattlePayGetPurchaseList, Status = SessionStatus.Authed, Processing = PacketProcessing.Inplace)]
        [WorldPacketHandler(ClientOpcodes.BattlePayGetProductList, Status = SessionStatus.Authed, Processing = PacketProcessing.Inplace)]
        [WorldPacketHandler(ClientOpcodes.UpdateVasPurchaseStates, Status = SessionStatus.Authed, Processing = PacketProcessing.Inplace)]
        //[WorldPacketHandler(ClientOpcodes.Unknown_3743, Processing = PacketProcessing.Inplace)]
        //[WorldPacketHandler(ClientOpcodes.OverrideScreenFlash, Processing = PacketProcessing.Inplace)]
        //[WorldPacketHandler(ClientOpcodes.GetAccountCharacterList, Processing = PacketProcessing.Inplace)]
        //[WorldPacketHandler(ClientOpcodes.QueuedMessagesEnd, Processing = PacketProcessing.Inplace)]
        void HandleUnused(EmptyPaket empty) { }        
    }
}
