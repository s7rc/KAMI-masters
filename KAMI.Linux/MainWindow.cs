using Gtk;
using KAMI.Core;
using System;
using System.Globalization;

namespace KAMI.Linux
{
    /// <summary>
    /// GTK4 Main Window for KAMI Linux - matches Windows WPF functionality
    /// </summary>
    public class MainWindow : Window
    {
        private readonly KAMICore _kami;
        
        // UI Elements
        private readonly Label _statusLabel;
        private readonly Label _infoLabel;
        private readonly Button _toggleButton;
        private readonly Entry _sensitivityEntry;
        private readonly CheckButton _invertXCheck;
        private readonly CheckButton _invertYCheck;
        
        private bool _waitingForToggleKey = false;

        public MainWindow() : base()
        {
            Title = "KAMI - Kot And Mouse Injector";
            SetDefaultSize(400, 350);
            
            // Initialize KAMI Core
            _kami = new KAMICore(OnException);
            _kami.OnUpdate += OnKamiUpdate;
            
            // Create main layout
            var mainBox = Box.New(Orientation.Vertical, 10);
            mainBox.MarginTop = 15;
            mainBox.MarginBottom = 15;
            mainBox.MarginStart = 15;
            mainBox.MarginEnd = 15;
            
            // Status section
            var statusFrame = new Frame();
            statusFrame.Label = "Status";
            _statusLabel = Label.New("Status: Unconnected");
            _statusLabel.Halign = Align.Start;
            statusFrame.Child = _statusLabel;
            mainBox.Append(statusFrame);
            
            // Game info section
            var infoFrame = new Frame();
            infoFrame.Label = "Game Info";
            _infoLabel = Label.New("Not connected to emulator");
            _infoLabel.Halign = Align.Start;
            _infoLabel.Wrap = true;
            infoFrame.Child = _infoLabel;
            mainBox.Append(infoFrame);
            
            // Controls section
            var controlsFrame = new Frame();
            controlsFrame.Label = "Controls";
            var controlsBox = Box.New(Orientation.Vertical, 8);
            controlsBox.MarginTop = 8;
            controlsBox.MarginBottom = 8;
            controlsBox.MarginStart = 8;
            controlsBox.MarginEnd = 8;
            
            // Toggle hotkey button
            var toggleRow = Box.New(Orientation.Horizontal, 10);
            var toggleLabel = Label.New("Toggle Key:");
            _toggleButton = Button.NewWithLabel(GetKeyName(_kami.Config.ToggleKey));
            _toggleButton.OnClicked += OnToggleButtonClicked;
            toggleRow.Append(toggleLabel);
            toggleRow.Append(_toggleButton);
            controlsBox.Append(toggleRow);
            
            // Sensitivity entry
            var sensRow = Box.New(Orientation.Horizontal, 10);
            var sensLabel = Label.New("Sensitivity:");
            _sensitivityEntry = Entry.New();
            _sensitivityEntry.GetBuffer().SetText(_kami.Config.Sensitivity.ToString(CultureInfo.InvariantCulture), -1);
            _sensitivityEntry.GetBuffer().OnInsertedText += OnSensitivityChanged;
            _sensitivityEntry.GetBuffer().OnDeletedText += OnSensitivityDeleted;
            _sensitivityEntry.WidthChars = 10;
            sensRow.Append(sensLabel);
            sensRow.Append(_sensitivityEntry);
            controlsBox.Append(sensRow);
            
            // Invert checkboxes
            var invertRow = Box.New(Orientation.Horizontal, 15);
            _invertXCheck = CheckButton.NewWithLabel("Invert X");
            _invertXCheck.Active = _kami.Config.InvertX;
            _invertXCheck.OnToggled += OnInvertXToggled;
            _invertYCheck = CheckButton.NewWithLabel("Invert Y");
            _invertYCheck.Active = _kami.Config.InvertY;
            _invertYCheck.OnToggled += OnInvertYToggled;
            invertRow.Append(_invertXCheck);
            invertRow.Append(_invertYCheck);
            controlsBox.Append(invertRow);
            
            // Manual inject button (backup for when global hotkey doesn't work)
            var injectButton = Button.NewWithLabel("▶ Start Injection");
            injectButton.MarginTop = 10;
            injectButton.OnClicked += (sender, args) =>
            {
                _kami.ToggleInjector();
                var btn = sender as Button;
                if (btn != null)
                {
                    btn.Label = _kami.Injecting ? "⏹ Stop Injection" : "▶ Start Injection";
                }
            };
            controlsBox.Append(injectButton);
            
            controlsFrame.Child = controlsBox;
            mainBox.Append(controlsFrame);
            
            // Info label at bottom
            var helpLabel = Label.New("Press the Toggle Key to start/stop mouse injection.\nMake sure RPCS3 has IPC enabled.");
            helpLabel.Halign = Align.Start;
            helpLabel.MarginTop = 10;
            mainBox.Append(helpLabel);
            
            Child = mainBox;
            
            // Handle key presses for hotkey binding
            var keyController = EventControllerKey.New();
            keyController.OnKeyPressed += OnKeyPressed;
            AddController(keyController);
            
            // Handle window close
            OnCloseRequest += OnWindowCloseRequest;
            
            // Start KAMI
            _kami.Start();
        }

