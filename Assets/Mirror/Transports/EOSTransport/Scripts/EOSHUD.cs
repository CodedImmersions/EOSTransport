using Epic.OnlineServices.Lobby;
using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpicTransport
{
    public class EOSHUD : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("Editor-only GUI for connection")]
        [SerializeField] private string LobbyName = "My Lobby";
        [SerializeField] private int MaxPlayers = 10;

        private string state;
        private Vector2 scrollrect;
        private List<LobbyData> lobbies;

        private void Awake()
        {
            lobbies = new List<LobbyData>();
            InvokeRepeating(nameof(UpdateState), 0, 0.5f);
        }

        private void UpdateState()
        {
            if (EOSManager.Initialized)
            {
                if (EOSTransport.ConnectedToLobby) state = $"In lobby ({EOSTransport.ConnectedLobbyInfo.GetPlayerCount()} players)";
                else state = "Initialized";
            }
            else state = "Not initialized";
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            GUILayout.EndVertical();

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUI.enabled = !EOSTransport.ConnectedToLobby;
            if (GUILayout.Button("Create Lobby")) EOSTransport.CreateLobby(LobbyName, (uint)MaxPlayers);

            LobbyName = GUILayout.TextField(LobbyName, 40, GUILayout.MinWidth(150), GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Find Lobbies"))
            {
                EOSTransport.FindLobbies(cb =>
                {
                    lobbies.Clear();
                    foreach (LobbyDetails details in cb)
                    {
                        LobbyDetailsCopyInfoOptions copyopt = new LobbyDetailsCopyInfoOptions();
                        details.CopyInfo(ref copyopt, out LobbyDetailsInfo? info);

                        if (info.HasValue)
                        {
                            LobbyDetailsGetMemberCountOptions membercountopt = new LobbyDetailsGetMemberCountOptions();

                            lobbies.Add(new LobbyData()
                            {
                                id = info.Value.LobbyId,
                                players = details.GetMemberCount(ref membercountopt),
                                maxplayers = info.Value.MaxMembers
                            });
                        }
                    }
                });
            }

            GUI.enabled = EOSTransport.ConnectedToLobby;
            if (GUILayout.Button("Leave Lobby")) EOSTransport.LeaveLobby();
            GUI.enabled = true;

            if (lobbies != null && lobbies.Count > 0 && !EOSTransport.ConnectedToLobby)
            {
                GUILayout.Label("Available Lobbies:");
                scrollrect = GUILayout.BeginScrollView(scrollrect, GUILayout.Height(Mathf.Min(lobbies.Count, 10) * 30 + 10));

                foreach (var lobby in lobbies)
                {
                    GUILayout.BeginHorizontal("box");
                    GUILayout.Label($"{lobby.id} ({lobby.players}/{lobby.maxplayers})", GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Join", GUILayout.Width(80))) EOSTransport.JoinLobbyByID(lobby.id);

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.BeginVertical(GUILayout.ExpandWidth(false));

            GUILayout.Label($"State: {state}");

            if (EOSManager.Initialized) GUILayout.Label($"Player: {EOSManager.LocalUserProductID}");

            if (EOSTransport.ConnectedToLobby)
            {
                GUILayout.Label($"Lobby: {EOSTransport.ConnectedLobbyInfo.LobbyId}");
                GUILayout.Label($"RTT: {Math.Round(NetworkTime.rtt * 1000)} ms");
            }

            GUILayout.EndVertical();
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
        }
#endif
    }

    [Serializable]
    public class LobbyData
    {
        public string id;
        public uint players;
        public uint maxplayers;
    }
}