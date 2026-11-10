
using UnityEngine;
using System;
using System.IO.Ports;
using System.Globalization;
using System.Threading;
using System.Collections.Generic;

public class ESP32reader : MonoBehaviour
{
    [Header("Sensor 1 - Hoogte")]
    public float usbValue;
    public float targetValue;

    [Header("Sensor 2 - Roll")]
    public float usbValue2;
    public float targetValue2;

    [Header("Instellingen")]
    public float smoothSpeed = 5f;
    public float retryInterval = 3f;
    public string preferredPort = "";
    public float bootWaitSeconds = 2f;

    [Header("Debug")]
    public bool debugLogs = true;

    private SerialPort serialPort;
    private Thread worker;
    private volatile bool running;
    private volatile bool connected;

    private readonly object dataLock = new object();

    private float receivedHeight;
    private float receivedRoll;

    public bool HasHeight { get; private set; }
    public bool HasRoll { get; private set; }

    public bool HasReceivedValue => HasHeight || HasRoll;

    private string currentPort = "";

    public string StatusText =>
        connected ? "Verbonden met " + currentPort : "Zoekt naar ESP32...";

    void Start()
    {
        if (debugLogs)
            Debug.Log("[ESP32] Start zoeken...");

        StartConnection();
    }

    void StartConnection()
    {
        if (worker != null && worker.IsAlive)
            return;

        running = true;
        worker = new Thread(ConnectionLoop);
        worker.IsBackground = true;
        worker.Start();
    }

    void ConnectionLoop()
    {
        while (running)
        {
            string[] ports;

            try
            {
                ports = SerialPort.GetPortNames();
            }
            catch
            {
                Thread.Sleep(1000);
                continue;
            }

            List<string> portList = new List<string>(ports);

            if (!string.IsNullOrEmpty(preferredPort))
            {
                portList.Remove(preferredPort);
                portList.Insert(0, preferredPort);
            }

            foreach (string port in portList)
            {
                if (!running)
                    break;

                SerialPort candidate = null;

                try
                {
                    if (debugLogs)
                        Debug.Log("[ESP32] Probeer " + port);

                    candidate = new SerialPort(
                        port,
                        115200,
                        Parity.None,
                        8,
                        StopBits.One
                    );

                    candidate.ReadTimeout = 500;
                    candidate.WriteTimeout = 500;
                    candidate.NewLine = "\n";
                    candidate.DtrEnable = false;
                    candidate.RtsEnable = false;

                    candidate.Open();

                    Thread.Sleep((int)(bootWaitSeconds * 1000));

                    candidate.DiscardInBuffer();

                    // Handshake met ESP32
                    candidate.WriteLine("HELLO");

                    bool handshakeValid = false;
                    DateTime handshakeStart = DateTime.UtcNow;

                    while (running &&
                           (DateTime.UtcNow - handshakeStart).TotalSeconds < 5)
                    {
                        try
                        {
                            string response = candidate.ReadLine().Trim();

                            if (response == "ESP32_CONTROLLER")
                            {
                                handshakeValid = true;
                                break;
                            }
                        }
                        catch (TimeoutException) { }
                    }

                    if (!handshakeValid)
                        continue;

                    serialPort = candidate;
                    currentPort = port;
                    connected = true;

                    if (debugLogs)
                        Debug.Log("[ESP32] Verbonden: " + port);

                    ReadLoop(candidate);

                    connected = false;
                    serialPort = null;
                    currentPort = "";
                }
                catch (Exception e)
                {
                    if (debugLogs)
                        Debug.LogWarning("[ESP32] " + port + ": " + e.Message);
                }
                finally
                {
                    if (candidate != null)
                    {
                        try
                        {
                            if (candidate.IsOpen)
                                candidate.Close();

                            candidate.Dispose();
                        }
                        catch { }
                    }
                }
            }

            if (running && !connected)
                Thread.Sleep((int)(retryInterval * 1000));
        }
    }

    void ReadLoop(SerialPort port)
    {
        if (debugLogs)
            Debug.Log("[ESP32] Sensorverbinding actief!");

        while (running && port.IsOpen)
        {
            try
            {
                string line = port.ReadLine().Trim();

                if (ParseSensorData(line, out float height, out float roll))
                {
                    lock (dataLock)
                    {
                        // Sensor 1: Hoogte
                        if (height >= 0)
                        {
                            receivedHeight = height;
                            HasHeight = true;
                        }

                        // Sensor 2: Roll
                        if (roll >= 0)
                        {
                            receivedRoll = roll;
                            HasRoll = true;
                        }
                    }
                }
            }
            catch (TimeoutException)
            {
                // Wachten op nieuwe data.
            }
            catch (Exception e)
            {
                if (debugLogs)
                    Debug.LogWarning("[ESP32] Verbinding verloren: " + e.Message);

                break;
            }
        }
    }

    bool ParseSensorData(string line, out float height, out float roll)
    {
        height = -1;
        roll = -1;

        string[] parts = line.Split(',');

        if (parts.Length < 2)
            return false;

        bool validHeight = float.TryParse(
            parts[0].Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out height
        );

        bool validRoll = float.TryParse(
            parts[1].Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out roll
        );

        return validHeight && validRoll;
    }

    void Update()
    {
        lock (dataLock)
        {
            if (HasHeight)
                targetValue = receivedHeight;

            if (HasRoll)
                targetValue2 = receivedRoll;
        }

        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);

        usbValue = Mathf.Lerp(usbValue, targetValue, t);
        usbValue2 = Mathf.Lerp(usbValue2, targetValue2, t);
    }

    void OnDestroy()
    {
        StopConnection();
    }

    void OnApplicationQuit()
    {
        StopConnection();
    }

    void StopConnection()
    {
        running = false;

        if (serialPort != null)
        {
            try
            {
                serialPort.Close();
            }
            catch { }
        }

        if (worker != null && worker.IsAlive)
        {
            worker.Join(1000);
            worker = null;
        }
    }
}

