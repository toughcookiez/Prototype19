using Unity.Netcode;
using UnityEngine;

public class MultiplayerLauncher : MonoBehaviour
{
    private const int ButtonWidth = 140;
    private const int ButtonHeight = 36;
    private const int ButtonGap = 8;

    public bool showLauncher = true;

    private void OnGUI()
    {
        if (!showLauncher || NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
        {
            return;
        }

        int x = 16;
        int y = 16;

        if (GUI.Button(new Rect(x, y, ButtonWidth, ButtonHeight), "Host"))
        {
            NetworkManager.Singleton.StartHost();
        }

        y += ButtonHeight + ButtonGap;

        if (GUI.Button(new Rect(x, y, ButtonWidth, ButtonHeight), "Client"))
        {
            NetworkManager.Singleton.StartClient();
        }

        y += ButtonHeight + ButtonGap;

        if (GUI.Button(new Rect(x, y, ButtonWidth, ButtonHeight), "Server"))
        {
            NetworkManager.Singleton.StartServer();
        }
    }
}