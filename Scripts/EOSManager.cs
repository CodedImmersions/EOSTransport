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

#if EOSTRANSPORT_DEBUG
using Unity.Profiling;
#endif

namespace EpicTransport
{
    public class EOSManager : MonoBehaviour
    {
        public static EOSManager instance { get; private set; }

        #region Public Fields
        [Header("Settings")]
        [SerializeField] private TransportLogLevel transportLoggerLevel = TransportLogLevel.Warning;
        [SerializeField] private LogLevel eosLoggerLevel = LogLevel.Error;
        [SerializeField] private bool enableOverlay = false;
        #endregion

        #region Private Fields
#if UNITY_EDITOR
        //if the platform interface has already been initialized in the editor
        private bool editorPlatformAlreadyInitialized;
#endif

        private PlatformInterface Platform;
        private ulong connectAuthExpirationHandle;

        private LoginCredentialType loginCredentialType;
        private ExternalCredentialType externalCredentialType;
        private string connectCredToken;
        private string displayName;

        private ProductUserId localUserProductID;
        private string localUserProductIDString;

        private EpicAccountId localUserAccountID;
        private string localUserAccountIDString;

        private bool isConnecting;
        private bool initialized;

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

        #region Editor Library Loading

#if UNITY_EDITOR_WIN
        [DllImport("Kernel32.dll")]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("Kernel32.dll")]
        private static extern int FreeLibrary(IntPtr hLibModule);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private IntPtr libraryPointer;
#endif

#if UNITY_EDITOR_OSX
        [DllImport("libdl.dylib")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl.dylib")]
        private static extern int dlclose(IntPtr handle);

        [DllImport("libdl.dylib")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libdl.dylib")]
        private static extern IntPtr dlerror();

        private const int RTLD_NOW = 2;
        private IntPtr libraryPointer;

        public static IntPtr LoadLibrary(string path) {

            dlerror();
            IntPtr handle = dlopen(path, RTLD_NOW);
            if (handle == IntPtr.Zero) {
                IntPtr error = dlerror();
                throw new Exception("dlopen: " + Marshal.PtrToStringAnsi(error));
            }
            return handle;
        }

        public static int FreeLibrary(IntPtr handle) {
            return dlclose(handle);
        }

        public static IntPtr GetProcAddress(IntPtr handle, string procName) {

            dlerror();
            IntPtr res = dlsym(handle, procName);
            IntPtr errPtr = dlerror();
            if (errPtr != IntPtr.Zero) {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }
#endif

#if UNITY_EDITOR_LINUX
        [DllImport("__Internal")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("__Internal")]
        private static extern int dlclose(IntPtr handle);

        [DllImport("__Internal")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("__Internal")]
        private static extern IntPtr dlerror();

        private const int RTLD_NOW = 2;
        private IntPtr libraryPointer;

        public static IntPtr LoadLibrary(string path) {
            dlerror();

            IntPtr handle = dlopen(path, RTLD_NOW);

            if (handle == IntPtr.Zero) {
                IntPtr error = dlerror();
                throw new Exception("dlopen failed: " + Marshal.PtrToStringAnsi(error));
            }
            return handle;
        }

        public static int FreeLibrary(IntPtr handle) {
            return dlclose(handle);
        }

        public static IntPtr GetProcAddress(IntPtr handle, string procName) {
            dlerror();
            IntPtr res = dlsym(handle, procName);
            IntPtr errPtr = dlerror();

            if (errPtr != IntPtr.Zero) {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }

        #endif
        #endregion


        private void Awake()
        {
            instance = this;

            #if UNITY_EDITOR
            string libname = Epic.OnlineServices.Common.LIBRARY_NAME;

            #if UNITY_EDITOR_OSX
            string[] parts = libname.Split('.');
            libname = parts[0]; //removes the .dylib so unity can find it
            #endif

            string[] libs = UnityEditor.AssetDatabase.FindAssets(libname);
            string librarypath = string.Empty;

            if (libs.Length > 0) librarypath = UnityEditor.AssetDatabase.GUIDToAssetPath(libs[0]);
            else throw new System.IO.FileNotFoundException($"EOS Assembly '{Epic.OnlineServices.Common.LIBRARY_NAME}' Not Found in Unity Project.", Epic.OnlineServices.Common.LIBRARY_NAME);

            libraryPointer = LoadLibrary(librarypath);
            if (libraryPointer == IntPtr.Zero) throw new Exception("Failed to load library: " + librarypath);

            Bindings.Hook(libraryPointer, GetProcAddress);
#endif


            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass sys = new AndroidJavaClass("java.lang.System")) sys.CallStatic("loadLibrary", "EOSSDK");

#if UNITY_6000_0_OR_NEWER
                using (AndroidJavaClass eos = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK")) { eos.CallStatic("init", UnityEngine.Android.AndroidApplication.currentActivity); }
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

            if (options.EncryptionKey.Length != 64) throw new ArgumentOutOfRangeException(nameof(options.EncryptionKey), "Your EOS Encryption Key is not exactly 64 characters, or is not set. Please make sure it is exactly 32 hexadecimal bytes. (aka 64 characters, A-F, a-f, 0-9)");

            initoptions = options;
            instance.isConnecting = true;

            InitializeOptions initopt = new InitializeOptions()
            {
                ProductName = options.ProductName,
                ProductVersion = Application.version
            };

            Result initres = PlatformInterface.Initialize(ref initopt);
            if (initres != Result.Success && initres != Result.AlreadyConfigured) throw new EOSSDKException(initres, "Failed to initialize platform!");

            instance.gameObject.AddComponent<TransportLogger>().Initialize(instance.eosLoggerLevel, instance.transportLoggerLevel);

            instance.loginCredentialType = options.AuthInterfaceCredentialType;
            instance.externalCredentialType = options.ConnectInterfaceCredentialType;
            instance.connectCredToken = options.LoginToken;
            DisplayName = options.DisplayName;

            Options createopt = new Options()
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

                        CreateDeviceIdOptions idopt = new CreateDeviceIdOptions() { DeviceModel = SystemInfo.deviceModel };
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
            TransportLogger.Log("cil-ing");
            if (string.IsNullOrEmpty(displayName)) throw new ArgumentNullException(nameof(displayName), "DisplayName is null. You must set a Display Name in TransportInitializeOptions.");
            if (displayName.Count() > ConnectInterface.USERLOGININFO_DISPLAYNAME_MAX_LENGTH) throw new ArgumentOutOfRangeException(nameof(displayName), $"DisplayName must be less than or equal to {ConnectInterface.USERLOGININFO_DISPLAYNAME_MAX_LENGTH} characters long.");

            Epic.OnlineServices.Connect.LoginOptions loginopt = new Epic.OnlineServices.Connect.LoginOptions()
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

                        CreateUserOptions createopt = new CreateUserOptions() { ContinuanceToken = cb.ContinuanceToken };
                        Platform.GetConnectInterface().CreateUser(ref createopt, null, (ref CreateUserCallbackInfo cb2) =>
                        {
                            if (cb2.ResultCode != Result.Success) throw new EOSSDKException(cb2.ResultCode, "Failed to create user!");

                            localUserProductID = cb2.LocalUserId;
                            localUserProductIDString = cb2.LocalUserId.ToString();

                            instance.isConnecting = false;
                            instance.initialized = true;

                            TransportLogger.Log("New account created!");

                            AddNotifyAuthExpirationOptions aeexp2 = new AddNotifyAuthExpirationOptions();
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
            Epic.OnlineServices.Auth.LoginOptions loginopt = new Epic.OnlineServices.Auth.LoginOptions()
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

                CopyUserInfoOptions copyopt = new CopyUserInfoOptions()
                {
                    LocalUserId = LocalUserAccountID,
                    TargetUserId = LocalUserAccountID
                };

                Result res1 = Platform.GetUserInfoInterface().CopyUserInfo(ref copyopt, out UserInfoData? dat);
                if (res1 != Result.Success) throw new EOSSDKException(res1, "Failed to copy user info!");
                initoptions.DisplayName = dat.Value.DisplayName;

                CopyUserAuthTokenOptions authopt = new CopyUserAuthTokenOptions();
                Result res2 = Platform.GetAuthInterface().CopyUserAuthToken(ref authopt, LocalUserAccountID, out Token? token);
                if (res2 != Result.Success) throw new EOSSDKException(res2, "Failed to copy auth token!");
                initoptions.LoginToken = token?.AccessToken;

                ConnectInterfaceLogin();
            });
        }

#if EOSTRANSPORT_DEBUG
        static readonly ProfilerMarker k_MyCodeMarker = new ProfilerMarker("EOSManager Tick");
#endif

        internal static void Tick()
        {
            if (instance.Platform != null)
            {
#if EOSTRANSPORT_DEBUG
                k_MyCodeMarker.Begin();
#endif

                instance.Platform.Tick();

#if EOSTRANSPORT_DEBUG
                k_MyCodeMarker.End();
#endif
            }
        }

        #endregion

        private void OnApplicationQuit()
        {
            if (Platform != null)
            {
                Platform.GetConnectInterface().RemoveNotifyAuthExpiration(connectAuthExpirationHandle);

                Platform.Release();
                PlatformInterface.Shutdown();
                Platform = null;
            }

#if UNITY_EDITOR
            if (libraryPointer != IntPtr.Zero)
            {
                Bindings.Unhook();

                while (FreeLibrary(libraryPointer) != 0) { }
                libraryPointer = IntPtr.Zero;
            }
#endif
        }
    }

    [Serializable]
    public class TransportInitializeOptions
    {
        public LoginCredentialType AuthInterfaceCredentialType;
        public ExternalCredentialType ConnectInterfaceCredentialType;

        public string ProductName;
        public string ProductId;

        public string ClientId;
        public string ClientSecret;

        public string SandboxId;
        public string DeploymentId;

        /// <summary>
        /// A 32-byte (64-character) hexadecimal string used to encrypt Title Storage and Player Data Storage.
        /// </summary>
        public string EncryptionKey;


        public string DisplayName;

        public string AuthId;
        public string LoginToken;

        public TransportInitializeOptions() { }
    }
}
