using System;
using System.IO.Ports;
using System.Threading;

namespace PixelBoard
{
    public class SerialPortManager
    {
        private static readonly SerialPort serialPort = new SerialPort();

        public SerialPort SerialPort => serialPort;

        public SerialPortManager()
        {
            if (serialPort.IsOpen)
            {
                return;
            }

            serialPort.PortName = "COM5";
            serialPort.BaudRate = 115200;
            serialPort.ReadTimeout = 100;
            serialPort.WriteTimeout = 1000;
            serialPort.Handshake = Handshake.None;
            serialPort.DtrEnable = false;
            serialPort.RtsEnable = false;

            while (!serialPort.IsOpen)
            {
                try
                {
                    serialPort.Open();
                    Thread.Sleep(2000);
                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();
                    serialPort.DtrEnable = true;
                    serialPort.RtsEnable = true;
                    Console.WriteLine($"[PixelBoard] Connected to {serialPort.PortName}");
                }
                catch (UnauthorizedAccessException e)
                {
                    Console.WriteLine($"Error: {e.Message}");
                }
                catch (InvalidOperationException e)
                {
                    Console.WriteLine($"Error: {e.Message}");
                }
                catch (System.IO.IOException e)
                {
                    Console.WriteLine($"Error: {e.Message}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error: {e.Message}");
                }

                if (!serialPort.IsOpen)
                {
                    Thread.Sleep(250);
                }
            }
        }
    }
}