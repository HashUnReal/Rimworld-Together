using RTServer.Hooks.TCPNetwork;
using RTServer.Managers;
using RTShared.Files;
using RTNetwork.PacketManagers;
using RTNetwork.Packets;
using RTNetwork.Components;
using RTShared.Misc;
using RTShared.Files.Player;
using RTServer.Core;

namespace RTServer.PacketManagers
{
    public class PM_Synchronous : PM_Base
    {
        private static readonly object SessionLock = new object();

        [HandlesPacket(PacketHeader.Synchronous)]
        public override void Receive(ServerClient client, byte[] bytes, PacketHeader header)
        {
            PKT_Synchronous data = Serializer.ConvertBytesToObject<PKT_Synchronous>(bytes);

            switch (data.CurrentStepMode)
            {
                case PKT_Synchronous.StepMode.Ask:
                    TryStartSynchronousSession(client, data);
                    break;

                case PKT_Synchronous.StepMode.Accept:
                    AcceptSynchronousSession(client, data);
                    break;

                case PKT_Synchronous.StepMode.Reject:
                    RejectSynchronousSession(client, data);
                    break;

                case PKT_Synchronous.StepMode.Start:
                    StartSynchronousSession(client, data);
                    break;

                case PKT_Synchronous.StepMode.Action:
                    RouteToManager(client, data, header);
                    break;
            }
        }

        public static void ClearSessionFor(ServerClient client)
        {
            lock (SessionLock)
            {
                ServerClient partner = TryGetSynchronousPartner(client);
                if (partner != null) partner.GetData<FL_Player>().SynchronousClientID = 0;

                client.GetData<FL_Player>().SynchronousClientID = 0;
            }
        }

        private static void RouteToManager(ServerClient client, PKT_Synchronous data, PacketHeader header)
        {
            lock (SessionLock)
            {
                ServerClient partner = TryGetSynchronousPartner(client);
                if (partner == null)
                {
                    ClearSessionFor(client);
                    ResponseShortcutManager.SendUserUnavailablePacket(client);
                    return;
                }

                if (Master.ServerConfig.EnableSynchronousCompatibilityMode)
                {
                    EnqueueForSessionPair(client, partner, header, data);
                }
                else
                {
                    client.Listener.EnqueuePacket(header, data);
                    partner.Listener.EnqueuePacket(header, data);
                }
            }
        }

        private static void TryStartSynchronousSession(ServerClient client, PKT_Synchronous data)
        {
            FL_Settlement settlement = PM_Settlements.GetSettlementFileFromTile(data.ToTile);
            if (settlement == null)
            {
                ResponseShortcutManager.SendUnavailablePacket(client);
                return;
            }

            ServerClient toFind = ServerNetwork.GetConnectedClientFromUsername(settlement.Username);

            if (toFind == null) ResponseShortcutManager.SendUserUnavailablePacket(client);
            else
            {
                PKT_Synchronous _ = new PKT_Synchronous();
                _.CurrentStepMode = PKT_Synchronous.StepMode.Ask;
                FL_Settlement fromSettlement = PM_Settlements.GetSettlementFileFromUsername(client.GetData<FL_Player>().Username);
                if (fromSettlement == null)
                {
                    ResponseShortcutManager.SendUnavailablePacket(client);
                    return;
                }

                _.FromTile = fromSettlement.Tile;
                _.Username = client.GetData<FL_Player>().Username;
                _.ToTile = data.ToTile;
                _.Party = data.Party;
                _.CurrentType = data.CurrentType;

                toFind.Listener.EnqueuePacket(PacketHeader.Synchronous, _);
            }
        }

        private static void AcceptSynchronousSession(ServerClient client, PKT_Synchronous data)
        {
            FL_Settlement settlement = PM_Settlements.GetSettlementFileFromTile(data.ToTile);
            if (settlement == null)
            {
                ResponseShortcutManager.SendUnavailablePacket(client);
                return;
            }

            ServerClient toFind = ServerNetwork.GetConnectedClientFromUsername(settlement.Username);
            if (toFind == null)
            {
                ResponseShortcutManager.SendUserUnavailablePacket(client);
                return;
            }

            lock (SessionLock)
            {
                client.GetData<FL_Player>().SynchronousClientID = toFind.ID;
                toFind.GetData<FL_Player>().SynchronousClientID = client.ID;

                data.CurrentStepMode = PKT_Synchronous.StepMode.Accept;
                toFind.Listener.EnqueuePacket(PacketHeader.Synchronous, data);
            }
        }

        private static void RejectSynchronousSession(ServerClient client, PKT_Synchronous data)
        {
            FL_Settlement settlement = PM_Settlements.GetSettlementFileFromTile(data.ToTile);
            if (settlement == null)
            {
                ResponseShortcutManager.SendUnavailablePacket(client);
                return;
            }

            ServerClient toFind = ServerNetwork.GetConnectedClientFromUsername(settlement.Username);
            if (toFind == null)
            {
                ResponseShortcutManager.SendUserUnavailablePacket(client);
                return;
            }

            PKT_Synchronous _ = new PKT_Synchronous();
            _.CurrentStepMode = PKT_Synchronous.StepMode.Reject;
            _.FromTile = data.FromTile;
            _.ToTile = data.ToTile;

            toFind.Listener.EnqueuePacket(PacketHeader.Synchronous, _);
        }

        private static void StartSynchronousSession(ServerClient client, PKT_Synchronous data)
        {
            lock (SessionLock)
            {
                ServerClient partner = TryGetSynchronousPartner(client);
                if (partner == null)
                {
                    ClearSessionFor(client);
                    ResponseShortcutManager.SendUserUnavailablePacket(client);
                    return;
                }

                PKT_Synchronous _ = new PKT_Synchronous() { CurrentStepMode = PKT_Synchronous.StepMode.Start };
                if (Master.ServerConfig.EnableSynchronousCompatibilityMode) EnqueueForSessionPair(client, partner, PacketHeader.Synchronous, _);
                else partner.Listener.EnqueuePacket(PacketHeader.Synchronous, _);
            }
        }

        private static void EnqueueForSessionPair(ServerClient client, ServerClient partner, PacketHeader header, PKT_Synchronous data)
        {
            client.Listener.EnqueuePacket(header, data);
            partner.Listener.EnqueuePacket(header, data);
        }

        private static ServerClient TryGetSynchronousPartner(ServerClient client)
        {
            byte partnerID = client.GetData<FL_Player>().SynchronousClientID;
            if (partnerID == 0) return null;

            ServerClient partner = ServerNetwork.TryGetClientFromID(partnerID);
            if (partner == null) return null;

            if (partner.GetData<FL_Player>().SynchronousClientID != client.ID) return null;
            return partner;
        }
    }
}