        private void OnException(Exception ex)
        {
            Console.WriteLine($"KAMI Error: {ex.Message}");
            // Could show a dialog here
        }

        private void OnKamiUpdate(object sender, IntPtr ipc)
        {
            // Update UI on main thread
            GLib.Functions.IdleAdd(0, () =>
            {
                UpdateUI(ipc);
                return false; // Don't repeat
            });
        }

        private void UpdateUI(IntPtr ipc)
        {
            _statusLabel.SetText($"Status: {_kami.Status}");
            
            if (_kami.Connected)
            {
                string version = PineIPC.Version(ipc);
                string title = PineIPC.GetGameTitle(ipc);
                string titleId = PineIPC.GetGameID(ipc);
                string gameVersion = PineIPC.GetGameVersion(ipc);
                
                _infoLabel.SetText(
                    $"Version: {version}\n" +
                    $"Title: {title}\n" +
                    $"Title ID: {titleId}\n" +
                    $"Game Version: {gameVersion}\n" +
                    $"Emu Status: {_kami.EmuStatus}");
            }
            else
            {
                _infoLabel.SetText("Not connected to emulator.\nMake sure RPCS3 is running with IPC enabled.");
            }
        }

        private void OnToggleButtonClicked(Button sender, EventArgs args)
        {
            _waitingForToggleKey = true;
            _toggleButton.Label = "Press a key...";
        }

        private bool OnKeyPressed(EventControllerKey controller, EventControllerKey.KeyPressedSignalArgs args)
        {
            if (_waitingForToggleKey)
            {
                _waitingForToggleKey = false;
                
                uint keyval = args.Keyval;
                // Convert GTK keyval to evdev keycode (approximate mapping)
                int? evdevKey = GtkKeyToEvdev(keyval);
                
                if (args.Keyval == Gdk.Constants.KEY_Escape)
                {
                    // Escape = unbind
                    _kami.SetToggleKey(null);
                    _toggleButton.Label = "Unbound";
                }
                else if (evdevKey.HasValue)
                {
                    _kami.SetToggleKey(evdevKey);
                    _toggleButton.Label = GetKeyName(evdevKey);
                }
                
                return true; // Event handled
            }
            return false;
        }

        private void OnSensitivityChanged(EntryBuffer sender, EntryBuffer.InsertedTextSignalArgs args)
        {
            UpdateSensitivity();
        }

        private void OnSensitivityDeleted(EntryBuffer sender, EntryBuffer.DeletedTextSignalArgs args)
        {
            UpdateSensitivity();
        }

        private void UpdateSensitivity()
        {
            string text = _sensitivityEntry.GetBuffer().GetText();
            if (float.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out float sens))
            {
                _kami.SetSensitivity(sens);
            }
        }

        private void OnInvertXToggled(CheckButton sender, EventArgs args)
        {
            _kami.SetInvertX(_invertXCheck.Active);
        }

        private void OnInvertYToggled(CheckButton sender, EventArgs args)
        {
            _kami.SetInvertY(_invertYCheck.Active);
        }

        private bool OnWindowCloseRequest(Window sender, EventArgs args)
        {
            _kami.Stop();
            return false; // Allow window to close
        }

        /// <summary>
        /// Get display name for an evdev keycode
        /// </summary>
        private string GetKeyName(int? keycode)
        {
            if (!keycode.HasValue)
                return "Unbound";
                
            // Common evdev keycodes
            return keycode.Value switch
            {
                1 => "Escape",
                2 => "1", 3 => "2", 4 => "3", 5 => "4", 6 => "5",
                7 => "6", 8 => "7", 9 => "8", 10 => "9", 11 => "0",
                16 => "Q", 17 => "W", 18 => "E", 19 => "R", 20 => "T",
                21 => "Y", 22 => "U", 23 => "I", 24 => "O", 25 => "P",
                30 => "A", 31 => "S", 32 => "D", 33 => "F", 34 => "G",
                35 => "H", 36 => "J", 37 => "K", 38 => "L",
                44 => "Z", 45 => "X", 46 => "C", 47 => "V", 48 => "B",
                49 => "N", 50 => "M",
                57 => "Space",
                28 => "Enter",
                14 => "Backspace",
                15 => "Tab",
                29 => "LCtrl",
                42 => "LShift",
                54 => "RShift",
                56 => "LAlt",
                100 => "RAlt",
                59 => "F1", 60 => "F2", 61 => "F3", 62 => "F4",
                63 => "F5", 64 => "F6", 65 => "F7", 66 => "F8",
                67 => "F9", 68 => "F10", 87 => "F11", 88 => "F12",
                _ => $"Key{keycode.Value}"
            };
        }

