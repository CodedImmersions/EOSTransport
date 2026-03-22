using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace EpicTransport
{
    public abstract class Common : IDisposable
    {
        protected ProductUserId MyPUID;

        protected EOSTransport Transport { get; private set; }
        protected P2PInterface P2P { get; private set; }
        protected List<SocketId> deadsockets;

        protected ulong connreqid;
        protected ulong connestid;
        protected ulong connclosedid;

        public bool Initializing { get; protected set; }
        public bool Active { get; protected set; }

        public bool ShuttingDown { get; protected set; }

        //used to save memory
        private SendPacketOptions sendopt = new() { AllowDelayedDelivery = true, DisableAutoAcceptConnection = false };
        private ReceivePacketOptions receiveopt = new() { MaxDataSizeBytes = P2PInterface.MAX_PACKET_SIZE };

        private readonly byte[] internalSendBuffer;
        private readonly byte[] internalReceiveBuffer;
        private readonly byte[] internalExtraDataBuffer;
        private readonly ArraySegment<byte> cachedSegment;

        private uint lastPacketId;
        private readonly Dictionary<PacketKey, List<Packet>> incomingpackets;

        private int extralengthcache;
        private byte intmessagecache;
        private Packet pcache;
        private PacketKey pkcache;

        protected Common(EOSTransport transport)
        {
            this.MyPUID = EOSManager.LocalUserProductID;
            this.Transport = transport;
            this.P2P = EOSManager.GetP2PInterface();

            deadsockets = new List<SocketId>();

            internalSendBuffer = new byte[P2PInterface.MAX_PACKET_SIZE];
            internalReceiveBuffer = new byte[P2PInterface.MAX_PACKET_SIZE];
            internalExtraDataBuffer = new byte[20]; //we don't need to allocate much for extra data, not much is being sent through it.
            cachedSegment = new ArraySegment<byte>(internalReceiveBuffer);

            sendopt.LocalUserId = EOSManager.LocalUserProductID;
            receiveopt.LocalUserId = EOSManager.LocalUserProductID;

            incomingpackets = new Dictionary<PacketKey, List<Packet>>();

            AddNotifyPeerConnectionRequestOptions connreqopt = new() { LocalUserId = EOSManager.LocalUserProductID };
            connreqid = P2P.AddNotifyPeerConnectionRequest(ref connreqopt, null, OnIncomingConnectionRequest);

            AddNotifyPeerConnectionEstablishedOptions estopt = new() { LocalUserId = EOSManager.LocalUserProductID };
            connestid = P2P.AddNotifyPeerConnectionEstablished(ref estopt, null, OnPeerConnected);

            AddNotifyPeerConnectionClosedOptions closedopt = new() { LocalUserId = EOSManager.LocalUserProductID };
            connclosedid = P2P.AddNotifyPeerConnectionClosed(ref closedopt, null, OnRemoteConnectionClosed);

            pkcache = new PacketKey();
        }

        public void Dispose()
        {
            P2P.RemoveNotifyPeerConnectionRequest(connreqid);
            P2P.RemoveNotifyPeerConnectionEstablished(connestid);
            P2P.RemoveNotifyPeerConnectionClosed(connclosedid);
        }

        private void OnRemoteConnectionClosed(ref OnRemoteConnectionClosedInfo cb)
        {
            if (EOSTransport.HostMigrationEnabled && cb.SocketId.Value.SocketName == HostMigrationController.instance.socket.SocketName)
            {
                Debug.Log("ignoring disconnect message from HM socket.");
                deadsockets.Add(cb.SocketId.Value);
                return;
            }

            OnConnectionClosed(cb.RemoteUserId);

            switch (cb.Reason)
            {
                case ConnectionClosedReason.ClosedByLocalUser:
                    TransportLogger.Log($"Connection with {cb.RemoteUserId} closed by local user.");
                    break;

                case ConnectionClosedReason.ClosedByPeer:
                    TransportLogger.Log($"Connection with {cb.RemoteUserId} closed by remote user.");
                    break;

                case ConnectionClosedReason.TimedOut:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: timeout.");
                    break;

                case ConnectionClosedReason.TooManyConnections:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: max connections reached.");
                    break;

                case ConnectionClosedReason.InvalidMessage:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: invalid message received.");
                    break;

                case ConnectionClosedReason.InvalidData:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: invalid data received.");
                    break;

                case ConnectionClosedReason.ConnectionFailed:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: connection failed.");
                    break;

                case ConnectionClosedReason.ConnectionClosed:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: connection closed.");
                    break;

                case ConnectionClosedReason.NegotiationFailed:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: negotiation failed.");
                    break;

                case ConnectionClosedReason.UnexpectedError:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: unexpected error.");
                    break;

                case ConnectionClosedReason.ConnectionIgnored:
                    TransportLogger.LogWarning($"Connection to {cb.RemoteUserId} failed: connection ignored.");
                    break;
            }
        }

        internal bool SendData(ProductUserId peer, SocketId socket, ArraySegment<byte> data, int channel)
        {
            sendopt.RemoteUserId = peer;
            sendopt.SocketId = socket;
            sendopt.Channel = (byte)channel;
            sendopt.Reliability = EOSTransport.Channels[channel];
            sendopt.Data = data;

            return P2P.SendPacket(ref sendopt) == Result.Success;
        }

        protected bool SendInternalData(ProductUserId peer, SocketId socket, InternalMessages msg, byte[] extradata = null)
        {
            internalSendBuffer[0] = (byte)msg;

            if (extradata != null)
            {
                if (extradata.Length + 1 > internalSendBuffer.Length) { TransportLogger.LogError("internal message too large"); return false; }
                Buffer.BlockCopy(extradata, 0, internalSendBuffer, 1, extradata.Length);
            }

            sendopt.RemoteUserId = peer;
            sendopt.SocketId = socket;
            sendopt.Channel = EOSTransport.InternalChannel;
            sendopt.Reliability = PacketReliability.ReliableOrdered;
            sendopt.Data = new ArraySegment<byte>(internalSendBuffer, 0, extradata != null ?  1 + extradata.Length : 1);

            return P2P.SendPacket(ref sendopt) == Result.Success;
        }

        public void ReceiveData()
        {
            try
            {
                while (Transport.enabled && Receive(out ProductUserId ipeer, out SocketId isocket, out ArraySegment<byte> ibuffer, EOSTransport.InternalChannel))
                {
                    intmessagecache = ibuffer.Array[ibuffer.Offset];
                    extralengthcache = ibuffer.Count - 1;

                    if (extralengthcache > 0)
                    {
                        Buffer.BlockCopy(ibuffer.Array, ibuffer.Offset + 1, internalExtraDataBuffer, 0, extralengthcache);
                        OnInternalData(ipeer, isocket, (InternalMessages)intmessagecache, internalExtraDataBuffer);
                    }
                    else OnInternalData(ipeer, isocket, (InternalMessages)intmessagecache);
                }

                for (int ch = 0; ch < EOSTransport.Channels.Count; ch++)
                {
                    while (Transport.enabled && Receive(out ProductUserId peer, out SocketId socket, out ArraySegment<byte> buffer, (byte)ch))
                    {
                        pcache = new Packet(buffer);
                        pkcache.peer = peer;
                        pkcache.packetId = pcache.packetId;
                        pkcache.channel = (byte)ch;

                        if (!incomingpackets.TryGetValue(pkcache, out List<Packet> fragments))
                        {
                            fragments = new List<Packet>();
                            incomingpackets[pkcache] = fragments;
                        }

                        fragments.Add(pcache);

                        //if we are being packet fragment spammed, the following will happen:
                        //- server: kill the connection.
                        //- client: disconnect from server.

                        if (fragments.Count > EOSTransport.instance.maxFragments)
                        {
                            if (NetworkServer.active)
                            {
                                SendInternalData(peer, socket, InternalMessages.DISCONNECT);
                                TransportLogger.LogWarning($"Max packet fragments reached. Disconnecting connection '{peer}' due to packet spamming.");

                                CloseConnectionOptions closeopt = new() { LocalUserId = EOSManager.LocalUserProductID, RemoteUserId = peer, SocketId = socket };
                                P2P.CloseConnection(ref closeopt);
                            }
                            else if (NetworkClient.isConnected)
                            {
                                EOSTransport.LeaveLobby();
                                TransportLogger.LogWarning($"Max packet fragments reached. Disconnecting from server '{peer}' due to packet spamming.");
                            }
                        }

                        int expectedCount = -1;
                        for (int i = 0; i < fragments.Count; i++)
                        {
                            if (fragments[i].lastFragment)
                            {
                                expectedCount = fragments[i].fragmentId + 1;
                                break;
                            }
                        }

                        if (expectedCount != -1)
                        {
                            if (fragments.Count == expectedCount)
                            {
                                bool contiguous = true;
                                var seen = new bool[expectedCount];
                                foreach (var frag in fragments)
                                {
                                    if (frag.fragmentId >= expectedCount)
                                    {
                                        contiguous = false;
                                        break;
                                    }
                                    seen[frag.fragmentId] = true;
                                }

                                for (int i = 0; i < expectedCount && contiguous; i++) { if (!seen[i]) { contiguous = false; break; } }

                                if (contiguous)
                                {
                                    fragments.Sort(Packet.CompareByFragmentID);

                                    int length = 0;
                                    foreach (Packet frag in fragments) length += frag.data.Length;

                                    byte[] fulldat = new byte[length];
                                    int offset = 0;
                                    foreach (Packet frag in fragments)
                                    {
                                        Buffer.BlockCopy(frag.data, 0, fulldat, offset, frag.data.Length);
                                        offset += frag.data.Length;
                                    }

                                    OnReceiveData(peer, new ArraySegment<byte>(fulldat), (byte)ch);
                                    incomingpackets.Remove(pkcache);
                                }
                            }
                        }

                    }
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private bool Receive(out ProductUserId peer, out SocketId socket, out ArraySegment<byte> buffer, byte channel)
        {
            receiveopt.RequestedChannel = channel;

            peer = default;
            socket = default;

            Result result = P2P.ReceivePacket(ref receiveopt, ref peer, ref socket, out _, cachedSegment, out uint writtenbytes);

            if (result != Result.NotFound && result != Result.Success) TransportLogger.LogWarning($"Receive returned Result.{result}.");

            buffer = new ArraySegment<byte>(internalReceiveBuffer, 0, (int)writtenbytes);
            return result == Result.Success && writtenbytes > 0;
        }

        protected List<Packet> FragmentData(ArraySegment<byte> data, int channelId)
        {
            List<Packet> packets = new();
            uint id = lastPacketId++;

            int maxpayload = EOSTransport.instance.GetMaxPacketSize(channelId);
            int frags = Mathf.CeilToInt((float)data.Count / maxpayload);

            for (int i = 0; i < frags; i++)
            {
                int offset = i * maxpayload;
                int remaining = data.Count - offset;
                int length = Mathf.Min(maxpayload, remaining);

                byte[] dat = new byte[length];
                Array.Copy(data.Array, data.Offset + offset, dat, 0, length);

                packets.Add(new Packet
                {
                    packetId = id,
                    fragmentId = (ushort)i,
                    lastFragment = (i == frags - 1),
                    data = dat
                });
            }

            return packets;
        }

        #region Virtual Methods
        protected abstract void OnIncomingConnectionRequest(ref OnIncomingConnectionRequestInfo cb);
        protected abstract void OnPeerConnected(ref OnPeerConnectionEstablishedInfo cb1);
        protected abstract void OnConnectionClosed(ProductUserId remote);

        protected abstract void OnReceiveData(ProductUserId remote, ArraySegment<byte> data, byte channel);
        protected abstract void OnInternalData(ProductUserId remote, SocketId socket, InternalMessages msg, byte[] extradata = null);
        #endregion
    }

    public enum InternalMessages : byte
    {
        CONNECT = 0,
        ACCEPT_CONNECT = 1,
        DISCONNECT = 2,
        HOST_MIGRATION_READY = 3
    }

    public enum DisconnectReason : byte
    {
       SERVER_CLOSE = 0,
       KICKED = 1,
       MAX_CONNECTIONS_REACHED = 2
    }
}
