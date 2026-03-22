using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.UserInfo;
using Mirror;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace EpicTransport
{
    [DefaultExecutionOrder(-1000)]
    public class EOSManager : MonoBehaviour
    {
        public static EOSManager Instance => instance;
#pragma warning disable IDE1006 // Naming Styles
        public static EOSManager instance { get; private set; }
#pragma warning restore IDE1006 // Naming Styles

        #region Public Fields
        [Header("Settings")]
        [SerializeField] private TransportLogLevel transportLoggerLevel = TransportLogLevel.Warning;
        [SerializeField] private LogLevel eosLoggerLevel = LogLevel.Error;
        [SerializeField] private bool enableOverlay = false;
        #endregion

        #region Private Fields
        private PlatformInterface Platform;
        private ulong connectAuthExpirationHandle;

        private string displayName;

        private ProductUserId localUserProductID;
        private string localUserProductIDString;

        private EpicAccountId localUserAccountID;
        private string localUserAccountIDString;

        private bool isConnecting;
        private bool initialized;

#if UNITY_EDITOR
        private EditorSDKLoadHelper editorLoadHelper;
#endif
        private static TransportInitializeOptions initoptions;
        #endregion

        #region Static Fields
        public static bool IsConnecting { get { return instance.isConnecting; } }
        public static bool Initialized { get { return instance.initialized; } }

        public static ProductUserId LocalUserProductID { get { return instance.localUserProductID; } }
        public static string LocalUserProductIDString { get { return instance.localUserProductIDString; } }

        /// <summary>
        /// FOR AUTH INTERFACE ONLY! Returns the local user's Epic Account ID.
        /// </summary>
        public static EpicAccountId LocalUserAccountID { get { return instance.localUserAccountID; } }

        /// <summary>
        /// FOR AUTH INTERFACE ONLY! Returns the local user's Epic Account ID in string format.
        /// </summary>
        public static string LocalUserAccountIDString { get { return instance.localUserAccountIDString; } }

        public static string DisplayName { get { return instance.displayName; } set { instance.displayName = value; } }
        #endregion
        

        private void Awake()
        {
            //BUG: fixed by Hunter Allen (700075055887155310) on Discord. Delete this object if another instance already exists.
            if (instance == null)
                instance = this;
            else if (instance != this)
            {
                TransportLogger.LogWarning("An EOSManager already exists in this game run, destroying script instance.");
                Destroy(this);
            }

#if UNITY_EDITOR
            if (editorLoadHelper == null)
            {
                editorLoadHelper = new EditorSDKLoadHelper();
                editorLoadHelper.Load();
            }
#endif


            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass loader = new("com.epicgames.mobile.eossdk.LibraryLoader")) { loader.CallStatic("load"); }

#if UNITY_6000_0_OR_NEWER
                using AndroidJavaClass eos = new("com.epicgames.mobile.eossdk.EOSSDK");
                eos.CallStatic("init", UnityEngine.Android.AndroidApplication.currentActivity);
#else
                AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using (AndroidJavaClass eos = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK")) { eos.CallStatic("init", activity); }
#endif
            }
        }

        private void Start()
        {
            if (NetworkManager.singleton.dontDestroyOnLoad)
            {
                transform.parent = null;
                DontDestroyOnLoad(this);
            }
        }

        private void FixedUpdate() => Tick();

        #region Public Methods
        public static void Initialize(TransportInitializeOptions options)
        {
            if (IsConnecting || Initialized) return;

            if (options.EncryptionKey?.Length != 64) throw new ArgumentOutOfRangeException(nameof(options.EncryptionKey), "Your EOS Encryption Key is not exactly 64 characters, or is not set. Please make sure it is exactly 32 hexadecimal bytes. (aka 64 characters, A-F, a-f, 0-9)");

            initoptions = options;
            instance.isConnecting = true;

            InitializeOptions initopt = new()
            {
                ProductName = options.ProductName,
                ProductVersion = Application.version
            };

            Result initres = PlatformInterface.Initialize(ref initopt);
            if (initres != Result.Success && initres != Result.AlreadyConfigured) throw new EOSSDKException(initres, "Failed to initialize platform!");

            instance.gameObject.AddComponent<TransportLogger>().Initialize(instance.eosLoggerLevel, instance.transportLoggerLevel);

            DisplayName = options.DisplayName;

            Options createopt = new()
            {
                ProductId = options.ProductId,
                ClientCredentials = new ClientCredentials() { ClientId = options.ClientId, ClientSecret = options.ClientSecret },
                SandboxId = options.SandboxId,
                DeploymentId = options.DeploymentId,

                EncryptionKey = options.EncryptionKey,
                CacheDirectory = Application.temporaryCachePath,

#if UNITY_EDITOR
                Flags = instance.enableOverlay ? PlatformFlags.LoadingInEditor : PlatformFlags.LoadingInEditor | PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay,
#else
                Flags = instance.enableOverlay ? PlatformFlags.None : PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay,
#endif

#if UNITY_SERVER && !UNITY_EDITOR
                IsServer = true,
#endif
                TickBudgetInMilliseconds = 0
            };

            TransportLogger.Log("creating platform");
            instance.Platform = PlatformInterface.Create(ref createopt);
            TransportLogger.Log($"platform is null? {instance.Platform == null}");
            if (instance.Platform == null) throw new Exception("Failed to create platform!");

#if UNITY_EDITOR
            //for Transport Android Utils
            PlayerPrefs.SetString("EOSTransport Client ID", options.ClientId);
#endif

            if (ShouldUseAuthInterface(options.AuthInterfaceCredentialType))
            {
                //we are using auth + connect interface
                instance.AuthInterfaceLogin();
            }
            else
            {
                TransportLogger.Log("using connect");
                //we are using just connect interface
                if (options.ConnectInterfaceCredentialType == ExternalCredentialType.DeviceidAccessToken)
                {
                    try
                    {
                        TransportLogger.Log("using device id");

                        CreateDeviceIdOptions idopt = new() { DeviceModel = SystemInfo.deviceModel };
                        instance.Platform.GetConnectInterface().CreateDeviceId(ref idopt, null, (ref CreateDeviceIdCallbackInfo cb) =>
                        {
                            TransportLogger.Log("done");
                            if (cb.ResultCode != Result.Success && cb.ResultCode != Result.DuplicateNotAllowed) throw new EOSSDKException(cb.ResultCode, "Failed to create device ID!");
                            TransportLogger.Log("got device id ig");
                            instance.ConnectInterfaceLogin();
                        });
                    }
                    catch (Exception e) { Debug.LogException(e); }
                }
                else instance.ConnectInterfaceLogin();
            }
        }
        #endregion

        #region Helper Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldUseAuthInterface(LoginCredentialType cred) => cred != LoginCredentialType.ExternalAuth;

        public static Epic.OnlineServices.Achievements.AchievementsInterface GetAchievementsInterface() => instance.Platform.GetAchievementsInterface();
        public static Epic.OnlineServices.Auth.AuthInterface GetAuthInterface() => instance.Platform.GetAuthInterface();
        public static Epic.OnlineServices.Connect.ConnectInterface GetConnectInterface() => instance.Platform.GetConnectInterface();
        public static Epic.OnlineServices.Ecom.EcomInterface GetEcomInterface() => instance.Platform.GetEcomInterface(); //auth interface only
        public static Epic.OnlineServices.Friends.FriendsInterface GetFriendsInterface() => instance.Platform.GetFriendsInterface(); //auth interface only
        public static Epic.OnlineServices.KWS.KWSInterface GetKWSInterface() => instance.Platform.GetKWSInterface(); //auth interface only
        public static Epic.OnlineServices.Leaderboards.LeaderboardsInterface GetLeaderboardsInterface() => instance.Platform.GetLeaderboardsInterface();
        public static Epic.OnlineServices.Lobby.LobbyInterface GetLobbyInterface() => instance.Platform.GetLobbyInterface();
        public static Epic.OnlineServices.Metrics.MetricsInterface GetMetricsInterface() => instance.Platform.GetMetricsInterface(); //auth interface only
        public static Epic.OnlineServices.Mods.ModsInterface GetModsInterface() => instance.Platform.GetModsInterface(); //auth interface only
        public static Epic.OnlineServices.P2P.P2PInterface GetP2PInterface() => instance.Platform.GetP2PInterface();
        public static Epic.OnlineServices.PlayerDataStorage.PlayerDataStorageInterface GetPlayerDataStorageInterface() => instance.Platform.GetPlayerDataStorageInterface();
        public static Epic.OnlineServices.Presence.PresenceInterface GetPresenceInterface() => instance.Platform.GetPresenceInterface(); //auth interface only
        public static Epic.OnlineServices.ProgressionSnapshot.ProgressionSnapshotInterface GetProgressionSnapshotInterface() => instance.Platform.GetProgressionSnapshotInterface(); //auth interface only
        public static Epic.OnlineServices.Reports.ReportsInterface GetReportsInterface() => instance.Platform.GetReportsInterface();
        public static Epic.OnlineServices.Sanctions.SanctionsInterface GetSanctionsInterface() => instance.Platform.GetSanctionsInterface();
        public static Epic.OnlineServices.Sessions.SessionsInterface GetSessionsInterface() => instance.Platform.GetSessionsInterface();
        public static Epic.OnlineServices.Stats.StatsInterface GetStatsInterface() => instance.Platform.GetStatsInterface();
        public static Epic.OnlineServices.TitleStorage.TitleStorageInterface GetTitleStorageInterface() => instance.Platform.GetTitleStorageInterface();
        public static Epic.OnlineServices.UI.UIInterface GetUIInterface() => instance.Platform.GetUIInterface(); //auth interface only
        public static Epic.OnlineServices.UserInfo.UserInfoInterface GetUserInfoInterface() => instance.Platform.GetUserInfoInterface(); //auth interface only
        #endregion

        #region Internal Methods

        private void ConnectInterfaceLogin()
        {
            TransportLogger.Log("Login with connect interface running");

            if (!ShouldUseAuthInterface(initoptions.AuthInterfaceCredentialType))
            {
                if (string.IsNullOrEmpty(displayName)) throw new ArgumentNullException(nameof(displayName), "DisplayName is null. You must set a Display Name in TransportInitializeOptions.");
                if (displayName.Count() > ConnectInterface.USERLOGININFO_DISPLAYNAME_MAX_LENGTH) throw new ArgumentOutOfRangeException(nameof(displayName), $"DisplayName must be less than or equal to {ConnectInterface.USERLOGININFO_DISPLAYNAME_MAX_LENGTH} characters long.");
            }

            Epic.OnlineServices.Connect.LoginOptions loginopt = new()
            {
                Credentials = new Epic.OnlineServices.Connect.Credentials() { Type = initoptions.ConnectInterfaceCredentialType, Token = initoptions.LoginToken },
                UserLoginInfo = new UserLoginInfo() { DisplayName = displayName }
            };

            Platform.GetConnectInterface().Login(ref loginopt, null, ConnectLoginCallback);
        }

        private void ConnectLoginCallback(ref Epic.OnlineServices.Connect.LoginCallbackInfo cb)
        {
            if (Epic.OnlineServices.Common.IsOperationComplete(cb.ResultCode))
            {
                TransportLogger.Log(cb.ResultCode.ToString());

                switch (cb.ResultCode)
                {
                    case Result.Success:
                        //logged in
                        localUserProductID = cb.LocalUserId;
                        localUserProductIDString = cb.LocalUserId.ToString();

                        instance.isConnecting = false;
                        instance.initialized = true;
                        break;

                    case Result.InvalidUser:
                        //no user found, we need to create one.
                        if (cb.ContinuanceToken == null) throw new EOSSDKException(cb.ResultCode, "Continuance Token is null. Cannot create account.");

                        CreateUserOptions createopt = new() { ContinuanceToken = cb.ContinuanceToken };
                        Platform.GetConnectInterface().CreateUser(ref createopt, null, (ref CreateUserCallbackInfo cb2) =>
                        {
                            if (cb2.ResultCode != Result.Success) throw new EOSSDKException(cb2.ResultCode, "Failed to create user!");

                            localUserProductID = cb2.LocalUserId;
                            localUserProductIDString = cb2.LocalUserId.ToString();

                            instance.isConnecting = false;
                            instance.initialized = true;

                            TransportLogger.Log("New account created!");

                            AddNotifyAuthExpirationOptions aeexp2 = new();
                            connectAuthExpirationHandle = Platform.GetConnectInterface().AddNotifyAuthExpiration(ref aeexp2, null, ConnectExpiration);
                        });
                        break;

                    default:
                        TransportLogger.LogWarning($"EOS_Connect_Login returned unknown result 'Result.{cb.ResultCode}'.");
                        break;
                }
            }
            else
            {
                TransportLogger.LogError($"(Result.{cb.ResultCode}) operation not complete.");
            }
        }

        private void ConnectExpiration(ref AuthExpirationCallbackInfo cb)
        {
            Platform.GetConnectInterface().RemoveNotifyAuthExpiration(connectAuthExpirationHandle);
            ConnectInterfaceLogin();
        }

        private void AuthInterfaceLogin()
        {
            TransportLogger.Log("Login with auth interface running");

            Epic.OnlineServices.Auth.LoginOptions loginopt = new()
            {
                Credentials = new Epic.OnlineServices.Auth.Credentials()
                {
                    Type = initoptions.AuthInterfaceCredentialType,
                    Id = initoptions.AuthId,
                    Token = initoptions.LoginToken
                },

                ScopeFlags = AuthScopeFlags.BasicProfile | AuthScopeFlags.FriendsList | AuthScopeFlags.Country | AuthScopeFlags.Presence
            };

            Platform.GetAuthInterface().Login(ref loginopt, null, (ref Epic.OnlineServices.Auth.LoginCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success) throw new EOSSDKException(cb.ResultCode, "Failed to login to auth interface!");

                localUserAccountID = cb.LocalUserId;
                localUserAccountIDString = cb.LocalUserId.ToString();

                CopyUserInfoOptions copyopt = new()
                {
                    LocalUserId = LocalUserAccountID,
                    TargetUserId = LocalUserAccountID
                };

                Result res1 = Platform.GetUserInfoInterface().CopyUserInfo(ref copyopt, out UserInfoData? dat);
                if (res1 != Result.Success) throw new EOSSDKException(res1, "Failed to copy user info!");
                initoptions.DisplayName = dat.Value.DisplayName;

                CopyUserAuthTokenOptions authopt = new();
                Result res2 = Platform.GetAuthInterface().CopyUserAuthToken(ref authopt, LocalUserAccountID, out Token? token);
                if (res2 != Result.Success) throw new EOSSDKException(res2, "Failed to copy auth token!");
                initoptions.LoginToken = token?.AccessToken;

                ConnectInterfaceLogin();
            });
        }

        internal static void Tick()
        {
            instance.Platform?.Tick();
        }

        #endregion

        private void OnApplicationQuit()
        {
            EOSTransport.LeaveLobby();
            Platform?.GetConnectInterface().RemoveNotifyAuthExpiration(connectAuthExpirationHandle);

            Platform?.Release();
            Platform = null;
        }
    }

    [Serializable]
    public struct TransportInitializeOptions
    {
        /// <summary>
        /// The Auth Interface Credential Type. Set to <see cref="LoginCredentialType.ExternalAuth"/> if not using Auth Interface.
        /// </summary>
        public LoginCredentialType AuthInterfaceCredentialType;

        /// <summary>
        /// The Connect Interface Credential Type. Needed both with and without Auth Interface.
        /// </summary>
        public ExternalCredentialType ConnectInterfaceCredentialType;

        #region Keys
        /// <summary>
        /// The name of the product on the EOS Developer Dashboard.
        /// </summary>
        public string ProductName;

        /// <summary>
        /// The Product ID of the current app, found in Product Settings in the EOS Dashboard.
        /// </summary>
        public string ProductId;


        /// <summary>
        /// The Client ID of the current app, found in Product Settings in the EOS Dashboard.
        /// </summary>
        public string ClientId;

        /// <summary>
        /// The Client Secret of the current app, found in Product Settings in the EOS Dashboard.
        /// </summary>
        /// <remarks>Do not share this key with anyone.</remarks>
        public string ClientSecret;

        /// <summary>
        /// The Sandbox ID of the current app, found in Product Settings in the EOS Dashboard.
        /// </summary>
        public string SandboxId;

        /// <summary>
        /// The Deployment ID of the current app (Live Deployment), found in Product Settings in the EOS Dashboard.
        /// </summary>
        public string DeploymentId;

        /// <summary>
        /// A 32-byte (64-character) hexadecimal string used to encrypt Title Storage and Player Data Storage.
        /// </summary>
        public string EncryptionKey;
        #endregion

        /// <summary>
        /// The player's display name. Not needed for the Auth Interface, as usernames arre set automatically there.
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// The ID used for logging in with the Auth Interface. Not needed if not using Connect Interface.
        /// </summary>
        public string AuthId;

        /// <summary>
        /// The login token for the current user, used both for Connect and Auth Interfaces.
        /// </summary>
        public string LoginToken;
    }
}
