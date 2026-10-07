using UnityEngine;
using System.IO.Ports;
using System.Globalization;

public class ESP32reader : MonoBehaviour
{
    [Header("ESP32")]
    public float usbValue;

    private SerialPort serialPort;

    private const int BAUD_RATE = 115200;
    private const string DEVICE_NAME = "ESP32_CONTROLLER";

    void Start()
    {
        FindESP32();
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

        Debug.LogError("ESP32 niet gevonden!");
    }

    bool TryConnect(string portName)
    {
        SerialPort testPort = null;

        try
        {
            testPort = new SerialPort(portName, BAUD_RATE);

            testPort.ReadTimeout = 1000;
            testPort.WriteTimeout = 1000;
            testPort.NewLine = "\n";

            testPort.Open();

            // Geef de ESP32 even tijd om te initialiseren
            System.Threading.Thread.Sleep(200);

            // Oude data verwijderen
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
        }
        catch (System.Exception e)
        {
            Debug.Log("Poort " + portName + " is niet de ESP32: " + e.Message);

            if (testPort != null && testPort.IsOpen)
            {
                testPort.Close();
            }
        }

        return false;
    }

    void Update()
    {
        if (serialPort == null || !serialPort.IsOpen)
        {
            return;
        }

        try
        {
            while (serialPort.BytesToRead > 0)
            {
                string data = serialPort.ReadLine().Trim();
                if (float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    usbValue = value;
                    Debug.Log("ESP32 waarde: " + usbValue);
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
        if (serialPort != null)
        {
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }

            serialPort.Dispose();
            serialPort = null;
        }
    }
}
