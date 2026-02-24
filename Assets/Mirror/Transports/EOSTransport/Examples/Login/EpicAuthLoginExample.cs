using UnityEngine;
using EpicTransport;

using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using System.Collections;

/// <summary>
/// EOSTransport Epic Auth Login Example. DO NOT DIRECTLY USE!!!! INSTEAD PUT IT INTO YOUR OWN SCRIPT THAT ISN'T OBVIOUSLY NAMED AND DOESN'T HAVE API KEYS EXPOSED!!!!
/// </summary>
public class EpicAuthLoginExample : MonoBehaviour
{
    [Header("EXAMPLE ONLY\nPLEASE DO NOT DIRECTLY USE")]

    [SerializeField] private string productName;
    [SerializeField] private string productId;

    [SerializeField] private string clientId;
    [SerializeField] private string clientSecret;

    [SerializeField] private string sandboxId;
    [SerializeField] private string deploymentId;

    [SerializeField] private string encryptionKey;

    private void Start()
    {
        EOSManager.Initialize(new TransportInitializeOptions()
        {
            AuthInterfaceCredentialType = LoginCredentialType.AccountPortal,
            ConnectInterfaceCredentialType = ExternalCredentialType.Epic,

            ProductName = productName,
            ProductId = productId,
            ClientId = clientId,
            ClientSecret = clientSecret,
            SandboxId = sandboxId,
            DeploymentId = deploymentId,
            EncryptionKey = encryptionKey,

            DisplayName = "DevTestingExample"
        });
    }
}
