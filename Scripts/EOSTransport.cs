using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEngine;

using Mirror;
using Attribute = Epic.OnlineServices.Lobby.Attribute;

namespace EpicTransport
{
    public class EOSTransport : Transport
    {
        public static EOSTransport Instance => instance; //literally only used for people upgrading from older versions.
        public static EOSTransport instance;

        public const string Version = "3.0.0";
        public const string EOSVersion = "1.18.1.2";

        [Tooltip("The time in seconds before a connection to the server times out.")] public int timeout = 25;

        [Tooltip("Settings for how P2P connections are handled." +
            "\n\nNo Relays: Never uses the EOS Relay. Instead, uses NAT Transversal, which will reduce latency, but at the cost of exposing player's IPs." +
            "\n\nAllow Relays: Attempts to use NAT Transversal, and then uses the EOS Relay if NAT Transversal fails. Note that NAT Transversal will reduce latency, but at the cost of exposing player's IPs." +
            "\n\nForce Relays: Always uses the EOS Relay. This will hide player's IP Addresses from peers, but at the cost of increasing latency.")]
        public RelayControl relayControl = RelayControl.AllowRelays;
        public bool hostMigrationEnabled = false;

        public static bool HostMigrationEnabled => instance.hostMigrationEnabled;

        public static readonly List<PacketReliability> Channels = new List<PacketReliability>() { PacketReliability.ReliableOrdered, PacketReliability.UnreliableUnordered };

        public const string EpicScheme = "epic";
        public const string DefaultAttributeKey = "default";
        public const string HostAddressKey = "host_address";

        internal const byte InternalChannel = byte.MaxValue;
        internal const byte HostMigrationChannel = 254;

        //for DisconnectReason to show that the following data is the disconnect reason.
        internal const byte DRHeader = 201;

        public static bool ConnectedToLobby => instance.connectedToLobby;

        private bool connectedToLobby;
        private LobbyInfo connectedLobbyInfo = null;
        public static LobbyInfo ConnectedLobbyInfo => instance.connectedLobbyInfo;

        internal Common activeconnection;
        private EpicClient client;
        private EpicServer server;

        private void Awake()
        {
            instance = this;
        }

        private IEnumerator Start()
        {
            yield return new WaitUntil(() => EOSManager.Initialized);

            InternalStuff();

            SetRelayControlOptions controlopt = new SetRelayControlOptions() { RelayControl = relayControl };
            EOSManager.GetP2PInterface().SetRelayControl(ref controlopt);
        }

        public void ChangeRelayControl(RelayControl control)
        {
            relayControl = control;

            SetRelayControlOptions controlopt = new SetRelayControlOptions() { RelayControl = control };
            EOSManager.GetP2PInterface().SetRelayControl(ref controlopt);
        }

        #region Client
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool ClientConnected() => activeconnection != null && client != null && activeconnection.Active;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ClientActive() => client != null && client.Active;

        public override void ClientConnect(string address)
        {
            if (!EOSManager.Initialized)
            {
                OnClientDisconnected?.Invoke();
                throw new InvalidOperationException("Epic Online Services isn't initialized yet, cannot start client.");

            }

            if (activeconnection != null)
            {
                //OnClientDisconnected?.Invoke();
                throw new InvalidOperationException($"A connection is already running, cannot start client.\nIs Connection Client? {activeconnection is EpicClient} \nIs Connection Server? {activeconnection is EpicServer}");
            }

            NetworkManager.singleton.networkAddress = address;
            EpicClient c = new EpicClient(this, address);
            if (c != null)
            {
                activeconnection = c;
                client = c;
            }
        }

        public override void ClientConnect(Uri uri)
        {
            if (!EOSManager.Initialized)
            {
                OnClientDisconnected?.Invoke();
                throw new InvalidOperationException("Epic Online Services isn't initialized yet, cannot start client.");
                
            }

            if (activeconnection != null)
            {
                //OnClientDisconnected?.Invoke();
                throw new InvalidOperationException($"A connection is already running, cannot start client.\nIs Connection Client? {activeconnection is EpicClient} \nIs Connection Server? {activeconnection is EpicServer}");
            }

            TransportLogger.Log($"ClientConnect called, target address {uri.Host}");
            NetworkManager.singleton.networkAddress = uri.Host;
            EpicClient c = new EpicClient(this, uri.Host);
            if (c != null)
            {
                activeconnection = c;
                client = c;
            }
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId = 0) => client?.Send(segment, channelId);

