#if Linux
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using KAMI.Core.Common;

namespace KAMI.Core.Linux
{
    /// <summary>
    /// Linux key handler using evdev for global keyboard monitoring.
    /// Reads keyboard events from /dev/input/eventX devices.
    /// </summary>
    public class KeyHandler : IKeyHandler
    {
        public event KeyPressHandler OnKeyPress;

        private readonly Dictionary<KeyType, int?> _hotkeys = new Dictionary<KeyType, int?>();
        private Thread _readThread;
        private bool _running = false;
        private FileStream _keyboardDevice;
        private bool _mouseHookEnabled = false;

        // evdev event types
        private const ushort EV_KEY = 0x01;
        
        // Key states
        private const int KEY_RELEASE = 0;
        private const int KEY_PRESS = 1;
        private const int KEY_REPEAT = 2;

        public KeyHandler()
        {
            string keyboardDevice = FindKeyboardDevice();
            if (keyboardDevice != null)
            {
                try
                {
                    _keyboardDevice = new FileStream(keyboardDevice, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _running = true;
                    _readThread = new Thread(ReadLoop) { IsBackground = true };
                    _readThread.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not open keyboard device {keyboardDevice}: {ex.Message}");
                    Console.WriteLine("Global hotkeys will not work. Make sure user is in 'input' group.");
                }
            }
            else
            {
                Console.WriteLine("Warning: No keyboard device found in /dev/input/");
            }
        }

        /// <summary>
        /// Find a keyboard device by checking /dev/input/by-id/ for keyboard symlinks
        /// </summary>
        private string FindKeyboardDevice()
        {
            string byIdPath = "/dev/input/by-id";
            if (Directory.Exists(byIdPath))
            {
                foreach (var file in Directory.GetFiles(byIdPath))
                {
                    // Look for keyboard devices (usually contain "kbd" or "keyboard")
                    string fileName = Path.GetFileName(file).ToLower();
                    if (fileName.Contains("kbd") || fileName.Contains("keyboard"))
                    {
                        // Skip mouse devices that might have kbd in name
                        if (fileName.Contains("mouse"))
                            continue;
                            
                        try
                        {
                            var linkTarget = File.ResolveLinkTarget(file, false);
                            if (linkTarget != null)
                            {
                                string target = Path.GetFullPath(Path.Combine(byIdPath, linkTarget.FullName));
                                if (File.Exists(target))
                                {
                                    return target;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            // Fallback: check /sys/class/input for keyboard capabilities
            for (int i = 0; i < 20; i++)
            {
                string device = $"/dev/input/event{i}";
                if (File.Exists(device) && IsLikelyKeyboard(device))
                {
                    return device;
                }
            }

            return null;
        }

        /// <summary>
        /// Check if a device is likely a keyboard by examining device name and key capabilities
        /// </summary>
        private bool IsLikelyKeyboard(string devicePath)
        {
            try
            {
                string eventName = Path.GetFileName(devicePath);
                if (!eventName.StartsWith("event"))
                    return false;

                // Check device name - real keyboards contain specific strings
                string namePath = $"/sys/class/input/{eventName}/device/name";
                if (File.Exists(namePath))
                {
                    string name = File.ReadAllText(namePath).Trim().ToLower();
                    
                    // Skip non-keyboard devices
                    if (name.Contains("power button") || 
                        name.Contains("lid switch") ||
                        name.Contains("sleep button") ||
                        name.Contains("video bus") ||
                        name.Contains("mouse") ||
                        name.Contains("touchpad") ||
                        name.Contains("sensor"))
                    {
                        return false;
                    }
                    
                    // Real keyboards typically have these in their name
                    if (name.Contains("keyboard") || 
                        name.Contains("at translated set") ||
                        name.Contains("usb keyboard") ||
                        name.Contains("hid"))
                    {
                        // Verify it has key capabilities
                        string capsPath = $"/sys/class/input/{eventName}/device/capabilities/key";
                        if (File.Exists(capsPath))
                        {
                            string caps = File.ReadAllText(capsPath).Trim();
                            // Real keyboards have VERY long capability strings (100+ chars)
                            // Power buttons have short strings
                            if (caps.Length > 50)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[24]; // sizeof(input_event) on 64-bit
            
            while (_running && _keyboardDevice != null)
            {
                try
                {
                    int bytesRead = _keyboardDevice.Read(buffer, 0, buffer.Length);
                    if (bytesRead == buffer.Length)
                    {
                        ushort type = BitConverter.ToUInt16(buffer, 16);
                        ushort code = BitConverter.ToUInt16(buffer, 18);
                        int value = BitConverter.ToInt32(buffer, 20);

                        if (type == EV_KEY && value == KEY_PRESS)
                        {
                            // Check if this key matches our toggle hotkey
                            if (_hotkeys.TryGetValue(KeyType.InjectionToggle, out int? toggleKey))
                            {
                                if (toggleKey.HasValue && code == toggleKey.Value)
                                {
                                    OnKeyPress?.Invoke(this);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(100);
                }
            }
        }

        public void SetHotKey(KeyType keyType, int? key)
        {
            _hotkeys[keyType] = key;
        }

        public void SetEnableMouseHook(bool enabled)
        {
            _mouseHookEnabled = enabled;
            // Mouse button to key mapping not implemented yet on Linux
            // Would require writing to uinput device via ydotool
        }

        public void Dispose()
        {
            _running = false;
            _keyboardDevice?.Dispose();
            _readThread?.Join(1000);
        }
    }
}
#endif

