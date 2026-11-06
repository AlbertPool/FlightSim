using UnityEngine;
using System;
using System.IO.Ports;
using System.Globalization;
using System.Collections;
using System.Threading;

public class ESP32reader : MonoBehaviour
{
    [Header("ESP32 - Sensor 1 (hoogte)")]
    public float usbValue;
    public float targetValue;

    [Header("ESP32 - Sensor 2 (roll)")]
    public float usbValue2;
    public float targetValue2;

    public float smoothSpeed = 5f;

    [Header("Instellingen")]
    public float retryInterval = 3f;

    private SerialPort serialPort;
    private Coroutine searchCoroutine;

    private Thread readThread;
    private volatile bool running;

    private volatile bool hasReceivedValue = false;
    public bool HasReceivedValue => hasReceivedValue;

    private readonly object dataLock = new object();
    private float receivedValue;
    private float receivedValue2;

    private const int BAUD_RATE = 115200;
    private const string DEVICE_NAME = "ESP32_CONTROLLER";

    void Start()
    {
        searchCoroutine = StartCoroutine(SearchForESP32());
    }

    IEnumerator SearchForESP32()
    {
        while (serialPort == null || !serialPort.IsOpen)
        {
            Debug.Log("Zoeken naar ESP32...");

            FindESP32();

            if (serialPort == null || !serialPort.IsOpen)
            {
                Debug.LogWarning("ESP32 niet gevonden. Nieuwe poging over "
                    + retryInterval + " seconden.");

                yield return new WaitForSeconds(retryInterval);
            }
        }

        Debug.Log("ESP32 succesvol verbonden!");

        StartReading();

        searchCoroutine = null;
    }

    void FindESP32()
    {
        string[] ports = SerialPort.GetPortNames();

        Debug.Log("Aantal gevonden COM-poorten: " + ports.Length);

        foreach (string port in ports)
        {
            Debug.Log("COM-poort gevonden: " + port);

            if (TryConnect(port))
            {
                Debug.Log("ESP32 gevonden op: " + port);
                return;
            }
        }
    }

    bool TryConnect(string portName)
    {
        SerialPort testPort = null;

        try
        {
            testPort = new SerialPort(portName, BAUD_RATE)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                NewLine = "\n",
                DtrEnable = false,
                RtsEnable = false
            };

            testPort.Open();

            Thread.Sleep(200);
            testPort.DiscardInBuffer();

            testPort.WriteLine("HELLO");

            // Meetregels kunnen tussen het antwoord zitten, dus meerdere regels lezen
            for (int i = 0; i < 10; i++)
            {
                string response = testPort.ReadLine().Trim();
                Debug.Log("Antwoord van " + portName + ": " + response);

                if (response == DEVICE_NAME)
                {
                    serialPort = testPort;
                    return true;
                }
            }

            testPort.Close();
            testPort.Dispose();
        }
        catch (Exception e)
        {
            Debug.Log("Poort " + portName + " niet geschikt: " + e.Message);

            if (testPort != null)
            {
                try
                {
                    if (testPort.IsOpen)
                        testPort.Close();

                    testPort.Dispose();
                }
                catch { }
            }
        }

        return false;
    }

    void StartReading()
    {
        running = true;

        readThread = new Thread(ReadSerialData);
        readThread.IsBackground = true;
        readThread.Start();
    }

    void ReadSerialData()
    {
        while (running && serialPort != null && serialPort.IsOpen)
        {
            try
            {
                string data = serialPort.ReadLine().Trim();
                string[] parts = data.Split(',');

                // Verwacht "hoogte,roll". Een mislukte meting (-1) wordt genegeerd.
                if (parts.Length >= 2 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v1) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v2) &&
                    v1 > 0f && v2 > 0f)
                {
                    lock (dataLock)
                    {
                        receivedValue = v1;
                        receivedValue2 = v2;
                        hasReceivedValue = true;
                    }
                }
            }
            catch (TimeoutException)
            {
                // Geen nieuwe data
            }
            catch (Exception e)
            {
                Debug.LogError("Serial fout: " + e.Message);
                running = false;
            }
        }
    }

    void Update()
    {
        if (hasReceivedValue)
        {
            lock (dataLock)
            {
                targetValue = receivedValue;
                targetValue2 = receivedValue2;
            }
        }

        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        usbValue = Mathf.Lerp(usbValue, targetValue, t);
        usbValue2 = Mathf.Lerp(usbValue2, targetValue2, t);

        if (!running && serialPort != null)
        {
            Disconnect();

            if (searchCoroutine == null)
            {
                searchCoroutine = StartCoroutine(SearchForESP32());
            }
        }
    }

    void OnDestroy()
    {
        Disconnect();
    }

    void OnApplicationQuit()
    {
        Disconnect();
    }

    void Disconnect()
    {
        running = false;

        if (readThread != null && readThread.IsAlive)
        {
            readThread.Join(1500);
            readThread = null;
        }

        if (serialPort != null)
        {
            try
            {
                if (serialPort.IsOpen)
                    serialPort.Close();

                serialPort.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Fout bij afsluiten: " + e.Message);
            }

            serialPort = null;
        }

        hasReceivedValue = false;
    }
}