        public override void ClientDisconnect()
        {
            client?.Disconnect();
            client = null;
            activeconnection = null;
        }
        #endregion

        #region Server
        public override Uri ServerUri()
        {
            UriBuilder urib = new UriBuilder() { Scheme = EpicScheme, Host = EOSManager.LocalUserProductIDString };
            return urib.Uri;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool ServerActive() => activeconnection != null && server != null && activeconnection.Active;

        public override void ServerStart()
        {
            TransportLogger.Log("Server starting");

            if (ClientConnected()) { TransportLogger.LogWarning("Client already running!"); return; }

            if (!ServerActive())
            {
                server = new EpicServer(this, NetworkServer.maxConnections);
                activeconnection = server;

                //TODO: add metrics support
            }
        }

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = 0) => server.Send(connectionId, segment, channelId);

        public override void ServerDisconnect(int connectionId) => server?.Disconnect(connectionId);

        public override string ServerGetClientAddress(int connectionId) => server?.GetClientAddress(connectionId);

        public override void ServerStop()
        {
            server?.Shutdown();
            server = null;
            activeconnection = null;
        }
        #endregion

        #region Updates
        public override void ClientEarlyUpdate()
        {
            if (!enabled) return;

            EOSManager.Tick();

            if (client != null && !client.Active && !client.Initializing)
            {
                TransportLogger.Log("client.Connect called");
                client.Connect(client.HostProductId.ToString());
            }

            activeconnection?.ReceiveData();
        }
        override public void ClientLateUpdate() { } //not needed at this time

        public override void ServerEarlyUpdate()
        {
            if (!enabled) return;

            EOSManager.Tick();
            activeconnection?.ReceiveData();
        }

        public override void ServerLateUpdate() { } //not needed at this time
        #endregion

        public override void Shutdown()
        {
            server?.Shutdown();
            client?.Disconnect();

            server = null;
            client = null;
            activeconnection = null;
            TransportLogger.Log("Transport shut down.");

            GC.Collect();
        }

        public override bool Available() => EOSManager.Initialized;

        public override int GetMaxPacketSize(int channelId = 0) => P2PInterface.MAX_PACKET_SIZE - Packet.HeaderSize;

        public override int GetBatchThreshold(int channelId = 0) => P2PInterface.MAX_PACKET_SIZE;

        public override bool IsEncrypted => false; //TODO: maybe coming in the future??? not 100% sure yet

        #region Internal
        private ulong updateid;

        private void InternalStuff()
        {
            OnJoinedLobby += (id) =>
            {
                AddNotifyLobbyMemberStatusReceivedOptions updateopt = new AddNotifyLobbyMemberStatusReceivedOptions();
                updateid = EOSManager.GetLobbyInterface().AddNotifyLobbyMemberStatusReceived(ref updateopt, null, (ref LobbyMemberStatusReceivedCallbackInfo cb) =>
                {
                    OnLobbyMemberStatusUpdated?.Invoke(cb);
                    if (cb.CurrentStatus == LobbyMemberStatus.Promoted)
                    {
                        if (cb.TargetUserId == EOSManager.LocalUserProductID) ConnectedLobbyInfo.IsLobbyOwner = true;
                        else ConnectedLobbyInfo.IsLobbyOwner = false;
                    }
                });
            };

            OnLeftLobby += () => EOSManager.GetLobbyInterface().RemoveNotifyLobbyMemberStatusReceived(updateid);
        }
        #endregion


        #region Lobby Methods

        //arg1 (string) is lobby id
        public static Action<string> OnJoinedLobby;
        public static Action OnLeftLobby;
        public static Action<LobbyMemberStatusReceivedCallbackInfo> OnLobbyMemberStatusUpdated;

