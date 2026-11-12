using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// Game → tablet: sends "RMOTgame_data {json}" over UDP to the launcher on this PC
/// (127.0.0.1:19780), which hands it to the tablet as a launcher://game_data event.
/// Same framing as the launcher's DualLaneTest "mirror" report. Fire-and-forget: callers
/// resend about once a second while the tablet has to know.
/// </summary>
public static class TabletReport
{
    public const int LauncherPort = 19780;
    static UdpClient _udp;

    public static void Send(string json)
    {
        try
        {
            _udp ??= new UdpClient();
            byte[] data = Encoding.UTF8.GetBytes("RMOTgame_data " + json);
            _udp.Send(data, data.Length, "127.0.0.1", LauncherPort);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TabletReport] send failed: {e.Message}");
        }
    }

    /// <summary>
    /// {"type":"setup","data":{"screen":"...","players":N, ...}} - the setup screen the game is on
    /// and what it shows. extraFields is more JSON members, e.g. ,"category":"math".
    /// </summary>
    public static void Setup(string screen, int players, string extraFields = "") =>
        Send($"{{\"type\":\"setup\",\"data\":{{\"screen\":\"{screen}\",\"players\":{players}{extraFields}}}}}");
}
