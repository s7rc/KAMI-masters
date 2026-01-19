#if Linux
using KAMI.Core.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace KAMI.Core.Linux
{
    /// <summary>
    /// Linux mouse handler using evdev for raw mouse input.
    /// Reads relative mouse movements directly from /dev/input/eventX.
    /// Uses ydotool for cursor manipulation when needed.
    /// </summary>
    public class MouseHandler : IMouseHandler
    {
        private int _xDiff = 0;
        private int _yDiff = 0;
        private readonly object _lockObj = new object();
        private Thread _readThread;
        private bool _running = false;
        private FileStream _mouseDevice;
        private bool _confined = false;

        // evdev event structure (24 bytes on 64-bit)
        [StructLayout(LayoutKind.Sequential)]
        private struct InputEvent
        {
            public long Seconds;      // timeval.tv_sec
            public long Microseconds; // timeval.tv_usec  
            public ushort Type;
            public ushort Code;
            public int Value;
        }

        // Event types
        private const ushort EV_REL = 0x02;  // Relative movement
        private const ushort EV_KEY = 0x01;  // Key/button event
        
        // Relative event codes
        private const ushort REL_X = 0x00;
        private const ushort REL_Y = 0x01;

        public MouseHandler()
        {
            string mouseDevice = FindMouseDevice();
            if (mouseDevice != null)
            {
                try
                {
                    _mouseDevice = new FileStream(mouseDevice, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _running = true;
                    _readThread = new Thread(ReadLoop) { IsBackground = true };
                    _readThread.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not open mouse device {mouseDevice}: {ex.Message}");
                    Console.WriteLine("Mouse tracking will be limited. Make sure user is in 'input' group.");
                }
            }
            else
            {
                Console.WriteLine("Warning: No mouse device found in /dev/input/");
            }
        }

        /// <summary>
        /// Find a mouse device by checking /dev/input/by-id/ for mouse symlinks
        /// </summary>
        private string FindMouseDevice()
        {
            string byIdPath = "/dev/input/by-id";
            if (Directory.Exists(byIdPath))
            {
                foreach (var file in Directory.GetFiles(byIdPath))
                {
                    if (file.Contains("mouse") || file.Contains("Mouse"))
                    {
                        try
                        {
                            // Resolve symlink to actual device
                            string target = Path.GetFullPath(Path.Combine(byIdPath, File.ResolveLinkTarget(file, false)?.FullName ?? file));
                            if (File.Exists(target))
                            {
                                return target;
                            }
                        }
                        catch { }
                    }
                }
            }

            // Fallback: try common mouse event devices
            // Usually mice are on lower event numbers
            for (int i = 0; i < 20; i++)
            {
                string device = $"/dev/input/event{i}";
                if (File.Exists(device))
                {
                    // Check if this is a mouse by examining its capabilities
                    // For simplicity, we'll try the device and hope for the best
                    // A more robust solution would use libevdev or check capabilities via ioctl
                    if (IsLikelyMouse(device))
                    {
                        return device;
                    }
                }
            }

            // Last resort: try /dev/input/mice (legacy interface)
            if (File.Exists("/dev/input/mice"))
            {
                return "/dev/input/mice";
            }

            return null;
        }

        /// <summary>
        /// Check if a device is likely a mouse by checking device name and capabilities
        /// </summary>
        private bool IsLikelyMouse(string devicePath)
        {
            try
            {
                string eventName = Path.GetFileName(devicePath);
                if (!eventName.StartsWith("event"))
                    return false;

                // First check device name
                string namePath = $"/sys/class/input/{eventName}/device/name";
                if (File.Exists(namePath))
                {
                    string name = File.ReadAllText(namePath).Trim().ToLower();
                    Console.WriteLine($"[MouseHandler] Checking {eventName}: {name}");
                    
                    // Skip non-mouse devices
                    if (name.Contains("keyboard") || 
                        name.Contains("power button") ||
                        name.Contains("lid switch") ||
                        name.Contains("sleep button") ||
                        name.Contains("video bus") ||
                        name.Contains("pc speaker"))
                    {
                        return false;
                    }
                    
                    // Check if it's explicitly a mouse/pointer device
                    bool looksLikeMouse = name.Contains("mouse") || 
                                          name.Contains("pointer") ||
                                          name.Contains("touchpad") ||
                                          name.Contains("trackpoint") ||
                                          name.Contains("trackpad");
                    
                    // Check capabilities - mouse must have REL_X and REL_Y
                    string capsPath = $"/sys/class/input/{eventName}/device/capabilities/rel";
                    if (File.Exists(capsPath))
                    {
                        string capsStr = File.ReadAllText(capsPath).Trim();
                        // Parse hex string (might be "3" or "103" or similar)
                        if (long.TryParse(capsStr, System.Globalization.NumberStyles.HexNumber, null, out long capValue))
                        {
                            bool hasRelXY = (capValue & 0x3) == 0x3;
                            if (hasRelXY)
                            {
                                Console.WriteLine($"[MouseHandler] {eventName} has REL_X/Y capabilities - this is a mouse!");
                                return true;
                            }
                        }
                    }
                    
                    // If name looks like a mouse even without REL caps, try it
                    if (looksLikeMouse)
                    {
                        Console.WriteLine($"[MouseHandler] {eventName} name suggests mouse, trying it");
                        return true;
                    }
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[MouseHandler] Error checking {devicePath}: {ex.Message}");
            }
            return false;
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[24]; // sizeof(input_event) on 64-bit
            
            while (_running && _mouseDevice != null)
            {
                try
                {
                    int bytesRead = _mouseDevice.Read(buffer, 0, buffer.Length);
                    if (bytesRead == buffer.Length)
                    {
                        ushort type = BitConverter.ToUInt16(buffer, 16);
                        ushort code = BitConverter.ToUInt16(buffer, 18);
                        int value = BitConverter.ToInt32(buffer, 20);

                        if (type == EV_REL)
                        {
                            lock (_lockObj)
                            {
                                if (code == REL_X)
                                    _xDiff += value;
                                else if (code == REL_Y)
                                    _yDiff += value;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Device may have been disconnected
                    Thread.Sleep(100);
                }
            }
        }

        public (int, int) GetCenterDiff()
        {
            int x, y;
            lock (_lockObj)
            {
                x = _xDiff;
                y = _yDiff;
                _xDiff = 0;
                _yDiff = 0;
            }
            return (x, y);
        }

        public void ConfineCursor()
        {
            _confined = true;
            // On Wayland, cursor confinement is compositor-specific
            // For now, we rely on the game/emulator handling this
            // ydotool could be used to reset cursor position periodically if needed
        }

        public void ReleaseCursor()
        {
            _confined = false;
        }

        public void Dispose()
        {
            _running = false;
            _mouseDevice?.Dispose();
            _readThread?.Join(1000);
        }
    }
}
#endif
