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
                Console.WriteLine($"[KeyHandler] Found keyboard device: {keyboardDevice}");
                try
                {
                    _keyboardDevice = new FileStream(keyboardDevice, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _running = true;
                    _readThread = new Thread(ReadLoop) { IsBackground = true };
                    _readThread.Start();
                    Console.WriteLine("[KeyHandler] Started keyboard monitoring thread");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[KeyHandler] ERROR: Could not open keyboard device {keyboardDevice}: {ex.Message}");
                    Console.WriteLine("[KeyHandler] Global hotkeys will not work. Make sure user is in 'input' group.");
                    Console.WriteLine("[KeyHandler] Run: sudo usermod -aG input $USER && logout");
                }
            }
            else
            {
                Console.WriteLine("[KeyHandler] WARNING: No keyboard device found in /dev/input/");
                Console.WriteLine("[KeyHandler] Trying to list available devices:");
                try
                {
                    foreach (var f in Directory.GetFiles("/dev/input"))
                    {
                        Console.WriteLine($"  - {f}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Cannot list: {ex.Message}");
                }
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
        /// Check if a device is likely a keyboard by examining key capabilities
        /// </summary>
        private bool IsLikelyKeyboard(string devicePath)
        {
            try
            {
                string eventName = Path.GetFileName(devicePath);
                if (!eventName.StartsWith("event"))
                    return false;

                // Check for key capabilities (should have alphabet keys)
                string capsPath = $"/sys/class/input/{eventName}/device/capabilities/key";
                if (File.Exists(capsPath))
                {
                    string caps = File.ReadAllText(capsPath).Trim();
                    // A keyboard should have many key capabilities
                    // The caps string is hex, a real keyboard will have many bits set
                    return caps.Length > 10; // Heuristic: keyboards have longer capability strings
                }
            }
            catch { }
            return false;
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[24]; // sizeof(input_event) on 64-bit
            Console.WriteLine("[KeyHandler] ReadLoop started, waiting for key events...");
            
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
                            Console.WriteLine($"[KeyHandler] Key pressed: evdev code {code}");
                            
                            // Check if this key matches our toggle hotkey
                            if (_hotkeys.TryGetValue(KeyType.InjectionToggle, out int? toggleKey))
                            {
                                Console.WriteLine($"[KeyHandler] Toggle key is set to: {toggleKey}");
                                if (toggleKey.HasValue && code == toggleKey.Value)
                                {
                                    Console.WriteLine("[KeyHandler] TOGGLE KEY MATCHED! Invoking OnKeyPress...");
                                    OnKeyPress?.Invoke(this);
                                }
                            }
                            else
                            {
                                Console.WriteLine("[KeyHandler] No toggle key configured yet");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[KeyHandler] Read error: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
            Console.WriteLine("[KeyHandler] ReadLoop ended");
        }

        public void SetHotKey(KeyType keyType, int? key)
        {
            Console.WriteLine($"[KeyHandler] SetHotKey called: {keyType} = {key}");
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