        /// <summary>
        /// Creates a public lobby, then starts Mirror host if successful.
        /// </summary>
        /// <param name="lobbyId">The ID of the lobby. Must be between 4 and 60 characters.</param>
        /// <param name="maxPlayers">The maximum allowed members of the lobby. Value must be between 1 and 64.</param>
        /// <param name="attributes">(Optional) Extra attributes added to the lobby on creation.</param>
        /// <param name="bucketId">(Optional) The optional Bucket ID to add to this lobby, usually in GameMode:Region:MapName format.</param>
        /// <param name="permissionLevel">(Optional) Permission level of the lobby. Do not change this if you are not using the Auth Interface.</param>
        /// <param name="presenceEnabled">(Optional, Auth Interface Only) If lobby presence is broadcasted on your Epic Games profile..</param>
        public static void CreateLobby(string lobbyId, uint maxPlayers, List<AttributeData> attributes = null, string bucketId = null, LobbyPermissionLevel permissionLevel = LobbyPermissionLevel.Publicadvertised, bool presenceEnabled = false)
        {
            if (!EOSManager.Initialized) return;
            if (lobbyId.Length is < LobbyInterface.MIN_LOBBYIDOVERRIDE_LENGTH or > LobbyInterface.MAX_LOBBYIDOVERRIDE_LENGTH) throw new ArgumentOutOfRangeException(nameof(lobbyId), $"Specified lobby id must be between {LobbyInterface.MIN_LOBBYIDOVERRIDE_LENGTH} and {LobbyInterface.MAX_LOBBYIDOVERRIDE_LENGTH} characters. Current length: {lobbyId.Length}.");
            if (maxPlayers is < 1 or > LobbyInterface.MAX_LOBBY_MEMBERS) throw new ArgumentOutOfRangeException(nameof(maxPlayers), $"Max players must be between 1 and {LobbyInterface.MAX_LOBBY_MEMBERS}. Current max players: {maxPlayers}.");

            lobbyId = lobbyId.Replace(' ', '-'); //remove space to make URL-safe
            if (!Helper.IsUrlSafe(lobbyId)) throw new FormatException("Lobby ID is not URL-safe. Please amke it URL-safe, then try again.");

            CreateLobbyOptions createopt = new CreateLobbyOptions()
            {
                LocalUserId = EOSManager.LocalUserProductID,
                BucketId = bucketId != null ? bucketId : $"{Application.version}:{Version}:{EOSVersion}",
                MaxLobbyMembers = maxPlayers,
                DisableHostMigration = !HostMigrationEnabled,
                EnableJoinById = true,
                PermissionLevel = permissionLevel,
                PresenceEnabled = presenceEnabled,
                LobbyId = lobbyId
            };

            EOSManager.GetLobbyInterface().CreateLobby(ref createopt, null, (ref CreateLobbyCallbackInfo cb) =>
            {
                if (cb.ResultCode == Result.LobbyLobbyAlreadyExists) TransportLogger.LogError($"Failed to create lobby: lobby already exists in the EOS system. Please use the {nameof(JoinLobby)}(string) method instead.");
                if (cb.ResultCode != Result.Success) throw new EOSSDKException(cb.ResultCode, "Failed to create lobby!");

                if (attributes == null) attributes = new List<AttributeData>();
                attributes.Insert(0, new AttributeData() { Key = DefaultAttributeKey, Value = DefaultAttributeKey }); //why do we need this? note to self, look into why it's needed.
                attributes.Insert(1, new AttributeData() { Key = HostAddressKey, Value = EOSManager.LocalUserProductIDString });

                UpdateLobbyModificationOptions updateopt = new UpdateLobbyModificationOptions() { LocalUserId = EOSManager.LocalUserProductID, LobbyId = cb.LobbyId };
                EOSManager.GetLobbyInterface().UpdateLobbyModification(ref updateopt, out LobbyModification mod);

                foreach (AttributeData attr in attributes)
                {
                    LobbyModificationAddAttributeOptions addopt = new LobbyModificationAddAttributeOptions() { Attribute = attr, Visibility = LobbyAttributeVisibility.Public };
                    mod.AddAttribute(ref addopt);
                }

                string lobbyid = cb.LobbyId;

                UpdateLobbyOptions updateopt2 = new UpdateLobbyOptions() { LobbyModificationHandle = mod };
                EOSManager.GetLobbyInterface().UpdateLobby(ref updateopt2, null, (ref UpdateLobbyCallbackInfo cb2) =>
                {
                    if (cb2.ResultCode != Result.Success) throw new EOSSDKException(cb2.ResultCode, "Failed to add lobby attributes!");

                    CopyLobbyDetailsHandleOptions detailsopt = new CopyLobbyDetailsHandleOptions() { LocalUserId = EOSManager.LocalUserProductID, LobbyId = lobbyid };
                    Result res = EOSManager.GetLobbyInterface().CopyLobbyDetailsHandle(ref detailsopt, out LobbyDetails details);
                    if (res != Result.Success) throw new EOSSDKException(res, "Failed to get lobby details when joining lobby by ID!");

                    NetworkManager.singleton.StartHost();

                    instance.connectedLobbyInfo = new LobbyInfo()
                    {
                        IsLobbyOwner = true,
                        LobbyId = lobbyid,
                        CurrentLobbyDetails = details
                    };

                    instance.connectedToLobby = true;

                    OnJoinedLobby?.Invoke(lobbyId);
                });
            });
        }

