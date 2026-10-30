
using UnityEngine;
using System.IO.Ports;
using System.Globalization;
using System.Collections;

public class ESP32reader : MonoBehaviour
{
    [Header("ESP32")]
    public float usbValue;
public float targetValue;
public float smoothSpeed = 5f;
    [Header("Instellingen")]
    public float retryInterval = 3f;

    private SerialPort serialPort;
    private Coroutine searchCoroutine;

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
                Debug.LogWarning(
                    "ESP32 niet gevonden. Nieuwe poging over "
                    + retryInterval + " seconden."
                );

                yield return new WaitForSeconds(retryInterval);
            }
        }

        Debug.Log("ESP32 succesvol verbonden!");
        searchCoroutine = null;
    }

    void FindESP32()
    {
        string[] ports = SerialPort.GetPortNames();

        Debug.Log("Aantal gevonden poorten: " + ports.Length);

        foreach (string port in ports)
        {
            Debug.Log("Poort gevonden: " + port);

            if (TryConnect(port))
            {
                Debug.Log("ESP32 gevonden op: " + port);
                return;
            }
        }

        Debug.LogWarning("ESP32 niet gevonden!");
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
                NewLine = "\n"
            };

            testPort.Open();

            // Geef de ESP32 tijd om te initialiseren
            System.Threading.Thread.Sleep(200);

            testPort.DiscardInBuffer();

            // Handshake
            testPort.WriteLine("HELLO");

            string response = testPort.ReadLine().Trim();

            Debug.Log("Antwoord van " + portName + ": " + response);

            if (response == DEVICE_NAME)
            {
                serialPort = testPort;
                return true;
            }

            testPort.Close();
            testPort.Dispose();
        }
        catch (System.Exception e)
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

  void Update()
{
    if (serialPort != null && serialPort.IsOpen)
    {
        try
        {
            while (serialPort.BytesToRead > 0)
            {
                string data = serialPort.ReadLine().Trim();

                if (float.TryParse(
                    data,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value))
                {
                    targetValue = value;
                }
            }
        }
        catch (System.TimeoutException)
        {
            // Geen nieuwe data
        }
        catch (System.Exception e)
        {
            Debug.LogError("USB fout: " + e.Message);

            Disconnect();

            if (searchCoroutine == null)
                searchCoroutine = StartCoroutine(SearchForESP32());
        }
    }

    // Smooth de waarde geleidelijk richting de doelwaarde
    usbValue = Mathf.Lerp(
        usbValue,
        targetValue,
        1f - Mathf.Exp(-smoothSpeed * Time.deltaTime)
    );
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
        if (serialPort != null)
        {
            try
            {
                if (serialPort.IsOpen)
                    serialPort.Close();

                serialPort.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Fout bij afsluiten: " + e.Message);
            }

            serialPort = null;
        }
    }
}