        /// <summary>
        /// Convert GTK keyval to evdev keycode (basic mapping)
        /// </summary>
        private int? GtkKeyToEvdev(uint keyval)
        {
            // Basic mapping of common keys
            // GTK uses X11 keysyms, we need evdev codes
            return keyval switch
            {
                Gdk.Constants.KEY_Escape => 1,
                Gdk.Constants.KEY_1 or Gdk.Constants.KEY_exclam => 2,
                Gdk.Constants.KEY_2 or Gdk.Constants.KEY_at => 3,
                Gdk.Constants.KEY_3 or Gdk.Constants.KEY_numbersign => 4,
                Gdk.Constants.KEY_4 or Gdk.Constants.KEY_dollar => 5,
                Gdk.Constants.KEY_5 or Gdk.Constants.KEY_percent => 6,
                Gdk.Constants.KEY_6 => 7,
                Gdk.Constants.KEY_7 => 8,
                Gdk.Constants.KEY_8 => 9,
                Gdk.Constants.KEY_9 => 10,
                Gdk.Constants.KEY_0 => 11,
                Gdk.Constants.KEY_q or Gdk.Constants.KEY_Q => 16,
                Gdk.Constants.KEY_w or Gdk.Constants.KEY_W => 17,
                Gdk.Constants.KEY_e or Gdk.Constants.KEY_E => 18,
                Gdk.Constants.KEY_r or Gdk.Constants.KEY_R => 19,
                Gdk.Constants.KEY_t or Gdk.Constants.KEY_T => 20,
                Gdk.Constants.KEY_y or Gdk.Constants.KEY_Y => 21,
                Gdk.Constants.KEY_u or Gdk.Constants.KEY_U => 22,
                Gdk.Constants.KEY_i or Gdk.Constants.KEY_I => 23,
                Gdk.Constants.KEY_o or Gdk.Constants.KEY_O => 24,
                Gdk.Constants.KEY_p or Gdk.Constants.KEY_P => 25,
                Gdk.Constants.KEY_a or Gdk.Constants.KEY_A => 30,
                Gdk.Constants.KEY_s or Gdk.Constants.KEY_S => 31,
                Gdk.Constants.KEY_d or Gdk.Constants.KEY_D => 32,
                Gdk.Constants.KEY_f or Gdk.Constants.KEY_F => 33,
                Gdk.Constants.KEY_g or Gdk.Constants.KEY_G => 34,
                Gdk.Constants.KEY_h or Gdk.Constants.KEY_H => 35,
                Gdk.Constants.KEY_j or Gdk.Constants.KEY_J => 36,
                Gdk.Constants.KEY_k or Gdk.Constants.KEY_K => 37,
                Gdk.Constants.KEY_l or Gdk.Constants.KEY_L => 38,
                Gdk.Constants.KEY_z or Gdk.Constants.KEY_Z => 44,
                Gdk.Constants.KEY_x or Gdk.Constants.KEY_X => 45,
                Gdk.Constants.KEY_c or Gdk.Constants.KEY_C => 46,
                Gdk.Constants.KEY_v or Gdk.Constants.KEY_V => 47,
                Gdk.Constants.KEY_b or Gdk.Constants.KEY_B => 48,
                Gdk.Constants.KEY_n or Gdk.Constants.KEY_N => 49,
                Gdk.Constants.KEY_m or Gdk.Constants.KEY_M => 50,
                Gdk.Constants.KEY_space => 57,
                Gdk.Constants.KEY_Return => 28,
                Gdk.Constants.KEY_BackSpace => 14,
                Gdk.Constants.KEY_Tab => 15,
                Gdk.Constants.KEY_Control_L => 29,
                Gdk.Constants.KEY_Shift_L => 42,
                Gdk.Constants.KEY_Shift_R => 54,
                Gdk.Constants.KEY_Alt_L => 56,
                Gdk.Constants.KEY_Alt_R => 100,
                Gdk.Constants.KEY_F1 => 59,
                Gdk.Constants.KEY_F2 => 60,
                Gdk.Constants.KEY_F3 => 61,
                Gdk.Constants.KEY_F4 => 62,
                Gdk.Constants.KEY_F5 => 63,
                Gdk.Constants.KEY_F6 => 64,
                Gdk.Constants.KEY_F7 => 65,
                Gdk.Constants.KEY_F8 => 66,
                Gdk.Constants.KEY_F9 => 67,
                Gdk.Constants.KEY_F10 => 68,
                Gdk.Constants.KEY_F11 => 87,
                Gdk.Constants.KEY_F12 => 88,
                _ => null
            };
        }
    }
}
