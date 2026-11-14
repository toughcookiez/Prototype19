#if UNITY_EDITOR
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// Editor-only testing helper: grant any Card asset to this player at any time from the inspector.
// Compiled out of player builds entirely.
public partial class FirstPersonController
{
    public void EditorGiveCard(Card card)
    {
        if (card == null)
        {
            return;
        }

        bool isNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        if (!isNetworked || IsServer)
        {
            ApplyPickedCard(card);
        }
        else if (IsOwner)
        {
            EditorGiveCardServerRpc(card.name);
        }
        else
        {
            Debug.LogWarning("[DebugCards] Only the host or this player's owner can grant cards.", this);
        }
    }

    [ServerRpc]
    private void EditorGiveCardServerRpc(string cardAssetName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Card " + cardAssetName))
        {
            Card card = AssetDatabase.LoadAssetAtPath<Card>(AssetDatabase.GUIDToAssetPath(guid));

            if (card != null && card.name == cardAssetName)
            {
                ApplyPickedCard(card);
                return;
            }
        }

        Debug.LogWarning($"[DebugCards] Card asset '{cardAssetName}' not found.", this);
    }
}
#endif
