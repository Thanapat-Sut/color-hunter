using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Realtime LED-press receiver for ledinput-Copy.py's debug channel
/// (default UDP 9001). Each packet is one JSON event:
///   {"e":"down","x":3,"y":17,"ip":102}
///   {"e":"up","x":3,"y":17,"ip":102}
///
/// LED hardware already emits coords that match the game grid 1:1, so
/// this script forwards (x, y) to <see cref="onTouchDown"/> /
/// <see cref="onTouchUp"/> verbatim. Wire those events to
/// BaseGridManager.OnDebugTouchDown / OnDebugTouchUp in the Inspector.
/// </summary>
[Serializable]
public class LedTouchEvent : UnityEvent<int, int> { }

public class LedInputDebugReceiver : MonoBehaviour
{
    [Header("Network")]
    [SerializeField] int port = 9001;

    [Header("Logging")]
    [SerializeField] bool logToConsole = true;
    [SerializeField] bool logBindOnStart = true;

    [Header("Forwarding (wire to BaseGridManager.OnDebugTouchDown/Up)")]
    public LedTouchEvent onTouchDown;
    public LedTouchEvent onTouchUp;

    public event Action<string, int, int, int> OnLedEvent; // (eventType, x, y, ip)

    UdpClient _client;
    Thread _thread;
    readonly object _queueLock = new object();
    readonly Queue<string> _queue = new Queue<string>();
    volatile bool _running;

    [Serializable]
    class LedDebugEvent
    {
        public string e;
        public int x;
        public int y;
        public int ip;
    }

    void OnEnable()
    {
        try
        {
            _client = new UdpClient(port);
        }
        catch (SocketException ex)
        {
            Debug.LogError($"[LedInputDebug] bind failed on port {port}: {ex.Message}");
            return;
        }

        _running = true;
        _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "LedInputDebugReceiver" };
        _thread.Start();

        if (logBindOnStart)
            Debug.Log($"[LedInputDebug] listening on UDP {port}");
    }

    void OnDisable()
    {
        _running = false;
        try { _client?.Close(); } catch { }
        _client = null;
        try { _thread?.Join(200); } catch { }
        _thread = null;
    }

    void ReceiveLoop()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[] data = _client.Receive(ref ep);
                lock (_queueLock) _queue.Enqueue(Encoding.UTF8.GetString(data));
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LedInputDebug] recv error: {ex.Message}");
                break;
            }
        }
    }

    void Update()
    {
        lock (_queueLock)
        {
            while (_queue.Count > 0)
            {
                string raw = _queue.Dequeue();
                Dispatch(raw);
            }
        }
    }

    void Dispatch(string raw)
    {
        LedDebugEvent evt;
        try { evt = JsonUtility.FromJson<LedDebugEvent>(raw); }
        catch (Exception ex)
        {
            if (logToConsole) Debug.LogWarning($"[LedInputDebug] parse fail: {ex.Message}  raw={raw}");
            return;
        }
        if (evt == null || string.IsNullOrEmpty(evt.e)) return;

        if (logToConsole)
            Debug.Log($"[LedInputDebug] {evt.e.ToUpperInvariant()} ({evt.x},{evt.y}) ip={evt.ip}");

        OnLedEvent?.Invoke(evt.e, evt.x, evt.y, evt.ip);

        if (evt.e == "down") onTouchDown?.Invoke(evt.x, evt.y);
        else if (evt.e == "up") onTouchUp?.Invoke(evt.x, evt.y);
    }
}
