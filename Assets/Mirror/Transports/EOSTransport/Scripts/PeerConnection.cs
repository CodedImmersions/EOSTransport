using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

namespace EpicTransport
{
    public class PeerConnection
    {
        /// <summary>
        /// This user's product user id
        /// </summary>
        public ProductUserId ProductUserID { get; internal set; }

        /// <summary>
        /// The Mirror connection id of this user
        /// </summary>
        public int ConnectionID { get; internal set; }

        /// <summary>
        /// The EOS Socket ID of this connection. Socket IDs are ALWAYS different, so be careful what you use this for!
        /// </summary>
        public SocketId? SocketID { get; internal set; }

        /// <summary>
        /// Whether this connection is using NAT Transversal or Relay servers.
        /// </summary>
        public NetworkConnectionType P2PConnectionType { get; internal set; }
    }
}
