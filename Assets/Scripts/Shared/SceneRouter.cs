using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fortal.UDP;

public static class SceneRouter
{
    public static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public static void LoadRandomOtherScene()
    {
        CleanupUdpReceivers();

        int current = SceneManager.GetActiveScene().buildIndex;
        int count = SceneManager.sceneCountInBuildSettings;
        var others = new List<int>();
        for (int i = 0; i < count; i++)
            if (i != current) others.Add(i);
        if (others.Count == 0) return;
        SceneManager.LoadScene(others[Random.Range(0, others.Count)]);
    }

    // UdpReceiver only closes socket in OnApplicationQuit, not OnDestroy.
    // Force-close before scene switch to free the port.
    static void CleanupUdpReceivers()
    {
        var type = typeof(UdpReceiver);
        var clientField = type.GetField("udpClient", BindingFlags.NonPublic | BindingFlags.Instance);
        var threadField = type.GetField("receiveThread", BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var udp in Object.FindObjectsOfType<UdpReceiver>())
        {
            try
            {
                var thread = threadField?.GetValue(udp) as System.Threading.Thread;
                thread?.Abort();

                var client = clientField?.GetValue(udp) as System.Net.Sockets.UdpClient;
                client?.Close();
            }
            catch { }

            Object.Destroy(udp.gameObject);
        }
    }
}
