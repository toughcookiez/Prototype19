// Netcode for GameObjects transport over Steam Networking Sockets (P2P through Valve's relay).
// Adapted for NGO 2.x from the MIT-licensed community transport in
// https://github.com/Unity-Technologies/multiplayer-community-contributions
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

public class SteamNetworkingSocketsTransport : NetworkTransport
{
    private const int MaxMessagesPerReceive = 64;

    private class SteamConnection
    {
        public ulong SteamId;
        public HSteamNetConnection Handle;
    }

    private struct QueuedEvent
    {
        public NetworkEvent Type;
        public ulong ClientId;
        public ArraySegment<byte> Payload;
        public float ReceiveTime;
    }

    // Steam ID of the host to connect to. Set before NetworkManager.StartClient().
    [NonSerialized] public ulong ConnectToSteamId;

    private readonly Dictionary<ulong, SteamConnection> connections = new Dictionary<ulong, SteamConnection>();
    private readonly Queue<SteamNetConnectionStatusChangedCallback_t> statusChanges = new Queue<SteamNetConnectionStatusChangedCallback_t>();
    private readonly Queue<QueuedEvent> pendingEvents = new Queue<QueuedEvent>();
    private readonly IntPtr[] receiveBuffer = new IntPtr[MaxMessagesPerReceive];

    private Callback<SteamNetConnectionStatusChangedCallback_t> statusChangedCallback;
    private HSteamListenSocket listenSocket = HSteamListenSocket.Invalid;
    private SteamConnection serverConnection;
    private bool isServer;

    // Steam IDs are never 0, so 0 is free to represent the server on clients.
    public override ulong ServerClientId => 0;

    public override bool IsSupported => SteamManager.Initialized;

