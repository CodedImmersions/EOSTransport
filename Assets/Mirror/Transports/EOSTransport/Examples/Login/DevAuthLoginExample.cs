using System;
using System.Net.NetworkInformation;
using System.Net;
using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using UnityEngine;
using System.Linq;

namespace EpicTransport.Examples
{
    /// <summary>
    /// EOSTransport Device ID Login Example
    /// </summary>
    public class DevAuthLoginExample : MonoBehaviour
    {
        [Header("API Keys")]
        [SerializeField] private string productName;
        [SerializeField] private string productId;

        [SerializeField] private string clientId;
        [SerializeField] private string clientSecret;

        [SerializeField] private string sandboxId;
        [SerializeField] private string deploymentId;

        [SerializeField] private string encryptionKey;


        [Header("Dev Auth Tool")]
        [SerializeField] private ushort port = 8000;
        [SerializeField] private string credentialName;

        private void Start()
        {
            if (!IsPortActive()) throw new NotSupportedException($"Port {port} is not currently active. Login will not continue.");
            if (string.IsNullOrWhiteSpace(credentialName)) throw new NullReferenceException("The Credential Name is null/whitespace. Login will not continue.");

            EOSManager.Initialize(new TransportInitializeOptions()
            {
                AuthInterfaceCredentialType = LoginCredentialType.Developer,
                ConnectInterfaceCredentialType = ExternalCredentialType.Epic,

                ProductName = productName,
                ProductId = productId,
                ClientId = clientId,
                ClientSecret = clientSecret,
                SandboxId = sandboxId,
                DeploymentId = deploymentId,
                EncryptionKey = encryptionKey,

                AuthId = $"localhost:{port}",
                LoginToken = credentialName
            });
        }

        private bool IsPortActive()
        {
            IPGlobalProperties prop = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] listeners = prop.GetActiveTcpListeners();
            return listeners.Any(l => l.Port == port);
        }
    }
}
