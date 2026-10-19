using UnityEditor;
using UnityEngine;

public static class SteamEditorTools
{
    private const string UseSteamMenu = "Tools/Steam/Use Steam In Editor";

    [MenuItem(UseSteamMenu)]
    private static void ToggleUseSteam()
    {
        SteamManager.UseSteamInEditor = !SteamManager.UseSteamInEditor;
        Debug.Log("[Steam] Use Steam In Editor: " + SteamManager.UseSteamInEditor + " (applies next Play)");
    }

    [MenuItem(UseSteamMenu, true)]
    private static bool ToggleUseSteamValidate()
    {
        Menu.SetChecked(UseSteamMenu, SteamManager.UseSteamInEditor);
        return true;
    }
}
