using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Mirror;
using System;
using System.Threading.Tasks;
using UnityEngine;

#if EOSTRANSPORT_DEBUG
using Unity.Profiling;
#endif

namespace EpicTransport
{
    public sealed class EpicClient : Common
    {
        internal SocketId MySocketId { get; private set; }

        internal ProductUserId HostProductId { get; private set; }
        public PeerConnection HostConnection { get; private set; }

        private Action OnConnected;
        private Action<ArraySegment<byte>, int> OnDataReceived;
        private Action<ArraySegment<byte>, int> OnDataSent;
        private Action<TransportError, string> OnError;
        private Action<Exception> OnTransportException;
        private Action OnDisconnected;

        private TimeSpan timeout;

#if EOSTRANSPORT_DEBUG
        static readonly ProfilerMarker k_MyCodeMarker = new ProfilerMarker("EpicClient Send Loop");
#endif

        internal EpicClient(EOSTransport transport, string address) : base(transport)
        {
            if (!Helper.IsValidPUID(address)) throw new ArgumentException("Cannot create client, the address provided is not a valid Product User ID.");

            MySocketId = new SocketId() { SocketName = Helper.GenerateHexString(16) };
            HostProductId = ProductUserId.FromString(address);

            OnConnected += () => transport.OnClientConnected.Invoke();
            OnDataReceived += (data, channel) => transport.OnClientDataReceived.Invoke(data, channel);
            OnDataSent += (data, channel) => transport.OnClientDataSent?.Invoke(data, channel);
            OnError += (error, message) => transport.OnClientError.Invoke(error, message);
            OnTransportException += (e) => transport.OnClientTransportException.Invoke(e);
            OnDisconnected += () => transport.OnClientDisconnected.Invoke();

            timeout = TimeSpan.FromSeconds(Mathf.Max(1, transport.timeout));
        }

        internal async void Connect(string address)
        {
            if (!Helper.IsValidPUID(address)) throw new FormatException("Error while connecting to host: invalid user id.");

            Initializing = true;
            SendInternalData(HostProductId, MySocketId, InternalMessages.CONNECT);
            
            DateTime start = DateTime.UtcNow;

            while (!Active)
            {
                await Task.Delay(50);
                if (DateTime.UtcNow - start >= timeout)
                {
                    TransportLogger.LogError($"Connection to {address} timed out after {timeout.TotalMilliseconds} ms.");
                    OnError.Invoke(TransportError.Timeout, $"Connection to {address} timed out after {timeout.TotalMilliseconds} ms.");
                    OnConnectionClosed(HostProductId);

                    EOSTransport.instance.connectedToLobby = false;
                    EOSTransport.instance.connectedLobbyInfo = null;
                    return;
                }
            }

            TransportLogger.Log("the connect method has succeeded.");
        }

        internal void Disconnect()
        {
            ShuttingDown = true;

            SendInternalData(HostProductId, MySocketId, InternalMessages.DISCONNECT);
            OnDisconnected.Invoke();

            DisposeThis();
        }

        internal void Send(ArraySegment<byte> data, int channel)
        {
#if EOSTRANSPORT_DEBUG
            k_MyCodeMarker.Begin();
#endif

            foreach (var p in FragmentData(data, channel))
            {
                byte[] raw = p.ToBytes();
                SendData(HostProductId, MySocketId, new ArraySegment<byte>(raw, 0, raw.Length), channel);
            }

            OnDataSent.Invoke(data, channel);

#if EOSTRANSPORT_DEBUG
            k_MyCodeMarker.End();
#endif
        }

        protected override void OnIncomingConnectionRequest(ref OnIncomingConnectionRequestInfo cb)
        {
            if (deadsockets.Contains(cb.SocketId.Value))
            {
                TransportLogger.LogWarning("Connection coming from dead socket. Ignoring...");
                return;
            }

            TransportLogger.Log($"incoming connection request from {cb.RemoteUserId}.");

            if (cb.RemoteUserId == HostProductId)
            {
                TransportLogger.Log("incoming connection is host.");
                //host connection here! only P2P connection we need since Mirror is server authoritive.

                AcceptConnectionOptions acceptopt = new AcceptConnectionOptions()
                {
                    LocalUserId = EOSManager.LocalUserProductID,
                    RemoteUserId = cb.RemoteUserId,
                    SocketId = cb.SocketId
                };

                Result res = p2p.AcceptConnection(ref acceptopt);
                if (res != Result.Success) throw new EOSSDKException(res, "Failed to accept host P2P connection!");

                TransportLogger.Log($"accepted host connection {cb.RemoteUserId}.");
            }
        }

        protected override void OnPeerConnected(ref OnPeerConnectionEstablishedInfo cb)
        {
            TransportLogger.Log("host connected");
            HostConnection = new PeerConnection()
            {
                ProductUserID = cb.RemoteUserId,
                ConnectionID = 0, //connection id for server is ALWAYS 0
                P2PConnectionType = cb.NetworkType,
                SocketID = cb.SocketId
            };
        }

        protected override void OnConnectionClosed(ProductUserId remote)
        {
            TransportLogger.Log("connection closed.");
            if (remote == HostProductId) HostConnection = null;
            OnDisconnected.Invoke();
        }

        protected override void OnReceiveData(ProductUserId remote, ArraySegment<byte> data, byte channel)
        {
            if (remote != HostProductId)
            {
                TransportLogger.LogWarning("Received packet from an unknown non-host peer. Ignoring...");
                return;
            }

            OnDataReceived.Invoke(data, channel);
        }

        protected override void OnInternalData(ProductUserId remote, SocketId socket, InternalMessages msg, byte[] extradata = null)
        {
            TransportLogger.Log(msg.ToString());

            switch (msg)
            {
                case InternalMessages.ACCEPT_CONNECT:
                    Initializing = false;
                    Active = true;

                    OnConnected.Invoke();

                    TransportLogger.Log($"Handshake with {remote} succeeded.");
                    break;

                case InternalMessages.DISCONNECT:
                    ShuttingDown = true;

                    if (extradata != null)
                    {
                        if (extradata[0] == EOSTransport.DRHeader)
                        {
                            DisconnectReason dr = (DisconnectReason)extradata[1];
                            TransportLogger.Log($"Disconnected from host ({remote}) with reason DisconnectReason.{dr}");
                        }
                        else TransportLogger.Log($"Disconnected from host ({remote})");
                    }
                    else TransportLogger.Log($"Disconnected from host ({remote})");

                    //NOTE: not needed since the host will close us instead
                    /*CloseConnectionOptions closeopt = new CloseConnectionOptions()
                    {
                        LocalUserId = MyPUID,
                        RemoteUserId = remote,
                        SocketId = socket
                    };

                    p2p.CloseConnection(ref closeopt);*/

                    deadsockets.Add(socket);
                    OnDisconnected.Invoke();

                    Active = false;
                    break;

                default:
                    TransportLogger.LogWarning($"Received unknown message from {remote}. Ignoring...");
                    break;
            }
        }

        internal void DisposeThis()
        {
            TransportLogger.Log("disposing EpicClient");
            base.Dispose();

            MySocketId = new SocketId();
            HostProductId = null;

            HostConnection = null;
            OnConnected = null;
            OnDataReceived = null;
            OnDataSent = null;
            OnError = null;
            OnTransportException = null;
            OnDisconnected = null;
        }
    }
}
