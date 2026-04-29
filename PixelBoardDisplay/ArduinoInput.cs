using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace PixelBoard
{
    public class ArduinoButtonEventArgs : EventArgs
    {
        public bool LeftButton { get; }
        public bool RightButton { get; }
        public bool FireButton { get; }
        public bool JoyUp { get; }
        public bool JoyDown { get; }
        public bool JoyLeft { get; }
        public bool JoyRight { get; }
        public bool Extra1 { get; }

        public ArduinoButtonEventArgs(
            bool leftButton,
            bool rightButton,
            bool fireButton,
            bool joyUp,
            bool joyDown,
            bool joyLeft,
            bool joyRight,
            bool extra1)
        {
            LeftButton = leftButton;
            RightButton = rightButton;
            FireButton = fireButton;
            JoyUp = joyUp;
            JoyDown = joyDown;
            JoyLeft = joyLeft;
            JoyRight = joyRight;
            Extra1 = extra1;
        }
    }

    public class ArduinoInput
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        // Virtual-key codes (letters use their ASCII codes)
        // Joystick -> WASD:
        private const byte VK_A = 0x41;      // 'A' (joy left)
        private const byte VK_D = 0x44;      // 'D' (joy right)
        private const byte VK_W = 0x57;      // 'W' (joy up)
        private const byte VK_S = 0x53;      // 'S' (joy down)

        // Buttons -> Q, E (change as desired)
        private const byte VK_Q = 0x51;      // 'Q' (button 1 / left button)
        private const byte VK_E = 0x45;      // 'E' (button 2 / right button)

        // State tracking for keyup events
        private bool lastLeft = false;
        private bool lastRight = false;
        private bool lastJoyUp = false;
        private bool lastJoyDown = false;
        private bool lastJoyLeft = false;
        private bool lastJoyRight = false;

        private readonly SerialPortManager serialPortManager;

        private event ButtonEventHandler ButtonPressEvent;
        public delegate void ButtonEventHandler(object sender, ArduinoButtonEventArgs e);

        public ArduinoInput(SerialPortManager serialPortManager)
        {
            this.serialPortManager = serialPortManager;
            ManageKeyPresses(HandleKeys);
        }

        private void HandleKeys(object sender, ArduinoButtonEventArgs e)
        {
            // Buttons: map to Q / E
            if (e.LeftButton)
                keybd_event(VK_Q, 0, KEYEVENTF_EXTENDEDKEY | 0, 0);
            else if (lastLeft)
                keybd_event(VK_Q, 0, KEYEVENTF_KEYUP | 0, 0);

            if (e.RightButton)
                keybd_event(VK_E, 0, KEYEVENTF_EXTENDEDKEY | 0, 0);
            else if (lastRight)
                keybd_event(VK_E, 0, KEYEVENTF_KEYUP | 0, 0);

            // Joystick -> WASD
            if (e.JoyLeft)
                keybd_event(VK_A, 0, KEYEVENTF_EXTENDEDKEY | 0, 0);
            else if (lastJoyLeft)
                keybd_event(VK_A, 0, KEYEVENTF_KEYUP | 0, 0);

            if (e.JoyRight)
                keybd_event(VK_D, 0, KEYEVENTF_EXTENDEDKEY | 0, 0);
            else if (lastJoyRight)
                keybd_event(VK_D, 0, KEYEVENTF_KEYUP | 0, 0);

            if (e.JoyUp)
                keybd_event(VK_W, 0, KEYEVENTF_EXTENDEDKEY | 0, 0);
            else if (lastJoyUp)
                keybd_event(VK_W, 0, KEYEVENTF_KEYUP | 0, 0);

            if (e.JoyDown)
                keybd_event(VK_S, 0, KEYEVENTF_EXTENDEDKEY | 0, 0);
            else if (lastJoyDown)
                keybd_event(VK_S, 0, KEYEVENTF_KEYUP | 0, 0);

            // Remember for next call
            lastLeft = e.LeftButton;
            lastRight = e.RightButton;
            lastJoyUp = e.JoyUp;
            lastJoyDown = e.JoyDown;
            lastJoyLeft = e.JoyLeft;
            lastJoyRight = e.JoyRight;
        }

        private void ManageKeyPresses(ButtonEventHandler buttonPressEvent)
        {
            Thread buttonManager = new Thread(ButtonThread)
            {
                IsBackground = true
            };
            ButtonPressEvent += buttonPressEvent;
            buttonManager.Start();
        }

        private void ButtonThread()
        {
            var serial = serialPortManager.SerialPort;

            // Basic runtime diagnostics to help you see what's happening
            Console.WriteLine("[ArduinoInput] ButtonThread started");
            bool lastOpenState = serial.IsOpen;
            if (lastOpenState) Console.WriteLine($"[ArduinoInput] SerialPort {serial.PortName} already open");

            while (true)
            {
                try
                {
                    // detect open/close transitions (low-volume logging)
                    if (serial.IsOpen != lastOpenState)
                    {
                        lastOpenState = serial.IsOpen;
                        Console.WriteLine($"[ArduinoInput] SerialPort.IsOpen changed -> {lastOpenState}");
                    }

                    if (serial.IsOpen)
                    {
                        int b = -1;
                        try
                        {
                            b = serial.ReadByte();
                        }
                        catch (TimeoutException)
                        {
                            b = -1;
                        }

                        if (b == -1)
                        {
                            // nothing this iteration
                        }
                        else
                        {
                            // LOG every raw byte read for debugging
                            //Console.WriteLine($"[ArduinoInput] raw byte: 0x{b:X2} ({b})");

                            if (b == 'b')
                            {
                                try
                                {
                                    int input = serial.ReadByte(); // payload byte, bits = buttons

                                    // Diagnostic print of payload and bit breakdown
                                    //Console.WriteLine($"[ARDUINO INPUT] payload=0x{input:X2} bits={Convert.ToString(input, 2).PadLeft(8, '0')} b0={((input & 1) != 0)} b1={((input & 2) != 0)} b2={((input & 4) != 0)} b3={((input & 8) != 0)} b4={((input & 16) != 0)} b5={((input & 32) != 0)} b6={((input & 64) != 0)} b7={((input & 128) != 0)}");

                                    bool b0 = (input & (1 << 0)) != 0;
                                    bool b1 = (input & (1 << 1)) != 0;
                                    bool b2 = (input & (1 << 2)) != 0;
                                    bool b3 = (input & (1 << 3)) != 0;
                                    bool b4 = (input & (1 << 4)) != 0;
                                    bool b5 = (input & (1 << 5)) != 0;
                                    bool b6 = (input & (1 << 6)) != 0;
                                    bool b7 = (input & (1 << 7)) != 0;

                                    var e = new ArduinoButtonEventArgs(
                                        leftButton: b0,
                                        rightButton: b1,
                                        fireButton: b2,
                                        joyUp: b3,
                                        joyDown: b4,
                                        joyLeft: b5,
                                        joyRight: b6,
                                        extra1: b7);

                                    ButtonPressEvent?.Invoke(this, e);
                                }
                                catch (TimeoutException)
                                {
                                    // payload didn't arrive
                                    Console.WriteLine("[ArduinoInput] payload read timed out after marker");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"ArduinoInput: discarded byte {b} (0x{b:X2}) while waiting for marker 'b'");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ArduinoInput ButtonThread error: {ex.Message}");
                }

                Thread.Sleep(10);
            }
        }
    }
}
