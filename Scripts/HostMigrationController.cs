using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using EpicTransport.Attributes;

using Mirror;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;

using UnityEngine;

namespace EpicTransport
{
    [RequireComponent(typeof(HMSkip))]
    public class HostMigrationController : MonoBehaviour
    {
        [SerializeField] private float backupRefreshInterval = 0.2f;
        private BackupData LocalBackup = new BackupData();

        private bool hostmigrating;
        private bool connectionready;

        private readonly Dictionary<Type, List<FieldInfo>> typetofieldcache = new();
        private readonly Dictionary<NetworkIdentity, List<NetworkedField>> nitobackupcache = new();

        private JsonSerializerSettings jsonsettings;
        private byte[] keybackup;
        private byte[] ivbackup;

        private List<ProductUserId> peers;
        private SocketId socket;

        private ulong updateid;
        internal static HostMigrationMessage message { get; private set; }

        private void Awake()
        {
            socket = new SocketId() { SocketName = Helper.GenerateHexString(16) };
            EOSTransport.OnJoinedLobby += OnStartClient;
            EOSTransport.OnLeftLobby += OnStopClient;
        }

        private void OnDestroy()
        {
            EOSTransport.OnJoinedLobby -= OnStartClient;
            EOSTransport.OnLeftLobby -= OnStopClient;
        }

        private void OnStartClient(string lobbyname)
        {
            if (EOSTransport.HostMigrationEnabled)
            {
                jsonsettings = new JsonSerializerSettings()
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Formatting = Formatting.Indented
                };

                AddNotifyLobbyMemberStatusReceivedOptions updateopt = new AddNotifyLobbyMemberStatusReceivedOptions();
                updateid = EOSManager.GetLobbyInterface().AddNotifyLobbyMemberStatusReceived(ref updateopt, null, OnLobbyMemberUpdated);
                InvokeRepeating(nameof(Backup), 0, backupRefreshInterval);

                if (!hostmigrating)
                {
                    peers = new List<ProductUserId>();

                    uint playercount = EOSTransport.ConnectedLobbyInfo.GetPlayerCount();
                    for (uint i = 0; i < playercount; i++)
                    {
                        peers.Add(EOSTransport.ConnectedLobbyInfo.GetMemberByIndex(i));
                    }
                }
            }
        }

        private void OnStopClient()
        {
            CancelInvoke(nameof(Backup));
            EOSManager.GetLobbyInterface().RemoveNotifyLobbyMemberStatusReceived(updateid);
        }

        private void OnApplicationQuit()
        {
            EOSTransport.LeaveLobby();
        }

        private void Backup()
        {
            if (NetworkClient.active && EOSTransport.HostMigrationEnabled)
            {
                Dictionary<uint, NetworkIdentity> spawnedobjs = NetworkClient.spawned;
                if (spawnedobjs.Count <= 0) return;

                LocalBackup = new BackupData();

                foreach (NetworkIdentity ni in spawnedobjs.Values)
                {
                    if (ni.GetComponent<HMSkip>()) continue;

                    List<NetworkedField> networkedfields = RefreshFieldVals(ni);
                    bool hasrigidbody = ni.TryGetComponent(out Rigidbody rb);

                    if (ni.SpawnedFromInstantiate) //prefab
                    {
                        var prefab = new NetworkedPrefab
                        {
                            name = ni.gameObject.name,
                            scene = ni.gameObject.scene.name,
                            assetId = ni.assetId,

                            position = ni.transform.position,
                            rotation = ni.transform.rotation,
                            scale = ni.transform.localScale,
#if UNITY_6000_0_OR_NEWER
                            velocity = hasrigidbody ? rb.linearVelocity : Vector3.zero,
#else
                            velocity = hasrigidbody ? rb.velocity : Vector3.zero,
#endif

                            fields = networkedfields
                        };

                        LocalBackup.prefabs.Add(prefab);
                    }
                    else //regular gameobject
                    {
                        var obj = new NetworkedGameObject
                        {
                            name = ni.gameObject.name,
                            scene = ni.gameObject.scene.name,
                            sceneId = ni.sceneId,

                            position = ni.transform.position,
                            rotation = ni.transform.rotation,
                            scale = ni.transform.localScale,
#if UNITY_6000_0_OR_NEWER
                            velocity = hasrigidbody ? rb.linearVelocity : Vector3.zero,
#else
                            velocity = hasrigidbody ? rb.velocity : Vector3.zero,
#endif

                            fields = networkedfields
                        };

                        LocalBackup.gameobjects.Add(obj);
                    }
                }
            }
        }