    public override void Initialize(NetworkManager networkManager = null)
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError($"[Steam] {nameof(SteamNetworkingSocketsTransport)} requires Steam to be initialized.");
        }
    }

    public override bool StartServer()
    {
        if (!SteamManager.Initialized)
        {
            return false;
        }

        isServer = true;
        RegisterCallback();
        listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
        return listenSocket != HSteamListenSocket.Invalid;
    }

    public override bool StartClient()
    {
        if (!SteamManager.Initialized || ConnectToSteamId == 0)
        {
            Debug.LogError("[Steam] Cannot start client: Steam not initialized or no host Steam ID set.");
            return false;
        }

        isServer = false;
        RegisterCallback();

        SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
        identity.SetSteamID64(ConnectToSteamId);

        serverConnection = new SteamConnection
        {
            SteamId = ConnectToSteamId,
            Handle = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null)
        };

        if (serverConnection.Handle == HSteamNetConnection.Invalid)
        {
            serverConnection = null;
            return false;
        }

        connections[ServerClientId] = serverConnection;
        return true;
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        if (!connections.TryGetValue(clientId, out SteamConnection connection))
        {
            Debug.LogWarning($"[Steam] Send to unknown client {clientId} dropped.");
            return;
        }

        // NGO already batches per tick, so skip Nagle's extra delay. Steam's reliable channel is ordered.
        int sendFlags = networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced
            ? Constants.k_nSteamNetworkingSend_UnreliableNoNagle
            : Constants.k_nSteamNetworkingSend_ReliableNoNagle;

        GCHandle pinned = GCHandle.Alloc(payload.Array, GCHandleType.Pinned);
        EResult sendResult;

        try
        {
            IntPtr data = pinned.AddrOfPinnedObject() + payload.Offset;
            sendResult = SteamNetworkingSockets.SendMessageToConnection(connection.Handle, data, (uint)payload.Count, sendFlags, out long _);
        }
        finally
        {
            pinned.Free();
        }

        if (sendResult != EResult.k_EResultOK)
        {
            Debug.LogWarning($"[Steam] Send to {clientId} failed: {sendResult}");
        }
    }

    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        ProcessStatusChanges();

        if (pendingEvents.Count == 0)
        {
            ReceiveMessages();
        }

        if (pendingEvents.Count > 0)
        {
            QueuedEvent queued = pendingEvents.Dequeue();
            clientId = queued.ClientId;
            payload = queued.Payload;
            receiveTime = queued.ReceiveTime;
            return queued.Type;
        }

        clientId = 0;
        payload = default;
        receiveTime = Time.realtimeSinceStartup;
        return NetworkEvent.Nothing;
    }

    private void ProcessStatusChanges()
    {
        while (statusChanges.Count > 0)
        {
            SteamNetConnectionStatusChangedCallback_t change = statusChanges.Dequeue();
            HSteamNetConnection handle = change.m_hConn;
            ulong remoteSteamId = change.m_info.m_identityRemote.GetSteamID64();

            switch (change.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    // Incoming connection on our listen socket. NGO's connection approval does the real gatekeeping.
                    if (isServer && change.m_info.m_hListenSocket != HSteamListenSocket.Invalid)
                    {
                        EResult acceptResult = SteamNetworkingSockets.AcceptConnection(handle);

                        if (acceptResult != EResult.k_EResultOK)
                        {
                            Debug.LogWarning($"[Steam] Could not accept connection from {remoteSteamId}: {acceptResult}");
                            SteamNetworkingSockets.CloseConnection(handle, 0, "Accept failed", false);
                        }
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (isServer)
                    {
                        connections[remoteSteamId] = new SteamConnection { SteamId = remoteSteamId, Handle = handle };
                        Enqueue(NetworkEvent.Connect, remoteSteamId, default);
                    }
                    else
                    {
                        Enqueue(NetworkEvent.Connect, ServerClientId, default);
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Debug.Log($"[Steam] Connection with {remoteSteamId} ended: {change.m_info.m_eState} ({change.m_info.m_szEndDebug})");

                    // Closing is required to free the handle even when the peer already dropped it.
                    SteamNetworkingSockets.CloseConnection(handle, 0, null, false);

                    ulong transportId = isServer ? remoteSteamId : ServerClientId;

                    if (connections.TryGetValue(transportId, out SteamConnection known) && known.Handle == handle)
                    {
                        connections.Remove(transportId);

                        if (!isServer)
                        {
                            serverConnection = null;
                        }

                        Enqueue(NetworkEvent.Disconnect, transportId, default);
                    }
                    break;
            }
        }
    }

    private void ReceiveMessages()
    {
        foreach (KeyValuePair<ulong, SteamConnection> entry in connections)
        {
            int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(entry.Value.Handle, receiveBuffer, MaxMessagesPerReceive);

            for (int i = 0; i < count; i++)
            {
                SteamNetworkingMessage_t message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(receiveBuffer[i]);
                byte[] data = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, data, 0, message.m_cbSize);
                SteamNetworkingMessage_t.Release(receiveBuffer[i]);

                Enqueue(NetworkEvent.Data, entry.Key, new ArraySegment<byte>(data));
            }
        }
    }

    private void Enqueue(NetworkEvent type, ulong clientId, ArraySegment<byte> payload)
    {
        pendingEvents.Enqueue(new QueuedEvent
        {
            Type = type,
            ClientId = clientId,
            Payload = payload,
            ReceiveTime = Time.realtimeSinceStartup
        });
    }

    public override ulong GetCurrentRtt(ulong clientId)
    {
        if (!connections.TryGetValue(clientId, out SteamConnection connection))
        {
            return 0;
        }

        SteamNetConnectionRealTimeStatus_t status = default;
        SteamNetConnectionRealTimeLaneStatus_t lanes = default;

        return SteamNetworkingSockets.GetConnectionRealTimeStatus(connection.Handle, ref status, 0, ref lanes) == EResult.k_EResultOK
            ? (ulong)Mathf.Max(0, status.m_nPing)
            : 0;
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if (connections.TryGetValue(clientId, out SteamConnection connection))
        {
            // Linger so NGO's final disconnect reason message still gets delivered.
            SteamNetworkingSockets.CloseConnection(connection.Handle, 0, "Disconnected by server", true);
            connections.Remove(clientId);
        }
    }

    public override void DisconnectLocalClient()
    {
        if (serverConnection != null)
        {
            SteamNetworkingSockets.CloseConnection(serverConnection.Handle, 0, "Client disconnected", true);
            connections.Remove(ServerClientId);
            serverConnection = null;
        }
    }

    public override void Shutdown()
    {
        if (SteamManager.Initialized)
        {
            foreach (SteamConnection connection in connections.Values)
            {
                SteamNetworkingSockets.CloseConnection(connection.Handle, 0, "Shutdown", true);
            }

            if (listenSocket != HSteamListenSocket.Invalid)
            {
                SteamNetworkingSockets.CloseListenSocket(listenSocket);
            }
        }

        connections.Clear();
        statusChanges.Clear();
        pendingEvents.Clear();
        listenSocket = HSteamListenSocket.Invalid;
        serverConnection = null;
        isServer = false;

        statusChangedCallback?.Dispose();
        statusChangedCallback = null;
    }

    private void RegisterCallback()
    {
        statusChangedCallback ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(change => statusChanges.Enqueue(change));
    }
}
