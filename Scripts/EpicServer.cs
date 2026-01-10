using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Mirror;
using System;
using System.Collections.Generic;

#if EOSTRANSPORT_DEBUG
using Unity.Profiling;
#endif

namespace EpicTransport
{
    public sealed class EpicServer : Common
    {
        public List<PeerConnection> clients { get; private set; }
        private BidirectionalDictionary<int, ProductUserId> userids;
        private BidirectionalDictionary<int, SocketId> sockets;

        private int maxconnections;
        private int nextconnectionid;

        private Action<int, string> OnConnected;
        private Action<int, ArraySegment<byte>, int> OnDataReceived;
        private Action<int, ArraySegment<byte>, int> OnDataSent;
        private Action<int, TransportError, string> OnError;
        private Action<int, Exception> OnTransportException;
        private Action<int> OnDisconnected;

#if EOSTRANSPORT_DEBUG
        static readonly ProfilerMarker k_MyCodeMarker = new ProfilerMarker("EpicServer Send Loop");
#endif

        internal EpicServer(EOSTransport transport, int maxconns) : base(transport)
        {
            OnConnected += (connid, clientaddress) => transport.OnServerConnectedWithAddress?.Invoke(connid, clientaddress);

            OnDataReceived += (connid, data, channel) => transport.OnServerDataReceived.Invoke(connid, data, channel);
            OnDataSent += (connid, data, channel) => transport.OnServerDataSent?.Invoke(connid, data, channel);
            OnError += (connid, error, message) => transport.OnServerError.Invoke(connid, error, message);
            OnTransportException += (connid, e) => transport.OnServerTransportException.Invoke(connid, e);
            OnDisconnected += (connid) => transport.OnServerDisconnected.Invoke(connid);

            clients = new List<PeerConnection>();
            userids = new BidirectionalDictionary<int, ProductUserId>();
            sockets = new BidirectionalDictionary<int, SocketId>();

            maxconnections = maxconns;
            nextconnectionid = 1; //we skip 0 because it is reserved for the server.

            Active = true;
        }

        internal void Send(int connid, ArraySegment<byte> data, int channel)
        {
#if EOSTRANSPORT_DEBUG
            k_MyCodeMarker.Begin();
#endif

            foreach (var p in FragmentData(data, channel))
            {
                byte[] raw = p.ToBytes();
                SendData(userids[connid], sockets[connid], new ArraySegment<byte>(raw, 0, raw.Length), channel);
            }

            OnDataSent.Invoke(connid, data, channel);

#if EOSTRANSPORT_DEBUG
            k_MyCodeMarker.End();
#endif
        }

        internal void SendInternalAll(InternalMessages msg, byte[] extradata = null)
        {
            foreach (PeerConnection conn in clients)
            {
                SendInternalData(conn.ProductUserID, conn.SocketID.Value, msg, extradata);
            }
        }

        internal void Disconnect(int connid)
        {
            if (userids == null || !userids.TryGetValue(connid, out ProductUserId p) || !sockets.TryGetValue(connid, out SocketId s)) return;

            SendInternalData(userids[connid], sockets[connid], InternalMessages.DISCONNECT, new byte[] { EOSTransport.DRHeader, (byte)DisconnectReason.KICKED });

            CloseConnectionOptions closeopt = new CloseConnectionOptions()
            {
                LocalUserId = MyPUID,
                RemoteUserId = p,
                SocketId = s
            };

            p2p.CloseConnection(ref closeopt);

            deadsockets.Add(s);

            clients.RemoveAll(p => p.ConnectionID == connid);
            userids.Remove(connid);
            sockets.Remove(connid);

            OnDisconnected.Invoke(connid);
        }

        internal void Shutdown()
        {
            ShuttingDown = true;

            foreach (PeerConnection conn in clients)
            {
                SendInternalData(conn.ProductUserID, conn.SocketID.Value, InternalMessages.DISCONNECT, new byte[] { EOSTransport.DRHeader, (byte)DisconnectReason.SERVER_CLOSE });

                CloseConnectionOptions closeopt = new CloseConnectionOptions() { LocalUserId = MyPUID, RemoteUserId = conn.ProductUserID, SocketId = conn.SocketID };
                p2p.CloseConnection(ref closeopt);
            }

            DisposeThis();
        }

        internal void DisposeThis()
        {
            TransportLogger.Log("disposing EpicClient");
            base.Dispose();

            maxconnections = -1;
            nextconnectionid = -1;
            clients.Clear();
            userids = null;
            sockets = null;


            OnConnected = null;
            OnDataReceived = null;
            OnDataSent = null;
            OnError = null;
            OnTransportException = null;
            OnDisconnected = null;
        }