        private List<NetworkedField> RefreshFieldVals(NetworkIdentity identity)
        {
            if (nitobackupcache.TryGetValue(identity, out var cache))
            {
                foreach (var nf in cache)
                {
                    var behaviour = GetBehaviour(identity, nf.scriptInstanceId);
                    if (behaviour != null)
                    {
                        var type = behaviour.GetType();

                        var fieldname = nf.fieldName.Split('.')[1];

                        var field = type.GetField(fieldname, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            object val = field.GetValue(behaviour);
                            if (val != null)
                            {
                                if (val is UnityEngine.Object || val is IEnumerable<UnityEngine.Object>)
                                {
                                    TransportLogger.LogWarning($"Backing up {field.FieldType.FullName} isn't yet supported with Host Migration.");
                                    continue;
                                }
                                else if (val is Color || val is Color32)
                                {
                                    TransportLogger.LogWarning($"Backing up Colors isn't yet supported with Host Migration as it causes a stack overflow. We are working on a fix.");
                                    continue;
                                }

                                nf.value = new SerializableValue
                                {
                                    type = field.FieldType.AssemblyQualifiedName,
                                    json = JsonConvert.SerializeObject(val, jsonsettings)
                                };
                            }

                        }
                    }
                }

                return cache;
            }
            
            //NOTE: we refresh if that NI isn't in the list
            List<NetworkedField> result = new();
            typetofieldcache.Clear();

            foreach (var behaviour in identity.GetComponents<NetworkBehaviour>())
            {
                Type type = behaviour.GetType();
                if (!typetofieldcache.TryGetValue(type, out List<FieldInfo> fields))
                {
                    fields = new List<FieldInfo>();
                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if ((System.Attribute.IsDefined(field, typeof(SyncVarAttribute)) && !System.Attribute.IsDefined(field, typeof(DoNotBackupAttribute)))
                            || System.Attribute.IsDefined(field, typeof(ForceBackupAttribute)))
                        {
                            if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) || typeof(IEnumerable<UnityEngine.Object>).IsAssignableFrom(field.FieldType))
                            {
                                TransportLogger.LogWarning($"Backing up {field.FieldType.FullName} isn't yet supported with Host Migration.");
                                continue;
                            }
                            else if (field.FieldType == typeof(Color) || field.FieldType == typeof(Color32))
                            {
                                TransportLogger.LogWarning($"Backing up Colors isn't yet supported with Host Migration as it causes a stack overflow. We are working on a fix.");
                                continue;
                            }

                            fields.Add(field);
                        }
                    }

