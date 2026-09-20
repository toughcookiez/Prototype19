using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpawnPoint : MonoBehaviour
{
    private static readonly List<SpawnPoint> activeSpawnPoints = new List<SpawnPoint>();

    [Tooltip("Optional spawn index (e.g. 0 for Player 1, 1 for Player 2). Set to -1 to allow any player.")]
    public int spawnIndex = -1;

    public static IReadOnlyList<SpawnPoint> All => activeSpawnPoints;

    private void OnEnable()
    {
        if (!activeSpawnPoints.Contains(this))
        {
            activeSpawnPoints.Add(this);
        }
    }

    private void OnDisable()
    {
        activeSpawnPoints.Remove(this);
    }

    /// <summary>
    /// Finds a spawn position and rotation for the specified player index.
    /// Priority:
    /// 1. SpawnPoint component with matching spawnIndex
    /// 2. Any SpawnPoint component (assigned via playerIndex modulo count)
    /// 3. GameObject tagged "Respawn"
    /// 4. Calculated default offset position
    /// </summary>
    public static bool TryGetSpawnPoint(int playerIndex, out Vector3 position, out Quaternion rotation)
    {
        List<SpawnPoint> validPoints = new List<SpawnPoint>();
        for (int i = 0; i < activeSpawnPoints.Count; i++)
        {
            if (activeSpawnPoints[i] != null && activeSpawnPoints[i].gameObject.activeInHierarchy)
            {
                validPoints.Add(activeSpawnPoints[i]);
            }
        }

        if (validPoints.Count > 0)
        {
            // 1. Exact match for spawnIndex
            for (int i = 0; i < validPoints.Count; i++)
            {
                if (validPoints[i].spawnIndex == playerIndex)
                {
                    position = validPoints[i].transform.position;
                    rotation = Quaternion.Euler(0f, validPoints[i].transform.eulerAngles.y, 0f);
                    return true;
                }
            }

            // 2. Modulo distribution across available spawn points
            int index = Mathf.Abs(playerIndex) % validPoints.Count;
            SpawnPoint selected = validPoints[index];
            position = selected.transform.position;
            rotation = Quaternion.Euler(0f, selected.transform.eulerAngles.y, 0f);
            return true;
        }

        // 3. Fallback to GameObjects tagged "Respawn"
        try
        {
            GameObject[] respawnObjects = GameObject.FindGameObjectsWithTag("Respawn");
            if (respawnObjects != null && respawnObjects.Length > 0)
            {
                int index = Mathf.Abs(playerIndex) % respawnObjects.Length;
                GameObject ro = respawnObjects[index];
                if (ro != null)
                {
                    position = ro.transform.position;
                    rotation = Quaternion.Euler(0f, ro.transform.eulerAngles.y, 0f);
                    return true;
                }
            }
        }
        catch
        {
            // Tag might not exist in project settings, ignore safely
        }

        // 4. Default fallback: players face each other spaced out
        float xOffset = (playerIndex % 2 == 0) ? -5f : 5f;
        float zOffset = (playerIndex / 2) * 2f;
        position = new Vector3(xOffset, 1.5f, zOffset);
        rotation = Quaternion.Euler(0f, (playerIndex % 2 == 0) ? 90f : 270f, 0f);
        return false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.75f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 1f, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);

        // Forward arrow
        Gizmos.color = Color.yellow;
        Vector3 arrowStart = transform.position + Vector3.up * 1f;
        Vector3 forward = transform.forward * 1.5f;
        Gizmos.DrawRay(arrowStart, forward);
    }
}