        /// <summary>
        /// Joins a lobby based on the <see cref="LobbyDetails"/> provided. Starts Mirror client if successful.
        /// </summary>
        /// <param name="lobby">The <see cref="LobbyDetails"/> of the lobby to join.</param>
        public static void JoinLobby(LobbyDetails lobby)
        {
            if (!EOSManager.Initialized) return;

            JoinLobbyOptions joinopt = new JoinLobbyOptions() { LocalUserId = EOSManager.LocalUserProductID, LobbyDetailsHandle = lobby };
            EOSManager.GetLobbyInterface().JoinLobby(ref joinopt, null, (ref JoinLobbyCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success) throw new EOSSDKException(cb.ResultCode, "Failed to join lobby!");

                LobbyDetailsCopyAttributeByKeyOptions copyopt = new LobbyDetailsCopyAttributeByKeyOptions() { AttrKey = HostAddressKey };
                Result res1 = lobby.CopyAttributeByKey(ref copyopt, out Attribute? hostaddress);
                if (res1 != Result.Success) throw new EOSSDKException(res1, "Failed to get Host Address when joining lobby!");

                UriBuilder urib = new UriBuilder() { Scheme = EpicScheme, Host = hostaddress.Value.Data.Value.Value.AsUtf8 };
                NetworkManager.singleton.StartClient(urib.Uri);

                LobbyDetailsCopyInfoOptions copyopt2 = new LobbyDetailsCopyInfoOptions();
                Result res2 = lobby.CopyInfo(ref copyopt2, out LobbyDetailsInfo? info);
                if (res2 != Result.Success) throw new EOSSDKException(res2, "Failed to copy lobby info!");

                instance.connectedLobbyInfo = new LobbyInfo()
                {
                    IsLobbyOwner = false,
                    LobbyId = info.Value.LobbyId,
                    CurrentLobbyDetails = lobby
                };

                instance.connectedToLobby = true;

                OnJoinedLobby?.Invoke(info.Value.LobbyId);
            });
        }