        protected override void OnIncomingConnectionRequest(ref OnIncomingConnectionRequestInfo cb)
        {
            if (deadsockets.Contains(cb.SocketId.Value))
            {
                TransportLogger.LogWarning("Connection coming from dead socket. Ignoring...");
                return;
            }

            TransportLogger.Log($"incoming connection request from {cb.RemoteUserId}.");

            AcceptConnectionOptions acceptopt = new AcceptConnectionOptions()
            {
                LocalUserId = EOSManager.LocalUserProductID,
                RemoteUserId = cb.RemoteUserId,
                SocketId = cb.SocketId
            };

            Result res2 = p2p.AcceptConnection(ref acceptopt);
            if (res2 != Result.Success) throw new EOSSDKException(res2, $"Failed to accept client P2P connection! (Client: {cb.RemoteUserId})");
            TransportLogger.Log($"accepted connection {cb.RemoteUserId}.");
        }

        protected override void OnPeerConnected(ref OnPeerConnectionEstablishedInfo cb1)
        {
            ProductUserId remote = cb1.RemoteUserId;
            TransportLogger.Log($"{remote.ToString()} connected");

            //TODO: fix NRE
            //clients.Find(c => c.ProductUserID == remote).P2PConnectionType = cb1.NetworkType;
        }

        protected override void OnConnectionClosed(ProductUserId remote)
        {
            if (!userids.TryGetValue(remote, out int conn)) return;
            TransportLogger.Log($"P2P Connection with {remote} closed.");

            deadsockets.Add(sockets[conn]);

            clients.RemoveAll(p => p.ConnectionID == conn);
            userids.Remove(conn);
            sockets.Remove(conn);

            OnDisconnected.Invoke(conn);
        }

        protected override void OnReceiveData(ProductUserId remote, ArraySegment<byte> data, byte channel)
        {
            if (userids.TryGetValue(remote, out int connid)) OnDataReceived.Invoke(connid, data, channel);
            else TransportLogger.LogWarning($"Received packet from unknown connection ({remote}). Ignoring...");
        }

        protected override void OnInternalData(ProductUserId remote, SocketId socket, InternalMessages msg, byte[] extradata = null)
        {
            TransportLogger.Log(msg.ToString());

            switch (msg)
            {
                case InternalMessages.CONNECT:
                    if (clients.Count >= maxconnections)
                    {
                        TransportLogger.LogWarning($"{remote.ToString()} cannot connect: max connections reached.");
                        if (SendInternalData(remote, socket, InternalMessages.DISCONNECT, new byte[] { EOSTransport.DRHeader, (byte)DisconnectReason.MAX_CONNECTIONS_REACHED })) OnDataSent.Invoke(userids[remote], new ArraySegment<byte>(new byte[] { (byte)InternalMessages.DISCONNECT }), EOSTransport.InternalChannel);
                    }

                    SendInternalData(remote, socket, InternalMessages.ACCEPT_CONNECT);
                    int connid = nextconnectionid++; //puts the current nextconnectionid into connid, then increments nextconnectionid by one afterwards.

                    clients.Add(new PeerConnection()
                    {
                        ProductUserID = remote,
                        ConnectionID = connid,
                        SocketID = socket
                    });

                    userids.Add(connid, remote);
                    sockets.Add(connid, socket);

                    TransportLogger.Log($"remote client {remote} connected; assigning connection id {connid}.");
                    OnConnected.Invoke(connid, remote.ToString());
                    break;

                case InternalMessages.DISCONNECT:

                    if (!userids.TryGetValue(remote, out int conn)) return;

                    if (extradata != null)
                    {
                        if (extradata[0] == EOSTransport.DRHeader)
                        {
                            DisconnectReason dr = (DisconnectReason)extradata[1];
                            TransportLogger.Log($"Disconnecting connection {conn} ({remote}) with reason DisconnectReason.{dr}");
                        }
                        else TransportLogger.Log($"Disconnecting connection {conn} ({remote})");
                    }
                    else TransportLogger.Log($"Disconnecting connection {conn} ({remote})");

                    CloseConnectionOptions closeopt = new CloseConnectionOptions()
                    {
                        LocalUserId = MyPUID,
                        RemoteUserId = remote,
                        SocketId = socket
                    };

                    p2p.CloseConnection(ref closeopt);

                    deadsockets.Add(socket);

                    clients.RemoveAll(p => p.ConnectionID == conn);
                    userids.Remove(conn);
                    sockets.Remove(conn);

                    OnDisconnected.Invoke(conn);

                    break;

                default:
                    TransportLogger.LogWarning($"Received unknown message from {remote}. Ignoring...");
                    break;
            }
        }

        internal string GetClientAddress(int connection) => userids[connection].ToString();
    }
}