                    typetofieldcache[type] = fields;
                }

                foreach (var field in fields)
                {
                    result.Add(new NetworkedField
                    {
                        scriptInstanceId = behaviour.GetInstanceID(),
                        fieldName = $"{type.Name}.{field.Name}",
                        value = new SerializableValue
                        {
                            type = field.FieldType.AssemblyQualifiedName,
                            json = JsonConvert.SerializeObject(field.GetValue(behaviour), jsonsettings)
                        }
                    });

                }
            }

            nitobackupcache[identity] = result;
            return result;
        }

        private NetworkBehaviour GetBehaviour(NetworkIdentity identity, int instanceId)
        {
            foreach (var b in identity.GetComponents<NetworkBehaviour>()) { if (b.GetInstanceID() == instanceId) return b; }
            return null;
        }

        private void LoadBackNetworkedObjects()
        {
            Dictionary<ulong, NetworkIdentity> sceneidtoni = new Dictionary<ulong, NetworkIdentity>();
            Dictionary<uint, GameObject> assetidtoobj = new Dictionary<uint, GameObject>();

            string path = Path.Combine(Application.temporaryCachePath, "HostMigrationData.json");
            string json = Decrypt(File.ReadAllBytes(path), keybackup, ivbackup);
            BackupData dat = JsonConvert.DeserializeObject<BackupData>(json);

            if (dat.gameobjects.Count > 0)
            {
                foreach (NetworkIdentity ni in FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!sceneidtoni.ContainsKey(ni.sceneId)) sceneidtoni.Add(ni.sceneId, ni);
                }

                foreach (var objdat in dat.gameobjects)
                {
                    GameObject obj = sceneidtoni[objdat.sceneId].gameObject;
                    if (obj == null) continue;

                    obj.name = objdat.name;
                    obj.transform.position = objdat.position;
                    obj.transform.rotation = objdat.rotation;
                    obj.transform.localScale = objdat.scale;

                    if (obj.TryGetComponent<Rigidbody>(out var rb))
                    {
#if UNITY_6000_0_OR_NEWER
                        rb.linearVelocity = objdat.velocity;
#else
                        rb.velocity = objdat.velocity;
#endif
                    }

                    if (!NetworkServer.spawned.ContainsValue(sceneidtoni[objdat.sceneId])) NetworkServer.Spawn(obj);
                    if (objdat.scene.Equals("DontDestroyOnLoad")) DontDestroyOnLoad(obj);

                    foreach (var field in objdat.fields)
                    {
                        var behaviour = GetBehaviour(obj.GetComponent<NetworkIdentity>(), field.scriptInstanceId);
                        if (behaviour != null)
                        {
                            var type = behaviour.GetType();

                            var parts = field.fieldName.Split('.');
                            if (parts.Length < 2) continue;

                            var fieldname = parts[1];
                            var info = type.GetField(fieldname, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (info != null)
                            {
                                try
                                {
                                    info.SetValue(behaviour, ConvertVal(field.value, info.FieldType));
                                }
                                catch (Exception e)
                                {
                                    TransportLogger.LogError($"failed to set field {field.fieldName} on {info.DeclaringType}: {e.Message}");
                                }

                            }
                        }
                    }
                }
            }

            if (dat.prefabs.Count > 0)
            {
                foreach (GameObject obj in NetworkManager.singleton.spawnPrefabs)
                {
                    var ni = obj.GetComponent<NetworkIdentity>();
                    if (!assetidtoobj.ContainsKey(ni.assetId)) assetidtoobj.Add(ni.assetId, obj);
                }

                foreach (var prefabdat in dat.prefabs)
                {
                    GameObject prefab = assetidtoobj[prefabdat.assetId];
                    if (prefab == null) continue;

                    GameObject obj = Instantiate(prefab, prefabdat.position, prefabdat.rotation);
                    obj.name = prefabdat.name;
                    obj.transform.localScale = prefabdat.scale;
                    NetworkServer.Spawn(obj);

                    if (obj.TryGetComponent<Rigidbody>(out var rb))
                    {
#if UNITY_6000_0_OR_NEWER
                        rb.linearVelocity = prefabdat.velocity;
#else
                        rb.velocity = prefabdat.velocity;
#endif
                    }

                    if (prefabdat.scene.Equals("DontDestroyOnLoad")) DontDestroyOnLoad(obj);

                    foreach (var field in prefabdat.fields)
                    {
                        var behaviour = GetBehaviour(obj.GetComponent<NetworkIdentity>(), field.scriptInstanceId);
                        if (behaviour != null)
                        {
                            var type = behaviour.GetType();

                            var parts = field.fieldName.Split('.');
                            if (parts.Length < 2) continue;

                            var fieldname = parts[1];
                            var info = type.GetField(fieldname, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (info != null)
                            {
                                try
                                {
                                    info.SetValue(behaviour, ConvertVal(field.value, info.FieldType));
                                }
                                catch (Exception e)
                                {
                                    TransportLogger.LogError($"failed to set field {field.fieldName} on {info.DeclaringType}: {e.Message}");
                                }

                            }
                        }
                    }
                }
            }

            File.Delete(path);
        }

        private void OnLobbyMemberUpdated(ref LobbyMemberStatusReceivedCallbackInfo cb)
        {
            TransportLogger.LogError($"Member Updated. User: {cb.TargetUserId.ToString()}, Action: {cb.CurrentStatus}.");

            switch (cb.CurrentStatus)
            {
                case LobbyMemberStatus.Promoted:
                    hostmigrating = true;

                    //host migration is happening, hooray! :)
                    if (cb.TargetUserId == EOSManager.LocalUserProductID)
                    {
                        if (NetworkServer.active) return;

                        string lobbyid = EOSTransport.ConnectedLobbyInfo.LobbyId;
                        DateTime start = DateTime.Now;

                        string path = Path.Combine(Application.temporaryCachePath, "HostMigrationData.json");
                        string json = JsonConvert.SerializeObject(LocalBackup, jsonsettings);
                        File.WriteAllBytes(path, Encrypt(json, out keybackup, out ivbackup));

                        TransportLogger.Log("Host Migrating. I am the next host!");

                        NetworkManager.singleton.StopHost();
                        EOSTransport.instance.Shutdown();

                        StartCoroutine(StartNewHost(start, lobbyid));
                    }
                    else
                    {
                        TransportLogger.Log($"Host Migrating. Next host is {cb.TargetUserId.ToString()}.");
                        NetworkManager.singleton.StopHost();
                        NetworkManager.singleton.StopClient();

                        ClientWaitLoop(cb.TargetUserId);
                    }
                    break;


                case LobbyMemberStatus.Joined:
                    peers.Add(cb.TargetUserId);
                    break;

                    //only remove them if HM hasn't started because we need to send all the users a message when the new host is ready
                case LobbyMemberStatus.Left:
                case LobbyMemberStatus.Disconnected:
                case LobbyMemberStatus.Kicked:
                    ProductUserId pid = cb.TargetUserId;
                    if (!hostmigrating) peers.RemoveAll(p => p == pid);
                    break;
            }
        }

        private IEnumerator StartNewHost(DateTime start, string lobbyid)
        {
            yield return new WaitUntil(() => !EOSTransport.instance.ClientActive());

            TransportLogger.Log("starting back up");
            NetworkManager.singleton.networkAddress = EOSManager.LocalUserProductIDString;
            NetworkManager.singleton.StartHost();

            LoadBackNetworkedObjects();

            #region Setting new Host Address
            AttributeData attribute = new AttributeData() { Key = EOSTransport.HostAddressKey, Value = EOSManager.LocalUserProductIDString };
            UpdateLobbyModificationOptions modopt = new UpdateLobbyModificationOptions() { LocalUserId = EOSManager.LocalUserProductID, LobbyId = lobbyid };
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
            #endregion

            DateTime end = DateTime.Now;
            TimeSpan ts = end - start;
            TransportLogger.Log($"Host Migration transfer done! Took {ts.TotalMilliseconds}ms");

            BroadcastHMMessage();
            hostmigrating = false;
        }

        private void BroadcastHMMessage()
        {
            if (!hostmigrating) return;

            P2PInterface p2p = EOSManager.GetP2PInterface();

            //we won't be using the data, but just in case we do, we can.
            ArraySegment<byte> data = new ArraySegment<byte>(new byte[] { (byte)InternalMessages.HOST_MIGRATION_READY });

            foreach (ProductUserId peer in peers)
            {
                SendPacketOptions sendopt = new SendPacketOptions()
                {
                    LocalUserId = EOSManager.LocalUserProductID,
                    RemoteUserId = peer,
                    SocketId = socket,

                    Channel = EOSTransport.HostMigrationChannel,
                    Reliability = PacketReliability.ReliableOrdered,

                    Data = data,

                    AllowDelayedDelivery = true,
                    DisableAutoAcceptConnection = false
                };

                p2p.SendPacket(ref sendopt);
            }
        }

        private async void ClientWaitLoop(ProductUserId next)
        {
            AddNotifyPeerConnectionRequestOptions requestopt = new AddNotifyPeerConnectionRequestOptions() { LocalUserId = EOSManager.LocalUserProductID };
            ulong noti = EOSManager.GetP2PInterface().AddNotifyPeerConnectionRequest(ref requestopt, next, ClientListenForMessage);

            TransportLogger.Log("waiting for host migration ready message..."); 

            DateTime start = DateTime.UtcNow;
            TimeSpan timeout = TimeSpan.FromSeconds(EOSTransport.instance.timeout);

            while (!connectionready)
            {
                await Task.Delay(50);
                if (DateTime.UtcNow - start >= timeout)
                {
                    TransportLogger.LogError($"re-connection to host timed out after {timeout.TotalMilliseconds} ms.");
                    return;
                }
            }

            TransportLogger.Log("Ready message received, now connecting...");
            UriBuilder urib = new UriBuilder
            {
                Scheme = EOSTransport.EpicScheme,
                Host = next.ToString()
            };

            NetworkManager.singleton.StartClient(urib.Uri);

            EOSManager.GetP2PInterface().RemoveNotifyPeerConnectionRequest(noti);
            connectionready = false;
        }

        private void ClientListenForMessage(ref OnIncomingConnectionRequestInfo cb)
        {
            ProductUserId newhost = (ProductUserId)cb.ClientData;
            if (newhost == null) return;

            if (cb.RemoteUserId == newhost)
            {
                //close the connection since it's no longer needed.
                CloseConnectionOptions closeopt = new CloseConnectionOptions()
                {
                    LocalUserId = EOSManager.LocalUserProductID,
                    RemoteUserId = cb.RemoteUserId,
                    SocketId = cb.SocketId
                };

                Result res = EOSManager.GetP2PInterface().CloseConnection(ref closeopt);
                if (res == Result.Success) connectionready = true;
            }
        }

        private object ConvertVal(SerializableValue val, Type targetType)
        {
            if (val == null || string.IsNullOrEmpty(val.json)) return null;

            try
            {
                if (targetType.IsEnum) return Enum.Parse(targetType, val.json.Trim('"'));
                return JsonConvert.DeserializeObject(val.json, targetType, jsonsettings);

            }
            catch (Exception e)
            {
                TransportLogger.LogError($"converting the value failed for {targetType}: {e.Message}");
                return null;
            }
        }

        #region Encryption

        private byte[] Encrypt(string input, out byte[] key, out byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                aes.GenerateKey();
                aes.GenerateIV();

                key = aes.Key;
                iv = aes.IV;

                using (MemoryStream ms = new MemoryStream())
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))

                using (StreamWriter sw = new StreamWriter(cs))
                {
                    sw.Write(input);
                    sw.Close();
                    return ms.ToArray();
                }
            }
        }

        private string Decrypt(byte[] input, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                aes.Key = key;
                aes.IV = iv;

                using (MemoryStream ms = new MemoryStream(input))
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))

                using (StreamReader sr = new StreamReader(cs)) return sr.ReadToEnd();
            }
        }

        #endregion
    }

    [Serializable]
    public class BackupData
    {
        public List<NetworkedGameObject> gameobjects = new List<NetworkedGameObject>();
        public List<NetworkedPrefab> prefabs = new List<NetworkedPrefab>();

        public BackupData() { }
    }

    [Serializable]
    public class SerializableValue
    {
        public string type;
        public string json;
    }

    [Serializable]
    public class NetworkedGameObject
    {
        public string name;
        public string scene;

        public ulong sceneId;

        public bool hasNetworkTransform;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public Vector3 velocity = Vector3.zero;

        public List<NetworkedField> fields;

        public NetworkedGameObject() { }
    }

    [Serializable]
    public class NetworkedPrefab
    {
        public string name;
        public string scene;

        public uint assetId;

        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public Vector3 velocity = Vector3.zero;

        public List<NetworkedField> fields;

        public NetworkedPrefab() { }
    }

    [Serializable]
    public class NetworkedField
    {
        public int scriptInstanceId;
        public string fieldName;
        public SerializableValue value;
    }

    public struct HostMigrationMessage : NetworkMessage
    {
        public string nexthost;
    }
}