        /// <summary>
        /// Joins a lobby using the specified id.
        /// Note that this will only work if the lobby has <see cref="CreateLobbyOptions.EnableJoinById"/> enabled.
        /// </summary>
        /// <param name="id">The ID of the lobby to join.</param>
        public static void JoinLobbyByID(string id)
        {
            if (!EOSManager.Initialized) return;

            JoinLobbyByIdOptions idopt = new JoinLobbyByIdOptions() { LocalUserId = EOSManager.LocalUserProductID, LobbyId = id };
            EOSManager.GetLobbyInterface().JoinLobbyById(ref idopt, null, (ref JoinLobbyByIdCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success) throw new EOSSDKException(cb.ResultCode, "Failed to join lobby by ID!");

                CopyLobbyDetailsHandleOptions detailsopt = new CopyLobbyDetailsHandleOptions() { LocalUserId = EOSManager.LocalUserProductID, LobbyId = id };
                Result res = EOSManager.GetLobbyInterface().CopyLobbyDetailsHandle(ref detailsopt, out LobbyDetails details);
                if (res != Result.Success) throw new EOSSDKException(res, "Failed to get lobby details when joining lobby by ID!");

                LobbyDetailsCopyAttributeByKeyOptions copyopt = new LobbyDetailsCopyAttributeByKeyOptions() { AttrKey = HostAddressKey };
                Result res2 = details.CopyAttributeByKey(ref copyopt, out Attribute? hostaddress);
                if (res2 != Result.Success) throw new EOSSDKException(res2, "Failed to get Host Address when joining lobby by ID!");

                string ha = hostaddress.Value.Data.Value.Value.AsUtf8;
                TransportLogger.Log(ha);
                UriBuilder urib = new UriBuilder() { Scheme = EpicScheme, Host = ha };
                NetworkManager.singleton.StartClient(urib.Uri);

                instance.connectedLobbyInfo = new LobbyInfo()
                {
                    IsLobbyOwner = false,
                    LobbyId = id,
                    CurrentLobbyDetails = details
                };

                instance.connectedToLobby = true;
                OnJoinedLobby?.Invoke(id);
            });
            
        }

        /// <summary>
        /// Finds all the current open lobbies.
        /// </summary>
        /// <param name="callback">The callback for once the search has finished. Note: will not be called if there is an error thrown.</param>
        /// <param name="maxResults">The maximum results returned. Value must be between 1 and 200.</param>
        public static void FindLobbies(Action<List<LobbyDetails>> callback, uint maxResults = 200)
        {
            SearchForLobbiesByAttribute(new AttributeData { Key = DefaultAttributeKey, Value = DefaultAttributeKey }, maxResults, cb => { callback.Invoke(cb); });
        }

        /// <summary>
        /// Searches for lobbies based on attribute of the lobby.
        /// </summary>
        /// <param name="attribute">The <see cref="Epic.OnlineServices.Lobby.Attribute"/> used to search.</param>
        /// <param name="maxResults">The maximum results returned. Value must be between 1 and 200.</param>
        /// <param name="callback">The callback for once the search has finished. Note: will not be called if there is an error thrown.</param>
        public static void SearchForLobbiesByAttribute(AttributeData attribute, uint maxResults, Action<List<LobbyDetails>> callback)
        {
            if (maxResults > LobbyInterface.MAX_SEARCH_RESULTS) throw new ArgumentOutOfRangeException(nameof(maxResults), $"Max results must be between 1 and {LobbyInterface.MAX_SEARCH_RESULTS}. Current max results: {maxResults}.");

            List<LobbyDetails>result = new List<LobbyDetails>();

            CreateLobbySearchOptions searchopt = new CreateLobbySearchOptions() { MaxResults = maxResults };
            Result res1 = EOSManager.GetLobbyInterface().CreateLobbySearch(ref searchopt, out LobbySearch search);
            if (res1 != Result.Success) throw new EOSSDKException(res1, "Failed to create a lobby search!");

            LobbySearchSetParameterOptions setparamsopt = new LobbySearchSetParameterOptions() { Parameter = attribute };
            Result res2 = search.SetParameter(ref setparamsopt);
            if (res2 != Result.Success) throw new EOSSDKException(res2, "Failed to set the params of a lobby search!");

            LobbySearchFindOptions findopt = new LobbySearchFindOptions() { LocalUserId = EOSManager.LocalUserProductID };
            search.Find(ref findopt, null, (ref LobbySearchFindCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success && cb.ResultCode != Result.NotFound) throw new EOSSDKException(cb.ResultCode, "Failed to search for lobbies!");

                LobbySearchGetSearchResultCountOptions countopt = new LobbySearchGetSearchResultCountOptions();
                for (uint i = 0; i < search.GetSearchResultCount(ref countopt); i++)
                {
                    LobbySearchCopySearchResultByIndexOptions copyopt = new LobbySearchCopySearchResultByIndexOptions() { LobbyIndex = i };
                    Result res3 = search.CopySearchResultByIndex(ref copyopt, out LobbyDetails details);
                    if (res3 != Result.Success) throw new EOSSDKException(res3, "Failed to copy lobby by index in lobby search!");

                    result.Add(details);
                }

                callback.Invoke(result);
            });
        }

        /// <summary>
        /// Searches for lobbies based on the id of the lobby.
        /// </summary>
        /// <param name="id">The ID of the lobby to search for.</param>
        /// <param name="callback">The callback for once the search has finished. Note: will not be called if there is an error thrown</param>
        /// <param name="maxResults">The maximum results returned. Value must be between 1 and 200.</param>
        public static void SearchForLobbiesByID(string id, Action<List<LobbyDetails>> callback, uint maxResults = 1)
        {
            if (maxResults > LobbyInterface.MAX_SEARCH_RESULTS) throw new ArgumentOutOfRangeException(nameof(maxResults), $"Max results must be between 1 and {LobbyInterface.MAX_SEARCH_RESULTS}. Current max results: {maxResults}.");

            List<LobbyDetails> result = new List<LobbyDetails>();

            CreateLobbySearchOptions searchopt = new CreateLobbySearchOptions() { MaxResults = maxResults };
            Result res1 = EOSManager.GetLobbyInterface().CreateLobbySearch(ref searchopt, out LobbySearch search);
            if (res1 != Result.Success) throw new EOSSDKException(res1, "Failed to create a lobby search!");

            LobbySearchSetLobbyIdOptions setidopt = new LobbySearchSetLobbyIdOptions() { LobbyId = id };
            Result res2 = search.SetLobbyId(ref setidopt);
            if (res2 != Result.Success) throw new EOSSDKException(res2, "Failed to set the lobby id of a lobby search!");

            LobbySearchFindOptions findopt = new LobbySearchFindOptions() { LocalUserId = EOSManager.LocalUserProductID };
            search.Find(ref findopt, null, (ref LobbySearchFindCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success && cb.ResultCode != Result.NotFound) throw new EOSSDKException(cb.ResultCode, "Failed to search for lobbies!");

                LobbySearchGetSearchResultCountOptions countopt = new LobbySearchGetSearchResultCountOptions();
                for (uint i = 0; i < search.GetSearchResultCount(ref countopt); i++)
                {
                    LobbySearchCopySearchResultByIndexOptions copyopt = new LobbySearchCopySearchResultByIndexOptions() { LobbyIndex = i };
                    Result res3 = search.CopySearchResultByIndex(ref copyopt, out LobbyDetails details);
                    if (res3 != Result.Success) throw new EOSSDKException(res3, "Failed to copy lobby by index in lobby search!");

                    result.Add(details);
                }

                callback.Invoke(result);
            });
        }

        /// <summary>
        /// Searches for lobbies based on a member of the lobby.
        /// </summary>
        /// <param name="member">The <see cref="ProductUserId"/> of the member to search for.</param>
        /// <param name="callback">The callback for once the search has finished. Note: will not be called if there is an error thrown</param>
        /// <param name="maxResults">The maximum results returned. Value must be between 1 and 200.</param>
        public static void SearchForLobbiesByMember(ProductUserId member, Action<List<LobbyDetails>> callback, uint maxResults = 1)
        {
            if (maxResults > LobbyInterface.MAX_SEARCH_RESULTS) throw new ArgumentOutOfRangeException(nameof(maxResults), $"Max results must be between 1 and {LobbyInterface.MAX_SEARCH_RESULTS}. Current max results: {maxResults}.");

            List<LobbyDetails> result = new List<LobbyDetails>();

            CreateLobbySearchOptions searchopt = new CreateLobbySearchOptions() { MaxResults = maxResults };
            Result res1 = EOSManager.GetLobbyInterface().CreateLobbySearch(ref searchopt, out LobbySearch search);
            if (res1 != Result.Success) throw new EOSSDKException(res1, "Failed to create a lobby search!");

            LobbySearchSetTargetUserIdOptions setmemberopt = new LobbySearchSetTargetUserIdOptions() { TargetUserId = member };
            Result res2 = search.SetTargetUserId(ref setmemberopt);
            if (res2 != Result.Success) throw new EOSSDKException(res2, "Failed to set the target user of a lobby search!");

            LobbySearchFindOptions findopt = new LobbySearchFindOptions() { LocalUserId = EOSManager.LocalUserProductID };
            search.Find(ref findopt, null, (ref LobbySearchFindCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success && cb.ResultCode != Result.NotFound) throw new EOSSDKException(cb.ResultCode, "Failed to search for lobbies!");

                LobbySearchGetSearchResultCountOptions countopt = new LobbySearchGetSearchResultCountOptions();
                for (uint i = 0; i < search.GetSearchResultCount(ref countopt); i++)
                {
                    LobbySearchCopySearchResultByIndexOptions copyopt = new LobbySearchCopySearchResultByIndexOptions() { LobbyIndex = i };
                    Result res3 = search.CopySearchResultByIndex(ref copyopt, out LobbyDetails details);
                    if (res3 != Result.Success) throw new EOSSDKException(res3, "Failed to copy lobby by index in lobby search!");

                    result.Add(details);
                }

                callback.Invoke(result);
            });
        }

        /// <summary>
        /// Leaves/Destroys the currently connected lobby and stops the Mirror client/server.
        /// </summary>
        public static void LeaveLobby() //TODO: move host promotion picking the the controller
        {
            if (!ConnectedToLobby) return;

            string id = ConnectedLobbyInfo.LobbyId;
            bool host = ConnectedLobbyInfo.IsLobbyOwner && NetworkServer.active;
            bool migration = HostMigrationEnabled;

            if (host && !migration) { DestroyTheLobby(id); return; }

            if (host && migration)
            {
                ProductUserId nextHost = default;

                LobbyDetailsGetMemberCountOptions countopt = new LobbyDetailsGetMemberCountOptions();
                uint count = ConnectedLobbyInfo.CurrentLobbyDetails.GetMemberCount(ref countopt);

                if (count <= 1)
                {
                    TransportLogger.Log("Only host in lobby; destroying instead of migrating.");
                    DestroyTheLobby(id);
                    return;
                }

                for (uint i = 0; i < count; i++)
                {
                    LobbyDetailsGetMemberByIndexOptions getopt = new LobbyDetailsGetMemberByIndexOptions() { MemberIndex = i };
                    ProductUserId member = ConnectedLobbyInfo.CurrentLobbyDetails.GetMemberByIndex(ref getopt);

                    if (member != EOSManager.LocalUserProductID) { nextHost = member; break; }
                }

                if (nextHost == default)
                {
                    TransportLogger.LogWarning("No valid member found to promote. Destroying lobby.");
                    DestroyTheLobby(id);
                    return;
                }

                if (PromoteMember(nextHost))
                {
                    TransportLogger.Log($"Host successfully promoted to {nextHost}. Leaving lobby...");
                    LeaveTheLobby(id);
                }
                else
                {
                    TransportLogger.LogError($"Failed to promote new host. Destroying lobby as fallback.");
                    DestroyTheLobby(id);
                }

                return;
            }

            LeaveTheLobby(id);
        }

        private static void LeaveTheLobby(string lobbyId)
        {
            LeaveLobbyOptions leaveopt = new LeaveLobbyOptions()
            {
                LocalUserId = EOSManager.LocalUserProductID,
                LobbyId = lobbyId
            };

            EOSManager.GetLobbyInterface().LeaveLobby(ref leaveopt, null, (ref LeaveLobbyCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success && cb.ResultCode != Result.AlreadyPending)
                    TransportLogger.LogError($"Failed to leave lobby: {cb.ResultCode}");

                NetworkManager.singleton.StopHost();
                NetworkManager.singleton.StopClient();

                instance.connectedLobbyInfo = null;
                instance.connectedToLobby = false;
                OnLeftLobby?.Invoke();
            });
        }

        private static void DestroyTheLobby(string lobbyId)
        {
            DestroyLobbyOptions destroyopt = new DestroyLobbyOptions()
            {
                LocalUserId = EOSManager.LocalUserProductID,
                LobbyId = lobbyId
            };

            EOSManager.GetLobbyInterface().DestroyLobby(ref destroyopt, null, (ref DestroyLobbyCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success && cb.ResultCode != Result.AlreadyPending)
                    TransportLogger.LogError($"Failed to destroy lobby: {cb.ResultCode}");

                NetworkManager.singleton.StopHost();

                instance.connectedLobbyInfo = null;
                instance.connectedToLobby = false;
                OnLeftLobby?.Invoke();
            });
        }

        /// <summary>
        /// Kicks the provided lobby member from the lobby.
        /// </summary>
        /// <param name="member">The <see cref="ProductUserId"/> of the member to kick.</param>
        public static void KickMember(ProductUserId member)
        {
            if (!ConnectedToLobby) return;
            if (ConnectedLobbyInfo.IsLobbyOwner && NetworkServer.active)
            {
                KickMemberOptions kickopt = new KickMemberOptions()
                {
                    LocalUserId = EOSManager.LocalUserProductID,
                    TargetUserId = member,
                    LobbyId = ConnectedLobbyInfo.LobbyId
                };

                EOSManager.GetLobbyInterface().KickMember(ref kickopt, null, (ref KickMemberCallbackInfo cb) =>
                {
                    if (cb.ResultCode != Result.Success) throw new EOSSDKException(cb.ResultCode, "Failed to kick lobbby member!");
                    TransportLogger.Log($"Member {member} kicked from lobby.");
                });
            }
        }

        /// <summary>
        /// Kicks the provided lobby member from the lobby.
        /// </summary>
        /// <param name="connectionId">The connection id of the member to kick.</param>
        public static void KickMember(int connectionId)
        {
            if (!ConnectedToLobby) return;
            if (ConnectedLobbyInfo.IsLobbyOwner && NetworkServer.active)
            {
                instance.ServerDisconnect(connectionId);
            }
        }

        /// <summary>
        /// Promotes the provided lobby member to become the new lobby owner.
        /// </summary>
        /// <param name="newOwner">The <see cref="ProductUserId"/> of the new lobby owner.</param>
        public static bool PromoteMember(ProductUserId newOwner)
        {
            if (!ConnectedToLobby) return false;
            if (ConnectedLobbyInfo.IsLobbyOwner && NetworkServer.active)
            {
                PromoteMemberOptions promoteopt = new PromoteMemberOptions() { LocalUserId = EOSManager.LocalUserProductID, TargetUserId = newOwner, LobbyId = ConnectedLobbyInfo.LobbyId };

                EOSManager.GetLobbyInterface().PromoteMember(ref promoteopt, null, (ref PromoteMemberCallbackInfo cb) =>
                {
                    if (cb.ResultCode != Result.Success && cb.ResultCode != Result.AlreadyPending) throw new EOSSDKException(cb.ResultCode, "Failed to promote lobby member!");
                });
            }

            return true;
        }

        /// <summary>
        /// Add/Update an attribute to the current lobby.
        /// </summary>
        /// <param name="attribute"></param>
        public static void UpdateAttribute(AttributeData attribute)
        {
            if (!ConnectedToLobby || !ConnectedLobbyInfo.IsLobbyOwner) return;

            UpdateLobbyModificationOptions modopt = new UpdateLobbyModificationOptions() { LocalUserId = EOSManager.LocalUserProductID, LobbyId = ConnectedLobbyInfo.LobbyId };
            EOSManager.GetLobbyInterface().UpdateLobbyModification(ref modopt, out LobbyModification mod);

            LobbyModificationAddAttributeOptions addopt = new LobbyModificationAddAttributeOptions() { Attribute = attribute, Visibility = LobbyAttributeVisibility.Public };
            Result res = mod.AddAttribute(ref addopt);
            if (res != Result.Success) throw new EOSSDKException(res, "Failed to add lobby attribute!");

            UpdateLobbyOptions updateopt = new UpdateLobbyOptions() { LobbyModificationHandle = mod };
            EOSManager.GetLobbyInterface().UpdateLobby(ref updateopt, null, (ref UpdateLobbyCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success) throw new EOSSDKException(cb.ResultCode, "Failed to update lobby!");
                TransportLogger.Log($"updated attribute. key: {attribute.Key}. value: {attribute.Value}.");
            });
        }
        #endregion
    }

    [Serializable]
    public class LobbyInfo
    {
        public bool IsLobbyOwner { get; internal set; }

        public string LobbyId { get; internal set; }

        //TODO: figure out how to set
        //public List<Attribute> Attributes { get; internal set; } 
        
        public LobbyDetails CurrentLobbyDetails { get; internal set; }

        public LobbyInfo() { }

        public uint GetPlayerCount()
        {
            LobbyDetailsGetMemberCountOptions mco = new LobbyDetailsGetMemberCountOptions();
            return CurrentLobbyDetails.GetMemberCount(ref mco);
        }

        public ProductUserId GetMemberByIndex(uint index)
        {
            LobbyDetailsGetMemberByIndexOptions getopt = new LobbyDetailsGetMemberByIndexOptions() { MemberIndex = index };
            return CurrentLobbyDetails.GetMemberByIndex(ref getopt);
        }

        public uint GetMaxPlayers()
        {
            LobbyDetailsCopyInfoOptions cio = new LobbyDetailsCopyInfoOptions();
            CurrentLobbyDetails.CopyInfo(ref cio, out LobbyDetailsInfo? info);
            return info.Value.MaxMembers;
        }

        #region Get Attribute Method Overloads
        public bool? GetBoolAttribute(string key)
        {
            LobbyDetailsCopyAttributeByKeyOptions opt = new LobbyDetailsCopyAttributeByKeyOptions() { AttrKey = key };
            CurrentLobbyDetails.CopyAttributeByKey(ref opt, out Attribute? @out);
            return @out.Value.Data.Value.Value.AsBool;
        }

        public long? GetInt64Attribute(string key)
        {
            LobbyDetailsCopyAttributeByKeyOptions opt = new LobbyDetailsCopyAttributeByKeyOptions() { AttrKey = key };
            CurrentLobbyDetails.CopyAttributeByKey(ref opt, out Attribute? @out);
            return @out.Value.Data.Value.Value.AsInt64;
        }

        public double? GetDoubleAttribute(string key)
        {
            LobbyDetailsCopyAttributeByKeyOptions opt = new LobbyDetailsCopyAttributeByKeyOptions() { AttrKey = key };
            CurrentLobbyDetails.CopyAttributeByKey(ref opt, out Attribute? @out);
            return @out.Value.Data.Value.Value.AsDouble;
        }

        public string GetStringAttribute(string key)
        {
            LobbyDetailsCopyAttributeByKeyOptions opt = new LobbyDetailsCopyAttributeByKeyOptions() { AttrKey = key };
            CurrentLobbyDetails.CopyAttributeByKey(ref opt, out Attribute? @out);
            return @out.Value.Data.Value.Value.AsUtf8;
        }
        #endregion
    }
}
